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

    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SpawnScrapInLevel))]
    [HarmonyPostfix]
    private static void SpawnCats(RoundManager __instance)
    {
        if (!__instance.IsServer || CatRegistry.Prefab == null) return;
        var cfg = Plugin.Cfg;

        int count = 0;
        if (cfg.MaxCatsPerMoon.Value > 0 && Random.value < cfg.FirstCatChance.Value)
        {
            count = 1;
            while (count < cfg.MaxCatsPerMoon.Value && Random.value < cfg.SecondCatChance.Value) count++;
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
