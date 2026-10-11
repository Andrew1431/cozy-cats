#if DEBUG
using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace CozyCats;

// Debug builds only, driven by launch-two.ps1: `--cozy-dev host|join` skips the menus straight into a LAN game on this PC,
// and `--cozy-slot N` keeps the game in a window, placed so the host and client sit side by side.
// Every slot after the first keeps its own copy of the settings file, since two games sharing one corrupts it.
[HarmonyPatch]
internal static class DevLaunch
{
    private static readonly string Mode = Arg("--cozy-dev");
    private static readonly int Slot = int.TryParse(Arg("--cozy-slot"), out int s) ? s : -1;
    private static bool hosted, copied;
    private static int joinAttempts;

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // The game re-applies the saved fullscreen setting at startup (and whenever settings change).
    [HarmonyPatch(typeof(IngamePlayerSettings), "SetFullscreenMode")]
    [HarmonyPostfix]
    private static void KeepWindowed()
    {
        if (Slot < 0) return;
        var display = Screen.mainWindowDisplayInfo;
        // 90% of half the screen each, centred in the left/right half.
        int half = display.width / 2, w = half * 9 / 10, h = w * 9 / 16;
        Screen.SetResolution(w, h, FullScreenMode.Windowed);
        int x = Slot % 2 * half + (half - w) / 2;
        Screen.MoveMainWindowTo(display, new Vector2Int(x, Mathf.Max(0, (display.height - h) / 2)));
    }

    // Each game holds LCGeneralSaveData open while saving; a second game opening it at the same moment hits a sharing
    // violation, decides the file is corrupt and deletes it (wiping the player's settings). Every ES3 file access
    // resolves its path through FullPath, so redirect it there. The copy starts from the real settings.
    [HarmonyPatch(typeof(ES3Settings), nameof(ES3Settings.FullPath), MethodType.Getter)]
    [HarmonyPrefix]
    private static void SeparateSettings(ES3Settings __instance)
    {
        if (Slot < 1 || __instance.path != "LCGeneralSaveData") return;
        string own = $"LCGeneralSaveData_dev{Slot}";
        __instance.path = own;
        if (copied) return;
        copied = true;
        string dir = Application.persistentDataPath;
        try
        {
            if (!File.Exists(Path.Combine(dir, own)) && File.Exists(Path.Combine(dir, "LCGeneralSaveData")))
                File.Copy(Path.Combine(dir, "LCGeneralSaveData"), Path.Combine(dir, own));
        }
        catch (IOException e)
        {
            Plugin.Log.LogWarning($"[DevLaunch] couldn't copy settings for slot {Slot}: {e.Message}");
        }
    }

    [HarmonyPatch(typeof(PreInitSceneScript), "Start")]
    [HarmonyPostfix]
    private static void PickLan(PreInitSceneScript __instance)
    {
        if (Mode == null) return;
        __instance.StartCoroutine(After(0.5f, () => __instance.ChooseLaunchOption(online: false)));
    }

    [HarmonyPatch(typeof(MenuManager), "Start")]
    [HarmonyPostfix]
    private static void AutoConnect(MenuManager __instance)
    {
        if (__instance.isInitScene || !GameNetworkManager.Instance.disableSteam) return;
        if (Mode == "host" && !hosted)
        {
            hosted = true;
            __instance.StartCoroutine(After(1f, () =>
            {
                Plugin.Log.LogWarning("[DevLaunch] hosting a LAN game");
                __instance.ClickHostButton();
                __instance.lobbyNameInputField.text = "CatDev";
                __instance.ConfirmHostButton();
            }));
        }
        // A failed connection drops back to the menu, which lands here again: keep trying until the host is up.
        else if (Mode == "join" && joinAttempts < 15)
        {
            joinAttempts++;
            __instance.StartCoroutine(After(joinAttempts == 1 ? 4f : 2f, () =>
            {
                if (NetworkManager.Singleton.IsListening) return;
                Plugin.Log.LogWarning($"[DevLaunch] joining the LAN game on this PC (attempt {joinAttempts})");
                NetworkManager.Singleton.GetComponent<UnityTransport>().ConnectionData.Address = "127.0.0.1";
                __instance.StartAClient();
            }));
        }
    }

    private static IEnumerator After(float seconds, Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }
}
#endif
