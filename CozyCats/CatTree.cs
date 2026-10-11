using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LethalLib.Modules;
using UnityEngine;

namespace CozyCats;

// Ship furniture bought from the terminal and moved with [B] like the vanilla furniture. Cats living in the ship
// wander over now and then and curl up on one of its spots (empties exported from Blender as Spot_<Name>_<Clip>).
[HarmonyPatch]
public class CatTree : PerchHost
{
#if DEBUG
    public const int Price = 0;
#else
    public const int Price = 50;
#endif
    // ShipBuildModeManager only picks up colliders on this layer tagged "PlaceableObject".
    private const int PlaceableLayer = 26;

    public static UnlockableItem Unlockable;
    public static GameObject Prefab;

    public override string Label => "cat tree";

    private static Shader litShader;
    private static readonly Dictionary<string, Material> litMats = new Dictionary<string, Material>();

    public static void Register()
    {
        if (CatAssets.CatTree == null)
        {
            Plugin.Log.LogWarning("Cat tree missing from the asset bundle; it won't be in the store.");
            return;
        }
        Prefab = NetworkPrefabs.CreateNetworkPrefab("CozyCatTree");
        var model = Instantiate(CatAssets.CatTree, Prefab.transform);
        model.name = "Tree";
        var meshFilter = model.GetComponentInChildren<MeshFilter>();
        meshFilter.gameObject.AddComponent<MeshCollider>().sharedMesh = meshFilter.sharedMesh;

        // The build-mode box: the tree's footprint, centred halfway up so the overlap test clears the floor.
        var bounds = meshFilter.GetComponent<MeshRenderer>().bounds;
        var placeGO = new GameObject("PlacementCollider") { layer = PlaceableLayer, tag = "PlaceableObject" };
        placeGO.transform.SetParent(Prefab.transform, false);
        placeGO.transform.localPosition = new Vector3(bounds.center.x, bounds.extents.y, bounds.center.z);
        var box = placeGO.AddComponent<BoxCollider>();
        box.size = bounds.size;
        var sfx = placeGO.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 1f;

        var parent = Prefab.AddComponent<AutoParentToShip>();
        parent.overrideOffset = true;
        var place = placeGO.AddComponent<PlaceableShipObject>();
        place.parentObject = parent;
        place.mainMesh = meshFilter;
        place.mainTransform = Prefab.transform;
        place.placeObjectCollider = box;
        place.yOffset = bounds.extents.y;
        place.AllowPlacementOnCounters = false;
        place.AllowPlacementOnWalls = false;
        Prefab.AddComponent<CatTree>();

        var info = ScriptableObject.CreateInstance<TerminalNode>();
        info.clearPreviousText = true;
        info.displayText = "A big carpeted cat tree with a hideaway, a hammock and a lookout on top. " +
                           "Cats living on the ship will wander over and nap in it.\n\nMove it with [B] like any furniture.\n\n";
        Unlockable = new UnlockableItem
        {
            unlockableName = "Cat tree",
            prefabObject = Prefab,
            unlockableType = 1,
            IsPlaceable = true,
            spawnPrefab = true,
            alwaysInStock = true,
            canBeStored = true,
            maxNumber = 1,
        };
        Unlockables.RegisterUnlockable(Unlockable, StoreType.ShipUpgrade, null, null, info, Price);
        Plugin.Log.LogInfo($"Cat tree registered (${Price}), {bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00} m");
    }

    // Borrow the click every vanilla furniture piece makes when it's put down.
    public static void FixupFromGame(StartOfRound sor)
    {
        if (Prefab == null) return;
        var clip = sor.unlockablesList.unlockables
            .Select(u => u.prefabObject != null ? u.prefabObject.GetComponentInChildren<PlaceableShipObject>() : null)
            .FirstOrDefault(p => p != null && p.placeObjectSFX != null)?.placeObjectSFX
            ?? FindObjectsOfType<PlaceableShipObject>().FirstOrDefault(p => p.placeObjectSFX != null)?.placeObjectSFX;
        var place = Prefab.GetComponentInChildren<PlaceableShipObject>();
        place.placeObjectSFX = clip;
        var audio = place.GetComponent<AudioSource>();
        var mixer = sor.shipAmbianceAudio != null ? sor.shipAmbianceAudio.outputAudioMixerGroup : null;
        if (mixer != null) audio.outputAudioMixerGroup = mixer;
    }

    // LethalLib prefabs are HideAndDontSave and clones inherit that, which hides them from FindObjectsOfType
    // (the game finds furniture that way to sync where it was placed).
    private void Awake()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.None;
        placeable = GetComponentInChildren<PlaceableShipObject>();
        UseLitMaterials();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            var parts = t.name.Split('_');
            if (parts.Length != 3 || parts[0] != "Spot") continue;
            if (!CatItem.TryParsePose(parts[2], out byte pose))
            {
                Plugin.Log.LogWarning($"Cat tree spot {t.name}: unknown clip {parts[2]}");
                continue;
            }
            Spots.Add(new Spot { Point = t, Name = parts[1], Pose = pose, Host = this });
        }
    }

    // Bundle materials target the built-in pipeline; swap in HDRP/Lit copies, keeping each colour.
    private void UseLitMaterials()
    {
        litShader ??= Shader.Find("HDRP/Lit");
        if (litShader == null) return;
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (!litMats.TryGetValue(mats[i].name, out var lit))
                {
                    lit = new Material(litShader) { name = mats[i].name + " (Lit)" };
                    lit.SetColor("_BaseColor", mats[i].HasProperty("_Color") ? mats[i].color : Color.grey);
                    lit.SetFloat("_Smoothness", 0.1f);
                    litMats[mats[i].name] = lit;
                }
                mats[i] = lit;
            }
            r.sharedMaterials = mats;
        }
    }

    // Vanilla furniture prefabs carry their spot in the ship; ours is worked out from the room, so a fresh tree
    // lands on open floor. A moved tree is put back where it was by the game's own placement code after this.
    [HarmonyPatch(typeof(AutoParentToShip), "Awake")]
    [HarmonyPrefix]
    private static void DefaultSpot(AutoParentToShip __instance)
    {
        var sor = StartOfRound.Instance;
        if (sor == null || __instance.GetComponent<CatTree>() == null) return;
        var place = __instance.GetComponentInChildren<PlaceableShipObject>();
        // LethalLib numbers the PlaceableShipObject; the parent needs it too to restore a moved tree on clients.
        __instance.unlockableID = place.unlockableID;
        var b = sor.shipInnerRoomBounds.bounds;
        Vector3 p = new Vector3(b.center.x, b.max.y - 0.1f, b.center.z);
        p = Physics.Raycast(p, Vector3.down, out var hit, b.size.y + 1f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)
            ? hit.point : new Vector3(p.x, b.min.y, p.z);
        __instance.overrideOffset = true;
        __instance.positionOffset = sor.elevatorTransform.InverseTransformPoint(p);
        __instance.rotationOffset = Vector3.zero;
        Plugin.Log.LogInfo($"Cat tree default spot {p} (ship-local {__instance.positionOffset}), unlockable {place.unlockableID}");
    }
}
