/// The MDS machine ABI (MDX_MachineDumpStandard.txt, sections 2 and 4) as DSL helpers: the
/// per-track workset, firmware resources and services, relocated local addresses. Each patch
/// site is written as a label, a marker comment and the instruction whose next word the loader
/// patches, so the package's relocation and import lists come from the program itself
/// (`patches`) and the listing documents them.
module machinedrum.Abi

open dsp56k
open dsp56k.dsl
open dsp56k.dsl.Asm

/// Firmware memory resources (import kinds 1 to 4).
type Resource =
  | SineTable
  | AudioInput
  | AudioInputPosition
  | MainOutput
  | MainOutputPosition
  | TrackMemory
  | TempX
  | TempY

/// Firmware services (import kind 5), called with JSR.
type Service =
  | SampleInfo
  | SampleRead
  | RamWorkspace
  | RamRecord

module Resource =
  let abiSymbol r =
    match r with
    | SineTable -> 1 | AudioInput -> 2 | AudioInputPosition -> 3 | MainOutput -> 4
    | MainOutputPosition -> 5 | TrackMemory -> 6 | TempX -> 7 | TempY -> 8

module Service =
  let abiSymbol s =
    match s with
    | SampleInfo -> 9 | SampleRead -> 10 | RamWorkspace -> 11 | RamRecord -> 12

/// Bank numbers SAMPLE_INFO and SAMPLE_READ take in X0.
type Bank =
  | Internal = 0
  | Userwave = 1
  | Ram = 2

/// Service cost ceilings from the standard ("Service costs"), for cycle budgets.
let serviceCycles service frames =
  match service with
  | SampleInfo -> 149
  | SampleRead -> 202 + 33 * frames
  | RamWorkspace -> 140
  | RamRecord -> if frames = 0 then 180 else 272 + 55 * frames

/// Per-track limit on MD MKI and MKII.
let trackCycleLimit = 3951

let markerPrefix = "mds: "

let importMarker kind symbol = Comment $"{markerPrefix}import kind {kind} symbol {symbol}"
let relocateMarker = Comment $"{markerPrefix}relocate"

/// Markers for a patched word at the label itself (data), rather than after it.
let importHereMarker kind symbol = Comment $"{markerPrefix}import kind {kind} symbol {symbol} +0"
let relocateHereMarker = Comment $"{markerPrefix}relocate +0"

/// `name: move #>offset,reg` holding a firmware resource's address plus `offset`.
let inline resourceAddress name (resource: Resource) offset (reg: Reg) =
  let symbol = Resource.abiSymbol resource
  [| Label name
     importMarker (Mds.importKind symbol) symbol
     move (immL offset) reg |]

/// `name: jsr >0`, patched to the service's entry by the loader.
let callService name (service: Service) =
  let symbol = Service.abiSymbol service
  [| Label name
     importMarker 5 symbol
     jumpL Jsr 0 |]

/// `name: move #>target,reg` for an address inside the machine, relocated by its program base.
let inline localAddress name target (reg: Reg) =
  [| Label name
     relocateMarker
     move (immL target) reg |]

/// The patch list of a program written with the helpers above (word after each marked label).
let patches (items: Item array) : Mds.Patch array =
  [| for i in 0 .. items.Length - 2 do
       match items[i], items[i + 1] with
       | Label name, Comment text when text.StartsWith markerPrefix ->
         match text[markerPrefix.Length ..].Split ' ' with
         | [| "relocate" |] -> Mds.Relocation(name, 1)
         | [| "relocate"; "+0" |] -> Mds.Relocation(name, 0)
         | [| "import"; "kind"; kind; "symbol"; symbol |] -> Mds.Import(name, 1, int kind, int symbol)
         | [| "import"; "kind"; kind; "symbol"; symbol; "+0" |] -> Mds.Import(name, 0, int kind, int symbol)
         | _ -> ()
       | _ -> () |]

/// The workset: Y+0 reserved, the controls at Y+1..8, state from Y+9 (Y+10 for a stereo pair).
let parameters (names: string array) = Layout.fields Space.Y 1 names

/// The routine's view of a workset field: (r6+offset), by name.
let field (f: Layout.Field) = Layout.named r6 f
