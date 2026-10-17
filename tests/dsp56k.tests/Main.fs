module dsp56k.tests.Main

open Expecto

[<Tests>]
let all =
  testList "fsdsp56k" [
    OracleTests.tests
    OracleTests.parserTests
    GoldTests.tests
    EmulatorTests.tests
    CorpusTests.tests
    ImportTests.tests
    ToolTests.tests
    KernelTests.tests
  ]

[<EntryPoint>]
let main argv =
  // what the kernel tests keep is regenerated each run: nothing stale stays
  dsp56k.kernel.Audio.clean KernelTests.keptRoot
  runTestsWithCLIArgs [] argv all
