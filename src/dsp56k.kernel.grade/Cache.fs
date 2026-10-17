/// A content-addressed cache of grading runs, shared by the processes of one machine.
///
/// A run (the .NET kernel, the compiled program or a reference on the emulator, for one
/// scenario) is keyed by what it depends on: for the emulator, the memory and registers of the
/// session as started (the program, the firmware, the tables), the emulator library, the
/// scenario's parameter words and the harness; for .NET, the kernel's reflected definitions
/// (and theirs, transitively), the values of the script-level bindings they read, the assemblies
/// they call into and the scenario. A hit is the recorded run; a miss runs and records it.
///
/// On when FSDSP56K_GRADE_CACHE names the store's folder. FSDSP56K_GRADE_CACHE_MODE: `on`
/// (default), `off`, or `verify` (run anyway and compare with the record: a difference is a
/// nondeterministic run or an input the key misses). FSDSP56K_GRADE_SALT joins every key (a
/// caller's hash of code the key cannot see, a harness's calling convention say).
/// FSDSP56K_GRADE_CACHE_EVENTS names a file each lookup appends a line to (hit, miss, waited,
/// verified, differs), for statistics.
///
/// Entries are `entries/<2 hex>/<key>.bin` (the run, gzip) and `<key>.json` (the inputs' and
/// the payload's hashes, the time it took, where and when), written to a temporary file and
/// renamed, the manifest last. A process about to compute a key holds `<key>.lock` (created
/// exclusively, naming the host, the process and the time); another one waits for its result.
/// A lock is stale after FSDSP56K_GRADE_CACHE_STALE seconds (default 1800) or when its
/// process, on this host, is gone.
module dsp56k.kernel.GradeCache

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Reflection
open System.Reflection.Metadata
open System.Reflection.Metadata.Ecma335
open System.Reflection.PortableExecutable
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes
open System.Threading
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns

type Mode =
  | Off
  | On
  | Verify

let private env (name: string) =
  match Environment.GetEnvironmentVariable name with
  | null | "" -> None
  | v         -> Some v

let directory () = env "FSDSP56K_GRADE_CACHE"

let mode () =
  match directory (), env "FSDSP56K_GRADE_CACHE_MODE" with
  | None, _                  -> Off
  | Some _, Some "off"       -> Off
  | Some _, Some "verify"    -> Verify
  | Some _, _                -> On

let hex (bytes: byte array) = Convert.ToHexString(bytes).ToLowerInvariant()
let sha (bytes: byte array) = hex (SHA256.HashData bytes)
let shaText (s: string) = sha (Encoding.UTF8.GetBytes s)

let private files = ConcurrentDictionary<string * int64 * DateTime, string>()

/// A file's content hash, computed once per (path, length, time written) in a process.
let fileHash (path: string) =
  let i = FileInfo path
  files.GetOrAdd((i.FullName, i.Length, i.LastWriteTimeUtc), fun _ -> sha (File.ReadAllBytes path))

/// An assembly file's content without what changes when nothing it does changes: its metadata
/// (the module's MVID and the build's source revision blanked), its method bodies, its fields' initial data and its resources,
/// but not the debug directory nor F#'s cross-assembly optimisation and signature data (which
/// carry the stamps of the assemblies it references: rebuilt against a changed reference, an
/// assembly whose code is the same keeps its hash).
let contentHash (path: string) =
  use pe = new PEReader(File.OpenRead path)
  let md = pe.GetMetadataReader()
  use h = IncrementalHash.CreateHash HashAlgorithmName.SHA256
  let add (b: byte array) =
    h.AppendData(BitConverter.GetBytes b.Length)
    h.AppendData b
  let meta = Seq.toArray (pe.GetMetadata().GetContent())
  Array.fill meta (md.GetHeapMetadataOffset HeapIndex.Guid) (md.GetHeapSize HeapIndex.Guid) 0uy
  // the source revision the SDK appends to the informational version (`1.0.0+<commit>`): the
  // commit of whatever repository the build sits in, not of the code
  let hexDigit (b: byte) = (b >= '0'B && b <= '9'B) || (b >= 'a'B && b <= 'f'B)
  for i in 0 .. meta.Length - 41 do
    if meta[i] = '+'B && Seq.forall (fun k -> hexDigit meta[i + k]) (seq { 1 .. 40 }) then
      Array.fill meta (i + 1) 40 0uy
  add meta
  for m in md.MethodDefinitions do
    let d = md.GetMethodDefinition m
    if d.RelativeVirtualAddress <> 0 then
      match pe.GetMethodBody(d.RelativeVirtualAddress).GetILBytes() with
      | null -> ()
      | il -> add il
  for f in md.FieldDefinitions do
    let d = md.GetFieldDefinition f
    let rva = d.GetRelativeVirtualAddress()
    if rva <> 0 then
      // the field's own bytes: its type's size (a value type's layout, else a primitive's)
      let mutable signature = md.GetBlobReader d.Signature
      signature.ReadSignatureHeader() |> ignore
      let size =
        match signature.ReadSignatureTypeCode() with
        | SignatureTypeCode.TypeHandle ->
          let t = signature.ReadTypeHandle()
          if t.Kind = HandleKind.TypeDefinition then (md.GetTypeDefinition(TypeDefinitionHandle.op_Explicit t)).GetLayout().Size else 0
        | SignatureTypeCode.Boolean | SignatureTypeCode.Byte | SignatureTypeCode.SByte -> 1
        | SignatureTypeCode.Int16 | SignatureTypeCode.UInt16 | SignatureTypeCode.Char -> 2
        | SignatureTypeCode.Int32 | SignatureTypeCode.UInt32 | SignatureTypeCode.Single -> 4
        | _ -> 8
      let data = pe.GetSectionData rva
      add (Seq.toArray (data.GetContent(0, min size data.Length)))
  let fsharpData (name: string) =
    [ "FSharpOptimizationData"; "FSharpSignatureData"; "FSharpOptimizationCompressedData"; "FSharpSignatureCompressedData" ]
    |> List.exists name.StartsWith
  match pe.PEHeaders.CorHeader with
  | null -> ()
  | cor ->
    for r in md.ManifestResources do
      let res = md.GetManifestResource r
      if res.Implementation.IsNil && not (fsharpData (md.GetString res.Name)) then
        let mutable reader = (pe.GetSectionData(cor.ResourcesDirectory.RelativeVirtualAddress + int res.Offset)).GetReader()
        let n = reader.ReadInt32()
        add (reader.ReadBytes n)
  hex (h.GetHashAndReset())

let private assemblies = ConcurrentDictionary<string * int64 * DateTime, string>()

/// The content hash of an assembly's file (None: made in memory, as a script's).
let assemblyHash (a: Assembly) =
  if a.IsDynamic || String.IsNullOrEmpty a.Location || not (File.Exists a.Location) then None
  else
    let i = FileInfo a.Location
    Some(assemblies.GetOrAdd((i.FullName, i.Length, i.LastWriteTimeUtc), fun _ ->
      try contentHash a.Location with _ -> fileHash a.Location))

/// What a key is made of: named parts, each a hash, in the order added.
type Key() =
  let parts = ResizeArray<string * string>()
  member _.Hash(name: string, hash: string) = parts.Add(name, hash)
  member this.Bytes(name: string, bytes: byte array) = this.Hash(name, sha bytes)
  member this.Text(name: string, text: string) = this.Hash(name, shaText text)
  member _.Parts = List.ofSeq parts
  member _.Value =
    let salt = env "FSDSP56K_GRADE_SALT" |> Option.defaultValue ""
    shaText (String.concat "\n" [ yield "salt=" + salt; for n, h in parts -> n + "=" + h ])

// ---------------------------------------------------------------------------------------------
// What a kernel's .NET run depends on.

/// A value as text, arrays element by element (`%A` elides long ones).
let rec private valueText (v: obj | null) =
  match v with
  | null -> "null"
  | :? string as s -> s
  | :? Array as a -> "[|" + String.Join(";", [| for x in a -> valueText x |]) + "|]"
  | v -> sprintf "%A" v

/// The text of a method's reflected definition and every one it calls, the values of the
/// script-level bindings they read, and the assemblies of everything else they reach.
let definitionText (methods: MethodBase list) =
  let seen = HashSet<MethodBase>()
  let assemblies = SortedSet<string>(StringComparer.Ordinal)
  let values = SortedDictionary<string, string>(StringComparer.Ordinal)
  let text = StringBuilder()
  let typeName (t: Type | null) = match t with null -> "" | t -> string t
  let reach (t: Type | null) =
    match t with
    | null -> ()
    | t ->
      match assemblyHash t.Assembly with
      | Some h -> assemblies.Add($"{t.Assembly.FullName}={h}") |> ignore
      | None -> ()
  let rec visitMethod (m: MethodBase) =
    if seen.Add m then
      reach m.DeclaringType
      match Expr.TryGetReflectedDefinition m with
      | Some e ->
        text.Append(typeName m.DeclaringType).Append('.').Append(m.Name).Append('\n').Append(e.ToString()).Append('\n') |> ignore
        visit e
      | None -> ()
  and visit (e: Expr) =
    match e with
    | Call(_, mi, _) -> visitMethod mi
    | NewObject(ci, _) -> reach ci.DeclaringType
    | PropertyGet(None, pi, []) when (match pi.DeclaringType with null -> false | t -> t.Assembly.IsDynamic) ->
      // a script's binding: its value, the code reading it sees
      let name = typeName pi.DeclaringType + "." + pi.Name
      if not (values.ContainsKey name) then
        values[name] <- (try valueText (pi.GetValue null) with ex -> "unreadable " + ex.GetType().Name)
      match pi.GetMethod with
      | null -> ()
      | g -> visitMethod g
    | PropertyGet(_, pi, _) | PropertySet(_, pi, _, _) -> reach pi.DeclaringType
    | FieldGet(_, fi) | FieldSet(_, fi, _) -> reach fi.DeclaringType
    | _ -> ()
    match e with
    | ExprShape.ShapeVar _ -> ()
    | ExprShape.ShapeLambda(_, b) -> visit b
    | ExprShape.ShapeCombination(_, xs) -> List.iter visit xs
  for m in methods do visitMethod m
  String.concat "\n"
    [ yield text.ToString()
      for KeyValue(n, v) in values -> n + " = " + v
      yield! assemblies ]

/// A record type's shape: its properties, their types and attributes.
let typeText (t: Type) =
  String.concat "\n"
    [ yield string t
      for p in t.GetProperties() do
        yield p.Name + " : " + string p.PropertyType
        for a in p.GetCustomAttributesData() -> "  " + string a ]

// ---------------------------------------------------------------------------------------------
// The store.

let private host = Environment.MachineName
let private pid = Environment.ProcessId

let private entry (dir: string) (key: string) =
  let d = Path.Combine(dir, "entries", key.Substring(0, 2))
  Directory.CreateDirectory d |> ignore
  Path.Combine(d, key)

/// Written to a temporary file, then renamed over the name.
let private writeAtomic (path: string) (bytes: byte array) =
  let tmp = $"{path}.{host}.{pid}.{Guid.NewGuid():N}.tmp"
  File.WriteAllBytes(tmp, bytes)
  File.Move(tmp, path, true)

let private gzip (bytes: byte array) =
  use m = new MemoryStream()
  (use z = new GZipStream(m, CompressionLevel.Fastest)
   z.Write(bytes, 0, bytes.Length))
  m.ToArray()

let private gunzip (bytes: byte array) =
  use z = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress)
  use m = new MemoryStream()
  z.CopyTo m
  m.ToArray()

let private eventsLock = obj ()

let private event (what: string) (kind: string) (label: string) (key: string) (ms: float) =
  let line = String.concat "\t" [ DateTime.UtcNow.ToString "o"; host; string pid; what; kind; label; key; sprintf "%.1f" ms ] + "\n"
  let path =
    match env "FSDSP56K_GRADE_CACHE_EVENTS", directory () with
    | Some p, _ -> Some p
    | None, Some d -> Some(Path.Combine(d, "events", host + ".tsv"))
    | None, None -> None
  path |> Option.iter (fun p ->
    lock eventsLock (fun () ->
      Path.GetDirectoryName p |> Option.ofObj |> Option.iter (Directory.CreateDirectory >> ignore)
      File.AppendAllText(p, line)))

/// The recorded payload, if its manifest is there and the payload still has its hash.
let private read (path: string) =
  let manifest, payload = path + ".json", path + ".bin"
  if not (File.Exists manifest && File.Exists payload) then None
  else
    try
      let packed = File.ReadAllBytes payload
      match JsonNode.Parse(File.ReadAllText manifest) with
      | null -> None
      | m ->
        match m["payload"] with
        | null -> None
        | h when h.GetValue<string>() = sha packed -> Some(gunzip packed)
        | _ -> None
    with _ -> None

let private write (path: string) (kind: string) (label: string) (key: Key) (payload: byte array) (ms: float) =
  let packed = gzip payload
  writeAtomic (path + ".bin") packed
  let inputs = JsonObject()
  for n, h in key.Parts do inputs[n] <- JsonValue.Create h
  let m = JsonObject()
  m["key"] <- JsonValue.Create key.Value
  m["kind"] <- JsonValue.Create kind
  m["label"] <- JsonValue.Create label
  m["salt"] <- JsonValue.Create(env "FSDSP56K_GRADE_SALT" |> Option.defaultValue "")
  m["inputs"] <- inputs
  m["payload"] <- JsonValue.Create(sha packed)
  m["bytes"] <- JsonValue.Create payload.Length
  m["milliseconds"] <- JsonValue.Create ms
  m["host"] <- JsonValue.Create host
  m["process"] <- JsonValue.Create pid
  m["created"] <- JsonValue.Create(DateTime.UtcNow.ToString "o")
  writeAtomic (path + ".json") (Encoding.UTF8.GetBytes(m.ToJsonString()))

let private staleAfter () =
  env "FSDSP56K_GRADE_CACHE_STALE" |> Option.bind (fun s -> match Int32.TryParse s with | true, n -> Some n | _ -> None) |> Option.defaultValue 1800

let private alive (p: int) =
  try
    use proc = Process.GetProcessById p
    not proc.HasExited
  with _ -> false

/// Whether the lock's holder has given up: too old, or its process on this host is gone.
let private stale (lockPath: string) =
  try
    let age = DateTime.UtcNow - File.GetLastWriteTimeUtc lockPath
    if age.TotalSeconds > float (staleAfter ()) then true
    else
      match (File.ReadAllText lockPath).Split '\t' with
      | [| h; p; _ |] when h = host -> not (alive (int p))
      | _ -> false
  with _ -> false

/// Take the key's lock (Some: release it with Dispose), or None when another process recorded
/// the result while this one waited.
let private acquire (path: string) =
  let lockPath = path + ".lock"
  let rec go () =
    let taken =
      try
        use f = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write)
        let b = Encoding.UTF8.GetBytes($"{host}\t{pid}\t{DateTime.UtcNow:o}")
        f.Write(b, 0, b.Length)
        true
      with :? IOException -> false
    if taken then Some { new IDisposable with member _.Dispose() = try File.Delete lockPath with _ -> () }
    elif File.Exists(path + ".json") && (read path).IsSome then None
    else
      if stale lockPath then (try File.Delete lockPath with _ -> ())
      else Thread.Sleep 100
      go ()
  go ()

/// The run `compute` makes, through the cache: `kind` and `label` say what it is (in the
/// manifest and the events), `key` what it depends on.
let cached (kind: string) (label: string) (key: Key) (save: 'T -> byte array) (load: byte array -> 'T) (compute: unit -> 'T) : 'T =
  match mode (), directory () with
  | Off, _ | _, None -> compute ()
  | m, Some dir ->
    let k = key.Value
    let path = entry dir k
    let timed (f: unit -> 'U) =
      let sw = Stopwatch.StartNew()
      let r = f ()
      r, sw.Elapsed.TotalMilliseconds
    let fresh () =
      let r, ms = timed compute
      write path kind label key (save r) ms
      r, ms
    match m with
    | Verify ->
      let r, ms = timed compute
      let bytes = save r
      match read path with
      | Some recorded when recorded = bytes -> event "verified" kind label k ms
      | Some _ ->
        event "differs" kind label k ms
        File.WriteAllBytes(path + $".differs.{host}.{pid}.bin", gzip bytes)
      | None ->
        write path kind label key bytes ms
        event "miss" kind label k ms
      r
    | _ ->
      let hit, ms = timed (fun () -> read path)
      match hit with
      | Some bytes -> event "hit" kind label k ms; load bytes
      | None ->
        let sw = Stopwatch.StartNew()
        match acquire path with
        | None ->
          // another process computed it meanwhile
          event "waited" kind label k sw.Elapsed.TotalMilliseconds
          load (read path).Value
        | Some held ->
          use _ = held
          match read path with
          | Some bytes -> event "waited" kind label k sw.Elapsed.TotalMilliseconds; load bytes
          | None ->
            let r, ms = fresh ()
            event "miss" kind label k ms
            r
