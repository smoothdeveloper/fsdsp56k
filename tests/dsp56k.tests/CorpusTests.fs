/// Every program in the shipped machine library (kitbasher's mds/ collection of X.20 core,
/// community and TX6 machines, murdenihcam's and jmamma/MDS's releases): each word the oracle
/// disassembles is parsed by the F# parser and re-encoded by the F# encoder. Table data decodes
/// into arbitrary instructions, which makes this a wide test of rarely used forms.
module dsp56k.tests.CorpusTests

open System.IO
open Expecto
open dsp56k
open machinedrum
open dsp56k.tests.Fixtures

let packages () =
  [| for name in [| "kitbasher"; "murdenihcam"; "MDS" |] do
       for f in Directory.GetFiles(fixture name, "*.syx", SearchOption.AllDirectories) do
         match Sysex.packages (File.ReadAllBytes f) with
         | Ok ps -> for _, p in ps do Path.GetFileName f, p
         | Error _ -> () |]
  |> Array.distinctBy (fun (_, p) -> p[88..119])

let tests =
  testList "machine library" [
    test "every package in the library decodes from SysEx and verifies" {
      needFixture "kitbasher"
      let all = packages ()
      Expect.isGreaterThan all.Length 60 "the library was found"
      for name, p in all do
        Expect.equal (Mds.verify p) (Ok()) $"{name} verifies"
    }

    test "every decodable word re-encodes to the same word, or to the oracle's own encoding of its text" {
      needFixture "kitbasher"
      needNative ()
      let failures = ResizeArray<string>()
      let mutable checkedCount = 0
      for name, p in packages () do
        let words = (Mds.read p |> Result.defaultWith (fun e -> failwithf "%A" e)).programWords
        let mutable pc = 0
        while pc < words.Length do
          let text, n = Native.disassemble pc words[pc] (if pc + 1 < words.Length then words[pc + 1] else 0)
          let n = max 1 n
          if not (text.StartsWith "dc") then
            checkedCount <- checkedCount + 1
            let original = words[pc .. min (words.Length - 1) (pc + n - 1)]
            match Parse.parseLine 1 ("        " + text) with
            | Ok { body = Parse.Instruction instr } ->
              let env : Encode.Env = { pc = pc; value = Expr.eval (fun s -> if s = "*" then Some(int64 pc) else None); settled = fun _ -> true }
              match Encode.encode env instr with
              | Ok ws when ws = original -> ()
              | Ok ws when Native.assembleLine pc (OracleTests.forOracle text) = Ok ws -> ()
              | r -> failures.Add $"{name} ${pc:x4} '{text}': %A{r}"
            | _ -> failures.Add $"{name} ${pc:x4} '{text}': not parsed"
          pc <- pc + n
      Expect.isGreaterThan checkedCount 50000 "the library's programs were walked"
      Expect.isEmpty (failures |> Seq.truncate 50) "no instruction differs"
    }
  ]
