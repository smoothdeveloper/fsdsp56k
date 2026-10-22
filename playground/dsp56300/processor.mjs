import { A as e, O as t, P as n, _ as r, a as i, f as a, h as o, j as s, l as c, n as l, p as u, u as d, v as f, y as p, z as m } from "./Array.mjs";
import { r as h } from "./Reflection.mjs";
import { _ as g, a as _, b as v, c as y, d as b, f as x, g as S, h as C, i as w, l as T, n as E, o as D, r as O, s as k, t as A, u as j, v as M, x as N, y as P } from "./Range.mjs";
import { i as F, n as I, r as L, t as R } from "./ProcessingTiming.mjs";
import z from "./dsp56300.mjs";
var B = class extends AudioWorkletProcessor {
	constructor(e) {
		super(), this.this = new t(n()), this.options = e, this.this.contents = this, this.pending = void 0, this.kernel = void 0, this.block = [], this.cursor = 0, this.playing = !1, this.gain = .15, this.level = 0, this.quanta = 0, this.timing = R(), this.timer = typeof performance < "u" && typeof performance.now == "function" ? "high-resolution" : "1ms", this.loadRequest = 0, this.trace = i(Array(1024), 0, 1024, 0), this.traceCursor = 0, this.regions = [], this.regionTraces = [], this.regionCursors = [], this.writes = void 0, this.steps = 0, this.watch = [], this["init@29"] = 1, this.this.contents.port.onmessage = ((e) => {
			this.this.contents.message(e.data);
		});
	}
	watched(e) {
		let t = this, n = t.kernel, r, i;
		switch (n == null ? r = 1 : e.length > 6 ? (r = 0, i = n) : r = 1, r) {
			case 0: return N(g(() => S((t) => {
				let n;
				return v((n = d(6, e) + t[1] & 16777215, (() => {
					try {
						return _(i, t[0], n) | 0;
					} catch {
						return -1;
					}
				})()));
			}, t.watch)));
			default: return [];
		}
	}
	error(t) {
		let n = this;
		n.playing = !1, n.this.contents.port.postMessage({
			type: "error",
			message: e(t)
		});
	}
	message(t) {
		let n, a = this;
		try {
			let l = e(t.type);
			switch (l) {
				case "load": {
					L(a.timing), a.quanta = 0, a.playing = !1, a.kernel = void 0, a.regions = [], a.regionTraces = [], a.regionCursors = [], a.loadRequest = a.loadRequest + 1 | 0;
					let e = a.loadRequest | 0, r, o = a.pending;
					if (o == null) {
						let e = z({
							locateFile: (e) => e,
							instantiateWasm: (e, t) => {
								let n = new WebAssembly.Instance(a.options.processorOptions.module, e);
								return t(n), n.exports;
							}
						});
						a.pending = e, r = e;
					} else r = o;
					r.then((n) => {
						e === a.loadRequest && (a.kernel = E(n, t.image), a.block = [], a.cursor = 0, i(a.trace, 0, a.trace.length, 0), a.traceCursor = 0, a.this.contents.port.postMessage({ type: "ready" }));
					}).catch((n = a.this.contents, (e) => {
						n.error(e);
					}));
					break;
				}
				case "play":
					a.playing = t.value, L(a.timing), a.quanta = 0;
					break;
				case "gain": {
					let e = t.value;
					if (Number.isNaN(e) || r(e)) throw new s("Invalid gain");
					a.gain = f(0, p(.5, e));
					break;
				}
				default: {
					let e = l, n, r = a.kernel;
					if (r != null) n = r;
					else throw new s("DSP not ready");
					switch (e) {
						case "trace": {
							let e = t.regions;
							if (e.length > 8) throw new s("Too many trace regions");
							for (let t = 0; t <= e.length - 1; t++) {
								let r = d(t, e);
								if (!Number.isInteger(r.words) || r.words < 1 || r.words > 64) throw new s("Invalid trace length");
								_(n, r.bank, r.address), _(n, r.bank, r.address + r.words - 1);
							}
							a.regions = e, a.regionTraces = c(a.regions.length, (e) => i(Array(1024), 0, 1024, 0)), a.regionCursors = i(Array(a.regions.length), 0, a.regions.length, 0);
							break;
						}
						case "memory":
							b(n, t.bank, t.address, t.word);
							break;
						case "watch": {
							let e = t.words;
							if (e.length > 256) throw new s("Too many watched words");
							a.watch = N(g(() => P((e) => [e.bank, e.offset], e)));
							break;
						}
						case "registers":
							w(n, t.values);
							break;
						case "trig":
							j(n);
							break;
						case "activity":
							a.writes = t.value ? i(Array(40), 0, 40, 0) : void 0;
							break;
						case "begin": {
							a.playing = !1, O(n, o(t.entry) ? x(n).render : t.entry), a.steps = 0;
							let e = y(n);
							a.this.contents.port.postMessage({
								type: "step",
								status: 0,
								pc: C(n),
								steps: a.steps,
								registers: e,
								watched: a.this.contents.watched(e)
							});
							break;
						}
						case "step": {
							let e = (o(t.count) ? 1 : t.count) | 0;
							if (!Number.isInteger(e) || e < 1 || e > 1e5) throw new s("Invalid step count");
							let r = y(n), i = 0, c = 0;
							for (; i === 0 && c < e;) i = T(n) | 0, c = c + 1 | 0;
							a.steps = a.steps + c | 0;
							let l = y(n), u = N(g(() => S((e) => d(e, l) === d(e, r) ? M() : v(e), A(0, 1, l.length - 1))));
							a.this.contents.port.postMessage({
								type: "step",
								status: i,
								pc: C(n),
								steps: a.steps,
								registers: l,
								written: u,
								watched: a.this.contents.watched(l)
							});
							break;
						}
						default: throw new s("Unknown runtime message");
					}
				}
			}
		} catch (e) {
			a.this.contents.error(e.message);
		}
	}
	process(e, t) {
		let r, o = this, h = o.kernel, g, v;
		switch (h == null ? g = 1 : o.playing && t.length > 0 && d(0, t).length === 2 ? (g = 0, v = h) : g = 1, g) {
			case 0: {
				let h = typeof performance < "u" && typeof performance.now == "function" ? performance.now() : Date.now(), g = 0, y = d(0, d(0, t)), S = d(1, d(0, t)), C = y;
				try {
					for (let t = 0; t <= C.length - 1; t++) {
						let n;
						if (o.cursor === o.block.length) {
							if (x(v).inputBank >= 0) {
								if (C.length % x(v).frames !== 0) throw new s("Input blocks must divide the audio quantum");
								for (let n = 0; n <= x(v).frames - 1; n++) for (let r = 0; r <= 1; r++) {
									let i = ~~m(f(-8388608, p(8388607, (e.length > 0 && d(0, e).length > 0 ? d(t + n, d(p(r, d(0, e).length - 1), d(0, e))) : 0) * 8388608))) & 16777215;
									b(v, x(v).inputBank, x(v).input + n * 2 + r, i);
								}
							}
							let r = typeof performance < "u" && typeof performance.now == "function" ? performance.now() : Date.now();
							o.block = (n = o.writes, n == null ? D(v) : k(v, n)), g += f(0, (typeof performance < "u" && typeof performance.now == "function" ? performance.now() : Date.now()) - r);
							for (let e = 0; e <= o.regions.length - 1; e++) {
								let t = d(e, o.regions);
								for (let n = 0; n <= t.words - 1; n++) d(e, o.regionTraces)[d(e, o.regionCursors)] = _(v, t.bank, t.address + n) | 0, o.regionCursors[e] = (d(e, o.regionCursors) + 1) % 1024 | 0;
							}
							o.cursor = 0;
						}
						let r = (e) => (e << 8 >> 8) / 8388608;
						o.trace[o.traceCursor] = d(o.cursor, o.block) | 0, o.trace[o.traceCursor + 1] = d(o.cursor + 1, o.block) | 0, o.traceCursor = (o.traceCursor + 2) % o.trace.length | 0, u(C, t, r(d(o.cursor, o.block)) * o.gain), u(S, t, r(d(o.cursor + 1, o.block)) * o.gain), o.cursor = o.cursor + 2 | 0, o.level = f(o.level, f(Math.abs(d(t, C)), Math.abs(d(t, S))));
					}
					if (I(o.timing, (typeof performance < "u" && typeof performance.now == "function" ? performance.now() : Date.now()) - h, g, C.length / x(v).sampleRate * 1e3, +(o.timer === "1ms")), o.quanta = o.quanta + 1 | 0, o.quanta === 64) {
						let e = c(o.trace.length, (e) => d((o.traceCursor + e) % o.trace.length, o.trace) | 0), t = a((e, t) => c(t.length, (n) => d((d(e, o.regionCursors) + n) % t.length, t) | 0), o.regionTraces);
						o.this.contents.port.postMessage({
							type: "level",
							peak: o.level,
							samples: e,
							traces: t,
							timing: F(o.timing),
							timer: o.timer,
							writes: (r = o.writes, r == null ? n() : l(r))
						});
						let s = o.writes;
						if (s != null) {
							let e = s;
							i(e, 0, e.length, 0);
						}
						o.level = 0, o.quanta = 0;
					}
				} catch (e) {
					i(C, 0, C.length, 0), i(S, 0, S.length, 0), o.this.contents.error(e.message);
				}
				break;
			}
		}
		return !0;
	}
};
function V() {
	return h("dsp56k.WasmProcessor.DspProcessor", void 0, B, h("dsp56k.WasmProcessor.AudioWorkletProcessor", void 0, AudioWorkletProcessor));
}
function H(e) {
	return new B(e);
}
registerProcessor("dsp56300", B);
export { B as DspProcessor, H as DspProcessor_$ctor_4E60E31B, V as DspProcessor_$reflection };
