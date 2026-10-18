/// Wall-clock callback work as a share of the audio quantum's deadline, not OS CPU usage.
module dsp56k.ProcessingTiming

type Sample =
  { totalPercent: float; dspPercent: float; overheadPercent: float
    peakPercent: float; overruns: int; callbackMs: float; budgetMs: float }

type Window() =
  let mutable total, dsp, budget, peak = 0., 0., 0., 0.
  let mutable count, overruns = 0, 0

  member _.Reset() =
    total <- 0.; dsp <- 0.; budget <- 0.; peak <- 0.
    count <- 0; overruns <- 0

  member _.Add(elapsed, rendering, deadline, uncertainty) =
    if deadline <= 0. then invalidArg "deadline" "Audio deadline must be positive"
    let elapsed = max 0. elapsed
    total <- total + elapsed
    dsp <- dsp + max 0. (min elapsed rendering)
    budget <- budget + deadline
    peak <- max peak (elapsed / deadline * 100.)
    count <- count + 1
    // A coarse clock must not label a rounded 3 ms interval as certainly over 2.9 ms.
    if elapsed - max 0. uncertainty > deadline then overruns <- overruns + 1

  member this.Take() =
    let ratio value = if budget = 0. then 0. else value / budget * 100.
    let mean value = if count = 0 then 0. else value / float count
    let sample =
      { totalPercent = ratio total; dspPercent = ratio dsp; overheadPercent = ratio (total - dsp)
        peakPercent = peak; overruns = overruns; callbackMs = mean total; budgetMs = mean budget }
    this.Reset()
    sample
