/// The value types and operations a kernel is written with. Each operation is ordinary .NET code
/// computing what the DSP56300 computes (word widths, sign extension, the limiter on moves out of
/// an accumulator), so a kernel runs as a .NET function, and the kernel compiler reads the
/// `Intrinsic` name on each member to pick the instructions. The two meanings are checked
/// against each other on the emulator (tests/dsp56k.tests/KernelTests.fs).
///
/// Storage types are Frac (one word) and Long (two words, a1:a0). Acc is the 56-bit accumulator:
/// a value in flight, kept exact (with its extension byte) when the compiler spills it. State
/// fields live in Y unless marked [<XMemory>].
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

/// The number of words of a `Frac array` state field: `[<Length 4>] mutable history : Frac array`.
/// Elements are read and written with constant indices, the loop's index (`s.h[i + 1]`), or a
/// word computed when running (`s.h[Frac.index k]`, `s.h[Frac.index k + 32]`).
[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field, AllowMultiple = false)>]
type LengthAttribute(words: int) =
  inherit Attribute()
  member _.Words = words

/// A state field kept in X memory instead of Y: `[<XMemory>] mutable phase : Frac`,
/// `[<XMemory; Length 32>] mutable residuals : Frac array`. X state words are X:(r6+0) on, in
/// the order declared; the compiler's locals and spills follow them in the X workset.
[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field, AllowMultiple = false)>]
type XMemoryAttribute() =
  inherit Attribute()

/// A helper compiled once, as a subroutine its callers reach with bsr (rts back), instead of
/// inlined at every call: `[<Subroutine>] let refresh (p: P) (s: S) (x: Frac) = ...`. It takes
/// the parameter and state records (and the output) as the caller has them, up to four Frac
/// values (passed in X0, Y0, X1, Y1) and two Acc or Long values (A, B); it gives its result in
/// X0 (Frac) or A (Acc, Long). Each call costs the argument moves, bsr and rts (and saving the
/// caller's X1 or Y1 locals); a call takes one level of the system stack, a DO loop two.
[<AttributeUsage(AttributeTargets.Method, AllowMultiple = false)>]
type SubroutineAttribute() =
  inherit Attribute()

/// Loops in this function (or every function of this module) are not unswitched: a condition
/// the loop does not change is tested on each pass rather than splitting the loop into a copy
/// for each outcome (smaller code, a test and a branch more per pass).
[<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Class, AllowMultiple = false)>]
type NoUnswitchAttribute() =
  inherit Attribute()

/// Loops with constant bounds in this function (or every function of this module) are written
/// out: the body once for each pass, the index a constant in each copy. No DO loop, so no system
/// stack levels (a DO takes two) and no address register stepped, for a copy of the body a pass.
/// The function's own loops only: a helper it inlines keeps its DO loops unless marked too.
[<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Class, AllowMultiple = false)>]
type UnrollAttribute() =
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

  /// The word as a signed integer, to index a state array with a value computed when running:
  /// `s.residuals[Frac.index k]`, `s.residuals[Frac.index k + 32]` (`move k,n0` and an
  /// `(r0+n0)` access from the array's first word). The address is the array's plus the index,
  /// as the DSP adds them (linear, no wrap): an index outside the array fails on .NET and
  /// touches whatever is there on the DSP. Only an array index takes it.
  [<Intrinsic "frac.index">]
  static member index (f: Frac) = f.Raw

  /// One step of a 24-bit shift register: shifted left, the taps XORed in when the bit shifted
  /// out was set (`lsl a` `bcc` `eor #taps,a`).
  [<Intrinsic "frac.lfsr">]
  static member lfsr (taps: int) (state: Frac) =
    let w = state.Word
    let shifted = (w <<< 1) &&& 0xffffff
    Frac.ofRaw (if w &&& 0x800000 <> 0 then shifted ^^^ Bits.word taps else shifted)

  /// The word shifted right, zeros in from the top (logical): `lsr #n,a` on the word in a1. n is
  /// a constant, 0 to 31 (24 on gives 0).
  [<Intrinsic "frac.lsr">]
  static member lsr (n: int) (f: Frac) = Frac.ofRaw (f.Word >>> n)

  /// The word shifted right (logical) by a count computed when running: `lsr s,a`, the count the
  /// low six bits of s (0 to 63; 24 on gives 0).
  [<Intrinsic "frac.lsrBy">]
  static member lsrBy (s: Frac) (f: Frac) =
    let n = s.Raw &&& 0x3f
    Frac.ofRaw (if n >= 24 then 0 else f.Word >>> n)

  /// Whether bit n (a constant, 0 to 23) of the word is set. A condition only: `if Frac.bit 23 x
  /// then ...` (or `not (Frac.bit n x)`) is `btst #n,d` and a branch on the carry.
  [<Intrinsic "frac.bit">]
  static member bit (n: int) (f: Frac) = (f.Word >>> n) &&& 1 = 1

  /// 1 (the smallest word, $000001) when b holds, else 0. What a `let b = condition` keeps:
  /// its uses read the word back as `w <> Frac.zero`.
  [<Intrinsic "frac.ofBool">]
  static member ofBool (b: bool) = if b then Frac.ofRaw 1 else Frac.zero

  /// Frac * Frac: `mpy`, the 48-bit fractional product in an accumulator (-1 * -1 is +1, held
  /// in the extension).
  [<Intrinsic "frac.mul">]
  static member (*) (x: Frac, y: Frac) : Acc = { Acc.Raw = (int64 x.Raw * int64 y.Raw) <<< 1 }

  /// `mpysu s,u,a`: s signed times u read unsigned (u + 2^24 when its top bit is set).
  [<Intrinsic "frac.mulsu">]
  static member mulSU (s: Frac) (u: Frac) : Acc = { Acc.Raw = Bits.sext56 ((int64 s.Raw * (int64 u.Raw &&& 0xffffffL)) <<< 1) }

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

  /// `lsr #n,a`: a1 shifted right, zeros in from bit 47 (logical); a2 and a0 as they were. n is
  /// a constant, 0 to 31 (24 on clears a1).
  [<Intrinsic "acc.lsr">]
  static member lsr (n: int) (a: Acc) = Acc.withHi a (((a.Raw >>> 24) &&& 0xffffffL) >>> n)

  /// `lsr s,a`: as `Acc.lsr`, by a count computed when running, the low six bits of s (0 to
  /// 63; 24 on clears a1).
  [<Intrinsic "acc.lsrBy">]
  static member lsrBy (s: Frac) (a: Acc) =
    let n = s.Raw &&& 0x3f
    Acc.withHi a (if n >= 24 then 0L else ((a.Raw >>> 24) &&& 0xffffffL) >>> n)

  /// `asr s,a,a`: a shifted right (arithmetic) by a count computed when running, the low six
  /// bits of s (0 to 63).
  [<Intrinsic "acc.asrBy">]
  static member asrBy (s: Frac) (a: Acc) = { Acc.Raw = a.Raw >>> (s.Raw &&& 0x3f) }

  /// `asl s,a,a`: a shifted left by a count computed when running, the low six bits of s (0 to
  /// 63); bits shifted past bit 55 are lost.
  [<Intrinsic "acc.aslBy">]
  static member aslBy (s: Frac) (a: Acc) = { Acc.Raw = Bits.sext56 (a.Raw <<< (s.Raw &&& 0x3f)) }

  /// a with a1 replaced by the low 24 bits of `hi`, a2 and a0 kept.
  static member private withHi (a: Acc) (hi: int64) =
    let u = a.Raw &&& 0xffffffffffffffL
    { Acc.Raw = Bits.sext56 ((u &&& ~~~(0xffffffL <<< 24)) ||| ((hi &&& 0xffffffL) <<< 24)) }

  /// `and #mask,a`: a1 masked, a2 and a0 kept.
  [<Intrinsic "acc.andHi">]
  static member andHi (mask: int) (a: Acc) =
    let u = a.Raw &&& 0xffffffffffffffL
    let a1 = (u >>> 24) &&& (int64 (Bits.word mask))
    { Acc.Raw = Bits.sext56 ((u &&& ~~~(0xffffffL <<< 24)) ||| (a1 <<< 24)) }

  /// acc + x * y: `mac`. (`acc + x * y` and `acc - x * y` compile to MAC too.)
  [<Intrinsic "acc.mac">]
  static member mac (acc: Acc) (x: Frac) (y: Frac) = acc + x * y

  /// `macsu s,u,a`: the accumulator plus s signed times u read unsigned.
  [<Intrinsic "acc.macsu">]
  static member macSU (acc: Acc) (s: Frac) (u: Frac) = { Acc.Raw = Bits.sext56 (acc.Raw + ((int64 s.Raw * (int64 u.Raw &&& 0xffffffL)) <<< 1)) }

  /// `dmacss x,y,a`: the accumulator shifted right 24 bits (arithmetic), plus x * y. With
  /// `Frac.mulSU` first, the product of a word and a 48-bit value hi:lo in two instructions.
  [<Intrinsic "acc.dmac">]
  static member dmac (acc: Acc) (x: Frac) (y: Frac) = { Acc.Raw = Bits.sext56 ((acc.Raw >>> 24) + ((int64 x.Raw * int64 y.Raw) <<< 1)) }

  /// `asl a` then `rep #24` `div s,a`: non-restoring division, the quotient building up in a0
  /// (as the DSP computes it: each step shifts the carry in, then adds or subtracts the divisor
  /// by the signs of a and s).
  [<Intrinsic "acc.divide">]
  static member divide (a: Acc) (s: Frac) =
    let mask = (1L <<< 56) - 1L
    let mutable d = a.Raw &&& mask
    let mutable c = (d >>> 55) &&& 1L
    d <- (d <<< 1) &&& mask
    let sv = int64 s.Raw <<< 24
    let s23 = if s.Raw < 0 then 1L else 0L
    for _ in 1 .. 24 do
      let d55 = (d >>> 55) &&& 1L
      let shifted = ((d <<< 1) ||| c) &&& mask
      d <- (if d55 ^^^ s23 = 1L then shifted + sv else shifted - sv) &&& mask
      c <- 1L - ((d >>> 55) &&& 1L)
    { Acc.Raw = Bits.sext56 d }

  /// `rep #24` `div s,a` with no `asl` first and the carry `carry` (a constant, 0 or 1) coming in:
  /// `andi #$fe,ccr` (0) or `ori #$01,ccr` (1) before the REP. The same 24 steps as `divide`.
  [<Intrinsic "acc.divideCarry">]
  static member divideCarry (carry: int) (a: Acc) (s: Frac) =
    let mask = (1L <<< 56) - 1L
    let mutable d = a.Raw &&& mask
    let mutable c = int64 (carry &&& 1)
    let sv = int64 s.Raw <<< 24
    let s23 = if s.Raw < 0 then 1L else 0L
    for _ in 1 .. 24 do
      let d55 = (d >>> 55) &&& 1L
      let shifted = ((d <<< 1) ||| c) &&& mask
      d <- (if d55 ^^^ s23 = 1L then shifted + sv else shifted - sv) &&& mask
      c <- 1L - ((d >>> 55) &&& 1L)
    { Acc.Raw = Bits.sext56 d }

  /// `clb a,a`: the count NORMF takes to normalise a, in a1 (a2 its sign, a0 zero): 9 less the
  /// number of leading bits equal to the sign (bit 55 on), 0 when a is 0. `Acc.hi (Acc.clb a)`
  /// is the count as a word: 0 for a normalised a (a1 in [0.5, 1) or [-1, -0.5)), negative by
  /// the left shift that normalises it, positive when a uses its extension; -47 for all ones.
  [<Intrinsic "acc.clb">]
  static member clb (a: Acc) =
    let v = if a.Raw < 0L then ~~~a.Raw else a.Raw     // the sign bits as leading zeros
    let count = 56 - (64 - System.Numerics.BitOperations.LeadingZeroCount(uint64 v))
    { Acc.Raw = (if a.Raw = 0L then 0L else int64 (9 - count)) <<< 24 }

  /// `normf s,a`: a shifted right (arithmetic) by s when s is positive, left by -s when it is
  /// negative, by the count's low 6 bits (|s| mod 64); bits shifted past bit 55 are lost.
  /// `Acc.normf (Acc.hi (Acc.clb a)) a` normalises a (`clb a,b` `normf b1,a`).
  [<Intrinsic "acc.normf">]
  static member normf (s: Frac) (a: Acc) =
    let n = abs s.Raw &&& 0x3f
    if s.Raw < 0 then { Acc.Raw = Bits.sext56 (a.Raw <<< n) } else { Acc.Raw = a.Raw >>> n }

  /// `rnd a`: convergent rounding into a1 (to even on a tie), a0 cleared. `Acc.rnd (x * y)` is
  /// MPYR and `Acc.rnd (acc ± x * y)` MACR.
  [<Intrinsic "acc.rnd">]
  static member rnd (a: Acc) =
    let low = a.Raw &&& 0xffffffL
    let down = a.Raw - low
    let up = down + 0x1000000L
    let r =
      if low > 0x800000L then up
      elif low < 0x800000L then down
      elif (down >>> 24) &&& 1L = 1L then up
      else down
    { Acc.Raw = Bits.sext56 r }

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

  /// `move a,l:`: a1:a0, or the nearest 48-bit limit ($7fffff:ffffff, $800000:000000) when
  /// the value needs the extension byte (`tst a` and a branch on the E bit).
  [<Intrinsic "long.ofAccSat">]
  static member ofAccSat (a: Acc) =
    if a.Raw > 0x7fffffffffffL then { Long.Raw = 0x7fffffffffffL }
    elif a.Raw < -0x800000000000L then { Long.Raw = -0x800000000000L }
    else { Long.Raw = a.Raw }

/// Where a table's words are on the DSP.
type TableSource =
  /// A firmware resource in X memory, read through a loader-patched address (MDS import kind 1).
  | Resource of abiSymbol: int
  /// Words emitted after the machine's code, read from P memory.
  | Program

/// A read-only table. The words are what the .NET side reads; for a resource they are a stand-in
/// the harness loads at `address` in the firmware's place.
type Table =
  { name    : string
    source  : TableSource
    address : int
    words   : int array }

  /// `move x:(rn+table),d` (a resource) or `move p:(rn),d` (program words), the index in rn.
  [<Intrinsic "table.read">]
  static member read (t: Table) (index: Frac) = Frac.ofRaw t.words[index.Raw]

  /// Word `col` of row `row` in a table of rows `width` words wide.
  [<Intrinsic "table.row">]
  static member row (t: Table) (width: int) (row: Frac) (col: int) = Frac.ofRaw t.words[row.Raw * width + col]

  /// A table placed in the program.
  static member program name (words: int array) = { name = name; source = Program; address = 0; words = Array.map Bits.word words }

module Rom =
  /// The 32,768-word sine resource (MDS symbol 1). The firmware's contents are not published;
  /// this is a Q23 sine of the same size for the harness.
  let sine =
    { name = "sine"
      source = Resource 1
      address = 0x8000
      words = Array.init 32768 (fun i -> (Frac.q23 (Math.Sin(2.0 * Math.PI * float i / 32768.0))).Word) }

type Bank =
  | XMemory
  | YMemory

/// Memory a cursor walks through: firmware memory whose address the loader patches in (MDS
/// import `abiSymbol`). `address` is where the harness places it; `words` its contents for the
/// .NET side (the harness loads the same).
type Region =
  { name      : string
    bank      : Bank
    abiSymbol : int
    address   : int
    words     : int array }

  /// The region's first address (`move #>region,d`).
  [<Intrinsic "region.address">]
  static member addressOf (r: Region) = Frac.ofRaw r.address

  /// Word `index` of the region, the index computed when running: `move x:(r0+n0),d` with r0
  /// the region (loader-patched) and n0 the index. Linear, as Frac.index: an index outside the
  /// region fails on .NET.
  [<Intrinsic "region.read">]
  static member read (r: Region) (index: Frac) = Frac.ofRaw r.words[index.Raw]

  /// Word `index` of the region set to `value`: `move s,x:(r0+n0)`.
  [<Intrinsic "region.write">]
  static member write (r: Region) (index: Frac) (value: Frac) = r.words[index.Raw] <- value.Word

/// A firmware word at a loader-patched address (`move x:>cell,d`); `value` is what the harness
/// puts there.
type Cell =
  { name      : string
    abiSymbol : int
    address   : int
    mutable value : int }

  [<Intrinsic "cell.read">]
  static member read (c: Cell) = Frac.ofRaw c.value

/// An address register walking a region or a [<Length n>] state array: `(rn)+`, `(rn)+nn`,
/// `(rn)-`, with modulo addressing for a ring. Bind it with `let` (one register for the binding's
/// scope).
[<AllowNullLiteral>]
type Cursor(read: int -> int, write: int -> int -> unit, address: int, start: int, step: int, modulo: int) =
  let mutable i = start
  // modulo addressing with M = modulo (0: linear): the ring is the M + 1 words from the multiple
  // of the next power of two at or below the address
  let advance by =
    let next = i + by
    if modulo = 0 then i <- next
    else
      let mutable size = 1
      while size <= modulo do size <- size * 2
      let lower = (address + i) / size * size - address
      i <- if next > lower + modulo then next - (modulo + 1) elif next < lower then next + (modulo + 1) else next

  new(region: Region, start: int, step: int, modulo: int) =
    Cursor((fun k -> region.words[k]), (fun k w -> region.words[k] <- w), region.address, start, step, modulo)

  /// Read and move on by the step: `move x:(rn)+,d` (step 1) or `move x:(rn)+nn,d`.
  [<Intrinsic "cursor.next">]
  member _.Next() =
    let v = read i
    advance step
    Frac.ofRaw v

  /// Read in place: `move x:(rn),d`.
  [<Intrinsic "cursor.peek">]
  member _.Peek() = Frac.ofRaw (read i)

  /// Write and move on by the step: `move s,x:(rn)+` or `move s,x:(rn)+nn`.
  [<Intrinsic "cursor.put">]
  member _.Put(f: Frac) =
    write i f.Word
    advance step

  /// Read and move up one word, whatever the step: `move x:(rn)+,d`.
  [<Intrinsic "cursor.up">]
  member _.Up() =
    let v = read i
    advance 1
    Frac.ofRaw v

  /// Read and move down one word: `move x:(rn)-,d`.
  [<Intrinsic "cursor.down">]
  member _.Down() =
    let v = read i
    advance -1
    Frac.ofRaw v

  /// Write and move up one word: `move s,x:(rn)+`.
  [<Intrinsic "cursor.putUp">]
  member _.PutUp(f: Frac) =
    write i f.Word
    advance 1

  /// Write and move down one word: `move s,x:(rn)-`.
  [<Intrinsic "cursor.putDown">]
  member _.PutDown(f: Frac) =
    write i f.Word
    advance -1

  /// Where the cursor is, as an offset in the region (or the array).
  [<Intrinsic "cursor.position">]
  member _.Position() = Frac.ofRaw i

  /// Linear addressing from word `start` of the region, one word a step.
  [<Intrinsic "cursor.linear">]
  static member linear (r: Region) (start: Frac) = Cursor(r, start.Raw, 1, 0)

  /// Modulo addressing over the region (its size a power of two) from `start`, `step` words a step.
  [<Intrinsic "cursor.ring">]
  static member ring (r: Region) (start: Frac) (step: int) = Cursor(r, start.Raw, step, r.words.Length - 1)

  /// Modulo addressing with a modulus known when running: the ring is `last + 1` words (`last`
  /// is what goes in the M register), one word a step.
  [<Intrinsic "cursor.modulo">]
  static member modulo (r: Region) (start: Frac) (last: Frac) = Cursor(r, start.Raw, 1, last.Raw)

  /// Linear addressing over a [<Length n>] state array (`Cursor.linearIn s.h k`) from word
  /// `start`, `step` words a step: `lua (r6+field+start),rn`. Leaving the array fails on .NET.
  [<Intrinsic "cursor.linearIn">]
  static member linearIn (a: Frac array) (start: Frac) (step: int) =
    Cursor((fun k -> a[k].Raw), (fun k w -> a[k] <- Frac.ofRaw w), 0, start.Raw, step, 0)

  /// Modulo addressing over a [<Length n>] state array, as TX8-CY's six-word Y ring (`lua
  /// (r6+$10),r4`, `movec #5,m4`, `move #3,n4`): M = n - 1, so the cursor wraps within the n
  /// words whichever way it moves. The array must start on a multiple of the power of two at or
  /// above n in the workset (the compiler checks), as the DSP's ring is that aligned block.
  [<Intrinsic "cursor.ringIn">]
  static member ringIn (a: Frac array) (start: Frac) (step: int) =
    Cursor((fun k -> a[k].Raw), (fun k w -> a[k] <- Frac.ofRaw w), 0, start.Raw, step, a.Length - 1)

/// The shared temporary memory (MDS imports 7 and 8): 32 words each, not kept between calls.
/// Loop controls.
type Loop =
  /// The condition itself, tested on every pass of the loop around it: not unswitched
  /// (`if Loop.everyPass (p.mode > half) then ...`).
  [<Intrinsic "loop.everyPass">]
  static member everyPass (c: bool) = c

module Scratch =
  let x = { name = "scratch_x"; bank = XMemory; abiSymbol = 7; address = 0x400; words = Array.zeroCreate 32 }
  let y = { name = "scratch_y"; bank = YMemory; abiSymbol = 8; address = 0x400; words = Array.zeroCreate 32 }

/// The audio inputs: a 256-word ring of pairs (input A, input B), and the word holding where the
/// input is now (MDS imports 2 and 3). The harness fills the ring with a test signal.
module Input =
  let ring =
    { name = "input"; bank = XMemory; abiSymbol = 2; address = 0x500
      words = Array.init 256 (fun i ->
        let frame = float (i / 2)
        let v = if i % 2 = 0 then 0.6 * Math.Sin(2.0 * Math.PI * frame / 37.0) else 0.4 * Math.Sin(2.0 * Math.PI * frame / 11.0) + 0.2
        (Frac.q23 v).Word) }
  let position = { name = "input_position"; abiSymbol = 3; address = 0x600; value = 0x500 + 0x80 }

/// The main output: the last four 64-word blocks, pairs (MDS imports 4 and 5), and the word
/// holding the address of the block to read now.
module Main =
  let ring =
    { name = "main"; bank = XMemory; abiSymbol = 4; address = 0x900
      words = Array.init 256 (fun i ->
        let frame = float (i / 2)
        let v = if i % 2 = 0 then 0.7 * Math.Sin(2.0 * Math.PI * frame / 29.0) else -0.5 * Math.Sin(2.0 * Math.PI * frame / 13.0)
        (Frac.q23 v).Word) }
  let position = { name = "main_position"; abiSymbol = 5; address = 0x601; value = 0x900 + 0xc0 }

/// The track the machine plays on, and the tracks' external memory (MDS import 6: 1,536 words a
/// track, X and Y the same storage; the harness gives it Y memory).
type Track =
  /// The track number the firmware passes in R1, 0..15 (`current` on the .NET side).
  [<Intrinsic "track.number">]
  static member number () = Frac.ofRaw Track.current
  static member val current = 0 with get, set
  static member val memory =
    { name = "track_memory"; bank = YMemory; abiSymbol = 6; address = 0x1000; words = Array.zeroCreate (1536 * 16) }

/// The block being rendered: one word per frame, or a left, right pair per frame on a stereo
/// target (Kernel.Target.channels).
[<AllowNullLiteral>]
type Output() =
  let words = ResizeArray<int>()

  /// `move d,y:(r7)+`.
  [<Intrinsic "out.write">]
  member _.Write(f: Frac) = words.Add f.Word

  /// One stereo frame: `move l,y:(r7)+` `move r,y:(r7)+`.
  [<Intrinsic "out.pair">]
  member _.WritePair(left: Frac, right: Frac) =
    words.Add left.Word
    words.Add right.Word

  member _.Words = words.ToArray()
  member _.Clear() = words.Clear()
