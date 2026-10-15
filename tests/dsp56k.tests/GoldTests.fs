/// Shipped MDS packages built with Motorola asm56300 and jmamma/MDS tools/mds_pack.py: the F#
/// assembler must reproduce each program word for word, and the packer each package byte for byte.
module dsp56k.tests.GoldTests

open System.IO
open Expecto
open dsp56k
open machinedrum
open dsp56k.tests.Fixtures

/// (source, package, include folders) for every package whose sources are complete.
let cases () =
  [| for d in Directory.GetFiles(fixture "murdenihcam", "*.mds", SearchOption.AllDirectories) do
       let asm = string (Path.ChangeExtension(d, ".asm56k"))
       let root = string (Path.GetDirectoryName(string (Path.GetDirectoryName d)))
       // NFX-MO's generated tables.inc is not published, so it cannot be rebuilt.
       if File.Exists asm && not (asm.Contains "NFX-MO") then
         asm, d, [| root </> "source"; root </> "_source" |]
     for d in Directory.GetFiles(fixture "MDS" </> "machines", "*.mds", SearchOption.AllDirectories) do
       string (Path.ChangeExtension(d, ".asm56k")), d, [||] |]

/// kitbasher's TX6 drums (MIT): source, JSON definition and the released single-machine .syx.
let tx6 () =
  [| for v in [| "bd"; "sd"; "lt"; "ht"; "ch"; "oh"; "cy"; "cp" |] ->
       let dir = fixture "kitbasher" </> "mds" </> "tx6" </> v
       v, dir </> (v + ".asm"), dir </> (v + ".json"), dir </> (v + ".syx") |]

let tests =
  testList "gold packages" [
    test "TX6: sources assemble, pack and export to the released .syx byte for byte" {
      needFixture "kitbasher"
      // The two temporary-memory imports are offsets from the loader-patched bases.
      let imports = Map.ofList [ "mds_scratch_x", 0L; "mds_scratch_y", 0L ]
      let failures =
        [| for name, asm, json, syx in tx6 () do
             let released = File.ReadAllBytes syx
             let shipped = match Sysex.packages released with Ok [| _, p |] -> p | _ -> [||]
             let sprite = (Mds.read shipped |> Result.map _.sprite |> Result.toOption)
             match Assembler.assembleFile [||] imports asm, Mds.fromJson (File.ReadAllText json) with
             | Ok r, Ok m ->
               match Mds.pack m (snd (Assembler.image Space.P r)) r.symbols sprite with
               | Ok pkg when Sysex.ofPackage (int released[11] + 1) pkg = released -> ()
               | Ok _ -> $"{name}: differs"
               | Error e -> $"{name}: %A{e}"
             | Error e, _ -> $"{name}: {Assembler.errorText e}"
             | _, Error e -> $"{name}: %A{e}" |]
      Expect.isEmpty failures "all eight rebuild"
    }

    test "programs assemble word for word" {
      needFixture "murdenihcam"
      needFixture "MDS"
      let failures =
        [| for asm, mds, inc in cases () do
             let gold = (Mds.read (File.ReadAllBytes mds) |> Result.defaultWith (fun e -> failwithf "%A" e)).programWords
             match Assembler.assembleFile inc Map.empty asm with
             | Error e -> $"{Path.GetFileName asm}: {Assembler.errorText e}"
             | Ok r ->
               let _, img = Assembler.image Space.P r
               if img <> gold then $"{Path.GetFileName asm}: {img.Length} words, shipped {gold.Length}" |]
      Expect.isEmpty failures "every program matches"
      Expect.isGreaterThanOrEqual (cases ()).Length 16 "all packages were checked"
    }

    test "packages rebuild byte for byte from JSON, program and sprite" {
      needFixture "murdenihcam"
      needFixture "MDS"
      let failures =
        [| for asm, mds, inc in cases () do
             let gold = File.ReadAllBytes mds
             let json = string (Path.ChangeExtension(mds, ".json"))
             let pbm = string (Path.ChangeExtension(mds, ".pbm"))
             match Assembler.assembleFile inc Map.empty asm, Mds.fromJson (File.ReadAllText json) with
             | Ok r, Ok m ->
               // The MDS examples declare their cycle budget on the mds_pack command line.
               let m = if m.maxTrackCycles = 0 then { m with maxTrackCycles = (int gold[70] <<< 8) ||| int gold[71] } else m
               let sprite = if File.Exists pbm then Some(Mds.spriteOfPbm (File.ReadAllText pbm)) else None
               match Mds.pack m (snd (Assembler.image Space.P r)) r.symbols sprite with
               | Ok bytes when bytes = gold -> ()
               | Ok _ -> $"{Path.GetFileName mds}: bytes differ"
               | Error e -> $"{Path.GetFileName mds}: %A{e}"
             | _ -> $"{Path.GetFileName mds}: could not assemble or read its JSON" |]
      Expect.isEmpty failures "every package matches"
    }
  ]
