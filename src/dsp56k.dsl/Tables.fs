/// Constant tables laid out one after another under one label, with `equ` offsets for each
/// (the shape gen_tables.py-style generators produce), and the Q23 helpers they need.
module dsp56k.dsl.Tables
open dsp56k.dsl.Asm

open System
open dsp56k

type Table =
  { name   : string
    values : int array }

let one = 0x7fffff

/// Round half to even, as Python's round() does (generators ported from Python stay
/// word-identical).
let roundEven (v: float) = Math.Round(v, MidpointRounding.ToEven)

/// A signed Q23 word (saturating), as a 24-bit value.
let q23 (v: float) =
  int (Math.Max(-8388608.0, Math.Min(8388607.0, roundEven (v * 8388608.0)))) &&& 0xffffff

/// The offset of each table from the first.
let offsets (tables: Table array) =
  tables
  |> Array.scan (fun acc t -> acc + t.values.Length) 0
  |> Array.take tables.Length
  |> Array.zip (tables |> Array.map _.name)

/// `prefix NAME equ offset` for each table, then `label`, then the words, eight per line with a
/// comment naming each table.
let items (equPrefix: string) (labelName: string) (tables: Table array) =
  [| for name, off in offsets tables do
       Equ(equPrefix + name, Num(int64 off))
     Label labelName
     for t in tables do
       Comment t.name
       for row in Array.chunkBySize 8 t.values do
         Dc(row |> Array.map (fun v -> Num(int64 (v &&& 0xffffff)))) |]

let words (tables: Table array) = tables |> Array.collect _.values
