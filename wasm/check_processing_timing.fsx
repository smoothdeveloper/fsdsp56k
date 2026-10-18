#load "ProcessingTiming.fs"
open dsp56k.ProcessingTiming

let window = Window()
window.Add(1., 0.75, 2., 0.)
window.Add(3., 2., 2., 0.)
let sample = window.Take()
assert (sample.totalPercent = 100. && sample.dspPercent = 68.75)
assert (sample.overheadPercent = 31.25 && sample.peakPercent = 150.)
assert (sample.overruns = 1 && sample.callbackMs = 2. && sample.budgetMs = 2.)
assert ((window.Take()).totalPercent = 0.)
window.Add(1., 2., 2., 0.)
assert ((window.Take()).overheadPercent = 0.)
window.Add(3., 2., 2.9, 1.)
assert ((window.Take()).overruns = 0)
printfn "Processing timing: deadline fractions, overhead, overruns and reset passed"
