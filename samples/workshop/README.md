# workshop: a kernel ported to hand assembly one helper at a time

`Workshop.fs` is a small voice (a saw through a cubic soft clip, under a decaying envelope) written
in F#, with DSP56300 routines for two of its RENDER helpers. A helper takes its routine when its
symbol is defined; its F# body stays as the reference the routine is graded against.

```fsharp
#if HAND_SHAPE
  [<HandWritten "workshop.hand.shape">]
#endif
  let shape (x: Frac) (drive: Frac) =
    let d  = Acc.sat ((x * drive) <<< 2)
    let d3 = Acc.hi (d * d) * d
    Acc.sat (Acc.ofFrac d + (Acc.ofFrac d >>> 1) - (d3 >>> 1))
```

The three stages, each graded word for word against the F# run as .NET code:

```sh
fsdsp56k grade samples/workshop/Workshop.fs
fsdsp56k grade samples/workshop/Workshop.fs --define HAND_SHAPE
fsdsp56k grade samples/workshop/Workshop.fs --define HAND_SHAPE --define HAND_FOLLOW
```

| Stage | RENDER cycles | program words | `shape` on its own | `follow` on its own |
|---|---|---|---|---|
| all F# | 2460 | 81 | | |
| `HAND_SHAPE` | 2140 (-320) | 74 | 1024/1024 calls match, 26 -> 11 cycles | |
| `HAND_SHAPE`, `HAND_FOLLOW` | 2076 (-384) | 73 | 1024/1024, 26 -> 11 | 1024/1024, 21 -> 4 |

The tests (`tests/dsp56k.tests/HandTests.fs`) run the three stages and hold these lines.
