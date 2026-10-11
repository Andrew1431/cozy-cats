using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CozyCats;

// Ship furniture a cat can climb onto and nap on: the cat tree, or vanilla furniture given spots by FurniturePerch.
public abstract class PerchHost : MonoBehaviour
{
    internal class Spot
    {
        public Transform Point;
        public string Name;
        public byte Pose;
        public PerchHost Host;
        // Host only.
        public CatItem Occupant;
    }

    internal static readonly List<PerchHost> All = new List<PerchHost>();

    internal readonly List<Spot> Spots = new List<Spot>();
    protected PlaceableShipObject placeable;

    public abstract string Label { get; }

    protected virtual void OnEnable() => All.Add(this);
    protected virtual void OnDisable() => All.Remove(this);

    // Stored furniture stays spawned, parked far below the map.
    public bool IsOut => placeable != null && placeable.unlockableID >= 0
                         && placeable.unlockableID < StartOfRound.Instance.unlockablesList.unlockables.Count
                         && !StartOfRound.Instance.unlockablesList.unlockables[placeable.unlockableID].inStorage
                         && StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(placeable.transform.position + Vector3.up * 0.3f);

    public Bounds Footprint => placeable.placeObjectCollider.bounds;

    internal static IEnumerable<Spot> FreeSpots() =>
        All.Where(h => h.IsOut).SelectMany(h => h.Spots).Where(s => s.Occupant == null || !s.Occupant.Claims(s));

    internal static Spot RandomFreeSpot()
    {
        var free = FreeSpots().ToList();
        return free.Count > 0 ? free[Random.Range(0, free.Count)] : null;
    }

    // Where a cat facing the spot should sit: the cat's forward is the empty's Blender -Y (Unity local -Y).
    internal static float SpotYaw(Spot s)
    {
        Vector3 f = Vector3.ProjectOnPlane(-s.Point.up, Vector3.up);
        return f.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(f).eulerAngles.y : s.Point.eulerAngles.y;
    }

    internal void Release(CatItem cat)
    {
        foreach (var s in Spots)
            if (s.Occupant == cat) s.Occupant = null;
    }

    // A patch of open ship floor just off the furniture, for walking up to it and hopping back down.
    // The floor is the lowest thing the ring of rays finds, so shelves and tables in the way don't count.
    internal bool FloorBeside(Vector3 near, out Vector3 point)
    {
        var sor = StartOfRound.Instance;
        var b = Footprint;
        float reach = Mathf.Max(b.extents.x, b.extents.z) + 0.45f;
        Vector3 c = b.center;
        Vector3 toward = Vector3.ProjectOnPlane(near - c, Vector3.up);
        float start = toward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toward).eulerAngles.y : Random.Range(0f, 360f);
        var hits = new List<Vector3>();
        for (int i = 0; i < 12; i++)
        {
            float yaw = start + (i % 2 == 0 ? 1 : -1) * (i + 1) / 2 * 30f;
            Vector3 p = c + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * reach;
            p.y = b.min.y + 1f;
            if (!Physics.Raycast(p, Vector3.down, out var hit, 2.5f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) continue;
            if (!sor.shipInnerRoomBounds.bounds.Contains(hit.point + Vector3.up * 0.3f)) continue;
            hits.Add(hit.point);
        }
        if (hits.Count > 0)
        {
            float floor = hits.Min(h => h.y);
            // Nearest-first order is kept, so the cat hops down on the side it came from.
            point = hits.First(h => h.y < floor + 0.2f);
            return true;
        }
        point = default;
        return false;
    }
}
