/// The value types and operations a kernel is written with. Each operation is ordinary .NET code
/// computing what the DSP56300 computes (word widths, sign extension, the limiter on moves out of
/// an accumulator), so a kernel runs as a .NET function, and the kernel compiler reads the
/// `Intrinsic` name on each member to pick the instructions. The two meanings are checked
/// against each other on the emulator (tests/dsp56k.tests/KernelTests.fs).
///
/// Storage types are Frac (one word) and Long (two words, a1:a0). Acc is the 56-bit accumulator:
/// a value in flight, kept exact (with its extension byte) when the compiler spills it.
namespace dsp56k.kernel

open System

/// The compiler's name for an operation. Constant-only operations (`Const`) are evaluated when
/// the kernel is compiled and must have constant arguments.
[<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Property, AllowMultiple = false)>]
type IntrinsicAttribute(name: string) =
  inherit Attribute()
  member _.Name = name

[<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Property, AllowMultiple = false)>]
type ConstAttribute() =
  inherit Attribute()

module Bits =
  let sext24 (v: int) = (v <<< 8) >>> 8
  let sext48 (v: int64) = (v <<< 16) >>> 16
  let sext56 (v: int64) = (v <<< 8) >>> 8
  let word (v: int) = v &&& 0xffffff

/// A 24-bit word read as a signed fraction (s.23): $400000 = 0.5, $800000 = -1.
[<Struct>]
type Frac =
  { Raw : int }      // sign-extended

  /// The word, 0..$ffffff.
  member f.Word = Bits.word f.Raw

  [<Const>]
  static member ofRaw (w: int) = { Frac.Raw = Bits.sext24 (Bits.word w) }

  /// The nearest fraction, limited to [-1, 1 - 2^-23].
  [<Const>]
  static member q23 (v: float) =
    let r = Math.Round(v * 8388608.0, MidpointRounding.ToEven)
    { Frac.Raw = int (max -8388608.0 (min 8388607.0 r)) }

  [<Const>]
  static member zero = { Frac.Raw = 0 }

  /// $7fffff, the largest fraction.
  [<Const>]
  static member one = { Frac.Raw = 0x7fffff }

  /// Frac * Frac: `mpy`, the 48-bit fractional product in an accumulator (-1 * -1 is +1, held
  /// in the extension).
  [<Intrinsic "frac.mul">]
  static member (*) (x: Frac, y: Frac) : Acc = { Acc.Raw = (int64 x.Raw * int64 y.Raw) <<< 1 }

/// The 56-bit accumulator a2:a1:a0, as a signed integer of 56 bits.
and [<Struct>] Acc =
  { Raw : int64 }    // sign-extended

  [<Const>]
  static member ofRaw (v: int64) = { Acc.Raw = Bits.sext56 v }

  [<Const>]
  static member zero = { Acc.Raw = 0L }

  /// `move f,a`: the word into a1, a2 its sign, a0 cleared.
  [<Intrinsic "acc.ofFrac">]
  static member ofFrac (f: Frac) = { Acc.Raw = int64 f.Raw <<< 24 }

  /// `move hi,a` `move lo,a0`.
  [<Intrinsic "acc.ofLong">]
  static member ofLong (l: Long) = { Acc.Raw = l.Raw }

  /// a1 as it is (`move a1,d`): no limiting.
  [<Intrinsic "acc.hi">]
  static member hi (a: Acc) = Frac.ofRaw (int (a.Raw >>> 24))

  /// a0 (`move a0,d`).
  [<Intrinsic "acc.lo">]
  static member lo (a: Acc) = Frac.ofRaw (int a.Raw)

  /// `move a,d`: a1, or the nearest limit when the value needs the extension byte.
  [<Intrinsic "acc.sat">]
  static member sat (a: Acc) =
    if a.Raw > 0x7fffffffffffL then Frac.one
    elif a.Raw < -0x800000000000L then { Frac.Raw = -0x800000 }
    else Acc.hi a

  [<Intrinsic "acc.add">]
  static member (+) (a: Acc, b: Acc) = { Acc.Raw = Bits.sext56 (a.Raw + b.Raw) }

  /// `add x0,a`: the word aligned with a1.
  [<Intrinsic "acc.addFrac">]
  static member (+) (a: Acc, f: Frac) = { Acc.Raw = Bits.sext56 (a.Raw + (int64 f.Raw <<< 24)) }

  [<Intrinsic "acc.sub">]
  static member (-) (a: Acc, b: Acc) = { Acc.Raw = Bits.sext56 (a.Raw - b.Raw) }

  [<Intrinsic "acc.subFrac">]
  static member (-) (a: Acc, f: Frac) = { Acc.Raw = Bits.sext56 (a.Raw - (int64 f.Raw <<< 24)) }

  [<Intrinsic "acc.neg">]
  static member (~-) (a: Acc) = { Acc.Raw = Bits.sext56 (-a.Raw) }

  /// `asr #n,a,a`, n constant.
  [<Intrinsic "acc.asr">]
  static member (>>>) (a: Acc, n: int) = { Acc.Raw = a.Raw >>> n }

  /// `asl #n,a,a`, n constant.
  [<Intrinsic "acc.asl">]
  static member (<<<) (a: Acc, n: int) = { Acc.Raw = Bits.sext56 (a.Raw <<< n) }

  /// `and #mask,a`: a1 masked, a2 and a0 kept.
  [<Intrinsic "acc.andHi">]
  static member andHi (mask: int) (a: Acc) =
    let u = a.Raw &&& 0xffffffffffffffL
    let a1 = (u >>> 24) &&& (int64 (Bits.word mask))
    { Acc.Raw = Bits.sext56 ((u &&& ~~~(0xffffffL <<< 24)) ||| (a1 <<< 24)) }

  /// acc + x * y: `mac`.
  [<Intrinsic "acc.mac">]
  static member mac (acc: Acc) (x: Frac) (y: Frac) = acc + x * y

/// Two words a1:a0 (48 bits): what a workset field holds of an accumulator.
and [<Struct>] Long =
  { Raw : int64 }    // sign-extended

  [<Const>]
  static member zero = { Long.Raw = 0L }

  [<Const>]
  static member ofRaw (v: int64) = { Long.Raw = Bits.sext48 v }

  /// `move a1,hi` `move a0,lo`: the extension byte is dropped.
  [<Intrinsic "long.ofAcc">]
  static member ofAcc (a: Acc) = { Long.Raw = Bits.sext48 a.Raw }

/// Where a table's words are on the DSP.
type TableSource =
  /// A firmware resource, read through a loader-patched address (MDS import kind 1).
  | Resource of abiSymbol: int

/// A read-only table in X memory. The words are what the .NET side reads; for a resource they
/// are a stand-in that the harness loads in the firmware's place.
type Table =
  { name   : string
    source : TableSource
    words  : int array }

  /// `move x:(rn+table),d` with the index in rn.
  [<Intrinsic "table.read">]
  static member read (t: Table) (index: Frac) = Frac.ofRaw t.words[index.Raw]

module Rom =
  /// The 32,768-word sine resource (MDS symbol 1). The firmware's contents are not published;
  /// this is a Q23 sine of the same size for the harness.
  let sine =
    { name = "sine"
      source = Resource 1
      words = Array.init 32768 (fun i -> (Frac.q23 (Math.Sin(2.0 * Math.PI * float i / 32768.0))).Word) }

/// The block being rendered: one word per frame.
[<AllowNullLiteral>]
type Output() =
  let words = ResizeArray<int>()

  /// `move d,y:(r7)+`.
  [<Intrinsic "out.write">]
  member _.Write(f: Frac) = words.Add f.Word

  member _.Words = words.ToArray()
  member _.Clear() = words.Clear()
