/// Explicit protocol codec: works with trimming and NativeAOT without reflection metadata.
module dsp56k.PortableJson

open System.IO
open System.Text
open System.Text.Json
open dsp56k.Portable

let private property name (value: JsonElement) = value.GetProperty(name: string)
let private integer name value = (property name value).GetInt32()
let private text name value = (property name value).GetString() |> string
let private array read (value: JsonElement) = value.EnumerateArray() |> Seq.map read |> Seq.toArray
let private words = array (fun value -> value.GetInt32())

let fixture json : Fixture =
  use document = JsonDocument.Parse(json: string)
  let value = document.RootElement
  { version = integer "version" value; kernel = text "kernel" value
    frames = integer "frames" value; channels = integer "channels" value
    sampleRate = integer "sampleRate" value
    steps = property "steps" value |> array (fun step ->
      { action = text "action" step; words = words (property "words" step); blocks = integer "blocks" step })
    expected = property "expected" value |> array (fun block ->
      { output = words (property "output" block); state = words (property "state" block) })
    buffers = property "buffers" value |> array (fun buffer ->
      { name = text "name" buffer; words = words (property "words" buffer) }) }

let private encode write =
  use stream = new MemoryStream()
  use writer = new Utf8JsonWriter(stream)
  write writer
  writer.Flush()
  Encoding.UTF8.GetString(stream.ToArray())

let private writeWords (writer: Utf8JsonWriter) name (values: int array) =
  writer.WriteStartArray(name: string)
  for value in values do writer.WriteNumberValue value
  writer.WriteEndArray()

let writeFixture (value: Fixture) = encode (fun writer ->
  writer.WriteStartObject()
  writer.WriteNumber("version", value.version)
  writer.WriteString("kernel", value.kernel)
  writer.WriteNumber("frames", value.frames)
  writer.WriteNumber("channels", value.channels)
  writer.WriteNumber("sampleRate", value.sampleRate)
  writer.WriteStartArray "steps"
  for step in value.steps do
    writer.WriteStartObject()
    writer.WriteString("action", step.action)
    writeWords writer "words" step.words
    writer.WriteNumber("blocks", step.blocks)
    writer.WriteEndObject()
  writer.WriteEndArray()
  writer.WriteStartArray "expected"
  for block in value.expected do
    writer.WriteStartObject()
    writeWords writer "output" block.output
    writeWords writer "state" block.state
    writer.WriteEndObject()
  writer.WriteEndArray()
  writer.WriteStartArray "buffers"
  for buffer in value.buffers do
    writer.WriteStartObject()
    writer.WriteString("name", buffer.name)
    writeWords writer "words" buffer.words
    writer.WriteEndObject()
  writer.WriteEndArray()
  writer.WriteEndObject())

let writeMeasurement (value: Measurement) = encode (fun writer ->
  writer.WriteStartObject()
  writer.WriteString("target", value.target)
  writer.WriteString("kernel", value.kernel)
  writer.WriteString("runtime", value.runtime)
  writer.WriteNumber("blocks", value.blocks)
  writer.WriteNumber("samples", value.samples)
  writer.WriteNumber("medianRenderMs", value.medianRenderMs)
  writer.WriteNumber("worstRenderMs", value.worstRenderMs)
  writer.WriteNumber("audioMs", value.audioMs)
  writer.WriteNumber("realTimeFactor", value.realTimeFactor)
  writer.WriteNumber("outputChecks", value.outputChecks)
  writer.WriteNumber("stateChecks", value.stateChecks)
  writer.WriteNumber("bufferChecks", value.bufferChecks)
  writer.WriteEndObject())
