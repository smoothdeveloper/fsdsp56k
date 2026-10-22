/// `fsdsp56k audition`: a machine played live through the default output. The track renders
/// faster than real time; the output ring fills and the device's pace sets the loop's.
module dsp56k.tool.Audition

open machinedrum
open machinedrum.run

/// What audition plays: something it can trigger, set and render 32 words at a time.
type Voice =
  { trig      : unit -> unit
    render    : unit -> int array          // one block, signed 24-bit words
    parameter : int -> float -> unit }     // a parameter (0..7) to a front-panel setting

/// A package on an emulated track.
let ofTrack (track: Track.Track) (machine: Mds.Machine) =
  { trig      = fun () -> track.Trig 0 |> ignore
    render    = fun () -> fst (track.Render 0)
    parameter = fun i setting -> track.SetParameter(i, Track.convert machine.parameters[i] setting) }

/// An F# kernel run as .NET code through its portable host (`Host.Instance`), its settings
/// converted to parameter words by `words`.
// TODO: the command line does not compile or load F# kernels, so nothing builds an Instance
// for `audition` yet; a script or a test can call `play` with this voice.
let ofInstance (instance: dsp56k.Portable.Instance) (words: float array -> int array) (settings: float array) =
  let current = Array.copy settings
  instance.parameters (words current)
  { trig      = instance.trig
    render    = instance.render
    parameter = fun i setting ->
      current[i] <- setting
      instance.parameters (words current) }

type Played =
  { blocks : int
    trigs  : int }

/// Play `blocks` blocks (or until `stop`): a trig at the first block and every `trigEvery`,
/// `between` called before each block (live input: MIDI), each block's frames to `write`.
let play (write: float32 array -> unit) (voice: Voice) (blocks: int) (trigEvery: int option) (between: unit -> unit) (stop: unit -> bool) =
  let mutable b = 0
  let mutable trigs = 0
  while b < blocks && not (stop ()) do
    between ()
    if b = 0 || (trigEvery |> Option.exists (fun n -> n > 0 && b % n = 0)) then
      voice.trig ()
      trigs <- trigs + 1
    write (voice.render () |> Array.map (Track.toFloat >> float32))
    b <- b + 1
  { blocks = b; trigs = trigs }
