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
let main argv = runTestsWithCLIArgs [] argv all
