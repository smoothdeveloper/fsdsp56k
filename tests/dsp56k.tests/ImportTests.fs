/// Binary import: every package in the library recovered to items (code, labels, patch markers,
/// data), reassembled and repacked to the same bytes.
module dsp56k.tests.ImportTests

open Expecto
open machinedrum.run
open dsp56k.tests.Fixtures

let tests =
  testList "binary import" [
    test "every package in the library imports and rebuilds byte for byte" {
      needFixture "kitbasher"
      needNative ()
      let all = CorpusTests.packages ()
      let failures =
        [| for name, p in all do
             match Import.package p |> Result.mapError (sprintf "%A") |> Result.bind Import.rebuild with
             | Ok bytes when bytes = p -> ()
             | Ok _ -> $"{name}: rebuilt package differs"
             | Error e -> $"{name}: {e}" |]
      Expect.isEmpty failures "every import rebuilds"
    }
  ]
