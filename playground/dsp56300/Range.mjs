import { A as e, D as t, E as n, F as r, H as i, L as a, N as o, O as s, P as c, R as l, T as u, V as d, a as f, b as p, d as m, g as h, j as g, k as ee, l as _, m as v, n as y, p as b, t as x, u as S, x as te, y as ne } from "./Array.mjs";
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/List.js
var re = class n extends ee {
	constructor(e, t) {
		super(), this.head = e, this.tail = t;
	}
	toString() {
		let t = this, n = "[", i = !0, a = l(t);
		try {
			for (; a["System.Collections.IEnumerator.MoveNext"]();) {
				let t, r, o, s = a["System.Collections.Generic.IEnumerator`1.get_Current"]();
				n = (i ? n : n + "; ") + (t = s, r = t, typeof r == "string" ? (o = r, "\"" + o + "\"") : e(t)), i = !1;
			}
		} finally {
			r(a);
		}
		return n + "]";
	}
	Equals(e) {
		let r = this;
		return r === e || e instanceof n && ((e, n) => {
			loop: for (;;) {
				let r = e, i = n, o = r.tail, s = i.tail;
				if (o != null) {
					if (s != null) {
						let c = t(o), l = t(s);
						if (a(r.head, i.head)) {
							e = c, n = l;
							continue loop;
						}
						return !1;
					}
					return !1;
				}
				return s == null;
			}
		})(r, e);
	}
	GetHashCode() {
		return ((e, n, r) => {
			loop: for (;;) {
				let i = e, a = n, o = r, s = o.tail;
				if (s != null) {
					let c = t(s);
					if (i > 18) return a | 0;
					e = i + 1, n = (a << 1) + d(o.head) + 631 * i, r = c;
					continue loop;
				}
				return a | 0;
			}
		})(0, 0, this) | 0;
	}
	toJSON() {
		let e = this;
		return Array.from(e);
	}
	CompareTo(e) {
		let r = this;
		return e instanceof n ? ((e, n) => {
			loop: for (;;) {
				let r = e, i = n, a = r.tail, s = i.tail;
				if (a != null) {
					if (s != null) {
						let c = t(a), l = t(s), u = o(r.head, i.head) | 0;
						if (u === 0) {
							e = c, n = l;
							continue loop;
						}
						return u | 0;
					}
					return 1;
				}
				return s == null ? 0 : -1;
			}
		})(r, e) | 0 : 1;
	}
	GetEnumerator() {
		return ae(this);
	}
	[Symbol.iterator]() {
		return i(l(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		let e = this;
		return l(e);
	}
}, ie = class {
	constructor(e) {
		this.xs = e, this.it = this.xs, this.current = c();
	}
	"System.Collections.Generic.IEnumerator`1.get_Current"() {
		return this.current;
	}
	"System.Collections.IEnumerator.get_Current"() {
		return this.current;
	}
	"System.Collections.IEnumerator.MoveNext"() {
		let e = this, n = e.it.tail;
		if (n != null) {
			let r = t(n);
			return e.current = e.it.head, e.it = r, !0;
		}
		return !1;
	}
	"System.Collections.IEnumerator.Reset"() {
		let e = this;
		e.it = e.xs, e.current = c();
	}
	Dispose() {}
};
function ae(e) {
	return new ie(e);
}
function oe(e) {
	return e.tail == null;
}
function se(e) {
	return ((e, n) => {
		loop: for (;;) {
			let r = e, i = n.tail;
			if (i != null) {
				e = r + 1, n = t(i);
				continue loop;
			}
			return r | 0;
		}
	})(0, e) | 0;
}
function ce(e) {
	if (e.tail != null) return e.head;
	throw new g(u + " (Parameter 'list')");
}
function le(e) {
	let n = e.tail;
	if (n != null) return t(n);
	throw new g(u + " (Parameter 'list')");
}
function C(e) {
	let t = se(e) | 0, n = f(Array(t), 0, t, null);
	return ((e, t) => {
		loop: for (;;) {
			let r = e, i = t;
			if (!oe(i)) {
				b(n, r, ce(i)), e = r + 1, t = le(i);
				continue loop;
			}
			break;
		}
	})(0, e), n;
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Seq.js
var w = "Enumeration already finished.", T = "Enumeration has not started. Call MoveNext.", E = "Reset is not supported on this enumerator.";
function D() {
	throw te(E);
}
function O() {
	throw p(T);
}
function k() {
	throw p(w);
}
var A = class {
	constructor(e) {
		this.f = e;
	}
	toString() {
		let t = this, n = 0, i = "seq [", a = l(t);
		try {
			for (; n < 4 && a["System.Collections.IEnumerator.MoveNext"]();) n > 0 && (i += "; "), i += e(a["System.Collections.Generic.IEnumerator`1.get_Current"]()), n = n + 1 | 0;
			return n === 4 && (i += "; ..."), i + "]";
		} finally {
			r(a);
		}
	}
	GetEnumerator() {
		return this.f();
	}
	[Symbol.iterator]() {
		return i(l(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return this.f();
	}
};
function j(e) {
	return new A(e);
}
var M = class {
	constructor(e, t, n) {
		this.current = e, this.next = t, this.dispose = n;
	}
	"System.Collections.Generic.IEnumerator`1.get_Current"() {
		return this.current();
	}
	"System.Collections.IEnumerator.get_Current"() {
		return this.current();
	}
	"System.Collections.IEnumerator.MoveNext"() {
		return this.next();
	}
	"System.Collections.IEnumerator.Reset"() {
		D();
	}
	Dispose() {
		this.dispose();
	}
};
function N(e, t, n) {
	return new M(e, t, n);
}
function P(e) {
	let i, a, o = !1, s = !1, c, u = () => {
		if (s = !0, a != null) {
			let e = t(a);
			try {
				r(e);
			} finally {
				a = void 0;
			}
		}
		if (i != null) {
			let e = t(i);
			try {
				r(e);
			} finally {
				i = void 0;
			}
		}
	};
	return N(() => (o ? s && k() : O(), c == null ? k() : t(c)), () => {
		if (o ||= !0, s) return !1;
		{
			let o;
			for (; o == null;) {
				let s, d = i, f = a;
				if (d != null) {
					if (f != null) {
						let e = t(f);
						if (e["System.Collections.IEnumerator.MoveNext"]()) c = n(e["System.Collections.Generic.IEnumerator`1.get_Current"]()), o = !0;
						else try {
							r(e);
						} finally {
							a = void 0;
						}
					} else {
						let e = t(d);
						e["System.Collections.IEnumerator.MoveNext"]() ? a = (s = e["System.Collections.Generic.IEnumerator`1.get_Current"](), l(s)) : (u(), o = !1);
					}
				} else i = l(e);
			}
			return t(o);
		}
	}, () => {
		s || u();
	});
}
function F(e, r, i) {
	let a = !1, o, s = n(e()), c = () => {
		if (s != null) {
			let e = t(s);
			try {
				i(e);
			} finally {
				s = void 0;
			}
		}
	}, l = () => {
		try {
			c();
		} finally {
			o = void 0;
		}
	};
	return N(() => (a || O(), o == null ? k() : t(o)), () => {
		if (a ||= !0, s != null) {
			let e = t(s), n;
			try {
				n = r(e);
			} catch (e) {
				throw l(), e;
			}
			return n == null ? (l(), !1) : (o = n, !0);
		}
		return !1;
	}, c);
}
function I(e, n) {
	let r, i = n;
	return N(() => {
		if (r != null) {
			let e = t(r)[0];
			return t(r)[1], e;
		}
		return O();
	}, () => (r = e(i), r != null && (t(r)[0], i = t(r)[1], !0)), () => {});
}
function L(e) {
	return j(e);
}
function R(e) {
	return l(h("source", e));
}
function z(e) {
	return L(() => l(e()));
}
function B(e) {
	return L(() => P(e));
}
function V(e, t) {
	return L(() => I(e, t));
}
function H() {
	return z(() => []);
}
function U(e) {
	return z(() => v(e));
}
function W(e) {
	return e instanceof re ? C(e) : Array.from(e);
}
function ue(e, t, n) {
	return L(() => F(e, t, n));
}
function G(e, t) {
	return ue(() => R(t), (t) => t["System.Collections.IEnumerator.MoveNext"]() ? n(e(t["System.Collections.Generic.IEnumerator`1.get_Current"]())) : void 0, (e) => {
		r(e);
	});
}
function de(e, t) {
	return z(() => B(G(e, t)));
}
//#endregion
//#region Kernel.fs
function K(e, t) {
	if (t < 0) throw new g(`DSP ${e} failed (${t})`);
	return t | 0;
}
function q(e) {
	return Number.isInteger(e) && e >= 0 ? e <= 16777215 : !1;
}
var fe = class {
	constructor(e, t) {
		let n = new s(c());
		if (this.exports = e, this.image = t, n.contents = this, this.beforeRender = y(this.image.beforeRender), this.lastCycles = 0, this.lastOperations = 0, this["init@37"] = 1, this.image.version !== 1 || !Number.isInteger(this.image.words) || this.image.words < 256 || this.image.words > 1572864) throw new g("Unsupported kernel image");
		if (!Number.isInteger(this.image.frames) || this.image.frames < 1 || this.image.frames > 1024) throw new g("Invalid block size");
		if (!Number.isInteger(this.image.output) || this.image.output < 0 || this.image.output + 2 * this.image.frames > this.image.words) throw new g("Output buffer outside image");
		if (this.image.outputBank < 0 || this.image.outputBank > 2) throw new g("Invalid output bank");
		K("create", this.exports._dsp_create(this.image.words));
		try {
			let e = this.image.externalMemory;
			if (e != null) {
				let t = e | 0;
				if (!Number.isInteger(t) || t <= 0 || t >= this.image.words) throw new g("Invalid external-memory boundary");
				K("external memory", this.exports._dsp_bridge(t));
			}
			let t = this.image.segments;
			for (let e = 0; e <= t.length - 1; e++) {
				let r = S(e, t);
				if (r.address + r.data.length > this.image.words) throw new g("Memory segment outside image");
				m((e, t) => {
					J(n.contents, r.bank, r.address + e, t);
				}, r.data);
			}
			Y(n.contents, this.image.registers);
			let r = this.image.initialize;
			for (let e = 0; e <= r.length - 1; e++) {
				let t = S(e, r) | 0;
				X(n.contents, t);
			}
		} catch (e) {
			throw this.exports._dsp_destroy(), e;
		}
	}
};
function pe(e, t) {
	return new fe(e, t);
}
function me(e) {
	return e.image;
}
function he(e) {
	return e.lastCycles;
}
function ge(e) {
	return e.lastOperations;
}
function _e(e, t, n) {
	if (!(Number.isInteger(t) && Number.isInteger(n)) || t < 0 || t > 2 || n < 0 || n >= e.image.words) throw new g("Invalid memory read");
	return K("read", e.exports._dsp_read(t, n)) | 0;
}
function J(e, t, n, r) {
	if (!(Number.isInteger(t) && Number.isInteger(n) && q(r)) || t < 0 || t > 2 || n < 0 || n >= e.image.words) throw new g("Invalid memory write");
	K("write", e.exports._dsp_write(t, n, r));
}
function Y(e, t) {
	for (let n = 0; n <= t.length - 1; n++) {
		let r = S(n, t);
		if (!(Number.isInteger(r.register) && q(r.word)) || r.register < 0 || r.register > 33) throw new g("Invalid register write");
		K("register", e.exports._dsp_reg_write(r.register, r.word));
	}
}
function ve(e, t) {
	Y(e, t);
	for (let n = 0; n <= t.length - 1; n++) {
		let r, i = S(n, t);
		e.beforeRender = x((r = e.beforeRender, r.filter((e) => e.register !== i.register)), [i]);
	}
}
function X(e, t) {
	if (!Number.isInteger(t) || t < 0 || t >= e.image.words - 1) throw new g("Invalid entry point");
	return K("call", e.exports._dsp_call(t, 1e6)) | 0;
}
function ye(e) {
	Y(e, e.beforeRender);
	let t = e.image.trig;
	t != null && X(e, t);
}
function Z(e) {
	return _(K("snapshot", e.exports._dsp_snapshot()), (t) => e.exports._dsp_snapshot_word(t) | 0);
}
function be(e, t) {
	if (!Number.isInteger(t) || t < 0 || t >= e.image.words - 1) throw new g("Invalid entry point");
	Y(e, e.beforeRender), K("begin", e.exports._dsp_begin(t));
}
function xe(e) {
	return K("step", e.exports._dsp_step()) | 0;
}
function Q(e) {
	return e.exports._dsp_pc() | 0;
}
function Se(e, t) {
	Y(e, e.beforeRender);
	let n = Z(e), r = $(e), i = Z(e);
	for (let e = 0; e <= ne(t.length, i.length) - 1; e++) S(e, i) !== S(e, n) && b(t, e, S(e, t) + 1 | 0);
	return r;
}
function $(e) {
	Y(e, e.beforeRender);
	let t = e.exports._dsp_cycles(), n = e.exports._dsp_operations();
	X(e, e.image.render), e.lastCycles = e.exports._dsp_cycles() - t, e.lastOperations = e.exports._dsp_operations() - n;
	let r = e.image.channels, i;
	switch (i = r == null ? 1 : r === 1 ? 0 : 1, i) {
		case 0: return _(e.image.frames * 2, (t) => K("read", e.exports._dsp_read(e.image.outputBank, e.image.output + ~~(t / 2))) | 0);
		default: return _(e.image.frames * 2, (t) => K("read", e.exports._dsp_read(e.image.outputBank, e.image.output + t)) | 0);
	}
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Range.js
function Ce(e, t, n, r) {
	let i = o(e, n) | 0;
	if (i === 0) throw new g("The step of a range cannot be zero");
	let a = i > 0;
	return (n) => {
		let i = o(n, t) | 0;
		return a && i <= 0 || !a && i >= 0 ? [n, r(n, e)] : void 0;
	};
}
function we(e, t, n, r, i) {
	let a = Ce(t, n, r, i);
	return z(() => V(a, e));
}
function Te(e, t, n) {
	return we(e, t, n, 0, (e, t) => e + t);
}
//#endregion
export { z as _, _e as a, U as b, Z as c, J as d, me as f, de as g, Q as h, ve as i, xe as l, ge as m, pe as n, $ as o, he as p, be as r, Se as s, Te as t, ye as u, H as v, W as x, G as y };
