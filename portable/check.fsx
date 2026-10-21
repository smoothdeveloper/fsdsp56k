#load "../src/dsp56k.kernel.intrinsics/Intrinsics.fs"
#load "Host.fs"
#load "Json.fs"
open dsp56k.Portable

let mutable value = 0
let create () =
  { memory = fun _ _ _ -> failwith "Unexpected memory write"
    buffers = buffers
    parameters = fun words -> value <- words[0]
    init = ignore
    trig = ignore
    render = fun () -> [| value |]
    state = fun () -> [| value |] }
let scenario =
  [| { action = "parameters"; words = [| 7 |]; blocks = 0 }
     { action = "render"; words = [||]; blocks = 1 } |]
let expected, memory = capture create scenario
let fixture =
  { version = 1; kernel = "check"; frames = 1; channels = 1; sampleRate = 44100
    steps = scenario; expected = expected; buffers = memory }
let mutable clock = 0.
let now () = clock <- clock + 1.; clock
let result = grade "test" "test" now create 0 1 fixture
assert (dsp56k.PortableJson.fixture (dsp56k.PortableJson.writeFixture fixture) = fixture)
assert (result.outputChecks = 1 && result.stateChecks = 1 && result.bufferChecks > 0)
let refuses fixture =
  try grade "test" "test" now create 0 1 fixture |> ignore; false
  with _ -> true
assert (refuses { fixture with expected = [| { expected[0] with output = [| 8 |] } |] })
assert (refuses { fixture with expected = [| { expected[0] with state = [| 8 |] } |] })
assert (refuses { fixture with buffers = [| { memory[0] with words = [| 1 |] } |] })
assert (refuses { fixture with buffers = [||] })
assert (refuses { fixture with buffers = Array.append memory [| memory[0] |] })
assert (refuses { fixture with version = 2 })
printfn "[1/2] Portable grade rejects output, state, buffer and schema mismatches: passed"

// Also run with --define:FABLE_COMPILER to exercise the exact portable CLB fallback.
open dsp56k.kernel
for bit in 0 .. 54 do
  for raw in [| (1L <<< bit) - 1L; 1L <<< bit; -(1L <<< bit); -(1L <<< bit) - 1L |] do
    let accumulator = Acc.ofRaw raw
    let magnitude = if accumulator.Raw < 0L then ~~~accumulator.Raw else accumulator.Raw
    let leading = 56 - (64 - System.Numerics.BitOperations.LeadingZeroCount(uint64 magnitude))
    let expected = if accumulator.Raw = 0L then 0 else 9 - leading
    assert ((Acc.hi (Acc.clb accumulator)).Raw = expected)
printfn "[2/2] CLB across signed power-of-two boundaries: passed"

for raw in -20 .. 20 do
  for fraction in [| -0.5; 0.; 0.5 |] do
    let value = (float raw + fraction) / 8388608.
    assert ((Frac.q23 value).Raw = int (System.Math.Round(value * 8388608., System.MidpointRounding.ToEven)))
printfn "Q23 signed ties-to-even boundaries: passed"

let table = Array.zeroCreate 3
let resources = [| "table", table |]
writeResource resources "table" 1 [| 7; 0xffffff |]
assert (table = [| 0; 7; 0xffffff |])
let rejectsWrite offset words =
  try writeResource resources "table" offset words; false
  with _ -> true
assert (rejectsWrite -1 [| 1 |])
assert (rejectsWrite 3 [| 1 |])
assert (rejectsWrite 0 [| -1 |])
assert (rejectsWrite 0 [| 0x1000000 |])
let mutable memoryCalled = false
let writeTest name offset words =
  writeResource resources name offset words
  memoryCalled <- true
let host = { create() with memory = writeTest }
steps host [| { action = "memory:table:0"; words = [| 8 |]; blocks = 0 } |] ignore
assert (memoryCalled && table[0] = 8)
printfn "Portable resource writes: replay, bounds and 24-bit word checks passed"
