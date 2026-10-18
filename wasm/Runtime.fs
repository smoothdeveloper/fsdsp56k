/// Browser bindings for the isolated dsp56300 interpreter. Messages apply between audio quanta.
module dsp56k.Wasm

open Fable.Core
open Fable.Core.JsInterop

type RegisterWrite = { register: int; word: int }
type Segment = { bank: int; address: int; data: int array }

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
    output: int }

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

[<Emit("new AudioWorkletNode($0, 'dsp56300', {numberOfInputs: 0, numberOfOutputs: 1, outputChannelCount: [2], processorOptions: {module: $1}})")>]
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
