using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CozyCats;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(LethalLib.Plugin.ModGUID)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static CatConfig Cfg;

    private void Awake()
    {
        Log = Logger;
        Cfg = new CatConfig(Config);
        CatLooks.LoadSpecials(Cfg.SpecialCats.Value);

        InitNetcodePatcher();

        string bundlePath = Path.Combine(Path.GetDirectoryName(Info.Location), "cozycats");
        if (!CatAssets.Load(bundlePath))
        {
            Log.LogError($"Asset bundle missing at {bundlePath}; cats disabled.");
            return;
        }

        CatRegistry.Register();
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
#if DEBUG
        DebugKeys.Install();
#endif
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded. Meow.");
    }

    // NetcodePatcher emits RPC registration into RuntimeInitializeOnLoad methods; mods must invoke them by hand.
    private static void InitNetcodePatcher()
    {
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false).Length > 0)
                    method.Invoke(null, null);
            }
        }
    }
}
