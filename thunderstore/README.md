# CozyCats

Cats are hiding in the facility. Find them, scoop them up, and bring them home to the ship.

- **Every cat is unique**: random fur and eye colours, a random size and a name you'll see when you scan them.
- **Cozy, not chaotic**: cats sit, loaf, idle, blink and twitch their ears. Pick one up and it rolls over in your arms
  with its paws in the air and purrs quietly.
- **Meow on demand**: press LMB while holding a cat. Set down, they'll occasionally meow on their own.
- **Not for sale**: cats are worth nothing and the Company counter refuses them. Keep them on the ship as long as
  you're employed. They're lost when you're fired.
- **Never your downfall**: monsters can't hear cats, can't pick them up, and cats never make noise that draws
  attention. They survive a crew wipe like any other non-scrap item.
- One or two cats per moon at most, and some moons have none.

Everyone in the lobby needs the mod.

## Config

`BepInEx/config/CozyCats.cfg`, created the first time you launch:

| Section | Key | Default | What it does |
|---|---|---|---|
| Spawning | MaxCatsPerMoon | 2 | Most cats that can show up in one facility (0-5). |
| Spawning | FirstCatChance | 0.6 | Chance that at least one cat is hiding in the facility. |
| Spawning | ExtraCatChance | 0.25 | Chance for each additional cat after the first. |
| Visuals | ModelScale | 1.0 | Size multiplier for every cat. |
| Cats | SpecialCats | see below | Your own named cats. |
| Sounds | AmbientMeows | true | Cats that are set down meow softly every few minutes. |

Spawning and special cats follow the host's config.

### Your own cats

Add your own cats to `SpecialCats` and they'll show up just as often as any other name. Separate cats with `;` and
fields with `,`:

```
Name, FurColour, EyeColour[, CoatLabel shown when scanned]
```

Colours are `#RRGGBB` or a preset: Ginger, Black, White, Grey, Cream, Chocolate, Silver, Blue, Fawn, Peach, Green,
Yellow, Amber, Copper, Hazel, Olive.

The default is `Strider, Black, Yellow; Peaches, Peach, Olive`: Strider is a black cat with yellow eyes, and Peaches
is a peach-coloured cat with olive-green eyes. They're the two real cats this mod was made for. Keep them if you like.

## Credits

All cat sounds are CC0 recordings from Freesound. Attribution isn't required, but these people recorded their cats for
everyone, so thank you: PuzzlingGGG (BVMAE meows), foxboyprower (Old Tabby Sounds) and Sadiquecat (Pearl, Perran and
Minette purring).
