/// Browser bindings for the isolated dsp56300 interpreter. Messages apply between audio quanta.
module dsp56k.Wasm

open Fable.Core
open Fable.Core.JsInterop

type RegisterWrite = { register: int; word: int }
type Segment = { bank: int; address: int; data: int array }
type TraceRegion = { bank: int; address: int; words: int }

/// Versioned transport for assembled kernels; firmware-specific addresses belong to the caller.
type KernelImage =
  { version: int
    words: int
    frames: int
    sampleRate: int
    segments: Segment array
    registers: RegisterWrite array
    initialize: int array
    beforeRender: RegisterWrite array
    render: int
    trig: int option
    outputBank: int
    output: int
    inputBank: int
    input: int
    externalMemory: int option
    /// 1: the output buffer holds one word a frame (a mono block), played on both channels; absent or 2: left, right pairs.
    channels: int option }

[<AllowNullLiteral>]
type AudioContext =
  abstract sampleRate: float
  abstract destination: obj
  abstract resume: unit -> JS.Promise<unit>
  abstract close: unit -> JS.Promise<unit>

[<AllowNullLiteral>]
type MessagePort =
  abstract postMessage: obj -> unit
  abstract onmessage: (obj -> unit) with set

[<AllowNullLiteral>]
type AudioNode =
  abstract port: MessagePort
  abstract connect: obj -> unit
  abstract disconnect: unit -> unit

[<Emit("new AudioContext({sampleRate: $0})")>]
let createContext (sampleRate: int) : AudioContext = jsNative

[<Emit("$0.audioWorklet.addModule($1)")>]
let addProcessor (context: AudioContext) (url: string) : JS.Promise<unit> = jsNative

[<Emit("fetch($0).then(r => { if (!r.ok) throw new Error('Cannot fetch DSP runtime: ' + r.status); return r.arrayBuffer(); }).then(b => WebAssembly.compile(b))")>]
let compileModule (url: string) : JS.Promise<obj> = jsNative

[<Emit("new AudioWorkletNode($0, 'dsp56300', {numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [2], processorOptions: {module: $1}})")>]
let createNode (context: AudioContext) (compiledModule: obj) : AudioNode = jsNative

let load (node: AudioNode) (image: KernelImage) =
  node.port.postMessage (createObj [ "type" ==> "load"; "image" ==> image ])

let play (node: AudioNode) value =
  node.port.postMessage (createObj [ "type" ==> "play"; "value" ==> value ])

let gain (node: AudioNode) (value: float) =
  node.port.postMessage (createObj [ "type" ==> "gain"; "value" ==> value ])

let write (node: AudioNode) bank address word =
  node.port.postMessage (createObj [ "type" ==> "memory"; "bank" ==> bank; "address" ==> address; "word" ==> word ])

let registers (node: AudioNode) (values: RegisterWrite array) =
  node.port.postMessage (createObj [ "type" ==> "registers"; "values" ==> values ])

let trig (node: AudioNode) =
  node.port.postMessage (createObj [ "type" ==> "trig" ])

/// Optional intermediate-buffer traces; each region is sampled after every DSP block.
let trace (node: AudioNode) (regions: TraceRegion array) =
  node.port.postMessage (createObj [ "type" ==> "trace"; "regions" ==> regions ])

/// With `true`, each `level` message carries `writes`: per register (the numbers of `RegisterWrite`, then PC, SR, OMR, LA, LC,
/// SP), how many blocks changed it since the last one.
let activity (node: AudioNode) (value: bool) =
  node.port.postMessage (createObj [ "type" ==> "activity"; "value" ==> value ])

/// Stepping: `beginStep` stops the audio and starts a routine (the render entry by default), `step` runs `count`
/// instructions; each answers a `step` message: status (1: returned), pc, steps, registers (a snapshot), written.
let beginStep (node: AudioNode) (entry: int option) =
  node.port.postMessage (createObj [ "type" ==> "begin"; "entry" ==> (match entry with Some e -> box e | None -> null) ])

/// The words each `step` message also answers with (`watched`, in order): (bank 1 X, 2 Y; offset from r6).
let watch (node: AudioNode) (words: (int * int) array) =
  node.port.postMessage (createObj [ "type" ==> "watch"; "words" ==> [| for bank, offset in words -> createObj [ "bank" ==> bank; "offset" ==> offset ] |] ])

let step (node: AudioNode) (count: int) =
  node.port.postMessage (createObj [ "type" ==> "step"; "count" ==> count ])

/// The names of a snapshot's words, in order.
let registerNames =
  [| "r0"; "r1"; "r2"; "r3"; "r4"; "r5"; "r6"; "r7"; "n0"; "n1"; "n2"; "n3"; "n4"; "n5"; "n6"; "n7"
     "m0"; "m1"; "m2"; "m3"; "m4"; "m5"; "m6"; "m7"; "x0"; "x1"; "y0"; "y1"; "a0"; "a1"; "a2"; "b0"; "b1"; "b2"
     "pc"; "sr"; "omr"; "la"; "lc"; "sp" |]

[<AllowNullLiteral>]
type GainNode =
  abstract connect: obj -> unit

[<Emit("(()=>{const n=$0.createGain();n.gain.value=$1;return n})()")>]
let createGain (context: AudioContext) (value: float) : GainNode = jsNative

[<Emit("$0.gain.value=$1")>]
let setGain (node: GainNode) (value: float) : unit = jsNative
