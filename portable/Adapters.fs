/// Typed adapters for existing sample kernels. The synthesis functions are linked unchanged.
module dsp56k.PortableAdapters

open dsp56k.kernel
open dsp56k.Portable
open samples.kernels

let resources: (string * int array) array = [||]

let names =
  [| "sine"; "pluck"; "tx6-bd"; "tx6-sd"; "tx6-cp"; "tx6-lt"; "tx6-ht"; "tx6-ch"; "tx6-oh"; "tx6-cy"
     "input-thru" |]

let private word (words: int array) index =
  Frac.ofRaw (if index < words.Length then words[index] else 0)

let private output render =
  let output = Output()
  fun () ->
    output.Clear()
    render output
    output.Words

let private tx6BdState (s: Tx6Bd.State) =
  [| s.amp.Word; s.freq.Word; s.pitch.Word; s.click.Word; s.clickPole.Word; s.clickGain.Word; s.decayNormal.Word
     s.decayXl.Word; s.tune.Word; s.heat.Word; s.norm.Word; s.rng.Word; s.remaining.Word; s.ampPole.Word
     s.target.Word; s.u.Word; s.v.Word; s.clickLp.Word; s.bodyGain.Word; s.harmonic.Word
     s.harmonicPrevious.Word |]

let private tx6BdZero () : Tx6Bd.State =
  { amp = Frac.zero; freq = Frac.zero; pitch = Frac.zero; click = Frac.zero; clickPole = Frac.zero
    clickGain = Frac.zero; decayNormal = Frac.zero; decayXl = Frac.zero; tune = Frac.zero; heat = Frac.zero
    norm = Frac.zero; rng = Frac.zero; remaining = Frac.zero; ampPole = Frac.zero; target = Frac.zero
    u = Frac.zero; v = Frac.zero; clickLp = Frac.zero; bodyGain = Frac.zero; harmonic = Frac.zero
    harmonicPrevious = Frac.zero }

let private tx6SdState (s: Tx6Sd.State) =
  [| s.remaining.Word; s.gateHold.Word; s.gatePole.Word; s.p0.Word; s.cos0.Word; s.qbase.Word; s.bodyPole0.Word
     s.b0.Word; s.a1.Word; s.a2.Word; s.fast.Word; s.slow.Word; s.rng.Word; s.z1.Word; s.z2.Word; s.fp.Word
     s.sp.Word; s.noiseAmp.Word; s.glide.Word; s.s0.Word; s.c0.Word; s.d0.Word; s.r0.Word; s.bodyFast.Word
     s.bodySlow.Word; s.bodyPole1.Word |]

let private tx6SdZero () : Tx6Sd.State =
  { remaining = Frac.zero; gateHold = Frac.zero; gatePole = Frac.zero; p0 = Frac.zero; cos0 = Frac.zero
    qbase = Frac.zero; bodyPole0 = Frac.zero; b0 = Frac.zero; a1 = Frac.zero; a2 = Frac.zero; fast = Frac.zero
    slow = Frac.zero; rng = Frac.zero; z1 = Frac.zero; z2 = Frac.zero; fp = Frac.zero; sp = Frac.zero
    noiseAmp = Frac.zero; glide = Frac.zero; s0 = Frac.zero; c0 = Frac.zero; d0 = Frac.zero; r0 = Frac.zero
    bodyFast = Frac.zero; bodySlow = Frac.zero; bodyPole1 = Frac.zero }

let private tx6CpState (s: Tx6Cp.State) =
  [| s.sp.Word; s.lp.Word; s.b0.Word; s.a1.Word; s.a2.Word; s.tailStart.Word; s.lateStart.Word; s.airBase.Word
     s.airSpread.Word; s.durationWarp.Word; s.rng.Word; s.z1.Word; s.z2.Word; s.frame.Word; s.burstBase.Word
     s.tail.Word; s.late.Word; s.tailPole.Word; s.latePole.Word; s.foundation.Word; s.rise.Word
     s.foundationStart.Word; s.risePole.Word; s.gain.Word; s.burstGain.Word; s.tightStep.Word |]

let private tx6CpZero () : Tx6Cp.State =
  { sp = Frac.zero; lp = Frac.zero; b0 = Frac.zero; a1 = Frac.zero; a2 = Frac.zero; tailStart = Frac.zero
    lateStart = Frac.zero; airBase = Frac.zero; airSpread = Frac.zero; durationWarp = Frac.zero; rng = Frac.zero
    z1 = Frac.zero; z2 = Frac.zero; frame = Frac.zero; burstBase = Frac.zero; tail = Frac.zero; late = Frac.zero
    tailPole = Frac.zero; latePole = Frac.zero; foundation = Frac.zero; rise = Frac.zero
    foundationStart = Frac.zero; risePole = Frac.zero; gain = Frac.zero; burstGain = Frac.zero
    tightStep = Frac.zero }

let private tx6TomState (s: Tx6Tom.State) =
  [| s.remaining.Word; s.fp.Word; s.sp.Word; s.r0.Word; s.r1.Word; s.d0.Word; s.d1.Word; s.p0.Word; s.cos0.Word
     s.p1.Word; s.cos1.Word; s.qbase.Word; s.glide.Word; s.b0.Word; s.a1.Word; s.a2.Word; s.rng.Word; s.z1.Word
     s.z2.Word; s.fast.Word; s.slow.Word; s.lastGain.Word; s.s0.Word; s.c0.Word; s.s1.Word; s.c1.Word |]

let private tx6TomZero () : Tx6Tom.State =
  { remaining = Frac.zero; fp = Frac.zero; sp = Frac.zero; r0 = Frac.zero; r1 = Frac.zero; d0 = Frac.zero
    d1 = Frac.zero; p0 = Frac.zero; cos0 = Frac.zero; p1 = Frac.zero; cos1 = Frac.zero; qbase = Frac.zero
    glide = Frac.zero; b0 = Frac.zero; a1 = Frac.zero; a2 = Frac.zero; rng = Frac.zero; z1 = Frac.zero
    z2 = Frac.zero; fast = Frac.zero; slow = Frac.zero; lastGain = Frac.zero; s0 = Frac.zero; c0 = Frac.zero
    s1 = Frac.zero; c1 = Frac.zero }

let private tx6MetalState (s: Tx6Metal.State) =
  [| s.remaining.Word; s.fp.Word; s.sp.Word; s.fade.Word; s.b0.Word; s.a1.Word; s.a2.Word; s.s0.Word; s.p0.Word
     s.c0.Word; s.s1.Word; s.p1.Word; s.c1.Word; s.rng.Word; s.z1.Word; s.z2.Word; s.fast.Word; s.slow.Word
     s.attack.Word; s.bell.Word; s.click.Word; s.toneGain.Word; s.noiseGain.Word; s.metalTone.Word
     s.metalNoise.Word |]

let private tx6MetalZero () : Tx6Metal.State =
  { remaining = Frac.zero; fp = Frac.zero; sp = Frac.zero; fade = Frac.zero; b0 = Frac.zero; a1 = Frac.zero
    a2 = Frac.zero; s0 = Frac.zero; p0 = Frac.zero; c0 = Frac.zero; s1 = Frac.zero; p1 = Frac.zero
    c1 = Frac.zero; rng = Frac.zero; z1 = Frac.zero; z2 = Frac.zero; fast = Frac.zero; slow = Frac.zero
    attack = Frac.zero; bell = Frac.zero; click = Frac.zero; toneGain = Frac.zero; noiseGain = Frac.zero
    metalTone = Frac.zero; metalNoise = Frac.zero }

let create name =
  match name with
  | "sine" ->
    let mutable p: Sine.Params = { pitch = Frac.zero; amp = Frac.zero }
    let s: Sine.State = { phase = Long.zero; ampPrev = Frac.zero }
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words -> p <- { pitch = word words 0; amp = word words 1 }
      init = fun () -> Sine.init p s
      trig = fun () -> Sine.trig p s
      render = output (fun out -> Sine.render p s out)
      state = fun () ->
        [| int ((s.phase.Raw >>> 24) &&& 0xffffffL)
           int (s.phase.Raw &&& 0xffffffL)
           s.ampPrev.Word |] }
  | "pluck" ->
    let mutable p: Pluck.Params = { ptch = Frac.zero; dec = Frac.zero; lev = Frac.zero }
    let s: Pluck.State =
      { region = Frac.zero; last = Frac.zero; pos = Frac.zero
        pending = Frac.zero; prev = Frac.zero; noise = Frac.zero }
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { ptch = word words 0; dec = word words 1; lev = word words 2 }
      init = fun () -> Pluck.init p s
      trig = fun () -> Pluck.trig p s
      render = output (fun out -> Pluck.render p s out)
      state = fun () ->
        [| s.region.Word; s.last.Word; s.pos.Word; s.pending.Word; s.prev.Word; s.noise.Word |] }
  | "tx6-bd" ->
    let mutable p: Tx6Bd.Params =
      { tune = Frac.zero; dec = Frac.zero; tran = Frac.zero; heat = Frac.zero; xl = Frac.zero; clik = Frac.zero }
    let s = tx6BdZero ()
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { tune = word words 0; dec = word words 1; tran = word words 2
               heat = word words 3; xl = word words 4; clik = word words 5 }
      init = fun () -> Tx6Bd.init p s
      trig = fun () -> Tx6Bd.trig p s
      render = output (fun out -> Tx6Bd.render p s out)
      state = fun () -> tx6BdState s }
  | "tx6-sd" ->
    let mutable p: Tx6Sd.Params = { tune = Frac.zero; dec = Frac.zero; snap = Frac.zero; colr = Frac.zero; body = Frac.zero }
    let s = tx6SdZero ()
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { tune = word words 0; dec = word words 1; snap = word words 2; colr = word words 3; body = word words 4 }
      init = fun () -> Tx6Sd.init p s
      trig = fun () -> Tx6Sd.trig p s
      render = output (fun out -> Tx6Sd.render p s out)
      state = fun () -> tx6SdState s }
  | "tx6-cp" ->
    let mutable p: Tx6Cp.Params = { tune = Frac.zero; dec = Frac.zero; nois = Frac.zero; body = Frac.zero; tght = Frac.zero }
    let s = tx6CpZero ()
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { tune = word words 0; dec = word words 1; nois = word words 2; body = word words 3; tght = word words 4 }
      init = fun () -> Tx6Cp.init p s
      trig = fun () -> Tx6Cp.trig p s
      render = output (fun out -> Tx6Cp.render p s out)
      state = fun () -> tx6CpState s }
  | "tx6-lt" ->
    let mutable p: Tx6Tom.Params = { tune = Frac.zero; dec = Frac.zero; nois = Frac.zero; over = Frac.zero }
    let s = tx6TomZero ()
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { tune = word words 0; dec = word words 1; nois = word words 2; over = word words 3 }
      init = fun () -> Tx6Tom.ltInit p s
      trig = fun () -> Tx6Tom.ltTrig p s
      render = output (fun out -> Tx6Tom.ltRender p s out)
      state = fun () -> tx6TomState s }
  | "tx6-ht" ->
    let mutable p: Tx6Tom.Params = { tune = Frac.zero; dec = Frac.zero; nois = Frac.zero; over = Frac.zero }
    let s = tx6TomZero ()
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { tune = word words 0; dec = word words 1; nois = word words 2; over = word words 3 }
      init = fun () -> Tx6Tom.htInit p s
      trig = fun () -> Tx6Tom.htTrig p s
      render = output (fun out -> Tx6Tom.htRender p s out)
      state = fun () -> tx6TomState s }
  | "tx6-ch" ->
    let mutable p: Tx6Metal.Params = { tune = Frac.zero; dec = Frac.zero; layer = Frac.zero }
    let s = tx6MetalZero ()
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { tune = word words 0; dec = word words 1; layer = word words 2 }
      init = fun () -> Tx6Metal.chInit p s
      trig = fun () -> Tx6Metal.chTrig p s
      render = output (fun out -> Tx6Metal.chRender p s out)
      state = fun () -> tx6MetalState s }
  | "tx6-oh" ->
    let mutable p: Tx6Metal.Params = { tune = Frac.zero; dec = Frac.zero; layer = Frac.zero }
    let s = tx6MetalZero ()
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { tune = word words 0; dec = word words 1; layer = word words 2 }
      init = fun () -> Tx6Metal.ohInit p s
      trig = fun () -> Tx6Metal.ohTrig p s
      render = output (fun out -> Tx6Metal.ohRender p s out)
      state = fun () -> tx6MetalState s }
  | "tx6-cy" ->
    let mutable p: Tx6Metal.Params = { tune = Frac.zero; dec = Frac.zero; layer = Frac.zero }
    let s = tx6MetalZero ()
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { tune = word words 0; dec = word words 1; layer = word words 2 }
      init = fun () -> Tx6Metal.cyInit p s
      trig = fun () -> Tx6Metal.cyTrig p s
      render = output (fun out -> Tx6Metal.cyRender p s out)
      state = fun () -> tx6MetalState s }
  | "input-thru" ->
    let mutable p: InputThru.Params = { side = Frac.zero; lev = Frac.zero }
    let s: InputThru.State = { pos = Frac.zero }
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words -> p <- { side = word words 0; lev = word words 1 }
      init = fun () -> InputThru.init p s
      trig = fun () -> InputThru.trig p s
      render = output (fun out -> InputThru.render p s out)
      state = fun () -> [| s.pos.Word |] }
  | _ -> invalidArg "name" $"No portable adapter registered for {name}"
