# Changelog

## 2.0.0

Cats move now!

- Cats are skittish: they bolt from anyone standing nearby, but creep up to you if you crouch.
- Once picked up, a cat follows whoever put it down, all the way home to the ship.
- Once home, they walk to a spot in the ship and settle in.
- Faint eyeshine, so you can spot cats in the dark.
- `[Spawning] CatChances` replaces `MaxCatsPerMoon`, `ChanceFirstCat` and `ChanceExtraCat`: one chance per cat,
  e.g. `1, 0.8` (the default) or `1, 1, 1, 1, 1`. Customised old settings are carried over.

## 1.2.0

- More cats: every moon now has a cat, and usually a second one.
  - `[Spawning] FirstCatChance` 0.6 → `ChanceFirstCat` 1.0
  - `[Spawning] ExtraCatChance` 0.25 → `ChanceExtraCat` 0.8
  - The settings were renamed so everyone gets the new defaults. If you'd customised them, set the new keys again.

## 1.1.0

- Rescue bounty: bringing a cat into the ship for the first time pays the crew $100, shown in the usual
  "collected" box. New setting `[Rewards] RescueBounty` (default 100, 0 turns it off).

## 1.0.0

- First release. Cats are hiding in the facility, waiting to be rescued.
