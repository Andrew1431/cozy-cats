using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CozyCats;

// Nap spots on the game's own ship furniture. Offsets are in a flat frame on the furniture's root (its position,
// turned only by its heading) so they survive being moved and rotated with [B]. Furniture that isn't out does nothing.
public class FurniturePerch : PerchHost
{
    private class Def
    {
        public string Match, Name;
        public Vector3? Local;
        public float Yaw;
        // Fallback before tuning: this far up the furniture's height, centred.
        public float Height01;
        public Color Color;
    }

    // Matched against the unlockable's name, lowercased.
    private static readonly Def[] Defs =
    {
        new Def { Match = "bunk", Name = "TopBunk", Local = new Vector3(1.649f, 1.1f, -0.737f), Yaw = 240f, Color = new Color(1f, 0.25f, 0.25f) },
        new Def { Match = "bunk", Name = "BottomBunk", Local = new Vector3(1.494f, -0.813f, -0.711f), Yaw = 240f, Color = new Color(1f, 0.6f, 0.1f) },
        new Def { Match = "cabinet", Name = "Top", Local = new Vector3(0f, 0.887f, 0.01f), Yaw = 225f, Color = new Color(0.3f, 1f, 0.3f) },
        new Def { Match = "cupboard", Name = "Top", Local = new Vector3(1.093f, 1.542f, 0.457f), Yaw = 225f, Color = new Color(1f, 0.35f, 1f) },
    };

    internal class TunedSpot : Spot
    {
        public Vector3 Local;
        public float Yaw;
        public Color Color;
    }

    private string furnitureName;
    private Transform root;
    public override string Label => furnitureName;
    internal IEnumerable<TunedSpot> Tuned => Spots.OfType<TunedSpot>();

    private void Setup(PlaceableShipObject place, string name)
    {
        placeable = place;
        furnitureName = name;
        root = transform;
        string lower = name.ToLowerInvariant();
        Bounds? looks = null;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r.GetComponentInParent<GrabbableObject>() != null) continue;
            if (looks == null) looks = r.bounds;
            else { var b = looks.Value; b.Encapsulate(r.bounds); looks = b; }
        }
        var frame = Frame();
        foreach (var d in Defs.Where(d => lower.Contains(d.Match)))
        {
            Vector3 local = d.Local ?? Vector3.zero;
            if (d.Local == null && looks != null)
            {
                var b = looks.Value;
                var p = new Vector3(b.center.x, b.min.y + b.size.y * d.Height01, b.center.z);
                local = Quaternion.Inverse(frame) * (p - root.position);
            }
            var point = new GameObject($"CozyPerch_{d.Name}").transform;
            point.SetParent(root, false);
            var spot = new TunedSpot { Point = point, Name = d.Name, Pose = CatItem.PoseCurl, Host = this, Local = local, Yaw = d.Yaw, Color = d.Color };
            Spots.Add(spot);
            Apply(spot);
        }
        Plugin.Log.LogInfo($"Nap spots on the {name}: {string.Join(", ", Spots.Select(s => s.Name))}");
    }

    // The root's heading, ignoring any tilt from how the model was imported.
    private Quaternion Frame()
    {
        Vector3 f = Vector3.ProjectOnPlane(root.forward, Vector3.up);
        if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(root.up, Vector3.up);
        return Quaternion.LookRotation(f, Vector3.up);
    }

    // Spot transforms face along their -up, like the cat tree's empties.
    internal void Apply(TunedSpot s)
    {
        var frame = Frame();
        s.Point.position = root.position + frame * s.Local;
        s.Point.rotation = frame * Quaternion.Euler(0f, s.Yaw, 0f) * Quaternion.Euler(-90f, 0f, 0f);
    }

    internal void SetWorld(TunedSpot s, Vector3 world)
    {
        s.Local = Quaternion.Inverse(Frame()) * (world - root.position);
        Apply(s);
    }

    // Furniture spawns whenever it's unlocked, so look for new pieces now and then rather than hooking each one.
    internal class Scanner : MonoBehaviour
    {
        private float next;
        private readonly HashSet<string> logged = new HashSet<string>();

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
            var list = StartOfRound.Instance?.unlockablesList?.unlockables;
            if (list == null) return;
            foreach (var place in FindObjectsOfType<PlaceableShipObject>())
            {
                if (place.unlockableID < 0 || place.unlockableID >= list.Count) continue;
                var rootGo = place.parentObject != null ? place.parentObject.gameObject : place.mainTransform != null ? place.mainTransform.gameObject : null;
                if (rootGo == null || rootGo.GetComponent<PerchHost>() != null) continue;
                string name = list[place.unlockableID].unlockableName;
                if (logged.Add(name)) Plugin.Log.LogInfo($"Ship furniture: {name}");
                string lower = name.ToLowerInvariant();
                if (!Defs.Any(d => lower.Contains(d.Match))) continue;
                rootGo.AddComponent<FurniturePerch>().Setup(place, name);
            }
        }
    }
}
