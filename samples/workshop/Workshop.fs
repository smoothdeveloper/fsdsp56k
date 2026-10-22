/// A workshop: a kernel ported to hand assembly one helper at a time. `kernel` is the whole voice
/// in F#; `hand` holds DSP56300 routines for two of its RENDER helpers. Each helper takes its
/// routine when its symbol is defined, and keeps its F# body as the reference the grade checks the
/// routine against:
///
///   fsdsp56k grade samples/workshop/Workshop.fs                                       all F#
///   fsdsp56k grade samples/workshop/Workshop.fs --define HAND_SHAPE                   one by hand
///   fsdsp56k grade samples/workshop/Workshop.fs --define HAND_SHAPE --define HAND_FOLLOW
namespace workshop

open dsp56k.dsl.Asm

/// The hand-written routines, in the [<Subroutine>] convention: Frac arguments in x0, y0, x1, y1
/// in order, Acc arguments in a, b; the result in x0 (Frac) or a (Acc).
module hand =

  /// shape x drive: x0 = x, y0 = drive; the result in x0.
  let shape = [|
    mpy x0 y0 a                 // x * drive
    asli 2 a a                  // <<< 2
    move a x0                   // Acc.sat: the limiter
    mpy x0 x0 a  ++ move x0 b   // d * d; b = d
    move a1 y0                  // Acc.hi, not `move a,y0`: (-1)^2 must wrap as the F# does
    mpy x0 y0 a                 // d^3
    asr a                       // d^3 / 2
    sub a b      ++ move x0 a   // b = d - d^3 / 2; a = d
    asr a                       // d / 2
    add a b                     // 1.5 d - d^3 / 2
    move b x0                   // Acc.sat
    rts |]

  /// follow level target rate: a = level, x0 = target, y0 = rate; the result in a.
  let follow = [|
    move x0 b                   // b = target
    sub a b                     // target - level
    move b1 x1                  // Acc.hi
    mac x1 y0 a                 // level + (target - level) * rate
    rts |]

/// The voice: a saw through a cubic soft clip, under an envelope that falls from full to the
/// sustain level at the decay rate.
[<ReflectedDefinition>]
module kernel =

  open dsp56k.kernel

  type Params =
    { pitch   : Frac      // phase step per frame, / 16
      drive   : Frac      // gain into the clip, x 4
      decay   : Frac      // envelope rate, / 64
      sustain : Frac }    // envelope level held after the decay

  type State =
    { mutable phase : Frac
      mutable level : Frac }

  /// The cubic soft clip of x x drive x 4.
#if HAND_SHAPE
  [<HandWritten "workshop.hand.shape">]
#endif
  let shape (x: Frac) (drive: Frac) =
    let d  = Acc.sat ((x * drive) <<< 2)
    let d3 = Acc.hi (d * d) * d
    Acc.sat (Acc.ofFrac d + (Acc.ofFrac d >>> 1) - (d3 >>> 1))

  /// One step of a one-pole glide from level to target.
#if HAND_FOLLOW
  [<HandWritten "workshop.hand.follow">]
#endif
  let follow (level: Acc) (target: Frac) (rate: Frac) =
    Acc.mac level (Acc.hi (Acc.ofFrac target - level)) rate

  let init (_: Params) (s: State) =
    s.phase <- Frac.zero
    s.level <- Frac.zero

  let trig (_: Params) (s: State) =
    s.level <- Frac.one

  let render (p: Params) (s: State) (out: Output) =
    let step  = Acc.ofFrac p.pitch >>> 4
    let rate  = Frac.lsr 6 p.decay
    let mutable phase = Acc.ofFrac s.phase
    let mutable level = Acc.ofFrac s.level
    for _ in 0 .. 31 do
      level <- follow level p.sustain rate
      out.Write(Acc.hi (shape (Acc.hi phase) p.drive * Acc.hi level))
      phase <- phase + step
    s.phase <- Acc.hi phase
    s.level <- Acc.hi level

  let voice = Kernel.define "workshop" <@ init @> <@ trig @> <@ render @>
