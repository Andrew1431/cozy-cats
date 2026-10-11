# Changelog

## 2.1.1

- Your named cats (`SpecialCats`, Strider and Peaches by default) are much easier to find: each one not already on
  your ship has a 50% chance of hiding in the facility every landing, on top of the usual cats. Miss it and it tries
  again next landing. New setting `[Cats] SpecialCatChance`.
- There's only ever one of each named cat, and ordinary cats never get their names. Lose one from the ship and it
  comes back into the running.

## 2.1.0

**New: the cat tree!** Buy it from the terminal for $50 (`buy cat tree`, it's always in stock).

- A big carpeted cat tree with a cubby to hide in, a hammock, a ledge and a lookout on top.
- Move it with [B] like any other furniture, or pack it away with [X].
- Cats that live on the ship wander over now and then and nap in it: curled up in the hammock or the top perch,
  loafing in the cubby, sitting on the condo, or flopped across the ledge. Pick one up for a cuddle any time.
- Cats now potter about the ship on their own, even in orbit, instead of staying wherever they were put down.
- New nap poses: curled up asleep, and splayed out lounging.

## 2.0.1

- Cats vary a lot more in size, from little ones to proper chonks. Existing cats keep their place in the range.

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
