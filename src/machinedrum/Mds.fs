/// MDX Machine Dump Standard (MDS v1, jmamma/MDS docs/MDX_MachineDumpStandard.txt): machine
/// definitions, the `.mds` package writer (byte-identical to tools/mds_pack.py) and a reader.
module machinedrum.Mds

open dsp56k

open System
open System.Text
open System.Text.Json

type Transform =
  | Raw14 | UnipolarQ23 | UnipolarSquareQ23 | BipolarQ23
  | Pitch | Boolean     | Index             | Decay

type Control =
  | Continuous | Bipolar | Percent | Switch | Rotary | PitchControl

type ValueText =
  { first : int
    last  : int
    text  : string }

type Param =
  { name        : string
    defaultValue: int
    minValue    : int
    maxValue    : int
    transform   : Transform
    control     : Control
    linked      : bool
    valueText   : ValueText array }

/// A program word the loader patches: relocated by the program base, or given a firmware
/// resource or service address (kind/symbol as in section 4 of the standard).
type Patch =
  | Relocation of label: string * wordDelta: int
  | Import     of label: string * wordDelta: int * kind: int * abiSymbol: int

type Machine =
  { id             : string
    displayName    : string
    category       : string
    shortLabel     : string
    categoryOrder  : int
    machineVersion : int * int
    tonal          : bool
    neighbour      : bool
    stereoPair     : bool
    continuous     : bool
    parameters     : Param array
    initSymbol     : string
    trigSymbol     : string
    renderSymbol   : string
    xWorkset       : int
    yWorkset       : int
    patches        : Patch array
    maxTrackCycles : int
    description    : string }

type MdsError =
  | NotMds
  | Truncated
  | MissingSymbol of name: string
  | Invalid       of what: string

module Transform =
  let code t =
    match t with
    | Raw14 -> 0 | UnipolarQ23 -> 1 | UnipolarSquareQ23 -> 2 | BipolarQ23 -> 3
    | Pitch -> 4 | Boolean -> 5     | Index -> 6             | Decay -> 7

  let ofName name =
    match name with
    | "raw14"               -> Some Raw14
    | "unipolar_q23"        -> Some UnipolarQ23
    | "unipolar_square_q23" -> Some UnipolarSquareQ23
    | "bipolar_q23"         -> Some BipolarQ23
    | "pitch"               -> Some Pitch
    | "boolean"             -> Some Boolean
    | "index"               -> Some Index
    | "decay"               -> Some Decay
    | _ -> None

  let defaultControl t =
    match t with
    | Raw14 | Decay -> Continuous
    | UnipolarQ23 | UnipolarSquareQ23 -> Percent
    | BipolarQ23 -> Bipolar
    | Pitch -> PitchControl
    | Boolean -> Switch
    | Index -> Rotary

module Control =
  let code c =
    match c with
    | Continuous -> 0 | Bipolar -> 1 | Percent -> 2
    | Switch -> 3     | Rotary -> 4  | PitchControl -> 5

  let ofName name =
    match name with
    | "continuous" -> Some Continuous
    | "bipolar"    -> Some Bipolar
    | "percent"    -> Some Percent
    | "switch"     -> Some Switch
    | "rotary"     -> Some Rotary
    | "pitch"      -> Some PitchControl
    | _ -> None

/// The import kind each ABI symbol requires.
let importKind abiSymbol =
  match abiSymbol with
  | 1 | 2 | 3 | 4 | 5 -> 1
  | 6 -> 2
  | 7 -> 3
  | 8 -> 4
  | 9 | 10 | 11 | 12 -> 5
  | _ -> 0

let param name transform =
  { name = name; defaultValue = 0; minValue = 0; maxValue = (if transform = Boolean then 1 else 127)
    transform = transform; control = Transform.defaultControl transform; linked = false; valueText = [||] }

let machine id displayName category shortLabel =
  { id = id; displayName = displayName; category = category; shortLabel = shortLabel; categoryOrder = 0
    machineVersion = (0, 0); tonal = false; neighbour = false; stereoPair = false; continuous = false
    parameters = [||]; initSymbol = "INIT"; trigSymbol = "TRIG"; renderSymbol = "RENDER"
    xWorkset = 0; yWorkset = 0; patches = [||]; maxTrackCycles = 0; description = "" }

// ---------------------------------------------------------------------------------------------
// JSON definitions (section 5).

let fromJson (json: string) : Result<Machine, MdsError> =
  try
    use doc = JsonDocument.Parse json
    let root = doc.RootElement
    let str (e: JsonElement) (name: string) fallback =
      match e.TryGetProperty name with
      | true, v -> v.GetString() |> string
      | _ -> fallback
    let int' (e: JsonElement) (name: string) fallback =
      match e.TryGetProperty name with
      | true, v -> v.GetInt32()
      | _ -> fallback
    let bool' (e: JsonElement) (name: string) =
      match e.TryGetProperty name with
      | true, v -> v.GetBoolean()
      | _ -> false
    let items (e: JsonElement) (name: string) =
      match e.TryGetProperty name with
      | true, v -> [| for x in v.EnumerateArray() -> x |]
      | _ -> [||]
    let parameters =
      items root "params" |> Array.map (fun p ->
        let transform = Transform.ofName (str p "transform" "") |> Option.defaultWith (fun () -> failwith "transform")
        let control =
          match p.TryGetProperty "control" with
          | true, c -> Control.ofName (c.GetString() |> string) |> Option.defaultWith (fun () -> failwith "control")
          | _ -> Transform.defaultControl transform
        { name = str p "name" ""
          defaultValue = int' p "default" 0
          minValue = int' p "min" 0
          maxValue = int' p "max" (if transform = Boolean then 1 else 127)
          transform = transform
          control = control
          linked = bool' p "linked"
          valueText =
            items p "value_text" |> Array.map (fun v ->
              match v.TryGetProperty "value" with
              | true, x -> { first = x.GetInt32(); last = x.GetInt32(); text = str v "text" "" }
              | _ -> { first = int' v "min" 0; last = int' v "max" 0; text = str v "text" "" }) })
    let program = root.GetProperty "program"
    let patches =
      [| for r in items program "relocations" do
           if r.ValueKind = JsonValueKind.String then Relocation(r.GetString() |> string, 1)
           else Relocation(str r "symbol" "", int' r "word_delta" 1)
         for i in items program "imports" do
           let abi = int' i "abi_symbol" 0
           Import(str i "symbol" "", int' i "word_delta" 1, int' i "kind" (importKind abi), abi) |]
    let version =
      match (str root "machine_version" "0.0").Split '.' with
      | [| a; b |] -> int a, int b
      | _ -> 0, 0
    Ok
      { id = str root "id" ""
        displayName = str root "display_name" ""
        category = str root "category" "USR"
        shortLabel = str root "short_label" ""
        categoryOrder = int' root "category_order" 0
        machineVersion = version
        tonal = bool' root "tonal"
        neighbour = bool' root "neighbour"
        stereoPair = bool' root "stereo_pair"
        continuous = str root "retrigger" "restart" = "continuous"
        parameters = parameters
        initSymbol = str program "init_symbol" "INIT"
        trigSymbol = str program "trig_symbol" "TRIG"
        renderSymbol = str program "render_symbol" "RENDER"
        xWorkset = int' program "x_workset_words" 0
        yWorkset = int' program "y_workset_words" 0
        patches = patches
        maxTrackCycles = int' root "max_track_cycles" 0
        description = str root "description" "" }
  with e -> Error(Invalid e.Message)

// ---------------------------------------------------------------------------------------------
// Packages (section 1).

let crc32 (data: byte array) =
  let table =
    [| for n in 0u .. 255u ->
         let mutable c = n
         for _ in 0 .. 7 do
           c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1
         c |]
  let mutable crc = 0xFFFFFFFFu
  for b in data do
    crc <- table[int ((crc ^^^ uint32 b) &&& 0xFFu)] ^^^ (crc >>> 8)
  crc ^^^ 0xFFFFFFFFu

let padded (text: string) size =
  let bytes = Encoding.ASCII.GetBytes text
  Array.init size (fun i -> if i < bytes.Length then bytes[i] else 0uy)

/// The MDM1 machine description: 128 bytes plus 8 per value-text entry.
let description (m: Machine) =
  let d = Array.zeroCreate<byte> 128
  Array.blit "MDM1"B 0 d 0 4
  d[4] <- 1uy
  d[5] <- byte m.parameters.Length
  d[6] <- byte ((if m.stereoPair then 0x10 else 0) ||| (if m.tonal then 0x01 else 0)
                ||| (if m.neighbour then 0x08 else 0) ||| (if m.continuous then 0x04 else 0))
  m.parameters |> Array.iteri (fun i p -> if p.linked then d[7] <- d[7] ||| byte (1 <<< i))
  Array.blit (padded m.category 4) 0 d 8 4
  Array.blit (padded m.shortLabel 4) 0 d 12 4
  d[15] <- byte m.categoryOrder
  Array.blit (padded m.displayName 16) 0 d 16 16
  Array.blit (padded m.id 32) 0 d 32 32
  m.parameters |> Array.iteri (fun i p ->
    let o = 64 + 8 * i
    Array.blit (padded p.name 4) 0 d o 4
    d[o + 4] <- byte p.defaultValue
    d[o + 5] <- byte p.minValue
    d[o + 6] <- byte p.maxValue
    d[o + 7] <- byte ((Control.code p.control <<< 4) ||| Transform.code p.transform))
  [| yield! d
     for i, p in Array.indexed m.parameters do
       for v in p.valueText do
         yield! [| byte i; byte v.first; byte v.last; 0uy |]
         yield! padded v.text 4 |]

/// A 32x24 sprite from an ASCII P1 PBM: 32 big-endian columns, pixel (x,y) at bit y+8.
let spriteOfPbm (text: string) =
  let tokens =
    text.Split '\n'
    |> Array.collect (fun l ->
      let code = l.Split('#')[0]
      code.Split([| ' '; '\t'; '\r' |], StringSplitOptions.RemoveEmptyEntries))
  let pixels = tokens[3..]
  let sprite = Array.zeroCreate<byte> 128
  for y in 0 .. 23 do
    for x in 0 .. 31 do
      if pixels[y * 32 + x] = "1" then
        let bit = 8 + y
        let o = x * 4 + (3 - bit / 8)
        sprite[o] <- sprite[o] ||| byte (1 <<< (bit % 8))
  sprite

let align4 n = (n + 3) &&& ~~~3

/// Build a package from a definition, the assembled program (from address 0) and its symbols.
let pack (m: Machine) (program: int array) (symbols: Map<string, int64>) (sprite: byte array option) =
  let address label delta =
    match Map.tryFind label symbols with
    | Some v -> Ok(int v + delta)
    | None -> Error(MissingSymbol label)
  let resolved =
    m.patches
    |> Array.map (fun p ->
      match p with
      | Relocation(l, d) -> address l d |> Result.map (fun w -> Choice1Of2 w)
      | Import(l, d, kind, abi) -> address l d |> Result.map (fun w -> Choice2Of2(w, kind, abi)))
  match resolved |> Array.tryPick (function Error e -> Some e | _ -> None),
        address m.initSymbol 0, address m.trigSymbol 0, address m.renderSymbol 0 with
  | Some e, _, _, _ -> Error e
  | None, Error e, _, _ | None, _, Error e, _ | None, _, _, Error e -> Error e
  | None, Ok init, Ok trig, Ok render ->
    let words = program.Length
    let code = [| for w in program do yield! [| byte (w >>> 16); byte (w >>> 8); byte w |] |]
    let relocations = resolved |> Array.choose (function Ok(Choice1Of2 w) -> Some w | _ -> None) |> Array.sort
    let imports =
      resolved
      |> Array.choose (function Ok(Choice2Of2 i) -> Some i | _ -> None)
      |> Array.sortBy (fun (w, _, _) -> w)
    let relocBytes = Array.zeroCreate<byte> ((words + 7) / 8)
    for w in relocations do relocBytes[w / 8] <- relocBytes[w / 8] ||| byte (1 <<< (w &&& 7))
    let importBytes = [| for w, kind, abi in imports do yield! [| byte (w >>> 8); byte w; byte kind; byte abi |] |]
    let desc = description m
    let bitmap = defaultArg sprite (Array.zeroCreate 128)
    let relocOffset = align4 (128 + code.Length)
    let importOffset = align4 (relocOffset + relocBytes.Length)
    let descOffset = align4 (importOffset + importBytes.Length)
    let bitmapOffset = align4 (descOffset + desc.Length)
    let size = align4 (bitmapOffset + bitmap.Length)
    let p = Array.zeroCreate<byte> size
    let u16 o (v: int) = p[o] <- byte (v >>> 8); p[o + 1] <- byte v
    let u32 o (v: uint32) = p[o] <- byte (v >>> 24); p[o + 1] <- byte (v >>> 16); p[o + 2] <- byte (v >>> 8); p[o + 3] <- byte v
    Array.blit "MDS1"B 0 p 0 4
    u16 4 1
    u16 6 128
    u32 8 (uint32 size)
    u16 16 0x5603
    u16 18 1
    u16 22 words
    u16 24 imports.Length
    u16 26 m.xWorkset
    u16 28 m.yWorkset
    u16 30 ((fst m.machineVersion <<< 8) ||| snd m.machineVersion)
    u32 32 128u
    u32 36 (uint32 code.Length)
    u32 40 (uint32 code.Length)
    u32 44 (uint32 relocOffset)
    u32 48 (uint32 importOffset)
    u16 52 desc.Length
    u32 56 (uint32 descOffset)
    u16 64 init
    u16 66 trig
    u16 68 render
    u16 70 m.maxTrackCycles
    u32 76 (crc32 code)
    u32 80 (crc32 desc)
    Array.blit code 0 p 128 code.Length
    Array.blit relocBytes 0 p relocOffset relocBytes.Length
    Array.blit importBytes 0 p importOffset importBytes.Length
    Array.blit desc 0 p descOffset desc.Length
    Array.blit bitmap 0 p bitmapOffset bitmap.Length
    let canonical = Array.copy p
    Array.fill canonical 84 36 0uy
    let digest = Security.Cryptography.SHA256.HashData(Array.append canonical code)
    Array.blit digest 0 p 88 32
    u32 84 (crc32 p)
    Ok p

// ---------------------------------------------------------------------------------------------
// Reading.

type Package =
  { programWords : int array
    relocations  : int array
    imports      : (int * int * int) array
    description  : byte array
    sprite       : byte array
    init         : int
    trig         : int
    render       : int
    xWorkset     : int
    yWorkset     : int
    maxCycles    : int }

let read (bytes: byte array) =
  let u16 o = (int bytes[o] <<< 8) ||| int bytes[o + 1]
  let u32 o = (u16 o <<< 16) ||| u16 (o + 2)
  if bytes.Length < 128 || Encoding.ASCII.GetString(bytes, 0, 4) <> "MDS1" then Error NotMds
  else
    let n = u16 22
    let offset = u32 32
    let relocOffset = u32 44
    let importOffset = u32 48
    let descOffset = u32 56
    let descSize = u16 52
    if offset + 3 * n > bytes.Length || descOffset + descSize + 128 > bytes.Length then Error Truncated
    else
      Ok
        { programWords = [| for i in 0 .. n - 1 -> (int bytes[offset + 3 * i] <<< 16) ||| (int bytes[offset + 3 * i + 1] <<< 8) ||| int bytes[offset + 3 * i + 2] |]
          relocations  = [| for w in 0 .. n - 1 do if bytes[relocOffset + w / 8] &&& byte (1 <<< (w &&& 7)) <> 0uy then w |]
          imports      = [| for i in 0 .. u16 24 - 1 -> let o = importOffset + 4 * i in u16 o, int bytes[o + 2], int bytes[o + 3] |]
          description  = bytes[descOffset .. descOffset + descSize - 1]
          sprite       = bytes[align4 (descOffset + descSize) .. align4 (descOffset + descSize) + 127]
          init         = u16 64
          trig         = u16 66
          render       = u16 68
          xWorkset     = u16 26
          yWorkset     = u16 28
          maxCycles    = u16 70 }

/// Check a package's CRCs and content ID.
let verify (bytes: byte array) =
  if bytes.Length < 128 || Encoding.ASCII.GetString(bytes, 0, 4) <> "MDS1" then Error NotMds
  else
    let u32 o = (uint32 bytes[o] <<< 24) ||| (uint32 bytes[o + 1] <<< 16) ||| (uint32 bytes[o + 2] <<< 8) ||| uint32 bytes[o + 3]
    let offset, length = int (u32 32), int (u32 36)
    let program = bytes[offset .. offset + length - 1]
    let withoutCrc = Array.copy bytes
    Array.fill withoutCrc 84 4 0uy
    let canonical = Array.copy bytes
    Array.fill canonical 84 36 0uy
    let digest = Security.Cryptography.SHA256.HashData(Array.append canonical program)
    if crc32 program <> u32 76 then Error(Invalid "program CRC")
    elif crc32 withoutCrc <> u32 84 then Error(Invalid "package CRC")
    elif digest <> bytes[88..119] then Error(Invalid "content ID")
    else Ok()

/// A machine definition from a package's MDM1 description and header (the JSON-only fields,
/// `description` and patch labels, are left empty).
let machineOf (pkg: Package) (header: byte array) =
  let d = pkg.description
  let text (o: int) (n: int) = Encoding.ASCII.GetString(d, o, n).TrimEnd('\000')
  let transformOf code =
    match code with
    | 0 -> Raw14 | 1 -> UnipolarQ23 | 2 -> UnipolarSquareQ23 | 3 -> BipolarQ23
    | 4 -> Pitch | 5 -> Boolean | 6 -> Index | _ -> Decay
  let controlOf code =
    match code with
    | 0 -> Continuous | 1 -> Bipolar | 2 -> Percent | 3 -> Switch | 4 -> Rotary | _ -> PitchControl
  let count = int d[5]
  let entries = [| for o in 128 .. 8 .. d.Length - 8 -> int d[o], int d[o + 1], int d[o + 2], Encoding.ASCII.GetString(d, o + 4, 4).TrimEnd('\000') |]
  { machine (text 32 32) (text 16 16) (text 8 4) (text 12 3) with
      categoryOrder = int d[15]
      machineVersion = (int header[30], int header[31])
      tonal = d[6] &&& 0x01uy <> 0uy
      neighbour = d[6] &&& 0x08uy <> 0uy
      stereoPair = d[6] &&& 0x10uy <> 0uy
      continuous = d[6] &&& 0x04uy <> 0uy
      parameters =
        [| for i in 0 .. count - 1 ->
             let o = 64 + 8 * i
             { name = text o 4
               defaultValue = int d[o + 4]
               minValue = int d[o + 5]
               maxValue = int d[o + 6]
               transform = transformOf (int d[o + 7] &&& 15)
               control = controlOf (int d[o + 7] >>> 4)
               linked = (int d[7] >>> i) &&& 1 = 1
               valueText = [| for p, first, last, t in entries do if p = i then { first = first; last = last; text = t } |] } |]
      xWorkset = pkg.xWorkset
      yWorkset = pkg.yWorkset
      maxTrackCycles = pkg.maxCycles }
