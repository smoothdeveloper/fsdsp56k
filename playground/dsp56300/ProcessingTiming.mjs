import { j as e, k as t, v as n, y as r } from "./Array.mjs";
var i = class extends t {
	constructor(e, t, n, r, i, a, o) {
		super(), this.totalPercent = e, this.dspPercent = t, this.overheadPercent = n, this.peakPercent = r, this.overruns = i | 0, this.callbackMs = a, this.budgetMs = o;
	}
}, a = class {
	constructor() {
		this.total = 0, this.peak = 0, this.dsp = 0, this.budget = 0, this.overruns = 0, this.count = 0;
	}
};
function o() {
	return new a();
}
function s(e) {
	e.total = 0, e.dsp = 0, e.budget = 0, e.peak = 0, e.count = 0, e.overruns = 0;
}
function c(t, i, a, o, s) {
	if (o <= 0) throw new e("Audio deadline must be positive (Parameter 'deadline')");
	let c = n(0, i);
	t.total += c, t.dsp += n(0, r(c, a)), t.budget += o, t.peak = n(t.peak, c / o * 100), t.count = t.count + 1 | 0, c - n(0, s) > o && (t.overruns = t.overruns + 1 | 0);
}
function l(e) {
	let t = (t) => e.budget === 0 ? 0 : t / e.budget * 100, n = (t) => e.count === 0 ? 0 : t / e.count, r = new i(t(e.total), t(e.dsp), t(e.total - e.dsp), e.peak, e.overruns, n(e.total), n(e.budget));
	return s(e), r;
}
export { l as i, c as n, s as r, o as t };
