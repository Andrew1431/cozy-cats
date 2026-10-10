using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace CozyCats;

internal class CatConfig
{
    private const int MaxCats = 20;
    public readonly ConfigEntry<string> CatChances;
    public readonly ConfigEntry<float> ModelScale;
    public readonly ConfigEntry<bool> AmbientMeows;
    public readonly ConfigEntry<string> SpecialCats;
    public readonly ConfigEntry<int> RescueBounty;

    public CatConfig(ConfigFile cfg)
    {
        CatChances = cfg.Bind("Spawning", "CatChances", "1, 0.8",
            "Chance (0-1) for each cat hiding in the facility, separated by commas. Rolled in order, stopping at the first miss.\n" +
            "Examples: '1, 0.8' = always one cat, usually two. '1, 1, 1, 1, 1' = five cats. '1, 0.8, 0.6, 0.4' = up to four, " +
            "each less likely. '0.5' = at most one, half the time. Up to " + MaxCats + " cats.");
        ModelScale = cfg.Bind("Visuals", "ModelScale", 1.0f,
            new ConfigDescription("Size multiplier for cats. Requires restart.", new AcceptableValueRange<float>(0.5f, 2f)));
        SpecialCats = cfg.Bind("Cats", "SpecialCats", "Strider, Black, Yellow; Peaches, Peach, Olive",
            "Your own named cats, each as rare as any other name. Separate cats with ';' and fields with ','.\n" +
            "Format: Name, FurColour, EyeColour[, CoatLabel shown when scanned]\n" +
            "Colours: #RRGGBB or a preset: Ginger, Black, White, Grey, Cream, Chocolate, Silver, Blue, Fawn, Peach, " +
            "Green, Yellow, Amber, Copper, Hazel, Olive.\n" +
            "Only the host's list matters in multiplayer. Strider and Peaches are the original two; keep them if you like.");
        RescueBounty = cfg.Bind("Rewards", "RescueBounty", 100,
            new ConfigDescription("Credits paid to the crew, once per cat, the first time it's brought into the ship. 0 turns it off.",
                new AcceptableValueRange<int>(0, 1000)));
        AmbientMeows = cfg.Bind("Sounds", "AmbientMeows", true,
            "Cats that are set down meow softly every few minutes. Monsters can never hear them.");

        MigrateOldSpawnKeys(cfg);
    }

    // Each cat's chance, from CatChances. Bad entries are skipped with a warning.
    public float[] ParseCatChances()
    {
        var chances = new List<float>();
        foreach (var part in (CatChances.Value ?? "").Split(','))
        {
            string t = part.Trim();
            if (t.Length == 0) continue;
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float c)) chances.Add(UnityEngine.Mathf.Clamp01(c));
            else Plugin.Log.LogWarning($"[Spawning] CatChances: couldn't read '{t}' as a number from 0 to 1; skipping it.");
        }
        return chances.Take(MaxCats).ToArray();
    }

    // 1.2.0 had MaxCatsPerMoon + ChanceFirstCat + ChanceExtraCat (and 1.0/1.1 FirstCatChance + ExtraCatChance).
    // Carry a customised setup over to CatChances, then drop the old keys so they don't look like they still work.
    private void MigrateOldSpawnKeys(ConfigFile cfg)
    {
        var orphans = Traverse.Create(cfg).Property("OrphanedEntries").GetValue<Dictionary<ConfigDefinition, string>>();
        if (orphans == null) return;
        string Take(string key)
        {
            var def = new ConfigDefinition("Spawning", key);
            if (!orphans.TryGetValue(def, out var v)) return null;
            orphans.Remove(def);
            return v;
        }
        string max = Take("MaxCatsPerMoon"), first = Take("ChanceFirstCat"), extra = Take("ChanceExtraCat");
        bool hadOld = (Take("FirstCatChance") != null) | (Take("ExtraCatChance") != null) | max != null | first != null | extra != null;
        if (!hadOld) return;

        if (max != null && first != null && extra != null && CatChances.Value == (string)CatChances.DefaultValue
            && int.TryParse(max, out int n) && float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
            && float.TryParse(extra, NumberStyles.Float, CultureInfo.InvariantCulture, out float e))
        {
            var list = Enumerable.Range(0, System.Math.Clamp(n, 0, MaxCats)).Select(i => i == 0 ? f : e);
            CatChances.Value = string.Join(", ", list.Select(c => c.ToString("0.##", CultureInfo.InvariantCulture)));
            if (CatChances.Value.Length == 0) CatChances.Value = "0";
        }
        cfg.Save();
    }
}
