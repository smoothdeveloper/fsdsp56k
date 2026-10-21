namespace dsp56k.kernel

#nowarn "42" // the inline IL of `plain`: a retyping, no instruction

/// What a kernel's parameter word stands for, as a unit of measure on its type: `Frac<Hz>` is a Frac
/// whose word encodes a frequency (how it encodes it stays in the field's comment, as a formula).
/// Measures are erased when compiled: the code, the assembly and the .NET runs are those of `Frac`.
/// The unit is read at build time from the compiler's typed tree, into the kernel's input ports.
[<MeasureAnnotatedAbbreviation>]
type Frac<[<Measure>] 'u> = Frac

/// The units a parameter can carry.
module Units =
  /// A frequency.
  [<Measure>] type Hz
  /// A duration.
  [<Measure>] type ms
  /// A level on a decibel scale.
  [<Measure>] type dB
  /// A pitch interval.
  [<Measure>] type semitone
  /// A plain proportion: a gain, a mix, a feedback amount.
  [<Measure>] type ratio

  /// The Frac under a unit, to compute with: no instruction, the compilers read `plain x` as `x`.
  [<Intrinsic "unit.plain">]
  let inline plain (x: Frac<'u>) : Frac = (# "" x : Frac #)
