/// The F# encoder and printer against mborgerson/dsp56300's assembler and disassembler, which
/// were round-trip tested against Motorola asm56300 over the whole encoding space.
module dsp56k.tests.OracleTests

open Expecto
open dsp56k
open dsp56k.tests.Fixtures

let pc = 0x100

let env : Encode.Env =
  { pc = pc
    value = Expr.eval (fun _ -> None)
    settled = fun _ -> true }

let hex (ws: int array) = ws |> Array.map (sprintf "%06x") |> String.concat " "

/// mborgerson's parser takes no `<`/`>` after a memory space (it picks the form from the value);
/// asm56300 accepts both spellings.
let forOracle text = Native.oracleSpelling text

let report (failures: string array) total =
  if failures.Length > 0 then
    failwithf "%d of %d differ:\n%s" failures.Length total (failures |> Array.truncate 400 |> String.concat "\n")

let tests =
  testList "oracle" [
    test "every catalogue instruction encodes as mborgerson's assembler encodes its printed text" {
      needNative ()
      let catalogue = Catalogue.all pc
      let failures =
        [| for instr in catalogue do
             let text = Print.instr instr
             match Encode.encode env instr, Native.assembleLine pc (forOracle text) with
             | Ok ours, Ok theirs when ours = theirs -> ()
             | Ok ours, Ok theirs -> $"{text,-44} ours {hex ours}  oracle {hex theirs}"
             | Error e, _ -> $"{text,-44} F# cannot encode: %A{e}"
             | Ok ours, Error(Native.AssembleLineError m) -> $"{text,-44} ours {hex ours}  oracle rejects: {m}" |]
      report failures catalogue.Length
    }

    test "the oracle disassembles every encoding back to the same words" {
      needNative ()
      let catalogue = Catalogue.all pc
      let failures =
        [| for instr in catalogue do
             match Encode.encode env instr with
             | Ok ws ->
               let text, n = Native.disassemble pc ws[0] (if ws.Length > 1 then ws[1] else 0)
               match Native.assembleLine pc text with
               | Ok again when again = ws && n = ws.Length -> ()
               | Ok again -> $"{Print.instr instr,-40} {hex ws} -> '{text}' -> {hex again}"
               | Error(Native.AssembleLineError m) -> $"{Print.instr instr,-40} {hex ws} -> '{text}' rejected: {m}"
             | Error _ -> () |]
      report failures catalogue.Length
    }

    test "all 256 ALU bytes: the oracle's text parses and encodes back to the same byte" {
      needNative ()
      let failures =
        [| for b in 0 .. 255 do
             let text, _ = Native.disassemble 0 (0x200000 ||| b) 0
             if not (text.StartsWith "dc") then
               match Parse.parseLine 1 ("        " + text) with
               | Ok { body = Parse.Instruction instr } ->
                 match Encode.encode env instr with
                 | Ok [| w |] when w = (0x200000 ||| b) -> ()
                 | Ok ws -> $"{b:x2} '{text}' -> {hex ws}"
                 | Error e -> $"{b:x2} '{text}' -> %A{e}"
               | Ok _ -> $"{b:x2} '{text}' is not an instruction"
               | Error e -> $"{b:x2} '{text}' -> {Parse.errorText e}" |]
      report failures 256
    }
  ]

let parserTests =
  testList "printer and parser" [
    test "every catalogue instruction parses back from its printed text" {
      let catalogue = Catalogue.all pc
      let failures =
        [| for instr in catalogue do
             let text = Print.instr instr
             match Parse.parseLine 1 ("        " + text) with
             | Ok { body = Parse.Instruction parsed } when parsed = instr -> ()
             | Ok { body = Parse.Instruction parsed } -> $"{text,-44} parsed as {Print.instr parsed}: %A{parsed}"
             | Ok _ -> $"{text} is not an instruction"
             | Error e -> $"{text,-44} {Parse.errorText e}" |]
      report failures catalogue.Length
    }
  ]
