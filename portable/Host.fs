/// Direct execution of kernel functions; no DSP instruction interpreter or quotation compiler.
module dsp56k.Portable

open dsp56k.kernel

type Buffer = { name: string; words: int array }

type Instance =
  { parameters : int array -> unit
    memory     : string -> int -> int array -> unit
    init       : unit -> unit
    trig       : unit -> unit
    render     : unit -> int array
    state      : unit -> int array
    buffers    : unit -> Buffer array }

type Step =
  { action : string
    words  : int array
    blocks : int }

type Block =
  { output : int array
    state  : int array }

type Fixture =
  { version    : int
    kernel     : string
    frames     : int
    channels   : int
    sampleRate : int
    steps      : Step array
    expected   : Block array
    buffers    : Buffer array }

type Measurement =
  { target          : string
    kernel          : string
    runtime         : string
    blocks          : int
    samples         : int
    medianRenderMs  : float
    worstRenderMs   : float
    audioMs         : float
    realTimeFactor  : float
    outputChecks    : int
    stateChecks     : int
    bufferChecks    : int }

/// Shared host memory is deliberately reset between trials, never between render blocks.
let reset () =
  for region in [| Scratch.x; Scratch.y; Track.memory |] do
    Array.fill region.words 0 region.words.Length 0
  Track.current <- 0

let buffers () =
  [| for region in [| Scratch.x; Scratch.y; Track.memory |] do
       { name = region.name; words = Array.copy region.words } |]

/// Bounded resource writes used identically by .NET, JavaScript and WASM fixtures.
let writeResource (resources: (string * int array) array) name offset (words: int array) =
  let destination = resources |> Array.find (fun (key, _) -> key = name) |> snd
  if offset < 0 || offset > destination.Length - words.Length
     || words |> Array.exists (fun word -> word < 0 || word > 0xffffff) then
    invalidArg "words" "Resource write outside its buffer or 24-bit word range"
  Array.blit words 0 destination offset words.Length

let steps (host: Instance) (scenario: Step array) (render: unit -> unit) =
  for step in scenario do
    match step.action with
    | "parameters" -> host.parameters step.words
    | action when action.StartsWith "memory:" ->
      let parts = action.Split ':'
      if parts.Length <> 3 then invalidArg "action" "Expected memory:name:offset"
      host.memory parts[1] (int parts[2]) step.words
    | "init" -> host.init()
    | "trig" -> host.trig()
    | "render" when step.blocks >= 0 ->
      for _ in 1 .. step.blocks do render()
    | action -> failwith $"Unknown or invalid portable scenario step: {action}"

let capture (create: unit -> Instance) (scenario: Step array) =
  reset()
  let host = create()
  let blocks = ResizeArray<Block>()
  steps host scenario (fun () ->
    blocks.Add { output = host.render(); state = host.state() })
  blocks.ToArray(), host.buffers()

let private equalWords (name: string) (expected: int array) (actual: int array) =
  if expected.Length <> actual.Length then
    failwith $"{name}: expected {expected.Length} words, got {actual.Length}"
  for index in 0 .. expected.Length - 1 do
    if expected[index] <> actual[index] then
      failwith $"{name}[{index}]: expected {expected[index]}, got {actual[index]}"
  expected.Length

/// Checks every output/state word at each block and all declared shared buffers at scenario end.
/// Timings exclude snapshots and comparisons; include the kernel's normal output allocation.
let grade target runtime now create warmup trials (fixture: Fixture) =
  if fixture.version <> 1 || fixture.frames <= 0 || fixture.channels <= 0
     || fixture.sampleRate <= 0 || warmup < 0 || trials < 1 then
    invalidArg "fixture" "Invalid portable grading version, audio dimensions or trial count"
  let actual, memory = capture create fixture.steps
  let bufferNames (buffers: Buffer array) = buffers |> Array.map _.name |> Array.sort
  if bufferNames fixture.buffers <> bufferNames memory
     || (fixture.buffers |> Array.distinctBy _.name).Length <> fixture.buffers.Length then
    failwith $"{fixture.kernel}: expected buffer names must cover every declared buffer exactly once"
  if actual.Length <> fixture.expected.Length || actual.Length = 0 then
    failwith $"{fixture.kernel}: expected {fixture.expected.Length} nonempty blocks, got {actual.Length}"
  let mutable outputChecks, stateChecks, bufferChecks = 0, 0, 0
  for index in 0 .. actual.Length - 1 do
    if actual[index].output.Length <> fixture.frames * fixture.channels then
      failwith $"{fixture.kernel}: wrong output block size"
    outputChecks <- outputChecks + equalWords $"output block {index}" fixture.expected[index].output actual[index].output
    stateChecks <- stateChecks + equalWords $"state block {index}" fixture.expected[index].state actual[index].state
  for expected in fixture.buffers do
    match memory |> Array.tryFind (fun buffer -> buffer.name = expected.name) with
    | Some actual -> bufferChecks <- bufferChecks + equalWords expected.name expected.words actual.words
    | None -> failwith $"Unknown buffer: {expected.name}"
  let measure () =
    reset()
    let host = create()
    let mutable elapsed = 0.
    steps host fixture.steps (fun () ->
      let started = now()
      host.render() |> ignore
      elapsed <- elapsed + now() - started)
    elapsed
  for _ in 1 .. warmup do measure() |> ignore
  let times = Array.init trials (fun _ -> measure()) |> Array.sort
  let median =
    if trials % 2 = 0 then (times[trials / 2 - 1] + times[trials / 2]) / 2.
    else times[trials / 2]
  let audioMs = float (actual.Length * fixture.frames) * 1000. / float fixture.sampleRate
  { target = target; kernel = fixture.kernel; runtime = runtime
    blocks = actual.Length; samples = trials
    medianRenderMs = median; worstRenderMs = Array.last times
    audioMs = audioMs; realTimeFactor = audioMs / median
    outputChecks = outputChecks; stateChecks = stateChecks; bufferChecks = bufferChecks }
