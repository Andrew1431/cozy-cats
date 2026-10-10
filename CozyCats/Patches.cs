using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace CozyCats;

[HarmonyPatch]
internal static class Patches
{
    [HarmonyPatch(typeof(StartOfRound), "Awake")]
    [HarmonyPostfix]
    private static void AfterStartOfRoundAwake(StartOfRound __instance) => CatRegistry.FixupFromGame(__instance);

    // The counter accepts any held item, scrap or not, so cats have to be turned away explicitly.
    [HarmonyPatch(typeof(DepositItemsDesk), nameof(DepositItemsDesk.PlaceItemOnCounter))]
    [HarmonyPrefix]
    private static bool KeepCatsOffCounter(PlayerControllerB playerWhoTriggered)
    {
        if (playerWhoTriggered == null || !(playerWhoTriggered.currentlyHeldObjectServer is CatItem cat)) return true;
        HUDManager.Instance.DisplayTip("Absolutely not.", $"The Company can't have {cat.CatName}.", isWarning: true);
        return false;
    }

    // Reuse the vanilla "X collected! Value: $N" box for rescued cats, relabelled with their name and bounty.
    [HarmonyPatch(typeof(HUDManager), nameof(HUDManager.DisplayNewScrapFound))]
    [HarmonyPrefix]
    private static void BeforeDisplayScrap(HUDManager __instance, int ___nextBoxIndex, out (CatItem cat, int box) __state)
    {
        __state = default;
        if (__instance.itemsToBeDisplayed.Count > 0 && __instance.itemsToBeDisplayed[0] is CatItem cat)
            __state = (cat, ___nextBoxIndex);
    }

    [HarmonyPatch(typeof(HUDManager), nameof(HUDManager.DisplayNewScrapFound))]
    [HarmonyPostfix]
    private static void AfterDisplayScrap(HUDManager __instance, (CatItem cat, int box) __state)
    {
        if (__state.cat == null) return;
        var box = __instance.ScrapItemBoxes[__state.box];
        box.headerText.text = $"{__state.cat.CatName} rescued!";
        box.valueText.text = $"Bounty: ${__state.cat.PendingBountyDisplay}";
    }

    // Mines go off when an item lands on them. A cat wandering or fleeing across one must never do that.
    [HarmonyPatch(typeof(Landmine), "OnTriggerEnter")]
    [HarmonyPrefix]
    private static bool CatsDontTriggerMines(Collider other) => other.GetComponent<CatItem>() == null;

    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SpawnScrapInLevel))]
    [HarmonyPostfix]
    private static void SpawnCats(RoundManager __instance)
    {
        if (!__instance.IsServer || CatRegistry.Prefab == null) return;
        var cfg = Plugin.Cfg;

        int count = 0;
        foreach (float chance in cfg.ParseCatChances())
        {
            if (Random.value >= chance) break;
            count++;
        }
        if (count == 0) return;

        var spawns = Object.FindObjectsOfType<RandomScrapSpawn>();
        if (spawns.Length == 0) return;

        for (int i = 0; i < count; i++)
        {
            var spawn = spawns[Random.Range(0, spawns.Length)];
            Vector3 pos = __instance.GetRandomNavMeshPositionInRadiusSpherical(spawn.transform.position, Mathf.Max(spawn.itemSpawnRange, 1f));
            var go = Object.Instantiate(CatRegistry.Prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), __instance.spawnedScrapContainer);
            go.GetComponent<GrabbableObject>().fallTime = 0f;
            go.GetComponent<NetworkObject>().Spawn();
            Plugin.Log.LogInfo($"Cat hiding in the facility at {pos} (spawner {spawn.name} at {spawn.transform.position})");
        }
    }
}
