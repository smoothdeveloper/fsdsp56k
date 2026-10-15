/// MDS packages to DSL programs: the code recovered from the program words, entry points named
/// INIT/TRIG/RENDER, relocated operands turned into label references and every patch site
/// marked as Abi helpers mark them, so the items reassemble and repack to the same package.
module machinedrum.run.Import

open dsp56k
open machinedrum

type Imported =
  { items   : Item array
    machine : Mds.Machine
    sprite  : byte array }

let package (bytes: byte array) =
  Mds.read bytes |> Result.map (fun pkg ->
    let names = Map.ofList [ pkg.init, "INIT"; pkg.trig, "TRIG"; pkg.render, "RENDER" ]
    let relocated = Set.ofArray pkg.relocations
    let recovered = Disassembler.itemsAt pkg.programWords [| pkg.init; pkg.trig; pkg.render |] names relocated
    // Patch markers: before the instruction whose extension word is patched, or right before
    // a patched data word (the data run is split there).
    let patched =
      Map.ofList
        [ for w in pkg.relocations -> w, (sprintf "RELOC_%04x" w, Abi.relocateMarker, Abi.relocateHereMarker)
          for w, kind, symbol in pkg.imports -> w, (sprintf "IMPORT_%04x" w, Abi.importMarker kind symbol, Abi.importHereMarker kind symbol) ]
    let label w = if pkg.programWords[w] < pkg.programWords.Length then Some(Disassembler.labelName pkg.programWords[w]) else None
    let items =
      [| for pc, it in recovered do
           match it with
           | Op _ ->
             match Map.tryFind (pc + 1) patched with
             | Some(name, marker, _) ->
               Label name
               marker
             | _ -> ()
             it
           | Dc values ->
             for k in 0 .. values.Length - 1 do
               let w = pc + k
               match Map.tryFind w patched with
               | Some(name, _, here) ->
                 Label name
                 here
                 // a relocated program address reads better as its label
                 match pkg.relocations |> Array.contains w, label w with
                 | true, Some l -> Dc [| Sym l |]
                 | _ -> Dc [| values[k] |]
               | None -> Dc [| values[k] |]
           | other -> other |]
      |> Array.fold (fun (acc: Item list) it ->
        // merge consecutive single-word Dc back into rows of up to 8
        match it, acc with
        | Dc [| v |], Dc row :: rest when row.Length < 8 && (match v with Num _ -> true | _ -> false) && (row |> Array.forall (function Num _ -> true | _ -> false)) ->
          Dc(Array.append row [| v |]) :: rest
        | _ -> it :: acc) []
      |> List.rev
      |> List.toArray
    let machine = { Mds.machineOf pkg bytes with patches = Abi.patches items; initSymbol = "INIT"; trigSymbol = "TRIG"; renderSymbol = "RENDER" }
    { items = items; machine = machine; sprite = pkg.sprite })

/// Reassemble and repack an import; equal to the original bytes when the round trip holds.
let rebuild (imported: Imported) =
  Assembler.assembleItems imported.items
  |> Result.mapError Assembler.errorText
  |> Result.bind (fun r ->
    let _, words = Assembler.image Space.P r
    Mds.pack imported.machine words r.symbols (Some imported.sprite) |> Result.mapError (sprintf "%A"))
