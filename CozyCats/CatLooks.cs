using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CozyCats;

// Everything about a cat's look is derived from one seed so it can live in the game's single per-item save int.
// The name (and any config-defined special look) is resolved by the host and synced as an "identity" string,
// because players can have different configs.
internal readonly struct CatLooks
{
    public const int SeedBits = 22;
    public const int SeedMask = (1 << SeedBits) - 1;

    private static readonly (string name, Color color)[] Furs =
    {
        ("Ginger", new Color(0.85f, 0.45f, 0.15f)),
        ("Black", new Color(0.07f, 0.07f, 0.08f)),
        // Light coats are kept below ~0.8 because the game's lighting/posterize pass blows them out to chrome.
        ("White", new Color(0.80f, 0.79f, 0.76f)),
        ("Grey", new Color(0.46f, 0.47f, 0.50f)),
        ("Cream", new Color(0.82f, 0.70f, 0.52f)),
        ("Chocolate", new Color(0.38f, 0.24f, 0.15f)),
        ("Silver", new Color(0.62f, 0.64f, 0.67f)),
        ("Blue", new Color(0.38f, 0.43f, 0.51f)),
        ("Fawn", new Color(0.78f, 0.62f, 0.45f)),
    };

    private static readonly (string name, Color color)[] Eyes =
    {
        ("Green", new Color(0.45f, 0.80f, 0.20f)),
        ("Yellow", new Color(0.95f, 0.80f, 0.15f)),
        ("Amber", new Color(0.95f, 0.55f, 0.10f)),
        ("Blue", new Color(0.30f, 0.60f, 0.95f)),
        ("Copper", new Color(0.80f, 0.40f, 0.15f)),
        ("Hazel", new Color(0.60f, 0.62f, 0.25f)),
    };

    // Extra colour names usable in the SpecialCats config, alongside the fur and eye names above.
    private static readonly (string name, Color color)[] ExtraColors =
    {
        ("Peach", new Color(0.86f, 0.63f, 0.45f)),
        ("Olive", new Color(0.50f, 0.60f, 0.22f)),
    };

    private static readonly string[] BuiltInNames =
    {
        "Mittens", "Biscuit", "Pumpkin", "Mochi", "Noodle", "Beans", "Waffles", "Tofu", "Pepper",
        "Socks", "Muffin", "Ziggy", "Olive", "Gizmo", "Nugget", "Clover", "Pudding", "Sprout",
        "Marbles", "Toast", "Bean", "Whiskers", "Luna", "Milo", "Cleo", "Oreo", "Salem", "Tater",
        "Dumpling", "Scrap", "Bolt", "Quota", "Gordion", "Rivet", "Sprocket", "Fuse", "Ducky", "Apparatus",
        "Biscotti", "Nimbus", "Pebble", "Juniper", "Maple", "Sushi", "Paprika", "Wasabi", "Bramble", "Cosmo",
        "Hazel", "Fig", "Pistachio", "Turnip", "Butters", "Kiwi", "Smudge", "Tinsel", "Mabel", "Ravioli",
    };

    private class SpecialCat
    {
        public string Name, CoatLabel;
        public Color Fur, Eye;
        public string Encode() => $"{Name}|{ColorUtility.ToHtmlStringRGB(Fur)}|{ColorUtility.ToHtmlStringRGB(Eye)}|{CoatLabel}";
    }

    private static List<SpecialCat> specials = new List<SpecialCat>();
    private static string[] namePool = BuiltInNames;

    public readonly string Name;
    public readonly string FurName;
    public readonly Color Fur;
    public readonly Color Eye;
    public readonly float SizeRoll;
    public float Size => SizeAt(SizeRoll);

    // Debug builds tweak these live (DebugKeys: U/J and I/K).
    public static float SizeMin = 1.15f, SizeMax = 2.05f;
    public static float SizeAt(float roll) => Mathf.Lerp(SizeMin, SizeMax, roll);
    public readonly int SoundRoll;

    // The rng draw order is fixed (fur, eye, name, size, sound) so the name pool's size never shifts other traits.
    public CatLooks(int seed, string identity)
    {
        var rng = new System.Random(seed);
        var fur = Furs[rng.Next(Furs.Length)];
        FurName = fur.name;
        Fur = fur.color;
        Eye = Eyes[rng.Next(Eyes.Length)].color;
        Name = BuiltInNames[rng.Next(BuiltInNames.Length)];
        SizeRoll = (float)rng.NextDouble();
        SoundRoll = rng.Next();

        if (string.IsNullOrEmpty(identity)) return;
        var parts = identity.Split('|');
        Name = parts[0];
        if (parts.Length >= 3 && ColorUtility.TryParseHtmlString("#" + parts[1], out var f) && ColorUtility.TryParseHtmlString("#" + parts[2], out var e))
        {
            Fur = f;
            Eye = e;
            if (parts.Length >= 4 && parts[3].Length > 0) FurName = parts[3];
        }
    }

    // Host only: pick the name from built-ins plus this host's special cats.
    public static string ResolveIdentity(int seed)
    {
        var rng = new System.Random(seed);
        rng.Next(Furs.Length);
        rng.Next(Eyes.Length);
        string name = namePool[rng.Next(namePool.Length)];
        var special = specials.FirstOrDefault(s => s.Name == name);
        return special != null ? special.Encode() : name;
    }

    public static int NewSeed() => Random.Range(1, SeedMask + 1);

    // Format: "Name, Fur, Eyes[, Coat label]; ..." where colours are a preset name or #RRGGBB.
    public static void LoadSpecials(string config)
    {
        specials = new List<SpecialCat>();
        foreach (var entry in (config ?? "").Split(';'))
        {
            var f = entry.Split(',').Select(s => s.Trim()).ToArray();
            if (f.Length < 3 || f[0].Length == 0) continue;
            if (f[0].Contains("|") || !TryColor(f[1], false, out var fur) || !TryColor(f[2], true, out var eye))
            {
                Plugin.Log.LogWarning($"Couldn't read special cat '{entry.Trim()}'. Expected: Name, FurColour, EyeColour[, CoatLabel]");
                continue;
            }
            string label = f.Length >= 4 && f[3].Length > 0 ? f[3] : (f[1].StartsWith("#") ? "Special" : f[1]);
            specials.RemoveAll(s => s.Name == f[0]);
            specials.Add(new SpecialCat { Name = f[0], Fur = fur, Eye = eye, CoatLabel = label.Replace("|", "") });
        }
        namePool = BuiltInNames.Where(n => specials.All(s => s.Name != n)).Concat(specials.Select(s => s.Name)).ToArray();
        Plugin.Log.LogInfo($"Special cats: {(specials.Count == 0 ? "none" : string.Join(", ", specials.Select(s => s.Name)))}");
    }

    // "Blue" is both a coat and an eye colour, so eye fields check the eye presets first.
    private static bool TryColor(string s, bool eye, out Color c)
    {
        if (s.StartsWith("#")) return ColorUtility.TryParseHtmlString(s, out c);
        var presets = eye ? Eyes.Concat(Furs) : Furs.Concat(Eyes);
        foreach (var (name, color) in presets.Concat(ExtraColors))
        {
            if (!string.Equals(name, s, StringComparison.OrdinalIgnoreCase)) continue;
            c = color;
            return true;
        }
        c = default;
        return false;
    }
}
