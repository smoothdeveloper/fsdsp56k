/// Mono WAV files of 24-bit DSP words.
module machinedrum.run.Wav

open System.IO

let write (path: string) (sampleRate: int) (words: int array) =
  use f = File.Create path
  use w = new BinaryWriter(f)
  let data = words.Length * 3
  w.Write("RIFF"B)
  w.Write(36 + data)
  w.Write("WAVEfmt "B)
  w.Write(16)
  w.Write(1s)
  w.Write(1s)
  w.Write(sampleRate)
  w.Write(sampleRate * 3)
  w.Write(3s)
  w.Write(24s)
  w.Write("data"B)
  w.Write(data)
  for s in words do
    w.Write(byte s)
    w.Write(byte (s >>> 8))
    w.Write(byte (s >>> 16))

let u16Of (d: byte array) p = uint16 d[p] ||| (uint16 d[p + 1] <<< 8)

/// 16- or 24-bit PCM WAV to 24-bit words (channels averaged).
let read (path: string) =
  let b = File.ReadAllBytes path
  let u16 o = int b[o] ||| (int b[o + 1] <<< 8)
  let u32 o = u16 o ||| (u16 (o + 2) <<< 16)
  let mutable o = 12
  let mutable channels, bits, rate, data = 1, 16, 44100, [||]
  while o + 8 <= b.Length do
    let id = System.Text.Encoding.ASCII.GetString(b, o, 4)
    let size = u32 (o + 4)
    if id = "fmt " then
      channels <- u16 (o + 10)
      rate <- u32 (o + 12)
      bits <- u16 (o + 22)
    elif id = "data" then data <- b[o + 8 .. o + 8 + size - 1]
    o <- o + 8 + size + (size &&& 1)
  let width = bits / 8
  let frames = data.Length / (width * channels)
  [| for i in 0 .. frames - 1 ->
       let sum =
         [| for c in 0 .. channels - 1 ->
              let p = (i * channels + c) * width
              if width = 2 then (int (int16 (u16Of data p))) <<< 8
              else (int data[p] ||| (int data[p + 1] <<< 8) ||| (int (sbyte data[p + 2]) <<< 16)) |]
         |> Array.sum
       (sum / channels) &&& 0xffffff |], rate
