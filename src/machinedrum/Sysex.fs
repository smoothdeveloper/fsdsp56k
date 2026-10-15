/// MDS machine transfers as MIDI SysEx (jmamma/MDS docs/MDX_MachineDumpProtocol.txt): `.syx`
/// files to packages and back. Open-loop dumps only (what a SysEx file holds).
module machinedrum.Sysex

open System

let init = [| 0xf0uy; 0x00uy; 0x20uy; 0x3cuy; 0x02uy; 0x00uy |]
let packetBytes = 420

type Command =
  | DumpHeader = 0x01
  | DataPacket = 0x02
  | EndOfDump = 0x7b

type Message =
  { command     : int
    transaction : int
    slot        : int
    sequence    : int
    body        : byte array }

type SysexError =
  | Malformed   of offset: int
  | BadChecksum of offset: int
  | Incomplete

/// 7-bit packing: a mask byte (bits 6..0 = high bits of data bytes 0..6) then up to 7 data bytes.
let pack7 (body: byte array) =
  [| for group in Array.chunkBySize 7 body do
       group |> Array.mapi (fun i v -> (int v >>> 7) <<< (6 - i)) |> Array.sum |> byte
       for v in group do
         v &&& 0x7fuy |]

let unpack7 (packed: byte array) =
  [| for group in Array.chunkBySize 8 packed do
       let mask = int group[0]
       for i in 1 .. group.Length - 1 do
         byte (int group[i] ||| (((mask >>> (7 - i)) &&& 1) <<< 7)) |]

let message (command: Command) transaction slot sequence (body: byte array) =
  let checked' = [| byte (transaction >>> 7); byte (transaction &&& 0x7f); byte slot; byte sequence; 0uy; yield! pack7 body |]
  let checksum = (checked' |> Array.sumBy int) &&& 0x3fff
  let length = checked'.Length + 4
  [| yield! init
     0x74uy
     0x01uy
     byte command
     yield! checked'
     byte (checksum >>> 7)
     byte (checksum &&& 0x7f)
     byte (length >>> 7)
     byte (length &&& 0x7f)
     0xf7uy |]

/// A package as an open-loop dump to `slot` (1..16), as mds_to_syx.py writes it (transaction 1).
let ofPackage (slot: int) (package: byte array) =
  let size = package.Length
  let count = (size + packetBytes - 1) / packetBytes
  let descriptor =
    [| byte (size >>> 24); byte (size >>> 16); byte (size >>> 8); byte size
       byte count
       0uy
       yield! package[84..87]        // package CRC-32
       yield! package[88..119] |]    // content ID
  [| yield! message Command.DumpHeader 1 (slot - 1) 0x7f descriptor
     for s in 0 .. count - 1 do
       yield! message Command.DataPacket 1 (slot - 1) s package[s * packetBytes .. min size ((s + 1) * packetBytes) - 1]
     yield! message Command.EndOfDump 1 (slot - 1) count [||] |]

/// The USR-machine messages of a SysEx stream (other SysEx is skipped).
let messages (bytes: byte array) =
  let out = ResizeArray<Message>()
  let mutable error = None
  let mutable i = 0
  while i < bytes.Length && error.IsNone do
    if bytes[i] = 0xf0uy then
      let fin = Array.IndexOf(bytes, 0xf7uy, i)
      if fin < 0 then error <- Some(Malformed i)
      else
        let m = bytes[i .. fin]
        if m.Length >= 19 && m[0..5] = init && m[6] = 0x74uy then
          let checked' = m[9 .. m.Length - 6]
          let checksum = (int m[m.Length - 5] <<< 7) ||| int m[m.Length - 4]
          if (checked' |> Array.sumBy int) &&& 0x3fff <> checksum then error <- Some(BadChecksum i)
          else
            out.Add
              { command = int m[8]
                transaction = (int m[9] <<< 7) ||| int m[10]
                slot = int m[11]
                sequence = int m[12]
                body = unpack7 m[14 .. m.Length - 6] }
        i <- fin + 1
    else i <- i + 1
  match error with
  | Some e -> Error e
  | None -> Ok(out.ToArray())

/// Every machine package in a SysEx stream, with the slot (1..16) it was addressed to.
let packages (bytes: byte array) =
  messages bytes
  |> Result.bind (fun ms ->
    let result = ResizeArray<int * byte array>()
    let mutable current : (int * int * ResizeArray<byte>) option = None
    for m in ms do
      match m.command, current with
      | 0x01, _ ->
        let size = (int m.body[0] <<< 24) ||| (int m.body[1] <<< 16) ||| (int m.body[2] <<< 8) ||| int m.body[3]
        current <- Some(m.slot + 1, size, ResizeArray())
      | 0x02, Some(_, _, data) -> data.AddRange m.body
      | 0x7b, Some(slot, size, data) when data.Count = size ->
        result.Add(slot, data.ToArray())
        current <- None
      | _ -> ()
    if result.Count = 0 then Error Incomplete else Ok(result.ToArray()))
