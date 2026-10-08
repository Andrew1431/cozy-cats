using System.Linq;
using LethalLib.Modules;
using UnityEngine;

namespace CozyCats;

internal static class CatRegistry
{
    public static Item CatItemDef;
    public static GameObject Prefab;

    public static Item[] SoundSources = new Item[0];

    // Picked by ear in-game; names missing from this game version are skipped (cats then drop silently).
    private static readonly string[] SoftItemNames = { "Soccer ball" };

    private const int PropsLayer = 6;
    private const int ScanNodeLayer = 22;

    public static void Register()
    {
        float scale = Plugin.Cfg.ModelScale.Value;

        Prefab = NetworkPrefabs.CreateNetworkPrefab("CozyCat");
        Prefab.tag = "PhysicsProp";
        Prefab.layer = PropsLayer;

        var model = Object.Instantiate(CatAssets.CatModel, Prefab.transform);
        model.name = "Model";
        model.transform.localScale = Vector3.one * scale;
        model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // FBX imports facing -Z; face the prefab's forward
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PropsLayer;

        var col = Prefab.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.2f, 0f) * scale;
        col.size = new Vector3(0.25f, 0.4f, 0.5f) * scale;

        var audio = Prefab.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.rolloffMode = AudioRolloffMode.Linear;
        audio.minDistance = 1f;
        audio.maxDistance = 15f;

        var purrGO = new GameObject("Purr");
        purrGO.transform.SetParent(Prefab.transform, false);
        var purr = purrGO.AddComponent<AudioSource>();
        purr.playOnAwake = false;
        purr.loop = true;
        purr.volume = 0f;
        purr.spatialBlend = 1f;
        purr.rolloffMode = AudioRolloffMode.Linear;
        purr.minDistance = 0.5f;
        purr.maxDistance = 5f;

        var scanGO = new GameObject("ScanNode") { layer = ScanNodeLayer };
        scanGO.transform.SetParent(Prefab.transform, false);
        scanGO.transform.localPosition = new Vector3(0f, 0.3f, 0f) * scale;
        var scanCol = scanGO.AddComponent<BoxCollider>();
        scanCol.isTrigger = true;
        scanCol.size = Vector3.one * 0.5f;
        var scan = scanGO.AddComponent<ScanNodeProperties>();
        scan.maxRange = 13;
        scan.minRange = 1;
        scan.requiresLineOfSight = true;
        scan.headerText = "Cat";
        scan.subText = "Not for sale";
        scan.nodeType = 0;

        var item = ScriptableObject.CreateInstance<Item>();
        item.name = "CozyCatItem";
        item.itemName = "Cat";
        item.spawnPrefab = Prefab;
        item.isScrap = false;
        item.minValue = item.maxValue = 0;
        item.creditsWorth = 0;
        item.weight = 1.08f; // ~8 lb
        item.twoHanded = true;
        item.twoHandedAnimation = true;
        item.canBeGrabbedBeforeGameStart = true;
        item.itemSpawnsOnGround = true;
        item.requiresBattery = false;
        item.saveItemVariable = true;
        item.syncUseFunction = false; // meows go through CatItem's own RPC so every client hears the same clip
        item.isConductiveMetal = false;
        item.canBeInspected = false;
        item.restingRotation = Vector3.zero;
        item.verticalOffset = 0f;
        item.positionOffset = Vector3.zero;
        item.rotationOffset = Vector3.zero;
        item.toolTips = CatAssets.Meows.Length > 0 ? new[] { "Meow : [LMB]" } : new string[0];
        item.meshVariants = new Mesh[0];
        item.materialVariants = new Material[0];
        item.clinkAudios = new AudioClip[0];
        CatItemDef = item;

        var cat = Prefab.AddComponent<CatItem>();
        cat.itemProperties = item;
        cat.grabbable = true;
        cat.grabbableToEnemies = false;

        Items.RegisterItem(item);
    }

    // Borrow animation names, sounds and the inventory icon from a vanilla two-handed scrap item,
    // since those only exist once the game has loaded.
    public static void FixupFromGame(StartOfRound sor)
    {
        var items = sor.allItemsList.itemsList;
        var template = items.FirstOrDefault(i => i != null && i.isScrap && i.twoHanded && i.twoHandedAnimation && !string.IsNullOrEmpty(i.grabAnim))
                       ?? items.FirstOrDefault(i => i != null && i.twoHanded);
        if (template == null)
        {
            Plugin.Log.LogWarning("No two-handed template item found; cat will use default hold animation.");
            return;
        }

        var item = CatItemDef;
        item.grabAnim = template.grabAnim;
        item.grabAnimationTime = template.grabAnimationTime;
        item.itemIcon ??= template.itemIcon;

        // Each cat picks its grab/drop sounds from these (CatItem plays them itself), so the shared item stays silent.
        SoundSources = SoftItemNames
            .Select(n => items.FirstOrDefault(i => i != null && i.itemName == n && i.dropSFX != null))
            .Where(i => i != null).ToArray();
        item.grabSFX = null;
        item.dropSFX = null;
        item.pocketSFX = null;
        Plugin.Log.LogInfo($"Cat sound sources: {string.Join(", ", SoundSources.Select(i => i.itemName))}");

        var templateAudio = template.spawnPrefab != null ? template.spawnPrefab.GetComponent<AudioSource>() : null;
        if (templateAudio != null)
        {
            foreach (var src in Prefab.GetComponentsInChildren<AudioSource>(true))
                src.outputAudioMixerGroup = templateAudio.outputAudioMixerGroup;
        }

        Plugin.Log.LogInfo($"Cat hold animation borrowed from '{template.itemName}' ({template.grabAnim})");
    }
}
