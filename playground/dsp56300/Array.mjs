var e = {
	Unspecified: 0,
	Utc: 1,
	Local: 2
}, t = class {
	constructor(e, t) {
		this.message = e ?? "", this.innerException = t;
	}
	toString() {
		return this.message;
	}
};
function n(e) {
	return Array.isArray(e) || ArrayBuffer.isView(e);
}
function r(e) {
	return e != null && typeof e.GetEnumerator == "function";
}
function i(e) {
	return e != null && typeof e.CompareTo == "function";
}
function a(e) {
	return e != null && typeof e.Equals == "function";
}
function o(e) {
	return e != null && typeof e.GetHashCode == "function";
}
function s(e) {
	return e != null && typeof e.Dispose == "function";
}
function c(e) {
	s(e) && e.Dispose();
}
function l() {
	return null;
}
function u(e, t) {
	return Object.getPrototypeOf(e)?.constructor === Object.getPrototypeOf(t)?.constructor;
}
var d = class {
	constructor(e) {
		this.current = l(), this.iter = e;
	}
	"System.Collections.Generic.IEnumerator`1.get_Current"() {
		return this.current;
	}
	"System.Collections.IEnumerator.get_Current"() {
		return this.current;
	}
	"System.Collections.IEnumerator.MoveNext"() {
		let e = this.iter.next();
		return this.current = e.value, !e.done;
	}
	"System.Collections.IEnumerator.Reset"() {
		throw new t("JS iterators cannot be reset");
	}
	Dispose() {}
};
function ee(e) {
	return r(e) ? e.GetEnumerator() : new d(e[Symbol.iterator]());
}
function te(e) {
	return { next() {
		let t = e["System.Collections.IEnumerator.MoveNext"](), n = t ? e["System.Collections.Generic.IEnumerator`1.get_Current"]() : void 0;
		return {
			done: !t,
			value: n
		};
	} };
}
function f(e, t) {
	return e.toString(10).padStart(t, "0");
}
function p(t) {
	let n = t;
	return typeof n.offset == "number" ? n.offset : t.kind === e.Utc ? 0 : t.getTimezoneOffset() * -6e4;
}
var m = class e {
	static id(t) {
		return e.idMap.has(t) || e.idMap.set(t, ++e.count), e.idMap.get(t);
	}
};
m.idMap = /* @__PURE__ */ new WeakMap(), m.count = 0;
function h(e) {
	let t = 0, n = 5381, r = e.length;
	for (; t < r;) n = n * 33 ^ e.charCodeAt(t++);
	return n;
}
function ne(e) {
	return e * 2654435761 | 0;
}
function re(e) {
	return h(e.toString(32));
}
function g(e) {
	let t = 0, n = e.length;
	for (let r = 0; r < n; r++) {
		let n = e[r];
		t = (t << 5) + t ^ n;
	}
	return t;
}
function ie(e) {
	return e.getTime();
}
function ae(e) {
	let t = e.length, n = Array(t);
	for (let r = 0; r < t; r++) n[r] = _(e[r]);
	return g(n);
}
function _(e) {
	if (e == null) return 0;
	switch (typeof e) {
		case "boolean": return +!!e;
		case "number": return ne(e);
		case "bigint": return re(e);
		case "string": return h(e);
		default: return o(e) ? e.GetHashCode() : n(e) ? ae(e) : e instanceof Date ? ie(e) : Object.getPrototypeOf(e)?.constructor === Object ? g(Object.values(e).map((e) => _(e))) : ne(m.id(e));
	}
}
function oe(e, t, n) {
	if (e == null) return t == null;
	if (t == null || e.length !== t.length) return !1;
	for (let r = 0; r < e.length; r++) if (!n(e[r], t[r])) return !1;
	return !0;
}
function se(e, t) {
	return oe(e, t, v);
}
function ce(e, t) {
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return !1;
	n.sort(), r.sort();
	for (let i = 0; i < n.length; i++) if (n[i] !== r[i] || !v(e[n[i]], t[r[i]])) return !1;
	return !0;
}
function v(e, t) {
	return e === t ? !0 : e == null ? t == null : t == null ? !1 : a(e) ? e.Equals(t) : n(e) ? n(t) && se(e, t) : typeof e == "object" ? e instanceof Date ? t instanceof Date && le(e, t) === 0 : Object.getPrototypeOf(e)?.constructor === Object && ce(e, t) : !1;
}
function le(e, t) {
	let n, r;
	return "offset" in e && "offset" in t ? (n = e.getTime(), r = t.getTime()) : (n = e.getTime() + p(e), r = t.getTime() + p(t)), n === r ? 0 : n < r ? -1 : 1;
}
function ue(e, t) {
	return e === t ? 0 : e < t ? -1 : e > t ? 1 : Number.isNaN(e) ? Number.isNaN(t) ? 0 : -1 : 1;
}
function de(e, t, n) {
	if (e == null) return t == null ? 0 : 1;
	if (t == null) return -1;
	if (e.length !== t.length) return e.length < t.length ? -1 : 1;
	for (let r = 0, i = 0; r < e.length; r++) if (i = n(e[r], t[r]), i !== 0) return i;
	return 0;
}
function fe(e, t) {
	return de(e, t, y);
}
function pe(e, t) {
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return n.length < r.length ? -1 : 1;
	n.sort(), r.sort();
	for (let i = 0, a = 0; i < n.length; i++) {
		let o = n[i];
		if (o !== r[i]) return o < r[i] ? -1 : 1;
		if (a = y(e[o], t[o]), a !== 0) return a;
	}
	return 0;
}
function y(e, t) {
	return e === t ? 0 : e == null ? t == null ? 0 : -1 : t == null ? 1 : i(e) ? e.CompareTo(t) : n(e) ? n(t) ? fe(e, t) : -1 : typeof e == "object" ? e instanceof Date ? t instanceof Date ? le(e, t) : -1 : Object.getPrototypeOf(e)?.constructor === Object ? pe(e, t) : -1 : ue(e, t);
}
function me(e, t = 0) {
	let n = 10 ** t, r = +(t ? e * n : e).toFixed(8), i = Math.floor(r), a = r - i, o = a > .49999999 && a < .50000001 ? i % 2 == 0 ? i : i + 1 : Math.round(r);
	return t ? o / n : o;
}
function he(e, t, n, r, i) {
	if (ArrayBuffer.isView(e) && ArrayBuffer.isView(n)) n.set(e.subarray(t, t + i), r);
	else for (let a = 0; a < i; ++a) n[r + a] = e[t + a];
}
function ge(e) {
	let t = 0, n = "[";
	for (let r of e) {
		if (t === 0) n += b(r);
		else if (t === 100) {
			n += "; ...";
			break;
		} else n += "; " + b(r);
		t++;
	}
	return n + "]";
}
function b(e, t = 0) {
	return typeof e == "string" ? "\"" + e + "\"" : x(e, t);
}
function x(e, t = 0) {
	if (typeof e == "object" && e) {
		if (typeof e.toString == "function" && e.toString !== Object.prototype.toString) return e.toString();
		if (Symbol.iterator in e) return ge(e);
		{
			let n = Object.getPrototypeOf(e)?.constructor;
			return n === Object && t < 10 ? "{ " + Object.entries(e).map(([e, n]) => e + " = " + b(n, t + 1)).join("\n  ") + " }" : n?.name ?? "";
		}
	}
	return String(e);
}
function _e(e) {
	let t = {}, n = Object.keys(e);
	for (let r = 0; r < n.length; r++) t[n[r]] = e[n[r]];
	return t;
}
function ve(e) {
	return "{ " + Object.entries(e).map(([e, t]) => e + " = " + b(t)).join("\n  ") + " }";
}
function ye(e) {
	return g(Object.values(e).map((e) => _(e)));
}
function be(e, t) {
	if (e === t) return !0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) if (!v(e[n[r]], t[n[r]])) return !1;
		return !0;
	}
	return !1;
}
function xe(e, t) {
	if (e === t) return 0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) {
			let i = y(e[n[r]], t[n[r]]);
			if (i !== 0) return i;
		}
		return 0;
	}
	return -1;
}
var Se = class {
	toJSON() {
		return _e(this);
	}
	toString() {
		return ve(this);
	}
	GetHashCode() {
		return ye(this);
	}
	Equals(e) {
		return be(this, e);
	}
	CompareTo(e) {
		return xe(this, e);
	}
}, Ce = class {
	get contents() {
		return this.getter();
	}
	set contents(e) {
		this.setter(e);
	}
	constructor(e, t) {
		typeof t == "function" ? (this.getter = e, this.setter = t) : (this.getter = () => e, this.setter = (t) => {
			e = t;
		});
	}
}, S = Symbol("numeric");
function we(e) {
	return typeof e == "number" || typeof e == "bigint" || e?.[S];
}
function Te(e, t) {
	return typeof e == "number" || typeof e == "bigint" ? e < t ? -1 : +(e > t) : e.CompareTo(t);
}
function Ee(e, t) {
	return typeof e == "number" ? e * t : typeof e == "bigint" ? e * BigInt(t) : e[S]().multiply(t);
}
function De(e, t) {
	return typeof e == "number" ? e.toFixed(t) : typeof e == "bigint" ? e.toString() : e[S]().toFixed(t);
}
function C(e, t) {
	return typeof e == "number" ? e.toPrecision(t) : typeof e == "bigint" ? e.toString() : e[S]().toPrecision(t);
}
function Oe(e, t) {
	let n = e < 0n ? "-" : "", r = (e < 0n ? -e : e).toString();
	if (e === 0n) return n + (t ? "0." + "0".repeat(t) : "0") + "e+0";
	t ??= r.length - 1;
	let i = r.length - 1;
	if (r.length > t + 1) {
		let e = r.charCodeAt(t + 1) >= 53;
		r = (BigInt(r.slice(0, t + 1)) + (e ? 1n : 0n)).toString(), r.length > t + 1 && (r = r.slice(0, t + 1), i += 1);
	} else r = r.padEnd(t + 1, "0");
	return n + (t > 0 ? r[0] + "." + r.slice(1) : r[0]) + "e+" + i;
}
function w(e, t) {
	return typeof e == "number" ? e.toExponential(t) : typeof e == "bigint" ? Oe(e, t) : e[S]().toExponential(t);
}
function T(e) {
	return typeof e == "number" ? (Number(e) >>> 0).toString(16) : typeof e == "bigint" ? BigInt.asUintN(64, e).toString(16) : e[S]().toHex();
}
var E = class e {
	constructor(e) {
		this.value = e;
	}
	toJSON() {
		return this.value;
	}
	toString() {
		return String(this.value);
	}
	GetHashCode() {
		return _(this.value);
	}
	Equals(t) {
		return t != null && v(this.value, t instanceof e ? t.value : t);
	}
	CompareTo(t) {
		return t == null ? 1 : y(this.value, t instanceof e ? t.value : t);
	}
};
function D(e) {
	if (e == null) throw new t("Option has no value");
	return e instanceof E ? e.value : e;
}
function ke(e) {
	return e == null || e instanceof E ? new E(e) : e;
}
var Ae = "Collection was empty.", je = "Value cannot be null.", Me = " (Parameter '", O = [
	"Sun",
	"Mon",
	"Tue",
	"Wed",
	"Thu",
	"Fri",
	"Sat"
], k = [
	"Sunday",
	"Monday",
	"Tuesday",
	"Wednesday",
	"Thursday",
	"Friday",
	"Saturday"
], Ne = [
	"Jan",
	"Feb",
	"Mar",
	"Apr",
	"May",
	"Jun",
	"Jul",
	"Aug",
	"Sep",
	"Oct",
	"Nov",
	"Dec"
], A = [
	"January",
	"February",
	"March",
	"April",
	"May",
	"June",
	"July",
	"August",
	"September",
	"October",
	"November",
	"December"
];
function j(e, t, n) {
	let r = 0, i = t;
	for (; i < e.length && e[i] === n;) i++, r++;
	return r;
}
function Pe(e, t) {
	return t >= e.length - 1 ? -1 : e.charCodeAt(t + 1);
}
function Fe(e, n) {
	let r = n, i = e[n], a = "", o = !1;
	for (; n < e.length;) {
		n++;
		let r = e[n];
		if (r === i) {
			o = !0;
			break;
		}
		if (r === "\\") {
			if (n < e.length) n++, a += e[n];
			else throw new t("Invalid string format");
		} else a += r;
	}
	if (!o) throw new t(`Invalid string format could not find matching quote for ${i}`);
	return [a, n - r + 1];
}
function M(n, r, i) {
	let a = 0, o = 0, s = "", c = i ? R(n.getTime(), e.Utc) : n;
	for (; a < r.length;) {
		let n = r[a];
		switch (n) {
			case "d":
				switch (o = j(r, a, "d"), a += o, o) {
					case 1:
						s += B(c);
						break;
					case 2:
						s += f(B(c), 2);
						break;
					case 3:
						s += O[q(c)];
						break;
					default: s += k[q(c)];
				}
				break;
			case "f":
				if (o = j(r, a, "f"), a += o, o <= 3) {
					let e = 10 ** (3 - o);
					s += f(Math.floor(H(c) / e), o);
				} else if (o <= 7) s += ("" + H(c)).padEnd(o, "0");
				else throw "Input string was not in a correct format.";
				break;
			case "F":
				if (o = j(r, a, "F"), a += o, o <= 3) {
					let e = 10 ** (3 - o), t = Math.floor(H(c) / e);
					t != 0 && (s += f(t, o));
				} else if (o <= 7) {
					let e = H(c);
					e != 0 && (s += f(e, 3));
				} else throw "Input string was not in a correct format.";
				break;
			case "g":
				o = j(r, a, "g"), a += o, s += "A.D.";
				break;
			case "h":
				switch (o = j(r, a, "h"), a += o, o) {
					case 1:
						let e = V(c) % 12;
						s += e || 12;
						break;
					default:
						let t = V(c) % 12;
						s += f(t || 12, 2);
				}
				break;
			case "H":
				switch (o = j(r, a, "H"), a += o, o) {
					case 1:
						s += V(c);
						break;
					default: s += f(V(c), 2);
				}
				break;
			case "K":
				if (o = j(r, a, "K"), a += o, o === 1) switch (Ie(c)) {
					case e.Utc:
						s += "Z";
						break;
					case e.Local:
						s += N(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified:
				}
				break;
			case "m":
				switch (o = j(r, a, "m"), a += o, o) {
					case 1:
						s += U(c);
						break;
					default: s += f(U(c), 2);
				}
				break;
			case "M":
				switch (o = j(r, a, "M"), a += o, o) {
					case 1:
						s += W(c);
						break;
					case 2:
						s += f(W(c), 2);
						break;
					case 3:
						s += Ne[W(c) - 1];
						break;
					default: s += A[W(c) - 1];
				}
				break;
			case "s":
				switch (o = j(r, a, "s"), a += o, o) {
					case 1:
						s += G(c);
						break;
					default: s += f(G(c), 2);
				}
				break;
			case "t":
				switch (o = j(r, a, "t"), a += o, o) {
					case 1:
						s += c.getHours() < 12 ? "A" : "P";
						break;
					default: s += c.getHours() < 12 ? "AM" : "PM";
				}
				break;
			case "y":
				switch (o = j(r, a, "y"), a += o, o) {
					case 1:
						s += K(c) % 100;
						break;
					case 2:
						s += f(K(c) % 100, 2);
						break;
					default: s += f(K(c), o);
				}
				break;
			case "z":
				o = j(r, a, "z"), a += o;
				let l = "";
				switch (Ie(c)) {
					case e.Utc:
						l = "+00:00";
						break;
					case e.Local:
						l = N(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified: l = N(qe(c).getTimezoneOffset() * -6e4);
				}
				let u = l[0] === "-" ? "-" : "+", d = parseInt(l.substring(1, 3), 10), ee = parseInt(l.substring(4, 6), 10);
				switch (o) {
					case 1:
						s += `${u}${d}`;
						break;
					case 2:
						s += `${u}${f(d, 2)}`;
						break;
					default: s += `${u}${f(d, 2)}:${f(ee, 2)}`;
				}
				break;
			case ":":
				s += ":", a++;
				break;
			case "/":
				s += "/", a++;
				break;
			case "'":
			case "\"":
				let [te, p] = Fe(r, a);
				s += te, a += p;
				break;
			case "%":
				let m = Pe(r, a);
				if (m >= 0 && m !== 37) a += 2, s += M(c, String.fromCharCode(m), i);
				else throw new t("Invalid format string");
				break;
			case "\\":
				let h = Pe(r, a);
				if (h >= 0) a += 2, s += String.fromCharCode(h);
				else throw new t("Invalid format string");
				break;
			default: a++, s += n;
		}
	}
	return s;
}
function Ie(t) {
	return t.kind ?? e.Unspecified;
}
function N(e) {
	let t = e < 0;
	e = Math.abs(e);
	let n = ~~(e / 36e5), r = e % 36e5 / 6e4;
	return (t ? "-" : "+") + f(n, 2) + ":" + f(r, 2);
}
function Le(t, n) {
	if (n) return t.toISOString();
	{
		let n = t.kind == null || t.kind === e.Local;
		return f(t.getFullYear(), 4) + "-" + f(t.getMonth() + 1, 2) + "-" + f(t.getDate(), 2) + "T" + f(t.getHours(), 2) + ":" + f(t.getMinutes(), 2) + ":" + f(t.getSeconds(), 2) + "." + f(t.getMilliseconds(), 3) + (n ? N(t.getTimezoneOffset() * -6e4) : "");
	}
}
function Re(e, t) {
	let n = e.toISOString();
	return n.substring(0, n.length - 1) + N(t);
}
function ze(n, r) {
	let i = new Date(n.getTime() + (n.offset ?? 0));
	if (typeof r != "string") return i.toISOString().replace(/\.\d+/, "").replace(/[A-Z]|\.\d+/g, " ") + N(n.offset ?? 0);
	if (r.length === 1) switch (r) {
		case "D": return P(i);
		case "d": return F(i);
		case "F": return P(i) + " " + I(i);
		case "f": return P(i) + " " + L(i);
		case "G": return F(i) + " " + I(i);
		case "g": return F(i) + " " + L(i);
		case "M":
		case "m": return Ue(i);
		case "O":
		case "o": return Re(i, n.offset ?? 0);
		case "R":
		case "r": return Be(R(n.getTime(), e.Utc));
		case "s": return Ve(z(i));
		case "T": return I(z(i));
		case "t": return L(z(i));
		case "u": return He(R(n.getTime(), e.Utc));
		case "U": {
			let t = R(n.getTime(), e.Utc);
			return P(t) + " " + I(t);
		}
		case "Y":
		case "y": return We(i);
		default: throw new t("Unrecognized Date print format");
	}
	else return M(i, r, !0);
}
function P(e) {
	return k[q(e)] + ", " + f(B(e), 2) + " " + A[W(e) - 1] + " " + K(e);
}
function F(e) {
	return f(W(e), 2) + "/" + f(B(e), 2) + "/" + K(e);
}
function I(e) {
	return f(V(e), 2) + ":" + f(U(e), 2) + ":" + f(G(e), 2);
}
function L(e) {
	return f(V(e), 2) + ":" + f(U(e), 2);
}
function Be(e) {
	let t = z(e);
	return O[q(t)] + ", " + f(B(t), 2) + " " + Ne[W(t) - 1] + " " + K(t) + " " + f(V(t), 2) + ":" + f(U(t), 2) + ":" + f(G(t), 2) + " GMT";
}
function Ve(e) {
	return f(K(e), 4) + "-" + f(W(e), 2) + "-" + f(B(e), 2) + "T" + f(V(e), 2) + ":" + f(U(e), 2) + ":" + f(G(e), 2);
}
function He(e) {
	let t = z(e);
	return f(K(t), 4) + "-" + f(W(t), 2) + "-" + f(B(t), 2) + " " + f(V(t), 2) + ":" + f(U(t), 2) + ":" + f(G(t), 2) + "Z";
}
function Ue(e) {
	return A[W(e) - 1] + " " + f(B(e), 2);
}
function We(e) {
	return K(e) + " " + A[W(e) - 1];
}
function Ge(n, r) {
	let i = n.kind === e.Utc;
	if (typeof r != "string") return F(n) + " " + I(n);
	if (r.length === 1) switch (r) {
		case "D": return P(n);
		case "d": return F(n);
		case "F": return P(n) + " " + I(n);
		case "f": return P(n) + " " + L(n);
		case "G": return F(n) + " " + I(n);
		case "g": return F(n) + " " + L(n);
		case "M":
		case "m": return Ue(n);
		case "O":
		case "o": return Le(n, i);
		case "R":
		case "r": return Be(n);
		case "s": return Ve(n);
		case "T": return I(n);
		case "t": return L(n);
		case "u": return He(n);
		case "U": return P(z(n)) + " " + I(z(n));
		case "Y":
		case "y": return We(n);
		default: throw new t("Unrecognized Date print format");
	}
	else return M(n, r, i);
}
function Ke(e, t, n) {
	return e.offset == null ? Ge(e, t) : ze(e, t);
}
function R(t, n) {
	let r = new Date(t);
	return r.kind = n ?? e.Unspecified, r;
}
function z(t) {
	return t.kind === e.Utc ? t : R(t.getTime(), e.Utc);
}
function qe(t) {
	return t.kind === e.Local ? t : R(t.getTime(), e.Local);
}
function B(t) {
	return t.kind === e.Utc ? t.getUTCDate() : t.getDate();
}
function V(t) {
	return t.kind === e.Utc ? t.getUTCHours() : t.getHours();
}
function H(t) {
	return t.kind === e.Utc ? t.getUTCMilliseconds() : t.getMilliseconds();
}
function U(t) {
	return t.kind === e.Utc ? t.getUTCMinutes() : t.getMinutes();
}
function W(t) {
	return (t.kind === e.Utc ? t.getUTCMonth() : t.getMonth()) + 1;
}
function G(t) {
	return t.kind === e.Utc ? t.getUTCSeconds() : t.getSeconds();
}
function K(t) {
	return t.kind === e.Utc ? t.getUTCFullYear() : t.getFullYear();
}
function q(t) {
	return t.kind === e.Utc ? t.getUTCDay() : t.getDay();
}
function Je(e) {
	return e.replace(/[$()*+.?[\\\^{|}\]]/g, "\\$&");
}
var Ye = Symbol.for("Fable.DateTimeFormattable");
function Xe(e) {
	return e != null && typeof e[Ye] == "function";
}
function Ze(e, t) {
	return e instanceof Date ? Ke(e, t) : e[Ye](t);
}
var J = /(^|[^%])%([0+\- ]*)(\*|\d+)?(?:\.(\d+))?(\w)/g;
function Qe(e, t) {
	return Te(e, t) < 0;
}
function $e(e) {
	return {
		input: e,
		cont: it(e)
	};
}
function et(e, t) {
	return typeof t == "string" ? e(t) : t.cont(e);
}
function tt(e) {
	return et((e) => console.log(e), e);
}
function nt(e, t, n, r, i) {
	let a = "";
	if (t ||= "", i ||= "", we(e)) switch (i.toLowerCase() !== "x" && (Qe(e, 0) ? (e = Ee(e, -1), a = "-") : t.indexOf(" ") >= 0 ? a = " " : t.indexOf("+") >= 0 && (a = "+")), r = r == null ? null : parseInt(r, 10), i) {
		case "f":
		case "F":
			r ??= 6, e = De(e, r);
			break;
		case "g":
		case "G":
			e = r == null ? C(e) : C(e, r);
			break;
		case "e":
		case "E":
			e = r == null ? w(e) : w(e, r);
			break;
		case "x":
			e = T(e);
			break;
		case "X":
			e = T(e).toUpperCase();
			break;
		default: e = String(e);
	}
	else e = e instanceof Date || Xe(e) ? Ze(e, void 0) : i === "A" && typeof e == "string" ? "\"" + e + "\"" : x(e);
	if (n = typeof n == "number" ? n : parseInt(n, 10), isNaN(n)) e = a + e;
	else {
		let r = t.indexOf("0") >= 0, i = t.indexOf("-") >= 0, o = i || !r ? " " : "0";
		o === "0" ? (e = ot(e, n - a.length, o, i), e = a + e) : e = ot(a + e, n, o, i);
	}
	return e;
}
function rt(e, n, r, i = "", a = -1) {
	return (...o) => {
		let s = i, c = n.slice(), l = r.slice();
		for (let e of o) {
			let [, , n, r, i, o] = l[0], u = r;
			if (a >= 0) u = a, a = -1;
			else if (u === "*") {
				if (e < 0) throw new t("Non-negative number required");
				a = e;
				continue;
			}
			s += c[0], s += nt(e, n, u, i, o), c.splice(0, 1), l.splice(0, 1);
		}
		return l.length === 0 ? (s += c[0], e(s)) : rt(e, c, l, s, a);
	};
}
function it(e) {
	return (t) => {
		J.lastIndex = 0;
		let n = [], r = [], i = 0, a = J.exec(e);
		for (; a;) {
			let t = a.index + (a[1] || "").length;
			n.push(e.substring(i, t).replace(/%%/g, "%")), r.push(a), i = J.lastIndex, --J.lastIndex, a = J.exec(e);
		}
		return n.length === 0 ? t(e.replace(/%%/g, "%")) : (n.push(e.substring(i).replace(/%%/g, "%")), rt(t, n, r));
	};
}
function at(e) {
	return typeof e != "string" || e.length === 0;
}
function ot(e, t, n, r) {
	n ||= " ", t -= e.length;
	for (let i = 0; i < t; i++) e = r ? e + n : n + e;
	return e;
}
function st(e, n, r, i) {
	if (r = typeof r == "number" ? r : void 0, i = typeof i == "number" ? i : 0, r && r < 0) throw new t("Count cannot be less than zero");
	if (r === 0) return [];
	let a = (i & 1) == 1, o = (i & 2) == 2;
	n ||= [], n = n.filter((e) => e).map(Je), n = n.length > 0 ? n : ["\\s"];
	let s = [], c = new RegExp(n.join("|"), "g"), l = !0, u = 0;
	do {
		let t = c.exec(e);
		if (t === null) {
			let t = o ? e.substring(u).trim() : e.substring(u);
			(!a || t.length > 0) && s.push(t), l = !1;
		} else {
			let n = o ? e.substring(u, t.index).trim() : e.substring(u, t.index);
			(!a || n.length > 0) && (r != null && s.length + 1 === r ? (s.push(o ? e.substring(u).trim() : e.substring(u)), l = !1) : s.push(n)), u = c.lastIndex;
		}
	} while (l);
	return s;
}
var ct = class extends t {
	constructor(e) {
		super(e);
	}
};
function lt(e) {
	return new ct(e);
}
var ut = class extends t {
	constructor(e) {
		super(e);
	}
};
function dt(e) {
	return new ut(e);
}
var ft = class extends t {
	constructor(e, t, n) {
		super(at(t) ? e : e + Me + t + "')", n), this.paramName = t;
	}
}, pt = class extends ft {
	constructor(e, t) {
		super(t, e, l());
	}
};
function mt(e, t) {
	return new pt(e, t);
}
function ht(e) {
	return mt(e, je);
}
function Y(e, t) {
	if (e == null) return Array(t);
	{
		let n = e;
		return typeof n == "function" ? new n(t) : Array(t);
	}
}
function gt(e) {
	return e === Infinity || e === -Infinity;
}
function _t(e, t) {
	return Math.max(e, t);
}
function vt(e, t) {
	return Math.min(e, t);
}
function X(e) {
	return !!v(e, l());
}
function yt(e, t) {
	if (v(t, l())) throw ht(e);
	return t;
}
function Z() {
	throw new t("An index satisfying the predicate was not found in the collection.");
}
function bt(e, t, n) {
	let r = e.length | 0, i = t.length | 0, a = Y(n, r + i);
	for (let t = 0; t <= r - 1; t++) $(a, t, Q(t, e));
	for (let e = 0; e <= i - 1; e++) $(a, e + r, Q(e, t));
	return a;
}
function xt(e, t, n, r) {
	let i = t | 0;
	return e.fill(r, i, i + n);
}
function St(e, t, n) {
	let r = t.length | 0, i = Y(n, r);
	for (let n = 0; n <= r - 1; n++) $(i, n, e(n, Q(n, t)));
	return i;
}
function Ct(e) {
	let t = e.length | 0, n = Array(t);
	for (let r = 0; r <= t - 1; r++) $(n, r, [r, Q(r, e)]);
	return n;
}
function wt(e, t) {
	let n = Y(t, 1);
	return $(n, 0, e), n;
}
function Tt(e, n, r) {
	if (e < 0) throw new t("The input must be non-negative (Parameter 'count')");
	let i = Y(r, e);
	for (let t = 0; t <= e - 1; t++) $(i, t, n(t));
	return i;
}
function Et(e) {
	return e.slice();
}
function Dt(e, t, n, r, i) {
	he(e, t, n, r, i);
}
function Ot(e, t) {
	let n = t.find(e);
	return n == null ? Z() : D(n);
}
function kt(e, t) {
	let n = t.findIndex(e) | 0;
	return n > -1 ? n | 0 : (Z(), -1);
}
function At(e, t) {
	for (let n = 0; n <= t.length - 1; n++) e(n, Q(n, t));
}
function jt(e, t, n) {
	if (X(t)) return !!X(n);
	if (X(n)) return !1;
	{
		let r = 0, i = !0, a = t.length | 0, o = n.length | 0;
		if (a > o || a < o) return !1;
		for (; r < a && i;) i = e(Q(r, t), Q(r, n)), r = r + 1 | 0;
		return i;
	}
}
function Q(e, n) {
	if (e < 0 || e >= n.length) throw new t("Index was outside the bounds of the array. (Parameter 'index')");
	return n[e];
}
function $(e, n, r) {
	if (n < 0 || n >= e.length) throw new t("Index was outside the bounds of the array. (Parameter 'index')");
	e[n] = r;
}
export { x as A, h as B, st as C, D, ke as E, c as F, te as H, oe as I, v as L, g as M, y as N, Ce as O, l as P, ee as R, $e as S, Ae as T, _ as V, gt as _, xt as a, lt as b, Ct as c, At as d, St as f, yt as g, X as h, jt as i, t as j, Se as k, Tt as l, wt as m, Et as n, Ot as o, $ as p, Dt as r, kt as s, bt as t, Q as u, _t as v, tt as w, dt as x, vt as y, me as z };
