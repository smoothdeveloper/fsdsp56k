/// Direct Fable/Rust transport; each node owns an independent WASM instance.
module dsp56k.PortableAudio

open Fable.Core
open Fable.Core.JsInterop
open dsp56k.Wasm

type Resource = { id: int; words: int array }
type Image =
  { kernel: int; frames: int; sampleRate: int
    parameters: int array; resources: Resource array; inputResource: int }

[<Emit("new AudioWorkletNode($0, 'fsharp-rust-wasm', {numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [2], processorOptions: {module: $1}})")>]
let createNode (context: AudioContext) (compiledModule: obj) : AudioNode = jsNative

let load (node: AudioNode) (image: Image) =
  node.port.postMessage (createObj [ "type" ==> "load"; "image" ==> image ])

let parameter (node: AudioNode) index word =
  node.port.postMessage (createObj [ "type" ==> "parameter"; "index" ==> index; "word" ==> word ])

let resource (node: AudioNode) id (words: int array) =
  node.port.postMessage (createObj [ "type" ==> "resource"; "id" ==> id; "words" ==> words ])
