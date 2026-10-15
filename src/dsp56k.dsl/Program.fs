/// Turning DSL items into words and listings.
module dsp56k.dsl.Program
open dsp56k.dsl.Asm

open dsp56k

/// Assemble items (two passes, asm56300's sizing).
let assemble (items: Item array) = Assembler.assembleItems items

/// The P-memory words from address `org` (normally 0).
let words (items: Item array) =
  assemble items |> Result.map (fun r -> Assembler.image Space.P r)

/// Motorola-syntax source for the items (assembles with asm56300 and with this assembler).
let source (items: Item array) = Print.program items + "\n"
