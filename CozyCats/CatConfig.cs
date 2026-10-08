using BepInEx.Configuration;

namespace CozyCats;

internal class CatConfig
{
    public readonly ConfigEntry<int> MaxCatsPerMoon;
    public readonly ConfigEntry<float> FirstCatChance;
    public readonly ConfigEntry<float> SecondCatChance;
    public readonly ConfigEntry<float> ModelScale;
    public readonly ConfigEntry<bool> AmbientMeows;
    public readonly ConfigEntry<string> SpecialCats;
    public readonly ConfigEntry<int> RescueBounty;

    public CatConfig(ConfigFile cfg)
    {
        MaxCatsPerMoon = cfg.Bind("Spawning", "MaxCatsPerMoon", 2,
            new ConfigDescription("Most cats that can show up in one facility.", new AcceptableValueRange<int>(0, 5)));
        FirstCatChance = cfg.Bind("Spawning", "FirstCatChance", 0.6f,
            new ConfigDescription("Chance (0-1) that at least one cat is hiding in the facility.", new AcceptableValueRange<float>(0f, 1f)));
        SecondCatChance = cfg.Bind("Spawning", "ExtraCatChance", 0.25f,
            new ConfigDescription("Chance (0-1) for each additional cat after the first.", new AcceptableValueRange<float>(0f, 1f)));
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
    }
}
