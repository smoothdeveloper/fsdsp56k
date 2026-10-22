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
    parameter = fun i setting ->
      if i < machine.parameters.Length then
        let p = machine.parameters[i]
        track.SetParameter(i, Track.convert p (max (float p.minValue) (min (float p.maxValue) setting))) }

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

/// The Machinedrum's default MIDI map (user's manual, OS 1.63, appendix B): track 1..16 listens
/// on the base channel plus (track - 1) / 4; its default note triggers it; its machine
/// parameters 1..8 are CC 16, 40, 72 or 96 onwards, by its place in that group of four.
// TODO: the MIDI map editor can move the notes; level (CC 8..11), mutes and the track effects
// (CC 24..39 and on) belong to the firmware, not to the DSP machine, and are not played.
module MidiMap =
  let defaultNotes = [| 36; 38; 40; 41; 43; 45; 47; 48; 50; 52; 53; 55; 57; 59; 60; 62 |]
  let parameterBase = [| 16; 40; 72; 96 |]

  /// The 0-based MIDI channel of `track` (1..16) for a base channel 1..16.
  let channel (baseChannel: int) (track: int) = (baseChannel - 1 + (track - 1) / 4) % 16

  let note (track: int) = defaultNotes[track - 1]

  /// The machine parameter (0..7) a CC sets on `track`, if any.
  let parameter (track: int) (cc: int) =
    let b = parameterBase[(track - 1) % 4]
    if cc >= b && cc < b + 8 then Some(cc - b) else None

/// What a MIDI message does to the voice on `track`: its note triggers it, its CCs set its
/// parameters (the CC value is the front-panel setting); anything else is ignored.
let respond (voice: Voice) (baseChannel: int) (track: int) (m: dsp56k.audio.Midi.Message) =
  if m.Channel = MidiMap.channel baseChannel track then
    match m.Kind with
    | 0x90 when m.data2 > 0 && m.data1 = MidiMap.note track -> voice.trig (); true
    | 0xb0 ->
      match MidiMap.parameter track m.data1 with
      | Some i -> voice.parameter i (float m.data2); true
      | None -> false
    | _ -> false
  else false
