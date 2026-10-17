/// The grades as one self-contained HTML page: a summary, then per kernel the verdicts, the
/// output (kernel over reference) and the cycles per call as SVG, the costs, and three listings
/// side by side: the F# kernel, the generated assembly (each instruction linked to the F# line
/// it came from) and the source the kernel was taken from.
module dsp56k.kernel.Html

open System
open System.IO
open System.Net
open System.Text
open System.Text.RegularExpressions
open dsp56k
open dsp56k.kernel.Grade

/// What the page says about a graded kernel besides its grade.
type Entry =
  { title           : string
    origin          : string                    // where the reference comes from
    license         : string
    notes           : string
    referenceSource : (string * string) option  // file name, text
    report          : Report }

let enc (s: string | null) = match WebUtility.HtmlEncode s with null -> "" | v -> v

let inv (v: float) = v.ToString("0.##", Globalization.CultureInfo.InvariantCulture)
let f2 (v: float) = v.ToString("0.00", Globalization.CultureInfo.InvariantCulture)

// ---------------------------------------------------------------------------------------------
// Highlighting.

let span cls (text: string) = if cls = "" then enc text else $"<span class=\"{cls}\">{enc text}</span>"

let fsKeywords =
  set [ "let"; "mutable"; "for"; "in"; "do"; "done"; "if"; "then"; "else"; "elif"; "type"; "module"; "open"
        "match"; "with"; "fun"; "function"; "rec"; "and"; "not"; "true"; "false"; "member"; "static"; "of"
        "yield"; "return"; "use"; "while"; "to"; "downto"; "namespace"; "inline"; "as"; "when"; "null"; "private" ]

let fsToken =
  Regex(@"(?<com>//.*$|\(\*.*?\*\))|(?<str>""(?:[^""\\]|\\.)*"")|(?<attr>\[<.*?>\])|(?<num>\b0x[0-9a-fA-F]+[lLuU]*\b|\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?[lLuUfmMy]*\b)|(?<id>\b[A-Za-z_][A-Za-z0-9_']*\b)|(?<op><-|->|>>>|<<<|&&&|\|\|\||\|>)",
        RegexOptions.Compiled)

let highlightFs (line: string) =
  let sb = StringBuilder()
  let mutable last = 0
  for m in fsToken.Matches line do
    sb.Append(enc (line.Substring(last, m.Index - last))) |> ignore
    let cls =
      if m.Groups["com"].Success then "k-c"
      elif m.Groups["str"].Success then "k-s"
      elif m.Groups["attr"].Success then "k-a"
      elif m.Groups["num"].Success then "k-n"
      elif m.Groups["op"].Success then "k-o"
      elif fsKeywords.Contains m.Value then "k-k"
      elif Char.IsUpper m.Value[0] then "k-t"
      else ""
    sb.Append(span cls m.Value) |> ignore
    last <- m.Index + m.Length
  sb.Append(enc (line.Substring last)).ToString()

let asmRegisters =
  Regex(@"^(?:[ab][012]?|[ab]10|ab|ba|[xy][01]?|[rnm][0-7]|sr|omr|sp|la|lc|ssh|ssl|ep|vba|sc|sz|mr|ccr|eom)$", RegexOptions.IgnoreCase)

let asmOperand =
  Regex(@"(?<com>;.*$)|(?<str>'[^']*')|(?<imm>#[<>]?)|(?<num>\$[0-9a-fA-F]+|%[01]+|\b\d+\b)|(?<mem>\b[xylp]:)|(?<id>\b[A-Za-z_][A-Za-z0-9_]*\b)", RegexOptions.Compiled)

let highlightAsmOperands (text: string) =
  let sb = StringBuilder()
  let mutable last = 0
  for m in asmOperand.Matches text do
    sb.Append(enc (text.Substring(last, m.Index - last))) |> ignore
    let cls =
      if m.Groups["com"].Success then "k-c"
      elif m.Groups["str"].Success then "k-s"
      elif m.Groups["imm"].Success || m.Groups["num"].Success then "k-n"
      elif m.Groups["mem"].Success then "k-k"
      elif asmRegisters.IsMatch m.Value then "k-r"
      else "k-l"
    sb.Append(span cls m.Value) |> ignore
    last <- m.Index + m.Length
  sb.Append(enc (text.Substring last)).ToString()

let asmLine = Regex(@"^(?<label>[A-Za-z_][A-Za-z0-9_]*:)?(?<ws>\s*)(?<mn>\.?[A-Za-z][A-Za-z0-9.]*)?(?<rest>.*)$")

let highlightAsm (line: string) =
  let t = line.TrimStart()
  if t.StartsWith ";" then span "k-c" line
  elif t.StartsWith "dc " || t.StartsWith ".dc " || t.StartsWith "dc\t" then enc line
  else
    let m = asmLine.Match line
    let label = m.Groups["label"].Value
    let mn = m.Groups["mn"].Value
    // `NAME equ value`: a name in the mnemonic column of a line with no label
    let directive (s: string) =
      Regex.IsMatch(s, @"^\.?(section|endsec|define|dc|ds|equ|org|include|if|else|endif|macro|endm|end)$", RegexOptions.IgnoreCase)
    String.concat "" [
      span "k-l k-d" label
      enc m.Groups["ws"].Value
      (if mn = "" then "" elif directive mn then span "k-k" mn else span "k-m" mn)
      highlightAsmOperands m.Groups["rest"].Value ]

// ---------------------------------------------------------------------------------------------
// Charts.

let toFloat (w: int) = float (if w &&& 0x800000 <> 0 then w - 0x1000000 else w) / 8388608.0

/// Where each step falls in the rendered samples.
let markers (r: Report) =
  let mutable at = 0
  let marked = r.steps |> List.exists (function Mark _ -> true | _ -> false)
  [ for s in r.steps do
      match s with
      | Render n -> at <- at + r.harness.target.blockWords * n
      // a pattern labels its own steps; the raw changes would only crowd them
      | Mark text -> yield at, text, (if text.Contains "TRIG" then "trig" else "set")
      | _ when marked -> ()
      | Trig -> yield at, "TRIG", "trig"
      | Settings v ->
        let names = r.harness.parameterNames
        let text = v |> Array.mapi (fun i x -> $"{(if i < names.Length then names[i] else string i)} {inv x}") |> String.concat " "
        yield at, text, "set"
      | Words _ -> yield at, "words", "set"
      | Init -> () ]

/// Min/max envelope of `xs` over `width` columns, as an SVG path (or a polyline when sparse).
let envelope (xs: float array) (width: float) (y: float -> float) =
  let n = xs.Length
  if n = 0 then ""
  elif float n <= width then
    xs |> Array.mapi (fun i v -> $"{(if i = 0 then 'M' else 'L')}{inv (float i * width / float (max 1 (n - 1)))},{inv (y v)}") |> String.concat ""
  else
    let cols = int width
    let lo = Array.init cols (fun c -> let a, b = c * n / cols, max (c * n / cols + 1) ((c + 1) * n / cols) in xs[a .. b - 1] |> Array.min)
    let hi = Array.init cols (fun c -> let a, b = c * n / cols, max (c * n / cols + 1) ((c + 1) * n / cols) in xs[a .. b - 1] |> Array.max)
    let top = hi |> Array.mapi (fun c v -> $"{(if c = 0 then 'M' else 'L')}{inv (float c + 0.5)},{inv (y v)}") |> String.concat ""
    let bottom = [| for c in cols - 1 .. -1 .. 0 -> $"L{inv (float c + 0.5)},{inv (y lo[c])}" |] |> String.concat ""
    top + bottom + "Z"

let waveform (r: Report) =
  let w, h, pad = 960.0, 240.0, 34.0
  let ours = r.dsp.outputs |> Array.concat |> Array.map toFloat
  let theirs = r.refRun |> Option.map (fun x -> x.outputs |> Array.concat |> Array.map toFloat)
  let n = ours.Length
  let peak = Array.append ours (defaultArg theirs [||]) |> Array.fold (fun m v -> max m (abs v)) 0.0
  let scale = if peak < 1e-9 then 1.0 else min 1.0 (peak * 1.1)
  let y v = pad + (h - 2.0 * pad) * (0.5 - 0.5 * v / scale)
  let x i = float i * w / float (max 1 n)
  let sparse = float n <= w
  let sb = StringBuilder()
  sb.Append($"<svg class=\"chart\" viewBox=\"0 0 {inv w} {inv h}\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"output waveform\">") |> ignore
  sb.Append($"<line class=\"axis\" x1=\"0\" x2=\"{inv w}\" y1=\"{inv (y 0.0)}\" y2=\"{inv (y 0.0)}\"/>") |> ignore
  let bw = r.harness.target.blockWords
  if n / bw <= 96 then
    for b in 1 .. n / bw - 1 do
      sb.Append($"<line class=\"grid\" x1=\"{inv (x (bw * b))}\" x2=\"{inv (x (bw * b))}\" y1=\"{inv pad}\" y2=\"{inv (h - pad)}\"/>") |> ignore
  let marks = markers r
  // a long pattern: bars get labels, trigs only their ticks
  let crowded = (marks |> List.length) > 40
  let marks = if crowded then marks |> List.map (fun (at, text, cls) -> at, (if cls = "trig" then "" else text), cls) else marks
  // labels go on the first row where they do not overlap an earlier one (about 6 units a character)
  let ends = Collections.Generic.List<float>()
  marks |> List.iter (fun (at, text, cls) ->
    let left = x at
    let row =
      match Seq.tryFindIndex (fun e -> e < left) ends with
      | Some r -> r
      | None -> ends.Add -1.0; ends.Count - 1
    ends[row] <- left + 6.2 * float text.Length + 8.0
    sb.Append($"<line class=\"mark {cls}\" x1=\"{inv (x at)}\" x2=\"{inv (x at)}\" y1=\"4\" y2=\"{inv (h - 4.0)}\"/>") |> ignore
    sb.Append($"<text class=\"mark-t {cls}\" x=\"{inv (x at + 3.0)}\" y=\"{12 + 11 * row}\">{enc text}</text>") |> ignore)
  let draw cls (xs: float array) =
    let d = envelope xs w y
    if sparse then sb.Append($"<path class=\"{cls} line\" d=\"{d}\"/>") |> ignore
    else sb.Append($"<path class=\"{cls} area\" d=\"{d}\"/>") |> ignore
  theirs |> Option.iter (draw "s-ref")
  draw "s-ours" ours
  // where the compiled output differs from the reference
  match theirs with
  | Some t ->
    for i in 0 .. min n t.Length - 1 do
      if ours[i] <> t[i] then sb.Append($"<line class=\"diff\" x1=\"{inv (x i)}\" x2=\"{inv (x i)}\" y1=\"{inv (h - pad)}\" y2=\"{inv (h - 6.0)}\"/>") |> ignore
  | None -> ()
  sb.Append($"<text class=\"tick\" x=\"4\" y=\"{inv (y scale + 10.0)}\">{inv scale}</text>") |> ignore
  sb.Append($"<text class=\"tick\" x=\"4\" y=\"{inv (y -scale - 3.0)}\">-{inv scale}</text>") |> ignore
  sb.Append("</svg>").ToString()

/// The first frames after the first TRIG, sample by sample.
let detail (r: Report) =
  let bw = r.harness.target.blockWords
  let first = markers r |> List.tryPick (fun (at, _, cls) -> if cls = "trig" then Some at else None) |> Option.defaultValue 0
  let take (outs: int array array) = outs |> Array.concat |> Array.skip (min first (outs.Length * bw)) |> Array.truncate (3 * bw) |> Array.map toFloat
  let ours = take r.dsp.outputs
  let theirs = r.refRun |> Option.map (fun x -> take x.outputs)
  let w, h, pad = 960.0, 160.0, 14.0
  let peak = Array.append ours (defaultArg theirs [||]) |> Array.fold (fun m v -> max m (abs v)) 0.0
  let scale = if peak < 1e-9 then 1.0 else peak * 1.1
  let y v = pad + (h - 2.0 * pad) * (0.5 - 0.5 * v / scale)
  let x i = 8.0 + float i * (w - 16.0) / float (max 1 (ours.Length - 1))
  let sb = StringBuilder()
  sb.Append($"<svg class=\"chart\" viewBox=\"0 0 {inv w} {inv h}\" role=\"img\" aria-label=\"first frames\">") |> ignore
  sb.Append($"<line class=\"axis\" x1=\"0\" x2=\"{inv w}\" y1=\"{inv (y 0.0)}\" y2=\"{inv (y 0.0)}\"/>") |> ignore
  for b in 1 .. ours.Length / bw do
    sb.Append($"<line class=\"grid\" x1=\"{inv (x (bw * b))}\" x2=\"{inv (x (bw * b))}\" y1=\"{inv pad}\" y2=\"{inv (h - pad)}\"/>") |> ignore
  let poly cls (xs: float array) =
    let pts = xs |> Array.mapi (fun i v -> $"{inv (x i)},{inv (y v)}") |> String.concat " "
    sb.Append($"<polyline class=\"{cls} line\" points=\"{pts}\"/>") |> ignore
  theirs |> Option.iter (fun t ->
    poly "s-ref" t
    t |> Array.iteri (fun i v -> sb.Append($"<circle class=\"s-ref ring\" cx=\"{inv (x i)}\" cy=\"{inv (y v)}\" r=\"4\"/>") |> ignore))
  poly "s-ours" ours
  ours |> Array.iteri (fun i v -> sb.Append($"<circle class=\"s-ours dot\" cx=\"{inv (x i)}\" cy=\"{inv (y v)}\" r=\"2\"/>") |> ignore)
  sb.Append("</svg>").ToString()

/// RENDER cycles per call, the kernel's bars beside the reference's, with the host's limit.
/// More calls than a chart has room for: each bar the worst of its neighbouring calls.
let maxBars = 240

let binned (xs: int64 array) =
  if xs.Length <= maxBars then xs
  else Array.init maxBars (fun c -> xs[c * xs.Length / maxBars .. max (c * xs.Length / maxBars) ((c + 1) * xs.Length / maxBars - 1)] |> Array.max)

let cycles (r: Report) =
  let ours = r.dsp.render |> List.toArray |> binned
  let theirs = r.refRun |> Option.map (fun x -> x.render |> List.toArray |> binned) |> Option.defaultValue [||]
  let limit = r.harness.cycleLimit |> Option.map float |> Option.defaultValue infinity
  let declared = float r.program.declaredCycles
  let top = (Array.append ours theirs |> Array.fold max 0L |> float |> max (if Double.IsInfinity limit then 1.0 else limit)) * 1.08
  let w, h, padL, padB = 620.0, 220.0, 44.0, 18.0
  let n = max 1 ours.Length
  let slot = (w - padL) / float n
  let bar = if theirs.Length > 0 then slot * 0.38 else slot * 0.7
  let y v = (h - padB) * (1.0 - v / top)
  let sb = StringBuilder()
  sb.Append($"<svg class=\"chart\" viewBox=\"0 0 {inv w} {inv h}\" role=\"img\" aria-label=\"cycles per RENDER\">") |> ignore
  for frac in [ 0.25; 0.5; 0.75; 1.0 ] do
    let v = top / 1.08 * frac
    sb.Append($"<line class=\"grid\" x1=\"{inv padL}\" x2=\"{inv w}\" y1=\"{inv (y v)}\" y2=\"{inv (y v)}\"/>") |> ignore
    sb.Append($"<text class=\"tick\" x=\"{inv (padL - 4.0)}\" y=\"{inv (y v + 3.0)}\" text-anchor=\"end\">{int v}</text>") |> ignore
  // one path per series: thousands of bars stay light
  let bars (cls: string) (xs: (float * int64) seq) =
    let d =
      xs |> Seq.map (fun (x, v) -> $"M{inv x},{inv (h - padB)}V{inv (y (float v))}h{inv bar}V{inv (h - padB)}Z") |> String.concat ""
    if d <> "" then sb.Append($"<path class=\"{cls}\" d=\"{d}\"/>") |> ignore
  let x0 i = padL + float i * slot + (slot - (if theirs.Length > 0 then 2.0 * bar + 2.0 else bar)) / 2.0
  let xo i = if theirs.Length > 0 then x0 i + bar + 2.0 else x0 i
  bars "b-ref" (theirs |> Seq.mapi (fun i v -> x0 i, v))
  bars "b-ours" (ours |> Seq.mapi (fun i v -> xo i, v) |> Seq.filter (fun (_, v) -> float v <= limit))
  bars "b-ours over" (ours |> Seq.mapi (fun i v -> xo i, v) |> Seq.filter (fun (_, v) -> float v > limit))
  if not (Double.IsInfinity limit) then
    sb.Append($"<line class=\"limit\" x1=\"{inv padL}\" x2=\"{inv w}\" y1=\"{inv (y limit)}\" y2=\"{inv (y limit)}\"/>") |> ignore
    sb.Append($"<text class=\"limit-t\" x=\"{inv (w - 4.0)}\" y=\"{inv (y limit - 4.0)}\" text-anchor=\"end\">track limit {int limit}</text>") |> ignore
  if declared > 0.0 && declared <> limit then
    sb.Append($"<line class=\"declared\" x1=\"{inv padL}\" x2=\"{inv w}\" y1=\"{inv (y declared)}\" y2=\"{inv (y declared)}\"/>") |> ignore
  let worstNote = if r.dsp.render.Length > maxBars then " (each bar the worst of its calls)" else ""
  sb.Append($"<text class=\"tick\" x=\"{inv padL}\" y=\"{inv (h - 4.0)}\">RENDER 1</text>") |> ignore
  sb.Append($"<text class=\"tick\" x=\"{inv w}\" y=\"{inv (h - 4.0)}\" text-anchor=\"end\">RENDER {r.dsp.render.Length}{worstNote}</text>") |> ignore
  sb.Append("</svg>").ToString()

/// Two horizontal bars, kernel and reference, for the summary table.
let ratioBar (ours: float) (theirs: float option) =
  let top = max ours (defaultArg theirs 0.0) |> max 1.0
  let wid v = 150.0 * v / top
  let refBar = match theirs with Some t -> $"<rect class=\"b-ref\" x=\"0\" y=\"11\" height=\"7\" width=\"{inv (wid t)}\"/>" | None -> ""
  $"<svg class=\"ratio\" viewBox=\"0 0 150 20\" width=\"150\" height=\"20\"><rect class=\"b-ours\" x=\"0\" y=\"2\" height=\"7\" width=\"{inv (wid ours)}\"/>{refBar}</svg>"

// ---------------------------------------------------------------------------------------------
// Sections.

let badge ok (text: string) = $"""<span class="badge {(if ok then "ok" else "bad")}">{enc text}</span>"""

let mismatchText (m: Mismatch) =
  $"{m.what}: {m.differing} words differ; first at block {m.block}, word {m.index}: expected ${m.expected:x6}, got ${m.actual:x6}"

let slug (s: string) = Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')

let fsPane (id: string) (source: string option) =
  match source |> Option.filter File.Exists with
  | None -> "<p class=\"muted\">The kernel's F# source was not found (compile with --quotations-debug+).</p>"
  | Some path ->
    let lines = File.ReadAllLines path
    let rows =
      lines |> Array.mapi (fun i l ->
        $"<div class=\"ln fs\" data-l=\"{i + 1}\">{highlightFs l}</div>")
    $"""<div class="pane"><div class="pane-h">F# kernel <span class="muted">{enc (Path.GetFileName path)}</span></div><div class="code" id="{id}-fs">{String.concat "" rows}</div></div>"""

let generatedPane (id: string) (r: Report) =
  let items, lines = r.compiled.items, r.compiled.lines
  let rows =
    items |> Array.mapi (fun i it ->
      let l = lines[i]
      let text = Print.item it
      let attr = if l > 0 then $" data-l=\"{l}\"" else ""
      $"<div class=\"ln gen\"{attr}>{highlightAsm text}</div>")
  $"""<div class="pane"><div class="pane-h">Generated <span class="muted">{r.ours.programWords} words · hover a line to see its F#</span></div><div class="code" id="{id}-gen">{String.concat "" rows}</div></div>"""

let referencePane (source: (string * string) option) =
  match source with
  | None -> ""
  | Some(name, text) ->
    let rows =
      text.Replace("\r", "").Split('\n') |> Array.mapi (fun i l ->
        $"<div class=\"ln src\">{highlightAsm l}</div>")
    $"""<div class="pane"><div class="pane-h">Ported from <span class="muted">{enc name}</span></div><div class="code">{String.concat "" rows}</div></div>"""

let alignment (r: Report) =
  if r.shapes.Length = 0 then ""
  else
    let block (s: Shape) =
      let rows =
        s.diff |> Array.map (fun l ->
          let mark = l.Substring(0, 1)
          let cls = match mark with "=" -> "same" | "~" -> "chg" | "+" -> "add" | _ -> "del"
          let body = l.Substring(2)
          let ours, theirs = match body.IndexOf " | " with -1 -> body, "" | k -> body.Substring(0, k), body.Substring(k + 3)
          let ours = ours.TrimEnd()
          $"<tr class=\"{cls}\"><td class=\"m\">{enc mark}</td><td>{enc ours}</td><td>{enc theirs}</td></tr>")
      $"""<details><summary><b>{s.entry}</b> · {s.ours} instructions against {s.reference} · edit distance {s.distance}</summary><table class="align"><thead><tr><th></th><th>generated</th><th>reference (disassembled)</th></tr></thead><tbody>{String.concat "" rows}</tbody></table></details>"""
    $"""<h3>Instruction alignment</h3><p class="muted">Mnemonics aligned by edit distance; labels canonicalized. <span class="k same">=</span> same mnemonic, <span class="k chg">~</span> different, <span class="k add">+</span> only generated, <span class="k del">−</span> only the reference.</p>{r.shapes |> Array.map block |> String.concat ""}"""

let costTable (r: Report) =
  let row name (ours: float) (theirs: float option) =
    let t = theirs |> Option.map (fun v -> inv (Math.Round v)) |> Option.defaultValue "–"
    let ratio = match theirs with Some v when v > 0.0 -> $"×{f2 (ours / v)}" | _ -> ""
    $"<tr><td>{name}</td><td class=\"n\">{inv (Math.Round ours)}</td><td class=\"n\">{t}</td><td class=\"n muted\">{ratio}</td></tr>"
  let c, t = r.ours, r.reference
  $"""<table class="cost"><thead><tr><th></th><th class="n">kernel</th><th class="n">reference</th><th></th></tr></thead><tbody>
{row "Program words" (float c.programWords) (t |> Option.map (fun x -> float x.programWords))}
{row "X workset" (float c.xWorkset) (t |> Option.map (fun x -> float x.xWorkset))}
{row "Y workset" (float c.yWorkset) (t |> Option.map (fun x -> float x.yWorkset))}
{row "INIT cycles" (float c.init) (t |> Option.map (fun x -> float x.init))}
{row "TRIG cycles" (float c.trig) (t |> Option.map (fun x -> float x.trig))}
{row "RENDER mean" c.renderMean (t |> Option.map _.renderMean)}
{row "RENDER worst" (float c.renderWorst) (t |> Option.map (fun x -> float x.renderWorst))}
</tbody></table>"""

let scenario (r: Report) =
  let names = r.harness.parameterNames
  let chip (s: Step) =
    match s with
    | Settings v -> $"""<span class="chip set">{v |> Array.mapi (fun i x -> $"{(if i < names.Length then names[i] else string i)} {inv x}") |> String.concat " · "}</span>"""
    | Words w -> $"""<span class="chip set">words {w |> Array.map (fun x -> $"${x:x6}") |> String.concat " "}</span>"""
    | Init -> """<span class="chip">INIT</span>"""
    | Trig -> """<span class="chip trig">TRIG</span>"""
    | Render n -> $"""<span class="chip">RENDER ×{n}</span>"""
    | Mark text -> $"""<span class="chip trig">{enc text}</span>"""
  let marked = r.steps |> List.exists (function Mark _ -> true | _ -> false)
  let markCount = r.steps |> List.filter (function Mark _ -> true | _ -> false) |> List.length
  r.steps
  |> List.filter (fun s ->
    match s with
    | Mark text -> markCount <= 40 || text.StartsWith "bar "      // a long pattern: its bars (trigs are in the script)
    | _ -> not marked)
  |> List.map chip |> String.concat """<span class="arrow">→</span>"""

/// A kernel's section; `media` is its files' folder relative to the page, when they were kept
/// (Audio.save): the page then plays them and links the folder.
let sectionWith (media: string option) (e: Entry) =
  let players =
    match media with
    | None -> ""
    | Some folder ->
      let k = e.report.kernel
      let file (suffix: string) = $"{folder}/{k}.{suffix}"
      let reference =
        if e.report.refRun.IsSome then $"""<label>reference <audio controls preload="none" src="{enc (file "reference.wav")}"></audio></label>""" else ""
      $"""<div class="players"><label>kernel <audio controls preload="none" src="{enc (file "kernel.wav")}"></audio></label>{reference}
<span class="files"><a href="{enc (file "script.txt")}">script</a> · <a href="{enc (file "labels.txt")}">Audacity labels</a> · <a href="{enc (file "kernel.asm56k")}">listing</a> · <a href="{enc folder}/">folder</a></span></div>"""
  let gridNote =
    if e.report.blocks <= 96 then $"; grid lines are blocks of {enc e.report.harness.target.blockText}"
    else $" of {enc e.report.harness.target.blockText}; dashed lines are the pattern's bars and trigs"
  let r = e.report
  let id = slug r.kernel
  let correct = r.correctness.IsEmpty
  let faithful = r.fidelity = Some None
  let worst = r.ours.renderWorst
  let fits = r.harness.cycleLimit |> Option.forall (fun l -> worst <= int64 l)
  let bw = r.harness.target.blockWords
  let fidelityCard =
    match r.fidelity with
    | None -> """<div class="card"><div class="card-h">Fidelity</div><div class="card-v muted">no reference</div></div>"""
    | Some None -> $"""<div class="card ok"><div class="card-h">Fidelity</div><div class="card-v">identical</div><div class="card-s">{r.blocks * bw} output words, word for word with the reference on the emulator</div></div>"""
    | Some(Some m) -> $"""<div class="card bad"><div class="card-h">Fidelity</div><div class="card-v">differs</div><div class="card-s">{enc (mismatchText m)}</div></div>"""
  let correctCard =
    if correct then $"""<div class="card ok"><div class="card-h">Correctness</div><div class="card-v">exact</div><div class="card-s">compiled program = .NET kernel: output and state after each of {r.blocks} blocks</div></div>"""
    else $"""<div class="card bad"><div class="card-h">Correctness</div><div class="card-v">differs</div><div class="card-s">{r.correctness |> List.map mismatchText |> List.map enc |> String.concat "<br>"}</div></div>"""
  let ratio = match r.reference with Some t when t.renderMean > 0.0 -> $"×{f2 (r.ours.renderMean / t.renderMean)} the reference" | _ -> ""
  let refOutLegend = if r.refRun.IsSome then "<span class=\"sw s-ref\"></span>reference" else ""
  let refBarLegend = if r.refRun.IsSome then "<span class=\"sw b-ref\"></span>reference" else ""
  let budgetCard =
    $"""<div class="card {(if fits then "ok" else "warn")}"><div class="card-h">RENDER cycles</div><div class="card-v">{worst}</div><div class="card-s">worst call; {ratio}; {(match r.harness.cycleLimit with Some l -> (if fits then "within" else "over") + $" the {l}-cycle track limit" | None -> "no track limit on this harness")}</div></div>"""
  $"""
<section class="kernel" id="{id}">
  <header class="k-head">
    <h2>{enc e.title}</h2>
    <div class="k-meta">{enc e.origin}{(if e.license = "" then "" else $" · {enc e.license}")}</div>
    <p>{enc e.notes}</p>
  </header>
  <div class="cards">{correctCard}{fidelityCard}{budgetCard}</div>
  <h3>Scenario</h3>
  <div class="chips">{scenario r}</div>
  <h3>Output</h3>
  {players}
  <div class="legend"><span class="sw s-ours"></span>kernel, compiled{refOutLegend}<span class="sw diff"></span>differing words</div>
  {waveform r}
  <div class="caption">All {r.blocks} blocks{gridNote}{(if r.harness.target.channels > 1 then ", words interleaved" else "")}. Below: the first {3 * bw} words after the first TRIG, word by word.</div>
  {detail r}
  <h3>Cost</h3>
  <div class="cost-row">
    <div class="legend"><span class="sw b-ours"></span>kernel{refBarLegend}{(if r.harness.cycleLimit.IsSome then "<span class=\"sw limit\"></span>track limit" else "")}</div>
    {cycles r}
    {costTable r}
  </div>
  <h3>Listings</h3>
  <div class="panes" data-k="{id}">{fsPane id r.compiled.source}{generatedPane id r}{referencePane e.referenceSource}</div>
  {alignment r}
</section>"""

let summary (entries: Entry list) =
  let rows =
    entries |> List.map (fun e ->
      let r = e.report
      let t = r.reference
      let fid = match r.fidelity with None -> """<span class="badge">no reference</span>""" | Some None -> badge true "identical" | Some(Some _) -> badge false "differs"
      let fits = r.harness.cycleLimit |> Option.forall (fun l -> r.ours.renderWorst <= int64 l)
      let words = match t with Some c -> $"{r.ours.programWords} / {c.programWords}" | None -> string r.ours.programWords
      let cyc = match t with Some c -> $"{inv (Math.Round r.ours.renderMean)} / {inv (Math.Round c.renderMean)}" | None -> inv (Math.Round r.ours.renderMean)
      let ratio = match t with Some c when c.renderMean > 0.0 -> $"×{f2 (r.ours.renderMean / c.renderMean)}" | _ -> ""
      $"""<tr><td><a href="#{slug r.kernel}">{enc e.title}</a><div class="muted small">{enc e.origin}</div></td><td>{badge r.correctness.IsEmpty (if r.correctness.IsEmpty then "exact" else "differs")}</td><td>{fid}</td><td class="n">{cyc} <span class="muted">{ratio}</span><br>{ratioBar r.ours.renderMean (t |> Option.map _.renderMean)}</td><td class="n">{words}</td><td>{badge fits (if fits then "fits" else "over")}</td></tr>""")
  $"""<table class="summary"><thead><tr><th>Kernel</th><th>Correctness</th><th>Fidelity</th><th class="n">RENDER cycles, kernel / reference</th><th class="n">Words</th><th>Track limit</th></tr></thead><tbody>{String.concat "" rows}</tbody></table>"""

let css = """
:root{--bg:#f7f6f2;--panel:#ffffff;--ink:#1c1e22;--muted:#6c7079;--line:#e4e1d8;--soft:#f0eee8;
--ours:#2f6bd6;--ref:#d48a2a;--ok:#2c8a57;--bad:#c63d3d;--warn:#b7801a;--hl:#fff1b8;
--kw:#8a3db8;--ty:#13808f;--num:#b5561c;--str:#2f7d32;--com:#8b9099;--reg:#1f6fb2;--mn:#a3306a;--lbl:#3a4fae;--attr:#7a6a00}
@media (prefers-color-scheme: dark){:root:not([data-theme="light"]){--bg:#15171b;--panel:#1d2026;--ink:#e6e7ea;--muted:#9097a3;--line:#2e323a;--soft:#23272e;
--ours:#6e9ef5;--ref:#e7a650;--ok:#4fbf83;--bad:#ef6b6b;--warn:#e0b04a;--hl:#4a4320;
--kw:#c792ea;--ty:#5fd0dc;--num:#f2a46b;--str:#9ccc65;--com:#727985;--reg:#7fb8f0;--mn:#ef7fb3;--lbl:#9aa8ff;--attr:#d6c35a}}
:root[data-theme="dark"]{--bg:#15171b;--panel:#1d2026;--ink:#e6e7ea;--muted:#9097a3;--line:#2e323a;--soft:#23272e;
--ours:#6e9ef5;--ref:#e7a650;--ok:#4fbf83;--bad:#ef6b6b;--warn:#e0b04a;--hl:#4a4320;
--kw:#c792ea;--ty:#5fd0dc;--num:#f2a46b;--str:#9ccc65;--com:#727985;--reg:#7fb8f0;--mn:#ef7fb3;--lbl:#9aa8ff;--attr:#d6c35a}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 "Inter",system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:1320px;margin:0 auto;padding:28px 16px 80px}
h1{font-size:28px;margin:0 0 4px;letter-spacing:-.01em}
h2{font-size:22px;margin:0}
h3{font-size:13px;text-transform:uppercase;letter-spacing:.08em;color:var(--muted);margin:28px 0 10px}
a{color:var(--ours)}
.muted{color:var(--muted)} .small{font-size:12px}
.lede{color:var(--muted);max-width:820px;margin:0 0 22px}
table{border-collapse:collapse;width:100%}
th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:top}
th{font-size:12px;color:var(--muted);font-weight:600}
td.n,th.n{text-align:right;font-variant-numeric:tabular-nums}
.summary{background:var(--panel);border:1px solid var(--line);border-radius:12px;overflow:hidden}
.badge{display:inline-block;padding:1px 9px;border-radius:999px;font-size:12px;font-weight:600;background:var(--soft);color:var(--muted)}
.badge.ok{background:color-mix(in srgb,var(--ok) 16%,transparent);color:var(--ok)}
.badge.bad{background:color-mix(in srgb,var(--bad) 16%,transparent);color:var(--bad)}
.kernel{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:22px 22px 16px;margin-top:28px}
.k-meta{color:var(--muted);font-size:13px;margin-top:2px}
.k-head p{margin:10px 0 0;max-width:900px}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px;margin-top:18px}
.card{border:1px solid var(--line);border-radius:10px;padding:12px 14px;border-left:4px solid var(--line)}
.card.ok{border-left-color:var(--ok)} .card.bad{border-left-color:var(--bad)} .card.warn{border-left-color:var(--warn)}
.card-h{font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.06em}
.card-v{font-size:24px;font-weight:650;margin:2px 0}
.card.ok .card-v{color:var(--ok)} .card.bad .card-v{color:var(--bad)} .card.warn .card-v{color:var(--warn)}
.card-s{font-size:13px;color:var(--muted)}
.chips{display:flex;flex-wrap:wrap;gap:6px;align-items:center}
.chip{font:12px ui-monospace,SFMono-Regular,Menlo,monospace;padding:3px 8px;border-radius:6px;background:var(--soft);border:1px solid var(--line)}
.chip.trig{border-color:var(--ours);color:var(--ours)} .chip.set{color:var(--ref)}
.arrow{color:var(--muted);font-size:12px}
.chart{width:100%;height:auto;display:block;background:var(--soft);border-radius:8px;margin:6px 0}
.chart .axis{stroke:var(--muted);stroke-width:1;opacity:.5}
.chart .grid{stroke:var(--line);stroke-width:1}
.chart .tick{fill:var(--muted);font:10px ui-monospace,monospace}
.chart .mark{stroke-width:1;stroke-dasharray:3 3}
.chart .mark.trig{stroke:var(--ours)} .chart .mark.set{stroke:var(--ref)}
.chart .mark-t{font:10px ui-monospace,monospace} .chart .mark-t.trig{fill:var(--ours)} .chart .mark-t.set{fill:var(--ref)}
.chart .area.s-ref{fill:var(--ref);opacity:.45;stroke:none}
.chart .area.s-ours{fill:none;stroke:var(--ours);stroke-width:1}
.chart .line{fill:none;stroke-width:1.6;vector-effect:non-scaling-stroke}
.chart .line.s-ref{stroke:var(--ref);stroke-width:4;opacity:.45}
.chart .line.s-ours{stroke:var(--ours)}
.chart .dot{fill:var(--ours)} .chart .ring{fill:none;stroke:var(--ref);stroke-width:1.5}
.chart .diff{stroke:var(--bad);stroke-width:1.5}
.chart .b-ours{fill:var(--ours)} .chart .b-ours.over{fill:var(--bad)} .chart .b-ref{fill:var(--ref)}
.chart .limit{stroke:var(--bad);stroke-dasharray:6 4} .chart .limit-t{fill:var(--bad);font:11px ui-monospace,monospace}
.chart .declared{stroke:var(--muted);stroke-dasharray:2 3}
.ratio .b-ours{fill:var(--ours)} .ratio .b-ref{fill:var(--ref)}
.legend{display:flex;gap:14px;align-items:center;font-size:12px;color:var(--muted);flex-wrap:wrap}
.sw{display:inline-block;width:14px;height:8px;border-radius:2px;margin-right:4px;vertical-align:middle}
.sw.s-ours,.sw.b-ours{background:var(--ours)} .sw.s-ref,.sw.b-ref{background:var(--ref)} .sw.diff,.sw.limit{background:var(--bad)}
.caption{font-size:12px;color:var(--muted)}
.players{display:flex;flex-wrap:wrap;gap:14px;align-items:center;font-size:12px;color:var(--muted);margin:4px 0 8px}
.players label{display:flex;gap:6px;align-items:center} .players audio{height:30px}
.cost-row{display:grid;grid-template-columns:1fr;gap:10px}
@media (min-width:1000px){.cost-row{grid-template-columns:3fr 2fr;align-items:start}.cost-row .legend{grid-column:1 / -1}}
.cost{font-size:14px}
.panes{display:grid;grid-template-columns:1fr;gap:12px}
@media (min-width:1100px){.panes{grid-template-columns:repeat(3,minmax(0,1fr))}}
.pane{border:1px solid var(--line);border-radius:10px;overflow:hidden;min-width:0;background:var(--panel)}
.pane-h{font-size:12px;font-weight:600;padding:8px 12px;border-bottom:1px solid var(--line);background:var(--soft);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.code{font:12px/1.55 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;max-height:640px;overflow:auto;padding:6px 0}
.ln{white-space:pre;padding:0 10px 0 4.2em;text-indent:-4.2em;min-height:1.55em}
.ln::before{display:inline-block;width:3.2em;margin-right:1em;text-align:right;color:var(--muted);opacity:.6;text-indent:0;user-select:none}
.ln[data-l]::before{content:attr(data-l)}
.code{counter-reset:src} .ln.src{counter-increment:src} .ln.src::before{content:counter(src)}
.ln[data-l]{cursor:pointer}
.ln.hl{background:var(--hl)}
.k-k{color:var(--kw)} .k-t{color:var(--ty)} .k-n{color:var(--num)} .k-s{color:var(--str)} .k-c{color:var(--com);font-style:italic}
.k-r{color:var(--reg)} .k-m{color:var(--mn);font-weight:600} .k-l{color:var(--lbl)} .k-d{font-weight:600} .k-a{color:var(--attr)} .k-o{color:var(--kw)}
details{border:1px solid var(--line);border-radius:10px;margin:8px 0;background:var(--panel)}
summary{cursor:pointer;padding:9px 12px;font-size:14px}
.align{font:12px/1.5 ui-monospace,monospace}
.align td{padding:1px 10px;border:0;white-space:pre}
.align td.m{width:1.5em;color:var(--muted)}
.align tr.same{opacity:.85} .align tr.chg td.m,.k.chg{color:var(--warn)} .align tr.add td.m,.k.add{color:var(--ours)} .align tr.del td.m,.k.del{color:var(--ref)}
.align tr.add{background:color-mix(in srgb,var(--ours) 8%,transparent)} .align tr.del{background:color-mix(in srgb,var(--ref) 9%,transparent)}
.k{font-family:ui-monospace,monospace;font-weight:700}
footer{margin-top:40px;color:var(--muted);font-size:12px}
"""

let script = """
document.querySelectorAll('.panes').forEach(function(panes){
  var lit=[];
  function clear(){lit.forEach(function(e){e.classList.remove('hl')});lit=[];}
  panes.addEventListener('mouseover',function(ev){
    var ln=ev.target.closest('.ln[data-l]'); if(!ln) return;
    clear(); var l=ln.getAttribute('data-l');
    panes.querySelectorAll('.ln[data-l="'+l+'"]').forEach(function(e){e.classList.add('hl');lit.push(e)});
  });
  panes.addEventListener('mouseleave',clear);
  panes.addEventListener('click',function(ev){
    var ln=ev.target.closest('.ln[data-l]'); if(!ln) return;
    var l=ln.getAttribute('data-l'), other=ln.classList.contains('fs')?'.gen':'.fs';
    var t=panes.querySelector('.ln'+other+'[data-l="'+l+'"]'); if(!t) return;
    var code=t.closest('.code'); code.scrollTop=t.offsetTop-code.offsetTop-code.clientHeight/3;
  });
});
"""

let lede = "Machines written in kernel F#, compiled to DSP56300 code and graded on the emulator. <b>Correctness</b>: the compiled program against the same F# run as .NET code. <b>Fidelity</b>: its output against the shipped machine the kernel was ported from. <b>Cost</b>: cycles and words against that machine. The compiler's optimizer is still young: the cycle ratios measure what is left to win."

/// The page with its own heading and introduction (HTML), for callers grading other references.
let pageWithMedia (heading: string) (introduction: string) (media: Entry -> string option) (entries: Entry list) =
  let stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
  $"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{enc heading}</title><style>{css}</style></head>
<body><main>
<h1>{enc heading}</h1>
<p class="lede">{introduction}</p>
{summary entries}
{entries |> List.map (fun e -> sectionWith (media e) e) |> String.concat "\n"}
<footer>Generated {stamp} by fsdsp56k's Dsp56k.Kernel.Grade.</footer>
</main><script>{script}</script></body></html>"""

let pageWith (heading: string) (introduction: string) (entries: Entry list) =
  pageWithMedia heading introduction (fun _ -> None) entries

let section (e: Entry) = sectionWith None e

let page (entries: Entry list) = pageWith "Kernel grades" lede entries
