/// Named words at fixed offsets from a base register: a record in memory (a voice's state, a
/// grain), addressed as (rn+offset). The fields can be emitted as `equ`s so listings keep the
/// names, and referenced by name or by number with the same encoding.
module dsp56k.dsl.Layout
open dsp56k.dsl.Asm

open dsp56k

type Field =
  { name   : string
    space  : Space
    offset : int }

/// Consecutive fields from `start`.
let fields space start (names: string array) =
  names |> Array.mapi (fun i n -> { name = n; space = space; offset = start + i })

/// Fields at explicit offsets (gaps and shared words allowed).
let at space (pairs: (string * int) array) =
  pairs |> Array.map (fun (n, o) -> { name = n; space = space; offset = o })

/// `NAME equ offset` for each field.
let equs (fs: Field array) = fs |> Array.map (fun f -> Equ(f.name, Num(int64 f.offset)))

/// The field relative to `baseReg`, by name (needs the field's equ in the program).
let named baseReg (f: Field) : Asm.Mem = { space = f.space; ea = Disp(Asm.rnOf f.name baseReg, Operand.sym f.name) }

/// The field relative to `baseReg`, by number.
let numbered baseReg (f: Field) : Asm.Mem = { space = f.space; ea = Disp(Asm.rnOf f.name baseReg, Operand.num f.offset) }

let find (fs: Field array) name = fs |> Array.find (fun f -> f.name = name)
