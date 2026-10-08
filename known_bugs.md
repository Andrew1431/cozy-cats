# Known bugs

- **No drop sound.** Dropping a cat should play its per-cat drop clip (borrowed from the Soccer ball) without alerting
  monsters (`CatItem.PlayDropSFX` override), but nothing is heard in game as of 1.0.0. Check whether `dropClip` is
  set when the override runs and whether the root AudioSource's mixer group/volume is valid.
