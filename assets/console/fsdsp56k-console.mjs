// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Util.js
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
function f(e) {
	return r(e) ? e.GetEnumerator() : new d(e[Symbol.iterator]());
}
function p(e) {
	return { next() {
		let t = e["System.Collections.IEnumerator.MoveNext"](), n = t ? e["System.Collections.Generic.IEnumerator`1.get_Current"]() : void 0;
		return {
			done: !t,
			value: n
		};
	} };
}
function m(e, t) {
	return e.toString(10).padStart(t, "0");
}
function h(t) {
	let n = t;
	return typeof n.offset == "number" ? n.offset : t.kind === e.Utc ? 0 : t.getTimezoneOffset() * -6e4;
}
var g = class e {
	static id(t) {
		return e.idMap.has(t) || e.idMap.set(t, ++e.count), e.idMap.get(t);
	}
};
g.idMap = /* @__PURE__ */ new WeakMap(), g.count = 0;
function _(e) {
	let t = 0, n = 5381, r = e.length;
	for (; t < r;) n = n * 33 ^ e.charCodeAt(t++);
	return n;
}
function ee(e) {
	return e * 2654435761 | 0;
}
function te(e) {
	return _(e.toString(32));
}
function ne(e) {
	let t = 0, n = e.length;
	for (let r = 0; r < n; r++) {
		let n = e[r];
		t = (t << 5) + t ^ n;
	}
	return t;
}
function re(e) {
	return e.getTime();
}
function ie(e) {
	let t = e.length, n = Array(t);
	for (let r = 0; r < t; r++) n[r] = v(e[r]);
	return ne(n);
}
function v(e) {
	if (e == null) return 0;
	switch (typeof e) {
		case "boolean": return +!!e;
		case "number": return ee(e);
		case "bigint": return te(e);
		case "string": return _(e);
		default: return o(e) ? e.GetHashCode() : n(e) ? ie(e) : e instanceof Date ? re(e) : Object.getPrototypeOf(e)?.constructor === Object ? ne(Object.values(e).map((e) => v(e))) : ee(g.id(e));
	}
}
function ae(e, t, n) {
	if (e == null) return t == null;
	if (t == null || e.length !== t.length) return !1;
	for (let r = 0; r < e.length; r++) if (!n(e[r], t[r])) return !1;
	return !0;
}
function oe(e, t) {
	return ae(e, t, y);
}
function se(e, t) {
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return !1;
	n.sort(), r.sort();
	for (let i = 0; i < n.length; i++) if (n[i] !== r[i] || !y(e[n[i]], t[r[i]])) return !1;
	return !0;
}
function y(e, t) {
	return e === t ? !0 : e == null ? t == null : t == null ? !1 : a(e) ? e.Equals(t) : n(e) ? n(t) && oe(e, t) : typeof e == "object" ? e instanceof Date ? t instanceof Date && ce(e, t) === 0 : Object.getPrototypeOf(e)?.constructor === Object && se(e, t) : !1;
}
function ce(e, t) {
	let n, r;
	return "offset" in e && "offset" in t ? (n = e.getTime(), r = t.getTime()) : (n = e.getTime() + h(e), r = t.getTime() + h(t)), n === r ? 0 : n < r ? -1 : 1;
}
function le(e, t) {
	return e === t ? 0 : e < t ? -1 : e > t ? 1 : Number.isNaN(e) ? Number.isNaN(t) ? 0 : -1 : 1;
}
function ue(e, t, n) {
	if (e == null) return t == null ? 0 : 1;
	if (t == null) return -1;
	if (e.length !== t.length) return e.length < t.length ? -1 : 1;
	for (let r = 0, i = 0; r < e.length; r++) if (i = n(e[r], t[r]), i !== 0) return i;
	return 0;
}
function de(e, t) {
	return ue(e, t, b);
}
function fe(e, t) {
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return n.length < r.length ? -1 : 1;
	n.sort(), r.sort();
	for (let i = 0, a = 0; i < n.length; i++) {
		let o = n[i];
		if (o !== r[i]) return o < r[i] ? -1 : 1;
		if (a = b(e[o], t[o]), a !== 0) return a;
	}
	return 0;
}
function b(e, t) {
	return e === t ? 0 : e == null ? t == null ? 0 : -1 : t == null ? 1 : i(e) ? e.CompareTo(t) : n(e) ? n(t) ? de(e, t) : -1 : typeof e == "object" ? e instanceof Date ? t instanceof Date ? ce(e, t) : -1 : Object.getPrototypeOf(e)?.constructor === Object ? fe(e, t) : -1 : le(e, t);
}
function pe(e, t = 0) {
	let n = 10 ** t, r = +(t ? e * n : e).toFixed(8), i = Math.floor(r), a = r - i, o = a > .49999999 && a < .50000001 ? i % 2 == 0 ? i : i + 1 : Math.round(r);
	return t ? o / n : o;
}
var me = /* @__PURE__ */ new WeakMap();
function he(e) {
	if (e == null) return null;
	let t = (t, n, r) => e(t)(n)(r);
	return me.set(t, e), t;
}
function ge(e) {
	return me.get(e) ?? ((t) => (n) => (r) => (i) => (a) => e(t, n, r, i, a));
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Option.js
var _e = class e {
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
		return v(this.value);
	}
	Equals(t) {
		return t != null && y(this.value, t instanceof e ? t.value : t);
	}
	CompareTo(t) {
		return t == null ? 1 : b(this.value, t instanceof e ? t.value : t);
	}
};
function x(e) {
	if (e == null) throw new t("Option has no value");
	return e instanceof _e ? e.value : e;
}
function ve(e) {
	return e == null || e instanceof _e ? new _e(e) : e;
}
function ye(e, t) {
	return e == null ? t : x(e);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Types.js
function be(e) {
	let t = 0, n = "[";
	for (let r of e) {
		if (t === 0) n += xe(r);
		else if (t === 100) {
			n += "; ...";
			break;
		} else n += "; " + xe(r);
		t++;
	}
	return n + "]";
}
function xe(e, t = 0) {
	return typeof e == "string" ? "\"" + e + "\"" : Se(e, t);
}
function Se(e, t = 0) {
	if (typeof e == "object" && e) {
		if (typeof e.toString == "function" && e.toString !== Object.prototype.toString) return e.toString();
		if (Symbol.iterator in e) return be(e);
		{
			let n = Object.getPrototypeOf(e)?.constructor;
			return n === Object && t < 10 ? "{ " + Object.entries(e).map(([e, n]) => e + " = " + xe(n, t + 1)).join("\n  ") + " }" : n?.name ?? "";
		}
	}
	return String(e);
}
function Ce(e, t) {
	if (t.length === 0) return e;
	{
		let n, r = !0;
		return t.length === 1 ? (n = xe(t[0]), r = n.indexOf(" ") >= 0) : n = t.map((e) => xe(e)).join(", "), e + (r ? " (" : " ") + n + (r ? ")" : "");
	}
}
var we = class {
	get name() {
		return this.cases()[this.tag];
	}
	toJSON() {
		return this.fields.length === 0 ? this.name : [this.name].concat(this.fields);
	}
	toString() {
		return Ce(this.name, this.fields);
	}
	GetHashCode() {
		let e = this.fields.map((e) => v(e));
		return e.splice(0, 0, ee(this.tag)), ne(e);
	}
	Equals(e) {
		return this === e ? !0 : u(this, e) ? this.tag === e.tag && oe(this.fields, e.fields) : !1;
	}
	CompareTo(e) {
		return this === e ? 0 : u(this, e) ? this.tag === e.tag ? de(this.fields, e.fields) : this.tag < e.tag ? -1 : 1 : -1;
	}
};
function Te(e) {
	let t = {}, n = Object.keys(e);
	for (let r = 0; r < n.length; r++) t[n[r]] = e[n[r]];
	return t;
}
function Ee(e) {
	return "{ " + Object.entries(e).map(([e, t]) => e + " = " + xe(t)).join("\n  ") + " }";
}
function De(e) {
	return ne(Object.values(e).map((e) => v(e)));
}
function Oe(e, t) {
	if (e === t) return !0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) if (!y(e[n[r]], t[n[r]])) return !1;
		return !0;
	}
	return !1;
}
function ke(e, t) {
	if (e === t) return 0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) {
			let i = b(e[n[r]], t[n[r]]);
			if (i !== 0) return i;
		}
		return 0;
	}
	return -1;
}
var S = class {
	toJSON() {
		return Te(this);
	}
	toString() {
		return Ee(this);
	}
	GetHashCode() {
		return De(this);
	}
	Equals(e) {
		return Oe(this, e);
	}
	CompareTo(e) {
		return ke(this, e);
	}
}, C = class {
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
}, w = Symbol("numeric");
function Ae(e) {
	return typeof e == "number" || typeof e == "bigint" || e?.[w];
}
function je(e) {
	return typeof e == "number" && Number.isInteger(e) || typeof e == "bigint";
}
function Me(e, t) {
	return typeof e == "number" || typeof e == "bigint" ? e < t ? -1 : +(e > t) : e.CompareTo(t);
}
function Ne(e, t) {
	return typeof e == "number" ? e * t : typeof e == "bigint" ? e * BigInt(t) : e[w]().multiply(t);
}
function Pe(e, t) {
	return typeof e == "number" ? e / t : typeof e == "bigint" ? Number(e) / t : e[w]().multiply(1 / t);
}
function Fe(e, t) {
	return typeof e == "number" ? e.toFixed(t) : typeof e == "bigint" ? e.toString() : e[w]().toFixed(t);
}
function Ie(e, t) {
	return typeof e == "number" ? e.toPrecision(t) : typeof e == "bigint" ? e.toString() : e[w]().toPrecision(t);
}
function Le(e, t) {
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
function Re(e, t) {
	return typeof e == "number" ? e.toExponential(t) : typeof e == "bigint" ? Le(e, t) : e[w]().toExponential(t);
}
function ze(e) {
	return typeof e == "number" ? (Number(e) >>> 0).toString(16) : typeof e == "bigint" ? BigInt.asUintN(64, e).toString(16) : e[w]().toHex();
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Global.js
var Be = "Collection was empty.", Ve = "Value cannot be null.", He = " (Parameter '", Ue = "The given key was not present in the dictionary.", We = [
	"Sun",
	"Mon",
	"Tue",
	"Wed",
	"Thu",
	"Fri",
	"Sat"
], Ge = [
	"Sunday",
	"Monday",
	"Tuesday",
	"Wednesday",
	"Thursday",
	"Friday",
	"Saturday"
], Ke = [
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
], qe = [
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
function T(e, t, n) {
	let r = 0, i = t;
	for (; i < e.length && e[i] === n;) i++, r++;
	return r;
}
function Je(e, t) {
	return t >= e.length - 1 ? -1 : e.charCodeAt(t + 1);
}
function Ye(e, n) {
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
function Xe(n, r, i) {
	let a = 0, o = 0, s = "", c = i ? ut(n.getTime(), e.Utc) : n;
	for (; a < r.length;) {
		let n = r[a];
		switch (n) {
			case "d":
				switch (o = T(r, a, "d"), a += o, o) {
					case 1:
						s += k(c);
						break;
					case 2:
						s += m(k(c), 2);
						break;
					case 3:
						s += We[ht(c)];
						break;
					default: s += Ge[ht(c)];
				}
				break;
			case "f":
				if (o = T(r, a, "f"), a += o, o <= 3) {
					let e = 10 ** (3 - o);
					s += m(Math.floor(pt(c) / e), o);
				} else if (o <= 7) s += ("" + pt(c)).padEnd(o, "0");
				else throw "Input string was not in a correct format.";
				break;
			case "F":
				if (o = T(r, a, "F"), a += o, o <= 3) {
					let e = 10 ** (3 - o), t = Math.floor(pt(c) / e);
					t != 0 && (s += m(t, o));
				} else if (o <= 7) {
					let e = pt(c);
					e != 0 && (s += m(e, 3));
				} else throw "Input string was not in a correct format.";
				break;
			case "g":
				o = T(r, a, "g"), a += o, s += "A.D.";
				break;
			case "h":
				switch (o = T(r, a, "h"), a += o, o) {
					case 1:
						let e = A(c) % 12;
						s += e || 12;
						break;
					default:
						let t = A(c) % 12;
						s += m(t || 12, 2);
				}
				break;
			case "H":
				switch (o = T(r, a, "H"), a += o, o) {
					case 1:
						s += A(c);
						break;
					default: s += m(A(c), 2);
				}
				break;
			case "K":
				if (o = T(r, a, "K"), a += o, o === 1) switch (Ze(c)) {
					case e.Utc:
						s += "Z";
						break;
					case e.Local:
						s += Qe(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified:
				}
				break;
			case "m":
				switch (o = T(r, a, "m"), a += o, o) {
					case 1:
						s += j(c);
						break;
					default: s += m(j(c), 2);
				}
				break;
			case "M":
				switch (o = T(r, a, "M"), a += o, o) {
					case 1:
						s += M(c);
						break;
					case 2:
						s += m(M(c), 2);
						break;
					case 3:
						s += Ke[M(c) - 1];
						break;
					default: s += qe[M(c) - 1];
				}
				break;
			case "s":
				switch (o = T(r, a, "s"), a += o, o) {
					case 1:
						s += mt(c);
						break;
					default: s += m(mt(c), 2);
				}
				break;
			case "t":
				switch (o = T(r, a, "t"), a += o, o) {
					case 1:
						s += c.getHours() < 12 ? "A" : "P";
						break;
					default: s += c.getHours() < 12 ? "AM" : "PM";
				}
				break;
			case "y":
				switch (o = T(r, a, "y"), a += o, o) {
					case 1:
						s += N(c) % 100;
						break;
					case 2:
						s += m(N(c) % 100, 2);
						break;
					default: s += m(N(c), o);
				}
				break;
			case "z":
				o = T(r, a, "z"), a += o;
				let l = "";
				switch (Ze(c)) {
					case e.Utc:
						l = "+00:00";
						break;
					case e.Local:
						l = Qe(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified: l = Qe(ft(c).getTimezoneOffset() * -6e4);
				}
				let u = l[0] === "-" ? "-" : "+", d = parseInt(l.substring(1, 3), 10), f = parseInt(l.substring(4, 6), 10);
				switch (o) {
					case 1:
						s += `${u}${d}`;
						break;
					case 2:
						s += `${u}${m(d, 2)}`;
						break;
					default: s += `${u}${m(d, 2)}:${m(f, 2)}`;
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
				let [p, h] = Ye(r, a);
				s += p, a += h;
				break;
			case "%":
				let g = Je(r, a);
				if (g >= 0 && g !== 37) a += 2, s += Xe(c, String.fromCharCode(g), i);
				else throw new t("Invalid format string");
				break;
			case "\\":
				let _ = Je(r, a);
				if (_ >= 0) a += 2, s += String.fromCharCode(_);
				else throw new t("Invalid format string");
				break;
			default: a++, s += n;
		}
	}
	return s;
}
function Ze(t) {
	return t.kind ?? e.Unspecified;
}
function Qe(e) {
	let t = e < 0;
	e = Math.abs(e);
	let n = ~~(e / 36e5), r = e % 36e5 / 6e4;
	return (t ? "-" : "+") + m(n, 2) + ":" + m(r, 2);
}
function $e(t, n) {
	if (n) return t.toISOString();
	{
		let n = t.kind == null || t.kind === e.Local;
		return m(t.getFullYear(), 4) + "-" + m(t.getMonth() + 1, 2) + "-" + m(t.getDate(), 2) + "T" + m(t.getHours(), 2) + ":" + m(t.getMinutes(), 2) + ":" + m(t.getSeconds(), 2) + "." + m(t.getMilliseconds(), 3) + (n ? Qe(t.getTimezoneOffset() * -6e4) : "");
	}
}
function et(e, t) {
	let n = e.toISOString();
	return n.substring(0, n.length - 1) + Qe(t);
}
function tt(n, r) {
	let i = new Date(n.getTime() + (n.offset ?? 0));
	if (typeof r != "string") return i.toISOString().replace(/\.\d+/, "").replace(/[A-Z]|\.\d+/g, " ") + Qe(n.offset ?? 0);
	if (r.length === 1) switch (r) {
		case "D": return E(i);
		case "d": return D(i);
		case "F": return E(i) + " " + O(i);
		case "f": return E(i) + " " + nt(i);
		case "G": return D(i) + " " + O(i);
		case "g": return D(i) + " " + nt(i);
		case "M":
		case "m": return ot(i);
		case "O":
		case "o": return et(i, n.offset ?? 0);
		case "R":
		case "r": return rt(ut(n.getTime(), e.Utc));
		case "s": return it(dt(i));
		case "T": return O(dt(i));
		case "t": return nt(dt(i));
		case "u": return at(ut(n.getTime(), e.Utc));
		case "U": {
			let t = ut(n.getTime(), e.Utc);
			return E(t) + " " + O(t);
		}
		case "Y":
		case "y": return st(i);
		default: throw new t("Unrecognized Date print format");
	}
	else return Xe(i, r, !0);
}
function E(e) {
	return Ge[ht(e)] + ", " + m(k(e), 2) + " " + qe[M(e) - 1] + " " + N(e);
}
function D(e) {
	return m(M(e), 2) + "/" + m(k(e), 2) + "/" + N(e);
}
function O(e) {
	return m(A(e), 2) + ":" + m(j(e), 2) + ":" + m(mt(e), 2);
}
function nt(e) {
	return m(A(e), 2) + ":" + m(j(e), 2);
}
function rt(e) {
	let t = dt(e);
	return We[ht(t)] + ", " + m(k(t), 2) + " " + Ke[M(t) - 1] + " " + N(t) + " " + m(A(t), 2) + ":" + m(j(t), 2) + ":" + m(mt(t), 2) + " GMT";
}
function it(e) {
	return m(N(e), 4) + "-" + m(M(e), 2) + "-" + m(k(e), 2) + "T" + m(A(e), 2) + ":" + m(j(e), 2) + ":" + m(mt(e), 2);
}
function at(e) {
	let t = dt(e);
	return m(N(t), 4) + "-" + m(M(t), 2) + "-" + m(k(t), 2) + " " + m(A(t), 2) + ":" + m(j(t), 2) + ":" + m(mt(t), 2) + "Z";
}
function ot(e) {
	return qe[M(e) - 1] + " " + m(k(e), 2);
}
function st(e) {
	return N(e) + " " + qe[M(e) - 1];
}
function ct(n, r) {
	let i = n.kind === e.Utc;
	if (typeof r != "string") return D(n) + " " + O(n);
	if (r.length === 1) switch (r) {
		case "D": return E(n);
		case "d": return D(n);
		case "F": return E(n) + " " + O(n);
		case "f": return E(n) + " " + nt(n);
		case "G": return D(n) + " " + O(n);
		case "g": return D(n) + " " + nt(n);
		case "M":
		case "m": return ot(n);
		case "O":
		case "o": return $e(n, i);
		case "R":
		case "r": return rt(n);
		case "s": return it(n);
		case "T": return O(n);
		case "t": return nt(n);
		case "u": return at(n);
		case "U": return E(dt(n)) + " " + O(dt(n));
		case "Y":
		case "y": return st(n);
		default: throw new t("Unrecognized Date print format");
	}
	else return Xe(n, r, i);
}
function lt(e, t, n) {
	return e.offset == null ? ct(e, t) : tt(e, t);
}
function ut(t, n) {
	let r = new Date(t);
	return r.kind = n ?? e.Unspecified, r;
}
function dt(t) {
	return t.kind === e.Utc ? t : ut(t.getTime(), e.Utc);
}
function ft(t) {
	return t.kind === e.Local ? t : ut(t.getTime(), e.Local);
}
function k(t) {
	return t.kind === e.Utc ? t.getUTCDate() : t.getDate();
}
function A(t) {
	return t.kind === e.Utc ? t.getUTCHours() : t.getHours();
}
function pt(t) {
	return t.kind === e.Utc ? t.getUTCMilliseconds() : t.getMilliseconds();
}
function j(t) {
	return t.kind === e.Utc ? t.getUTCMinutes() : t.getMinutes();
}
function M(t) {
	return (t.kind === e.Utc ? t.getUTCMonth() : t.getMonth()) + 1;
}
function mt(t) {
	return t.kind === e.Utc ? t.getUTCSeconds() : t.getSeconds();
}
function N(t) {
	return t.kind === e.Utc ? t.getUTCFullYear() : t.getFullYear();
}
function ht(t) {
	return t.kind === e.Utc ? t.getUTCDay() : t.getDay();
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/RegExp.js
function gt(e) {
	return e.replace(/[$()*+.?[\\\^{|}\]]/g, "\\$&");
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/String.js
var _t = Symbol.for("Fable.DateTimeFormattable");
function vt(e) {
	return e != null && typeof e[_t] == "function";
}
function yt(e, t) {
	return e instanceof Date ? lt(e, t) : e[_t](t);
}
var bt = /\{(\d+)(,-?\d+)?(?:\:([a-zA-Z])(\d{0,2})|\:(.+?))?\}/g;
function xt(e, t) {
	return Me(e, t) < 0;
}
function St(e) {
	let [t, n] = e.split(".");
	return n === void 0 && (n = ""), {
		integral: t,
		decimal: n
	};
}
function Ct(e) {
	return e.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}
function wt(e, ...n) {
	let r;
	return typeof e == "object" ? (r = String(n[0]), n.shift()) : r = e, r.replace(bt, (e, r, i, a, o, s) => {
		if (r < 0 || r >= n.length) throw new t("Index must be greater or equal to zero and less than the arguments' length.");
		let c = n[r], l;
		if (Ae(c)) switch (o = o == "" ? null : parseInt(o, 10), a) {
			case "b":
			case "B":
				if (!je(c)) throw new t("Format specifier was invalid.");
				c = (c >>> 0).toString(2).replace(/^0+/, "").padStart(o || 1, "0");
				break;
			case "c":
			case "C":
				let e = xt(c, 0);
				xt(c, 0) && (c = Ne(c, -1)), o ??= 2, c = Fe(c, o), l = St(c), c = o > 0 ? "¤" + Ct(l.integral) + "." + kt(l.decimal, o, "0") : "¤" + Ct(l.integral), e && (c = "(" + c + ")");
				break;
			case "d":
			case "D":
				if (!je(c)) throw new t("Format specifier was invalid.");
				c = String(c), o != null && (c = c.startsWith("-") ? "-" + Ot(c.substring(1), o, "0") : Ot(c, o, "0"));
				break;
			case "e":
			case "E": {
				o ??= 6, c = String(Re(c, o));
				let e = c.indexOf("e");
				c = c.slice(0, e) + a + c[e + 1] + Ot(c.slice(e + 2), 3, "0");
				break;
			}
			case "f":
			case "F":
				o ??= 2, c = Fe(c, o), o > 0 && (l = St(c), c = l.integral + "." + kt(l.decimal, o, "0"));
				break;
			case "g":
			case "G": {
				c = o == null ? Ie(c) : Ie(c, o);
				let e = c.indexOf("e");
				if (e >= 0) {
					let t = jt(jt(c.slice(0, e), "0"), "."), n = c[e + 1], r = c.slice(e + 2), i = r.length < 2 ? "0" + r : r;
					c = t + (a === "G" ? "E" : "e") + n + i;
				} else c.indexOf(".") >= 0 && (c = jt(jt(c, "0"), "."));
				break;
			}
			case "n":
			case "N":
				o ??= 2, c = Fe(c, o), l = St(c), c = o > 0 ? Ct(l.integral) + "." + kt(l.decimal, o, "0") : Ct(l.integral);
				break;
			case "p":
			case "P":
				o ??= 2, c = Fe(Ne(c, 100), o), l = St(c), c = o > 0 ? Ct(l.integral) + "." + kt(l.decimal, o, "0") + " %" : Ct(l.integral) + " %";
				break;
			case "r":
			case "R": throw new t("The round-trip format is not supported by Fable");
			case "x":
			case "X":
				if (!je(c)) throw new t("Format specifier was invalid.");
				o ??= 1, c = Ot(ze(c), o, "0"), a === "X" && (c = c.toUpperCase());
				break;
			default:
				if (a) throw new t("Format specifier was invalid.");
				if (s) {
					let e = "", t = s, n = (t.match(/%/g) ?? []).length;
					n > 0 && (c = Ne(c, 100 ** n)), c = t.replace(/([0#,]+)(\.[0#]+)?(,*)/, (t, n, r) => {
						let i = /,*$/.exec(n)[0].length;
						i > 0 && (n = n.substring(0, n.length - i), c = Pe(c, 1e3 ** i)), xt(c, 0) && (c = Ne(c, -1), e = "-"), r = r == null ? "" : r.substring(1), c = Fe(c, Math.max(r.length, 0));
						let [a, o] = c.split(".");
						o ||= "";
						let s = n.replace(/,/g, "").replace(/^#+/, "").length;
						a = Ot(a, s, "0");
						let l = r.replace(/#+$/, "").length;
						if (l > o.length ? o = kt(o, l, "0") : l < o.length && (o = o.substring(0, l) + o.substring(l).replace(/0+$/, "")), n.indexOf(",") > 0) {
							let e = a.length % 3, t = Math.floor(a.length / 3), n = e > 0 ? a.substr(0, e) + (t > 0 ? "," : "") : "";
							for (let r = 0; r < t; r++) n += a.substr(e + r * 3, 3) + (r < t - 1 ? "," : "");
							a = n;
						}
						return o.length > 0 ? a + "." + o : a;
					}), c = e + c;
				}
		}
		else c = c instanceof Date || vt(c) ? yt(c, s || a) : Se(c);
		return i = parseInt((i || " ").substring(1), 10), isNaN(i) || (c = Dt(String(c), Math.abs(i), " ", i < 0)), c;
	});
}
function Tt(e) {
	return typeof e != "string" || e.length === 0;
}
function Et(e, t) {
	return Array.isArray(t) ? t.join(e) : Array.from(t).join(e);
}
function Dt(e, t, n, r) {
	n ||= " ", t -= e.length;
	for (let i = 0; i < t; i++) e = r ? e + n : n + e;
	return e;
}
function Ot(e, t, n) {
	return Dt(e, t, n);
}
function kt(e, t, n) {
	return Dt(e, t, n, !0);
}
function At(e, n, r, i) {
	if (r = typeof r == "number" ? r : void 0, i = typeof i == "number" ? i : 0, r && r < 0) throw new t("Count cannot be less than zero");
	if (r === 0) return [];
	let a = (i & 1) == 1, o = (i & 2) == 2;
	n ||= [], n = n.filter((e) => e).map(gt), n = n.length > 0 ? n : ["\\s"];
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
function jt(e, ...t) {
	return t.length === 0 ? e.trimEnd() : e.replace(RegExp("[" + gt(t.join("")) + "]+$"), "");
}
function Mt(e, n, r) {
	if (n + (r || 0) > e.length) throw new t("Invalid startIndex and/or length");
	return r == null ? e.substr(n) : e.substr(n, r);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/System.js
var Nt = class extends t {
	constructor(e) {
		super(e);
	}
};
function Pt(e) {
	return new Nt(e);
}
var Ft = class extends t {
	constructor(e) {
		super(e);
	}
};
function It(e) {
	return new Ft(e);
}
var Lt = class extends t {
	constructor(e, t, n) {
		super(Tt(t) ? e : e + He + t + "')", n), this.paramName = t;
	}
}, Rt = class extends Lt {
	constructor(e, t) {
		super(t, e, l());
	}
};
function zt(e, t) {
	return new Rt(e, t);
}
function Bt(e) {
	return zt(e, Ve);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Native.js
function Vt(e, t) {
	if (e == null) return Array(t);
	{
		let n = e;
		return typeof n == "function" ? new n(t) : Array(t);
	}
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Double.js
function P(e, t) {
	return Math.max(e, t);
}
function F(e, t) {
	return Math.min(e, t);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Array.js
function Ht(e, t, n) {
	let r = e.length | 0, i = t.length | 0, a = Vt(n, r + i);
	for (let t = 0; t <= r - 1; t++) $t(a, t, L(t, e));
	for (let e = 0; e <= i - 1; e++) $t(a, e + r, L(e, t));
	return a;
}
function Ut(e, t, n, r) {
	let i = t | 0;
	return e.fill(r, i, i + n);
}
function I(e, t, n) {
	let r = t.length | 0, i = Vt(n, r);
	for (let n = 0; n <= r - 1; n++) $t(i, n, e(L(n, t)));
	return i;
}
function Wt(e, t) {
	let n = P(0, e) | 0;
	return t.slice(0, 0 + n);
}
function Gt(e, t) {
	let n = Array.isArray(e) ? e : Array.from(e);
	switch (n.length | 0) {
		case 0: return Vt(t, 0);
		case 1: return L(0, n);
		default: {
			let e = 0, r = 0;
			for (let e = 0; e <= n.length - 1; e++) {
				let t = L(e, n);
				r = r + t.length | 0;
			}
			let i = Vt(t, r);
			for (let t = 0; t <= n.length - 1; t++) {
				let r = L(t, n);
				for (let t = 0; t <= r.length - 1; t++) $t(i, e, L(t, r)), e = e + 1 | 0;
			}
			return i;
		}
	}
}
function Kt(e, t, n) {
	return Gt(I(e, t, l()), n);
}
function qt(e, t) {
	return t.find(e);
}
function Jt(e, t) {
	let n = t.findIndex(e) | 0;
	if (n > -1) return n;
}
function Yt(e, t, n) {
	let r = e;
	return n.reduce(r, t);
}
function Xt(e, t) {
	for (let n = 0; n <= t.length - 1; n++) e(n, L(n, t));
}
function Zt(e, n) {
	if (e < 1) throw new t("The input must be positive. (Parameter 'size')");
	let r = [];
	if (n.length > 0) {
		let t = ~~Math.ceil(n.length / e) | 0;
		for (let i = 0; i <= t - 1; i++) {
			let t, a = i * e | 0;
			t = n.slice(a, a + e), r.push(t);
		}
	}
	return r;
}
function Qt(e) {
	if (e.length !== 0) return ve(L(0, e));
}
function L(e, n) {
	if (e < 0 || e >= n.length) throw new t("Index was outside the bounds of the array. (Parameter 'index')");
	return n[e];
}
function $t(e, n, r) {
	if (n < 0 || n >= e.length) throw new t("Index was outside the bounds of the array. (Parameter 'index')");
	e[n] = r;
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/FSharp.Core.js
function en(e) {
	return !!y(e, l());
}
function tn(e, t) {
	if (y(t, l())) throw Bt(e);
	return t;
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Seq.js
var nn = "Enumeration already finished.", rn = "Enumeration has not started. Call MoveNext.", an = "Reset is not supported on this enumerator.";
function on() {
	throw It(an);
}
function sn() {
	throw Pt(rn);
}
function cn() {
	throw Pt(nn);
}
var ln = class {
	constructor(e) {
		this.f = e;
	}
	toString() {
		let e = this, t = 0, n = "seq [", r = f(e);
		try {
			for (; t < 4 && r["System.Collections.IEnumerator.MoveNext"]();) t > 0 && (n += "; "), n += Se(r["System.Collections.Generic.IEnumerator`1.get_Current"]()), t = t + 1 | 0;
			return t === 4 && (n += "; ..."), n + "]";
		} finally {
			c(r);
		}
	}
	GetEnumerator() {
		return this.f();
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return this.f();
	}
};
function un(e) {
	return new ln(e);
}
var dn = class {
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
		on();
	}
	Dispose() {
		this.dispose();
	}
};
function fn(e, t, n) {
	return new dn(e, t, n);
}
function pn(e, t, n) {
	let r = !1, i, a = ve(e()), o = () => {
		if (a != null) {
			let e = x(a);
			try {
				n(e);
			} finally {
				a = void 0;
			}
		}
	}, s = () => {
		try {
			o();
		} finally {
			i = void 0;
		}
	};
	return fn(() => (r || sn(), i == null ? cn() : x(i)), () => {
		if (r ||= !0, a != null) {
			let e = x(a), n;
			try {
				n = t(e);
			} catch (e) {
				throw s(), e;
			}
			return n == null ? (s(), !1) : (i = n, !0);
		}
		return !1;
	}, o);
}
function mn(e, t) {
	let n, r = t;
	return fn(() => {
		if (n != null) {
			let e = x(n)[0];
			return x(n)[1], e;
		}
		return sn();
	}, () => (n = e(r), n != null && (x(n)[0], r = x(n)[1], !0)), () => {});
}
function hn(e) {
	return un(e);
}
function gn(e) {
	return f(tn("source", e));
}
function _n(e) {
	return hn(() => f(e()));
}
function vn(e, t) {
	return hn(() => mn(e, t));
}
function yn(e) {
	return e instanceof kn ? Wn(e) : Array.from(e);
}
function bn(e, t, n) {
	return hn(() => pn(e, t, n));
}
function xn(e, t, n) {
	let r = gn(t);
	try {
		let t = gn(n);
		try {
			let n = 0, i = r["System.Collections.IEnumerator.MoveNext"](), a = t["System.Collections.IEnumerator.MoveNext"]();
			for (; n === 0 && i && a;) n = e(r["System.Collections.Generic.IEnumerator`1.get_Current"](), t["System.Collections.Generic.IEnumerator`1.get_Current"]()) | 0, n === 0 && (i = r["System.Collections.IEnumerator.MoveNext"](), a = t["System.Collections.IEnumerator.MoveNext"]());
			return (n === 0 ? i ? 1 : a ? -1 : 0 : n) | 0;
		} finally {
			c(t);
		}
	} finally {
		c(r);
	}
}
function Sn(e, t) {
	let n = gn(t);
	try {
		let t;
		for (; t == null && n["System.Collections.IEnumerator.MoveNext"]();) {
			let r = n["System.Collections.Generic.IEnumerator`1.get_Current"]();
			e(r) && (t = ve(r));
		}
		return t;
	} finally {
		c(n);
	}
}
function Cn(e, t, n) {
	let r = gn(n);
	try {
		let n = t;
		for (; r["System.Collections.IEnumerator.MoveNext"]();) n = e(n, r["System.Collections.Generic.IEnumerator`1.get_Current"]());
		return n;
	} finally {
		c(r);
	}
}
function wn(e, t) {
	Cn((t, n) => {
		e(n);
	}, void 0, t);
}
function Tn(e, t) {
	return bn(() => gn(t), (t) => t["System.Collections.IEnumerator.MoveNext"]() ? ve(e(t["System.Collections.Generic.IEnumerator`1.get_Current"]())) : void 0, (e) => {
		c(e);
	});
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/System.Collections.Generic.js
var En = class extends t {
	constructor(e) {
		super(e);
	}
};
function Dn(e) {
	return new En(e);
}
function On() {
	return Dn(Ue);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/List.js
var kn = class e extends S {
	constructor(e, t) {
		super(), this.head = e, this.tail = t;
	}
	toString() {
		let e = this, t = "[", n = !0, r = f(e);
		try {
			for (; r["System.Collections.IEnumerator.MoveNext"]();) {
				let e, i, a, o = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
				t = (n ? t : t + "; ") + (e = o, i = e, typeof i == "string" ? (a = i, "\"" + a + "\"") : Se(e)), n = !1;
			}
		} finally {
			c(r);
		}
		return t + "]";
	}
	Equals(t) {
		let n = this;
		return n === t || t instanceof e && ((e, t) => {
			loop: for (;;) {
				let n = e, r = t, i = n.tail, a = r.tail;
				if (i != null) {
					if (a != null) {
						let o = x(i), s = x(a);
						if (y(n.head, r.head)) {
							e = o, t = s;
							continue loop;
						}
						return !1;
					}
					return !1;
				}
				return a == null;
			}
		})(n, t);
	}
	GetHashCode() {
		return ((e, t, n) => {
			loop: for (;;) {
				let r = e, i = t, a = n, o = a.tail;
				if (o != null) {
					let s = x(o);
					if (r > 18) return i | 0;
					e = r + 1, t = (i << 1) + v(a.head) + 631 * r, n = s;
					continue loop;
				}
				return i | 0;
			}
		})(0, 0, this) | 0;
	}
	toJSON() {
		let e = this;
		return Array.from(e);
	}
	CompareTo(t) {
		let n = this;
		return t instanceof e ? ((e, t) => {
			loop: for (;;) {
				let n = e, r = t, i = n.tail, a = r.tail;
				if (i != null) {
					if (a != null) {
						let o = x(i), s = x(a), c = b(n.head, r.head) | 0;
						if (c === 0) {
							e = o, t = s;
							continue loop;
						}
						return c | 0;
					}
					return 1;
				}
				return a == null ? 0 : -1;
			}
		})(n, t) | 0 : 1;
	}
	GetEnumerator() {
		return jn(this);
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return f(this);
	}
}, An = class {
	constructor(e) {
		this.xs = e, this.it = this.xs, this.current = l();
	}
	"System.Collections.Generic.IEnumerator`1.get_Current"() {
		return this.current;
	}
	"System.Collections.IEnumerator.get_Current"() {
		return this.current;
	}
	"System.Collections.IEnumerator.MoveNext"() {
		let e = this, t = e.it.tail;
		if (t != null) {
			let n = x(t);
			return e.current = e.it.head, e.it = n, !0;
		}
		return !1;
	}
	"System.Collections.IEnumerator.Reset"() {
		let e = this;
		e.it = e.xs, e.current = l();
	}
	Dispose() {}
};
function jn(e) {
	return new An(e);
}
function Mn() {
	return new kn(l(), void 0);
}
function Nn(e, t) {
	return new kn(e, t);
}
function Pn(e) {
	return e.tail == null;
}
function Fn(e) {
	return ((e, t) => {
		loop: for (;;) {
			let n = e, r = t.tail;
			if (r != null) {
				e = n + 1, t = x(r);
				continue loop;
			}
			return n | 0;
		}
	})(0, e) | 0;
}
function In(e) {
	if (e.tail != null) return e.head;
	throw new t(Be + " (Parameter 'list')");
}
function Ln(e) {
	let n = e.tail;
	if (n != null) return x(n);
	throw new t(Be + " (Parameter 'list')");
}
function Rn() {
	return Mn();
}
function zn(e, t) {
	return Nn(e, t);
}
function Bn(e) {
	return Nn(e, Mn());
}
function Vn(e) {
	return Pn(e);
}
function Hn(e) {
	return Fn(e) | 0;
}
function R(e) {
	return In(e);
}
function Un(e) {
	return Ln(e);
}
function Wn(e) {
	let t = Fn(e) | 0, n = Ut(Array(t), 0, t, null);
	return ((e, t) => {
		loop: for (;;) {
			let r = e, i = t;
			if (!Pn(i)) {
				$t(n, r, In(i)), e = r + 1, t = Ln(i);
				continue loop;
			}
			break;
		}
	})(0, e), n;
}
function Gn(e, t, n) {
	let r = t, i = n;
	for (; !Pn(i);) r = e(r, R(i)), i = Ln(i);
	return r;
}
function Kn(e) {
	return Gn((e, t) => Nn(t, e), Mn(), e);
}
function qn(e, t) {
	let n = t;
	for (let t = e.length - 1; t >= 0; t--) n = Nn(L(t, e), n);
	return n;
}
function Jn(e, t) {
	return Gn((e, t) => Nn(t, e), t, Kn(e));
}
function Yn(e, t) {
	let n = Mn(), r = n;
	for (let n = 0; n <= e - 1; n++) {
		let e, i;
		r = (e = r, i = new kn(t(n), void 0), e.tail = i, i);
	}
	let i = r;
	return i.tail = Mn(), Ln(n);
}
function Xn(e, t) {
	return Yn(e, (e) => t);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Range.js
function Zn(e, n, r, i) {
	let a = b(e, r) | 0;
	if (a === 0) throw new t("The step of a range cannot be zero");
	let o = a > 0;
	return (t) => {
		let r = b(t, n) | 0;
		return o && r <= 0 || !o && r >= 0 ? [t, i(t, e)] : void 0;
	};
}
function Qn(e, t, n, r, i) {
	let a = Zn(t, n, r, i);
	return _n(() => vn(a, e));
}
function $n(e, t, n) {
	return Qn(e, t, n, 0, (e, t) => e + t);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Int32.js
var er = { AllowHexSpecifier: 512 };
function tr(e, t) {
	let [, n, r, i] = e;
	return {
		sign: n || "",
		prefix: r || "",
		digits: i,
		radix: t
	};
}
function nr(e, n) {
	switch (n) {
		case 8: return e ? [0, 255] : [-128, 127];
		case 16: return e ? [0, 65535] : [-32768, 32767];
		case 32: return e ? [0, 4294967295] : [-2147483648, 2147483647];
		default: throw new t("Invalid bit size.");
	}
}
function rr(e) {
	switch (e) {
		case 2: return /[^0-1]/;
		case 8: return /[^0-7]/;
		case 10: return /[^0-9]/;
		case 16: return /[^0-9a-fA-F]/;
		default: throw new t("Invalid Base.");
	}
}
function ir(e, t) {
	if (t & er.AllowHexSpecifier) return 16;
	switch (e) {
		case "0b":
		case "0B": return 2;
		case "0o":
		case "0O": return 8;
		case "0x":
		case "0X": return 16;
		default: return 10;
	}
}
function ar(e, t, n) {
	let r = /^\s*([\+\-])?(0[xXoObB])?([0-9a-fA-F]+)\s*$/.exec(e.replace(/_/g, ""));
	if (r != null) {
		let [, , e, i] = r;
		if (n ||= ir(e, t), !rr(n).test(i)) return tr(r, n);
	}
	return null;
}
function or(e, n, r, i, a) {
	let o = ar(e, n, a);
	if (o != null) {
		let e = Number.parseInt(o.sign + o.digits, o.radix);
		if (!Number.isNaN(e)) {
			let [t, n] = nr(!0, i);
			!r && o.radix !== 10 && e >= t && e <= n && (e = e << 32 - i >> 32 - i);
			let [a, s] = nr(r, i);
			if (e >= a && e <= s) return e;
		}
	}
	throw new t(`The input string ${e} was not in a correct format.`);
}
function sr(e, t, n, r, i) {
	try {
		return i.contents = or(e, t, n, r), !0;
	} catch {
		return !1;
	}
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Map.js
var cr = class {
	constructor(e, t) {
		this.k = e, this.v = t;
	}
};
function lr(e, t) {
	return new cr(e, t);
}
function z(e) {
	return e.k;
}
function B(e) {
	return e.v;
}
var V = class extends cr {
	constructor(e, t, n, r, i) {
		super(e, t), this.left = n, this.right = r, this.h = i | 0;
	}
};
function ur(e, t, n, r, i) {
	return new V(e, t, n, r, i);
}
function H(e) {
	return e.left;
}
function U(e) {
	return e.right;
}
function W(e) {
	return e.h | 0;
}
function dr(e, t) {
	MapTreeModule_sizeAux: for (;;) {
		let n = e, r = t;
		if (r != null) {
			let i = x(r);
			if (i instanceof V) {
				let r = i;
				e = dr(n + 1, H(r)), t = U(r);
				continue MapTreeModule_sizeAux;
			}
			return n + 1 | 0;
		}
		return n | 0;
	}
}
function fr(e) {
	return dr(0, e) | 0;
}
function G(e, t, n, r) {
	let i, a, o, s = e;
	if (s != null) {
		let e = x(s);
		o = e instanceof V ? (i = e, W(i)) : 1;
	} else o = 0;
	let c, l = r;
	if (l != null) {
		let e = x(l);
		c = e instanceof V ? (a = e, W(a)) : 1;
	} else c = 0;
	let u = (o < c ? c : o) | 0;
	return u === 0 ? lr(t, n) : ur(t, n, e, r, u + 1);
}
function pr(e, n, r, i) {
	let a, o, s, c, l, u, d, f, p, m = e;
	if (m != null) {
		let e = x(m);
		p = e instanceof V ? (a = e, W(a)) : 1;
	} else p = 0;
	let h, g = i;
	if (g != null) {
		let e = x(g);
		h = e instanceof V ? (o = e, W(o)) : 1;
	} else h = 0;
	if (h > p + 2) {
		let a = x(i);
		if (a instanceof V) {
			let i = a;
			if (s = H(i), (s == null ? 0 : (c = x(s), c instanceof V ? (l = c, W(l)) : 1)) > p + 1) {
				let a = x(H(i));
				if (a instanceof V) {
					let t = a;
					return G(G(e, n, r, H(t)), z(t), B(t), G(U(t), z(i), B(i), U(i)));
				}
				throw new t("internal error: Map.rebalance");
			}
			return G(G(e, n, r, H(i)), z(i), B(i), U(i));
		}
		throw new t("internal error: Map.rebalance");
	}
	if (p > h + 2) {
		let a = x(e);
		if (a instanceof V) {
			let e = a;
			if (u = U(e), (u == null ? 0 : (d = x(u), d instanceof V ? (f = d, W(f)) : 1)) > h + 1) {
				let a = x(U(e));
				if (a instanceof V) {
					let t = a;
					return G(G(H(e), z(e), B(e), H(t)), z(t), B(t), G(U(t), n, r, i));
				}
				throw new t("internal error: Map.rebalance");
			}
			return G(H(e), z(e), B(e), G(U(e), n, r, i));
		}
		throw new t("internal error: Map.rebalance");
	}
	return G(e, n, r, i);
}
function mr(e, t, n, r) {
	if (r != null) {
		let i = x(r), a = e.Compare(t, z(i)) | 0;
		if (i instanceof V) {
			let r = i;
			return a < 0 ? pr(mr(e, t, n, H(r)), z(r), B(r), U(r)) : a === 0 ? ur(t, n, H(r), U(r), W(r)) : pr(H(r), z(r), B(r), mr(e, t, n, U(r)));
		}
		return a < 0 ? ur(t, n, void 0, r, 2) : a === 0 ? lr(t, n) : ur(t, n, r, void 0, 2);
	}
	return lr(t, n);
}
function hr(e, t, n) {
	MapTreeModule_tryFind: for (;;) {
		let r = e, i = t, a = n;
		if (a != null) {
			let o = x(a), s = r.Compare(i, z(o)) | 0;
			if (s === 0) return ve(B(o));
			if (o instanceof V) {
				let a = o;
				e = r, t = i, n = s < 0 ? H(a) : U(a);
				continue MapTreeModule_tryFind;
			}
			return;
		}
		return;
	}
}
function gr(e, t, n) {
	let r = hr(e, t, n);
	if (r == null) throw On();
	return x(r);
}
function _r(e, t, n) {
	MapTreeModule_mem: for (;;) {
		let r = e, i = t, a = n;
		if (a != null) {
			let o = x(a), s = r.Compare(i, z(o)) | 0;
			if (o instanceof V) {
				let a = o;
				if (s < 0) {
					e = r, t = i, n = H(a);
					continue MapTreeModule_mem;
				}
				if (s === 0) return !0;
				e = r, t = i, n = U(a);
				continue MapTreeModule_mem;
			}
			return s === 0;
		}
		return !1;
	}
}
function vr(e, t) {
	MapTreeModule_iterOpt: for (;;) {
		let n = e, r = t;
		if (r != null) {
			let i = x(r);
			if (i instanceof V) {
				let r = i;
				vr(n, H(r)), n(z(r), B(r)), e = n, t = U(r);
				continue MapTreeModule_iterOpt;
			}
			n(z(i), B(i));
		}
		break;
	}
}
function yr(e, t) {
	vr(e, t);
}
function br(e, t, n) {
	MapTreeModule_foldOpt: for (;;) {
		let r = e, i = t, a = n;
		if (a != null) {
			let o = x(a);
			if (o instanceof V) {
				let a = o;
				e = r, t = r(br(r, i, H(a)), z(a), B(a)), n = U(a);
				continue MapTreeModule_foldOpt;
			}
			return r(i, z(o), B(o));
		}
		return i;
	}
}
function xr(e, t, n) {
	return br(e, t, n);
}
function Sr(e, t, n) {
	let r = n;
	yr((e, n) => {
		$t(t, r, [e, n]), r = r + 1 | 0;
	}, e);
}
function Cr(e, t) {
	return Gn((t, n) => mr(e, n[0], n[1], t), void 0, t);
}
function wr(e, t, n) {
	MapTreeModule_mkFromEnumerator: for (;;) {
		let r = e, i = t, a = n;
		if (a["System.Collections.IEnumerator.MoveNext"]()) {
			let o = a["System.Collections.Generic.IEnumerator`1.get_Current"]();
			e = r, t = mr(r, o[0], o[1], i), n = a;
			continue MapTreeModule_mkFromEnumerator;
		}
		return i;
	}
}
function Tr(e, t) {
	let n;
	for (let r = 0; r <= t.length - 1; r++) {
		let i = L(r, t);
		n = mr(e, i[0], i[1], n);
	}
	return n;
}
function Er(e, t) {
	if (n(t)) return Tr(e, t);
	if (t instanceof kn) return Cr(e, t);
	{
		let n = f(t);
		try {
			return wr(e, void 0, n);
		} finally {
			c(n);
		}
	}
}
var Dr = class extends S {
	constructor(e, t) {
		super(), this.stack = e, this.started = t;
	}
};
function Or(e) {
	MapTreeModule_collapseLHS: for (;;) {
		let t = e;
		if (Vn(t)) return Rn();
		{
			let n = Un(t), r = R(t);
			if (r != null) {
				let i = x(r);
				if (i instanceof V) {
					let t = i;
					e = qn([
						H(t),
						lr(z(t), B(t)),
						U(t)
					], n);
					continue MapTreeModule_collapseLHS;
				}
				return t;
			}
			e = n;
			continue MapTreeModule_collapseLHS;
		}
	}
}
function kr(e) {
	return new Dr(Or(Bn(e)), !1);
}
function Ar() {
	throw new t("enumeration not started");
}
function jr() {
	throw new t("enumeration already finished");
}
function Mr(e) {
	if (e.started) {
		let n = e.stack;
		if (Vn(n)) return jr();
		if (R(n) != null) {
			let e = x(R(n));
			if (e instanceof V) throw new t("Please report error: Map iterator, unexpected stack for current");
			return [z(e), B(e)];
		}
		throw new t("Please report error: Map iterator, unexpected stack for current");
	}
	return Ar();
}
function Nr(e) {
	if (e.started) {
		let n = e.stack;
		if (Vn(n)) return !1;
		if (R(n) != null) {
			if (x(R(n)) instanceof V) throw new t("Please report error: Map iterator, unexpected stack for moveNext");
			return e.stack = Or(Un(n)), !Vn(e.stack);
		}
		throw new t("Please report error: Map iterator, unexpected stack for moveNext");
	}
	return e.started = !0, !Vn(e.stack);
}
function Pr(e) {
	let t = kr(e);
	return {
		"System.Collections.Generic.IEnumerator`1.get_Current"() {
			return Mr(t);
		},
		"System.Collections.IEnumerator.get_Current"() {
			return Mr(t);
		},
		"System.Collections.IEnumerator.MoveNext"() {
			return Nr(t);
		},
		"System.Collections.IEnumerator.Reset"() {
			t = kr(e);
		},
		Dispose() {}
	};
}
var Fr = class e {
	constructor(e, t) {
		this.comparer = e, this.tree = t;
	}
	GetHashCode() {
		return Wr(this) | 0;
	}
	Equals(t) {
		let n = this;
		if (t instanceof e) {
			let e = t, r = f(n);
			try {
				let t = f(e);
				try {
					let e = () => {
						let n = r["System.Collections.IEnumerator.MoveNext"]();
						if (n === t["System.Collections.IEnumerator.MoveNext"]()) {
							if (n) {
								let n = r["System.Collections.Generic.IEnumerator`1.get_Current"](), i = t["System.Collections.Generic.IEnumerator`1.get_Current"]();
								return y(n[0], i[0]) && y(n[1], i[1]) ? e() : !1;
							}
							return !0;
						}
						return !1;
					};
					return e();
				} finally {
					c(t);
				}
			} finally {
				c(r);
			}
		} else return !1;
	}
	toString() {
		return "map [" + Et("; ", Tn((e) => wt("({0}, {1})", e[0], e[1]), this)) + "]";
	}
	get [Symbol.toStringTag]() {
		return "FSharpMap";
	}
	toJSON() {
		let e = this;
		return Array.from(e);
	}
	GetEnumerator() {
		return Pr(this.tree);
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return Pr(this.tree);
	}
	CompareTo(t) {
		let n, r = this;
		return (t instanceof e ? (n = t, xn((e, t) => {
			let n = r.comparer.Compare(e[0], t[0]) | 0;
			return (n === 0 ? b(e[1], t[1]) : n) | 0;
		}, r, n)) : 1) | 0;
	}
	"System.Collections.Generic.ICollection`1.Add2B595"(e) {
		throw It("Map cannot be mutated");
	}
	"System.Collections.Generic.ICollection`1.Clear"() {
		throw It("Map cannot be mutated");
	}
	"System.Collections.Generic.ICollection`1.Remove2B595"(e) {
		throw It("Map cannot be mutated");
	}
	"System.Collections.Generic.ICollection`1.Contains2B595"(e) {
		let t = this;
		return Hr(t, e[0]) && y(Br(t, e[0]), e[1]);
	}
	"System.Collections.Generic.ICollection`1.CopyToZ3B4C077E"(e, t) {
		Sr(this.tree, e, t);
	}
	"System.Collections.Generic.ICollection`1.get_IsReadOnly"() {
		return !0;
	}
	"System.Collections.Generic.ICollection`1.get_Count"() {
		return Vr(this) | 0;
	}
	"System.Collections.Generic.IReadOnlyCollection`1.get_Count"() {
		return Vr(this) | 0;
	}
	get size() {
		return Vr(this) | 0;
	}
	clear() {
		throw new t("Map cannot be mutated");
	}
	delete(e) {
		throw new t("Map cannot be mutated");
	}
	entries() {
		return Tn((e) => [e[0], e[1]], this);
	}
	get(e) {
		return Br(this, e);
	}
	has(e) {
		return Hr(this, e);
	}
	keys() {
		return Tn((e) => e[0], this);
	}
	set(e, n) {
		throw new t("Map cannot be mutated");
	}
	values() {
		return Tn((e) => e[1], this);
	}
	forEach(e, t) {
		let n = this;
		wn((t) => {
			e(t[1], t[0], n);
		}, n);
	}
};
function Ir(e, t) {
	return new Fr(e, t);
}
function Lr(e) {
	return Ir(e, void 0);
}
function Rr(e) {
	return e.tree;
}
function zr(e, t, n) {
	return Ir(e.comparer, mr(e.comparer, t, n, e.tree));
}
function Br(e, t) {
	return gr(e.comparer, t, e.tree);
}
function Vr(e) {
	return fr(e.tree) | 0;
}
function Hr(e, t) {
	return _r(e.comparer, t, e.tree);
}
function Ur(e, t) {
	return hr(e.comparer, t, e.tree);
}
function Wr(e) {
	let t = (e, t) => (e << 1) + t + 631 | 0, n = 0, r = f(e);
	try {
		for (; r["System.Collections.IEnumerator.MoveNext"]();) {
			let e = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
			n = t(n, v(e[0])) | 0, n = t(n, v(e[1])) | 0;
		}
	} finally {
		c(r);
	}
	return n | 0;
}
function Gr(e, t, n) {
	return zr(n, e, t);
}
function Kr(e, t) {
	return Br(t, e);
}
function qr(e, t, n) {
	return xr(e, t, Rr(n));
}
function Jr(e, t) {
	return Ir(t, Er(t, e));
}
function Yr(e, t) {
	return Ir(t, Er(t, e));
}
function Xr(e) {
	return Lr(e);
}
// ../../../toolchain-logo/art/src/Pixels.fs
var Zr = class extends S {
	constructor(e, t, n) {
		super(), this.r = e, this.g = t, this.b = n;
	}
}, Qr = "0123456789ABCDEF";
function $r(e) {
	return Qr.indexOf(e.toUpperCase());
}
function ei(e, t) {
	return $r(e[t]) * 16 + $r(e[t + 1]);
}
function K(e) {
	return new Zr(ei(e, 1), ei(e, 3), ei(e, 5));
}
function ti(e) {
	let t = (e) => {
		let t = ~~P(0, F(255, pe(e))) | 0;
		return Qr[~~(t / 16)] + Qr[t % 16];
	};
	return "#" + t(e.r) + t(e.g) + t(e.b);
}
function q(e, t, n) {
	return new Zr(e.r + (t.r - e.r) * n, e.g + (t.g - e.g) * n, e.b + (t.b - e.b) * n);
}
var ni = K("#FFFFFF");
K("#000000");
function ri(e, t) {
	return q(e, ni, t);
}
function J(e) {
	return .2126 * e.r + .7152 * e.g + .0722 * e.b;
}
function ii(e) {
	let t = J(e);
	return new Zr(t, t, t);
}
function ai(e) {
	return (P(e.r, P(e.g, e.b)) - F(e.r, F(e.g, e.b))) / 255;
}
function oi(e) {
	let t = P(e.r, P(e.g, e.b)), n = F(e.r, F(e.g, e.b)), r = t;
	if (r === n) return 0;
	{
		let t = r - n;
		return ((r === e.r ? (e.g - e.b) / t : r === e.g ? 2 + (e.b - e.r) / t : 4 + (e.r - e.g) / t) * 60 + 360) % 360;
	}
}
var si = I((e) => (e + .5) / 16, [
	0,
	8,
	2,
	10,
	12,
	4,
	14,
	6,
	3,
	11,
	1,
	9,
	15,
	7,
	13,
	5
]);
function ci(e, t) {
	return L((t & 3) * 4 + (e & 3), si);
}
function li(e, t, n) {
	return ~~Math.floor(n) + +(n - Math.floor(n) > ci(e, t)) | 0;
}
var ui = class extends S {
	constructor(e, t) {
		super(), this.rows = e, this.palette = t;
	}
}, di = class extends S {
	constructor(e) {
		super(), this.glyphs = e;
	}
};
function fi(e, t) {
	return qr((e, t, n) => Gr(t, n, e), e, t);
}
function pi(e, t) {
	let n, r = I((e) => e[0], Wt(e, t));
	return n = Yt(fi, Xr({ Compare: (e, t) => le(e, t) | 0 }), r), new di(Yr(Kt((e) => {
		let t = fi(n, e[0]);
		return I((e) => [e[0], new ui(e[1], t)], e[1]);
	}, t), { Compare: (e, t) => le(e, t) | 0 }));
}
function mi(e, t) {
	return Kr(t, e.glyphs);
}
var hi = class extends S {
	constructor(e, t, n) {
		super(), this.w = e | 0, this.h = t | 0, this.px = n;
	}
};
function gi(e, t) {
	return new hi(e, t, Ut(Array(e * t * 4), 0, e * t * 4, 0));
}
function _i(e, t, n) {
	return t >= 0 && n >= 0 && t < e.w && n < e.h;
}
function vi(e, t, n) {
	return _i(e, t, n) ? L(4 * (n * e.w + t) + 3, e.px) > 0 : !1;
}
function Y(e, t, n, r, i) {
	if (_i(e, t, n)) {
		let a = 4 * (n * e.w + t) | 0, o = L(a + 3, e.px) / 255, s = i + o * (1 - i);
		s > 0 && (e.px[a] = (r.r * i + L(a, e.px) * o * (1 - i)) / s, e.px[a + 1] = (r.g * i + L(a + 1, e.px) * o * (1 - i)) / s, e.px[a + 2] = (r.b * i + L(a + 2, e.px) * o * (1 - i)) / s), e.px[a + 3] = s * 255;
	}
}
function yi(e, t, n, r) {
	if (_i(e, t, n)) {
		let i = 4 * (n * e.w + t) | 0;
		e.px[i] = r.r, e.px[i + 1] = r.g, e.px[i + 2] = r.b, e.px[i + 3] = 255;
	}
}
function bi(e, t, n) {
	let r = 4 * (n * e.w + t) | 0;
	if (vi(e, t, n)) return new Zr(L(r, e.px), L(r + 1, e.px), L(r + 2, e.px));
}
function xi(e, t, n, r, i) {
	for (let a = 0; a <= t.rows.length - 1; a++) for (let o = 0; o <= L(a, t.rows).length - 1; o++) {
		let s = Ur(t.palette, L(a, t.rows)[o]);
		if (s != null) {
			let t = i(n + o, r + a, s);
			if (t != null) {
				let i = t;
				Y(e, n + o, r + a, i[0], i[1]);
			}
		}
	}
}
function Si(e, t, n, r) {
	xi(e, t, n, r, (e, t, n) => [n, 1]);
}
function Ci(e, t, n) {
	for (let r = 0; r <= e.w - 1; r++) Y(e, r, 0, t, n), Y(e, r, e.h - 1, t, n);
	for (let r = 1; r <= e.h - 2; r++) Y(e, 0, r, t, n), Y(e, e.w - 1, r, t, n);
}
// ../../../toolchain-logo/art/src/Font.fs
var wi = class extends S {
	constructor(e, t, n) {
		super(), this.height = e | 0, this.spacing = t | 0, this.glyphs = n;
	}
};
function Ti(e, t, n) {
	return new wi(e, t, Jr(Yt((e, t) => {
		let n, r, i, a;
		switch (t.startsWith("= ") ? n = 0 : Vn(e) || t === "" ? n = 2 : (n = 1, r = R(e)[0], i = Un(e), a = R(e)[1]), n) {
			case 0: {
				let n = Mt(t, 2);
				return zn([n === "space" ? " " : n[0], []], e);
			}
			case 1: return zn([r, Ht(a, [t])], i);
			default: return e;
		}
	}, Rn(), I((e) => jt(e, "\r"), At(n, ["\n"], void 0, 0))), { Compare: (e, t) => le(e, t) | 0 }));
}
var Ei = Yr(Kt((e) => yn(_n(() => Tn((t) => [t, e[1]], e[0].split("")))), [
	["—–‒−─━═", "-"],
	["→⇒", ">"],
	["←⇐", "<"],
	["‘’‚′", "'"],
	["“”„″", "\""],
	["…", "."],
	["•", "·"],
	["│┃║", "|"],
	["┌┐└┘├┤┬┴┼╭╮╯╰", "+"],
	["àáâãäåā", "a"],
	["ÀÁÂÃÄÅĀ", "A"],
	["çćč", "c"],
	["ÇĆČ", "C"],
	["èéêëēę", "e"],
	["ÈÉÊËĒĘ", "E"],
	["ìíîïī", "i"],
	["ÌÍÎÏĪ", "I"],
	["ñń", "n"],
	["ÑŃ", "N"],
	["òóôõöøō", "o"],
	["ÒÓÔÕÖØŌ", "O"],
	["ùúûüū", "u"],
	["ÙÚÛÜŪ", "U"],
	["ýÿ", "y"],
	["Ý", "Y"],
	["šśß", "s"],
	["ŠŚ", "S"],
	["žźż", "z"],
	["ŽŹŻ", "Z"],
	["ł", "l"],
	["Ł", "L"]
]), { Compare: (e, t) => le(e, t) | 0 });
function Di(e, t) {
	let n = Ur(e.glyphs, t);
	if (n == null) {
		let n, r = Ur(Ei, t);
		return n = r == null ? void 0 : Ur(e.glyphs, r), n ?? Br(e.glyphs, "?");
	}
	return n;
}
function Oi(e, t) {
	return L(0, Di(e, t)).length | 0;
}
function ki(e, t, n, r, i, a) {
	let o = Di(t, a);
	for (let t = 0; t <= o.length - 1; t++) for (let a = 0; a <= L(t, o).length - 1; a++) L(t, o)[a] === "#" && yi(e, n + a, r + t, i);
}
function Ai(e, t, n, r, i, a) {
	Cn((n, a) => (ki(e, t, n, r, i, a), n + Oi(t, a) + t.spacing | 0), n, a.split(""));
}
function ji(e, t, n, r, i, a, o) {
	ki(e, t, r + ~~((n - Oi(t, o)) / 2), i, a, o);
}
// ../../../toolchain-logo/art/src/Console.fs
var X = class e extends we {
	constructor(e, t) {
		super(), this.tag = e, this.fields = t;
	}
	cases() {
		return [
			"Text",
			"Bright",
			"Dim",
			"Accent",
			"Cyan",
			"Good",
			"Bad"
		];
	}
	static Text$ = new e(0, []);
	static Bright = new e(1, []);
	static Dim = new e(2, []);
	static Accent = new e(3, []);
	static Cyan = new e(4, []);
	static Good = new e(5, []);
	static Bad = new e(6, []);
}, Mi = class extends S {
	constructor(e, t, n) {
		super(), this.ch = e, this.pen = t, this.inverse = n;
	}
};
function Ni(e) {
	return ((t, n, r) => {
		go: for (;;) {
			let i = t, a = n, o = r;
			if (i >= e.length) return Wn(Kn(o));
			if (e[i] === "\x1B" && i + 1 < e.length && e[i + 1] === "[") {
				let s = Sn((t) => e[t] >= "@" && e[t] <= "~", $n(i + 2, 1, e.length - 1));
				if (s != null) {
					let c = s | 0, l = I((e) => {
						let t, n = 0;
						return t = [sr(e, 511, !1, 32, new C(() => n | 0, (e) => {
							n = e | 0;
						})), n], t[0] ? t[1] | 0 : 0;
					}, At(Mt(e, i + 2, c - i - 2), [";"], void 0, 0));
					t = c + 1, n = e[c] === "m" ? Yt((e, t) => {
						let n = e[0], r = e[1], i = e[2];
						switch (t) {
							case 0: return [
								X.Text$,
								!1,
								!1
							];
							case 1: return [
								n,
								!0,
								i
							];
							case 2:
							case 90: return [
								X.Dim,
								r,
								i
							];
							case 7: return [
								n,
								r,
								!0
							];
							case 22: return [
								n,
								!1,
								i
							];
							case 27: return [
								n,
								r,
								!1
							];
							case 31:
							case 91: return [
								X.Bad,
								r,
								i
							];
							case 32:
							case 92: return [
								X.Good,
								r,
								i
							];
							case 33:
							case 93: return [
								X.Accent,
								r,
								i
							];
							case 34:
							case 36:
							case 94:
							case 96: return [
								X.Cyan,
								r,
								i
							];
							case 37:
							case 39:
							case 97: return [
								X.Text$,
								r,
								i
							];
							default: return [
								n,
								r,
								i
							];
						}
					}, a, l) : a, r = o;
					continue go;
				}
				return Wn(Kn(o));
			}
			{
				let s = a[0], c = (e) => new Mi(e, a[1] && y(s, X.Text$) ? X.Bright : s, a[2]), l = e[i];
				switch (l) {
					case "	":
						t = i + 1, n = a, r = Jn(Xn(8 - Hn(o) % 8, c(" ")), o);
						continue go;
					case "\r":
						t = i + 1, n = a, r = o;
						continue go;
					default:
						t = i + 1, n = a, r = zn(c(l), o);
						continue go;
				}
			}
			break;
		}
	})(0, [
		X.Text$,
		!1,
		!1
	], Rn());
}
var Pi = class extends we {
	constructor(e, t) {
		super(), this.tag = e, this.fields = t;
	}
	cases() {
		return [
			"Command",
			"Output",
			"Pause"
		];
	}
}, Fi = class extends S {
	constructor(e, t, n) {
		super(), this.lines = e, this.todo = t, this.at = n;
	}
}, Ii = Ni("1.SYS:> "), Li = .05, Ri = .35;
function zi(e) {
	return new Fi([], e, 0);
}
function Bi(e) {
	return e.todo.length > 0;
}
function Vi(e, t) {
	if (t.todo.length === 0) return t;
	{
		let n = t.at + e, r = (e, r) => {
			let i;
			return n < e ? new Fi(t.lines, t.todo, n) : Vi(n - e, (i = Ht(t.lines, r), new Fi(i.length > 200 ? i.slice(i.length - 200, i.length) : i, t.todo.slice(1, t.todo.length), 0)));
		}, i = L(0, t.todo);
		switch (i.tag) {
			case 1: return r(Li, [Ni(i.fields[0])]);
			case 2: return r(i.fields[0], []);
			default: {
				let e = i.fields[0];
				return r(e.length / 26 + Ri, [Ht(Ii, Ni(e))]);
			}
		}
	}
}
function Hi(e) {
	return Vi(1e9, e);
}
function Ui(e, t, n) {
	let r, i, a, o, s, c = Qt(n.todo);
	s = c == null ? [Ii, !0] : c.tag === 0 ? (r = c.fields[0], [Ht(Ii, Ni(Mt(r, 0, F(r.length, ~~(n.at * 26))))), !0]) : [void 0, !1];
	let l = s[0], u = (t) => t.length === 0 ? [[]] : Zt(e, t), d = Ht(Kt(u, n.lines), ye((i = l, i == null ? void 0 : u(i)), [])), f = d.length > t ? d.slice(d.length - t, d.length) : d;
	return [f, l == null ? void 0 : s[1] ? (a = l, o = a.length % e | 0, a.length > 0 && o === 0 ? [0, F(t - 1, f.length)] : [o, f.length - 1]) : void 0];
}
var Wi = K("#08162E"), Gi = K("#DFE7F1"), Ki = K("#475569"), qi = K("#05080D");
function Ji(e) {
	switch (e.tag) {
		case 1: return K("#F4F8FC");
		case 2: return K("#6B7A8F");
		case 3: return K("#FF9A3C");
		case 4: return K("#6FD3F0");
		case 5: return K("#7BD88F");
		case 6: return K("#FF5A5A");
		default: return K("#C9D4E2");
	}
}
function Yi(e, t) {
	return [5 + e * 6 + 2 + 3, 14 + t * 10 + 2 + 4];
}
function Z(e, t, n, r, i, a) {
	for (let o = n; o <= n + i - 1; o++) for (let n = t; n <= t + r - 1; n++) yi(e, n, o, a);
}
function Xi(e, t, n, r, i, a, o) {
	let s = Yi(r, i), c = s[0] | 0, l = s[1] | 0, u = gi(c, l);
	Z(u, 0, 0, c, l, Wi), Z(u, 0, 0, c, 11, Gi), Z(u, 0, 11, c, 1, qi), Z(u, 0, 0, 2, l, Gi), Z(u, 2, 11, 1, l - 11, qi), Z(u, c - 2, 0, 2, l, Ki), Z(u, c - 3, 11, 1, l - 11, qi), Z(u, 0, l - 3, c, 3, Ki), Z(u, 3, l - 4, c - 6, 1, qi), Ai(u, t, 15, 1, qi, n), Si(u, mi(e, "gadget-close"), 3, 1), Si(u, mi(e, "gadget-depth"), c - 14, 1), Si(u, mi(e, "gadget-size"), c - 12, l - 12);
	let d = Ui(r, i, a), f = d[1];
	Xt((e, n) => {
		Xt((n, r) => {
			let i = 14 + e * 10 | 0, a = 5 + n * 6 | 0;
			r.inverse ? (Z(u, a, i - 1, 6, 10, Ji(r.pen)), ji(u, t, 6, a, i, Wi, r.ch)) : ji(u, t, 6, a, i, Ji(r.pen), r.ch);
		}, n);
	}, d[0]);
	let p, m, h;
	return f == null ? p = 1 : (f[1], f[0], o ? (p = 0, m = f[0], h = f[1]) : p = 1), p === 0 && Z(u, 5 + m * 6, 14 + h * 10 - 1, 6, 10, Ji(X.Accent)), u;
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/MapUtil.js
function Zi(e, t, n) {
	return e.has(t) ? (n.contents = e.get(t), !0) : !1;
}
function Qi(e, n) {
	if (e.has(n)) return e.get(n);
	throw new t(`The given key '${n}' was not present in the dictionary.`);
}
// ../../../toolchain-logo/art/src/Aesthetic.fs
var $i = class e extends we {
	constructor(e, t) {
		super(), this.tag = e, this.fields = t;
	}
	cases() {
		return [
			"Enabled",
			"Disabled",
			"Selected"
		];
	}
	static Enabled = new e(0, []);
	static Disabled = new e(1, []);
}, ea = class extends S {
	constructor(e, t, n, r, i, a, o, s, c, l, u, d, f) {
		super(), this.name = e, this.ground = t, this.panel = n, this.line = r, this.edgeLit = i, this.edgeShade = a, this.fg = o, this.dim = s, this.accent = c, this.font = l, this.radius = u | 0, this.ink = d, this.select = f;
	}
}, ta = K("#3A4A62"), na = K("#A6B4C6"), ra = K("#30B9DB"), ia = new ea("house", K("#060A11"), K("#0D1726"), K("#1A2A40"), K("#24395A"), K("#1A2A40"), K("#DFE7F1"), K("#7F90A7"), ra, "-apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif", 10, (e, t, n, r, i) => {
	let a;
	switch (e.tag) {
		case 0:
		case 2:
			a = 3;
			break;
		default: a = J(i) < 24 ? 0 : (n + r) % 2 == 1 ? 1 : 2;
	}
	switch (a) {
		case 0: return [i, 1];
		case 1: return;
		case 2: return [q(ta, na, J(i) / 255), 1];
		default: return [ri(i, .45 * t), 1];
	}
}, (e, t) => {
	Ci(e, ra, 1);
	for (let t = 0; t <= e.w - 1; t++) ci(t, 1) < .5 && Y(e, t, 1, ra, .35), ci(t, e.h - 2) < .5 && Y(e, t, e.h - 2, ra, .35);
	let n = (t * .6 % 1.6 - .3) * e.w;
	for (let t = 2; t <= e.h - 3; t++) for (let r = 0; r <= e.w - 1; r++) {
		let i = r - n + t * .7;
		i > -1.5 && i < 1.5 && vi(e, r, t) && Y(e, r, t, ni, .45);
	}
}), aa = K("#060A11");
function oa(e) {
	return q(q(e, ii(e), .7), aa, .2);
}
var sa = new ea("muted", ia.ground, K("#0A111C"), K("#152234"), K("#1B2B42"), K("#152234"), K("#C9D4E2"), K("#66768C"), K("#5F8FA8"), ia.font, 6, (e, t, n, r, i) => {
	if (J(i) < 16) return [i, 1];
	switch (e.tag) {
		case 2: return [q(oa(i), i, .6 + .4 * t), 1];
		case 0: return [q(oa(i), i, .7 * t), 1];
		default: return (n + r) % 2 == 1 ? void 0 : [q(oa(i), aa, .35), 1];
	}
}, (e, t) => {
	Ci(e, K("#5F8FA8"), .8);
}), Q = [
	K("#0055AA"),
	K("#FFFFFF"),
	K("#000022"),
	K("#FF8800")
];
function ca(e) {
	if (ai(e) > .3) {
		let t = oi(e);
		return t < 70 || t > 330;
	}
	return !1;
}
function la(e) {
	if (ai(e) > .12) {
		let t = oi(e);
		return t > 170 && t < 260;
	}
	return !1;
}
function ua(e, t, n, r) {
	if (J(r) < 12) return 2;
	{
		let i = F(2, (J(r) / 255 + .35 * n) * 2.4);
		switch (li(e, t, i) | 0) {
			case 0: return la(r) ? 0 : 2;
			case 1: return ca(r) ? 3 : 0;
			default: return ca(r) && i < 1.85 ? 3 : 1;
		}
	}
}
function da(e, t, n) {
	let r = bi(e, t, n);
	if (r == null) return 0;
	{
		let e = r;
		return ye(Jt((t) => y(t, e), Q), 0) | 0;
	}
}
var fa = new ea("workbench", L(0, Q), L(0, Q), L(1, Q), L(1, Q), L(2, Q), L(1, Q), L(1, Q), L(3, Q), "'Topaz', 'Topaz a500a1', ui-monospace, Menlo, monospace", 0, (e, t, n, r, i) => {
	let a;
	switch (a = e.tag === 1 && (n + r) % 2 == 0 ? 0 : 1, a) {
		case 0: return [L(0, Q), 1];
		default: return [L(ua(n, r, t, i), Q), 1];
	}
}, (e, t) => {
	for (let t = 0; t <= e.h - 1; t++) for (let n = 0; n <= e.w - 1; n++) Y(e, n, t, L(3 - da(e, n, t), Q), 1);
}), $ = [
	K("#031008"),
	K("#0B3B1B"),
	K("#1C7F3D"),
	K("#3FD46E"),
	K("#A8FFC4")
], pa = [
	ia,
	sa,
	fa,
	new ea("phosphor", K("#020A05"), K("#04140A"), L(1, $), L(2, $), L(1, $), K("#7DFFA8"), L(2, $), L(4, $), "ui-monospace, Menlo, monospace", 2, (e, t, n, r, i) => {
		let a = e.tag === 2 ? J(i) / 255 + .15 + .3 * t : e.tag === 0 ? J(i) / 255 + .3 * t : J(i) / 255 * .5, o;
		switch (o = J(i) < 16 ? 0 : e.tag === 1 && (n + r) % 2 == 1 ? 1 : 2, o) {
			case 0: return [L(0, $), 1];
			case 1: return;
			default: return [L(F(4, li(n, r, F(4, a * 4.2))), $), 1];
		}
	}, (e, t) => {
		Ci(e, L(3, $), 1);
		for (let t = 1; t <= e.w - 2; t++) ci(t, 1) < .5 && Y(e, t, 1, L(2, $), 1), ci(t, e.h - 2) < .5 && Y(e, t, e.h - 2, L(2, $), 1);
	})
];
function ma(e) {
	return ye(qt((t) => t.name === e, pa), ia);
}
var ha = /* @__PURE__ */ new Map([]);
function ga(e, t) {
	if (e.name === ia.name) return t;
	{
		ha.has(e.name) || ha.set(e.name, /* @__PURE__ */ new Map([]));
		let n = Qi(ha, e.name);
		n.size > 1e5 && n.clear();
		let r = gi(t.w, t.h);
		for (let i = 0; i <= t.h - 1; i++) for (let a = 0; a <= t.w - 1; a++) {
			let o = bi(t, a, i);
			if (o != null) {
				let t = o, s = ((~~t.r * 256 + ~~t.g) * 256 + ~~t.b) * 16 + (i & 3) * 4 + (a & 3) | 0, c, u = l();
				if (c = [Zi(n, s, new C(() => u, (e) => {
					u = e;
				})), u], c[0]) yi(r, a, i, c[1]);
				else {
					let o, c = e.ink($i.Enabled, 0, a, i, t);
					o = c == null ? t : c[0], n.set(s, o), yi(r, a, i, o);
				}
			}
		}
		return r;
	}
}
function _a(e, t, n) {
	let r = gi(L(0, n.rows).length + 4, n.rows.length + 4);
	return xi(r, n, 2, 2, he(ge(e.ink)(t)(0))), t.tag === 2 && e.select(r, t.fields[0]), r;
}
// ../../../toolchain-logo/fsdsp56k-pixel-font.js
window.fsdsp56kIcon = {
	palette: {
		o: "#05080D",
		a: "#2A3240",
		b: "#5A6B86",
		c: "#38425A",
		d: "#222936",
		L: "#1F2531",
		R: "#141920",
		h: "#F8FAFC",
		p: "#E2E8F0",
		i: "#CBD5E1",
		q: "#94A3B8",
		r: "#64748B",
		s: "#475569",
		B: "#378BBA",
		C: "#4FA6D6",
		D: "#1E6189",
		E: "#164A69",
		K: "#30B9DB",
		J: "#6BE6FF",
		M: "#1788A4",
		N: "#10627A",
		W: "#00E5FF"
	},
	grids: {
		"signal-64": "\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n..............oooo..................o.o.........................\n.............oWWWWo................opopoo.......................\n............oWooooWo..............ooKoKopo......................\n...........oWo....oWo............opoKoKoKo......................\n...........oWo.....oWo...........oKoKoKoKoo.....................\n..........oWo......oWo..........ooKoKoKoKopoo.o.o.o.o.o.........\n.........oWo........oWo........oWoKoKoKoKoKoKoKoKoKoKopo........\n..........o..........oWo......oWo.o.o.o.o.oopoKoKoKoKoo.........\n......................oWo.....oWo...........ooKoKoKoKo..........\n......................oWo....oWo.............oKoKoKopo..........\n.......................oWooooWo..............opoKoKoo...........\n........................oWWWWo................oopopo............\n.........................oooo...................o.o.............\n................................................................\n................................................................\n................................................................\n..............................o..o..............................\n............................ooiooioo............................\n..........................oopopccpoioo..........................\n........................oopohccccccpoioo........................\n......................oopohccccaaccccpoqoo......................\n....................oopohccccaaaaaaccccioqoo....................\n..................oopohccccaaaaaaaaaaccccioqoo..................\n................oopohccccaaaaaCaaJaaaaaccccioqoo................\n..............oopohccccaaaaaCCBaaKJJaaaaaccccioqoo..............\n............oopohccccaaaaaCCBBBaaKKKJJaaaaaccccioroo............\n..........oopohccccaaaaaCCBBBBBaaKKKKKJJaaaaaccccqoroo..........\n........oopohccccaaaaaCCBBBBBBEaaMKKKKKKJJaaaaaccccqoroo........\n.......opohccccaaaaaCCBBBBBBEEEaaMMMKKKKKKJJaaaaaccccqoro.......\n.......ohccccaaaaaCCBBBBBBEEEECaaaMMMMKKKKKKJJaaaaaccccqo.......\n.....ooccccaaaaaCCBBBBBBEEEECCBaaaaaMMMMKKKKKKJJaaaaaccccoo.....\n....occccaaaaaaDBBBBBBBBEEaDBBBaaaaaaaMMKKKKKKKKNaaaaaacccco....\n...oLbbddaaaaaaDDDBBBBBBCCaDDDBaaaaaaaJJKKKKKKNNNaaaaaaddbbRo...\n...oLLLbbddaaaaaDDDDBBBBBBCCDDDaaaaaJJKKKKKKNNNNaaaaaddbbRRRo...\n...oLLLLLbbddaaaaaDDDDBBBBBBCCDaaaJJKKKKKKNNNNaaaaaddbbRRRRRo...\n...oLLLLLLLbbddaaaaaDDDDBBBBBBCaaJKKKKKKNNNNaaaaaddbbRRRRRRRo...\n...oLLLLLLLLLbbddaaaaaDDDDBBBBBaaKKKKKNNNNaaaaaddbbRRRRRRRRRo...\n....oLLLLLLLLLLbbddaaaaaDDDDBBBaaKKKNNNNaaaaaddbbRRRRRRRRRRo....\n.....ooLLLLLLLLLLbbddaaaaaDDDDBaaKNNNNaaaaaddbbRRRRRRRRRRoo.....\n.......opLLLLLLLLLLbbddaaaaaDDDaaNNNaaaaaddbbRRRRRRRRRRso.......\n.......opopLLLLLLLLLLbbddaaaaaDaaNaaaaaddbbRRRRRRRRRRsoso.......\n.......opopopLLLLLLLLLLbbddaaaaaaaaaaddbbRRRRRRRRRRsososo.......\n.......ohopopopLLLLLLLLLLbbddaaaaaaddbbRRRRRRRRRRsososoro.......\n........oohopopoiLLLLLLLLLLbbddaaddbbRRRRRRRRRRsososoroo........\n..........oohopoioiLLLLLLLLLLbbddbbRRRRRRRRRRsososoroo..........\n............oohoioioiLLLLLLLLLLbbRRRRRRRRRRsososoroo............\n..............oopoioioiLLLLLLLLLRRRRRRRRRrososoroo..............\n................oopoioioiLLLLLLLRRRRRRRrorosoroo................\n..................oopoioioqLLLLLRRRRRrorororoo..................\n....................oopoioqoqLLLRRRrororoqoo....................\n......................oopoqoqoqLRrororoqoo......................\n........................ooioqoqoororoqoo........................\n..........................ooioqooroqoo..........................\n............................ooiooqoo............................\n..............................o..o..............................\n................................................................\n................................................................\n................................................................\n................................................................\n",
		"chip-64": "\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n..............................o..o..............................\n............................ooiooioo............................\n..........................ooiopccpoioo..........................\n........................ooiopccccccpoioo........................\n......................oopopccccaaccccpoqoo......................\n....................oopohccccaaaaaaccccioqoo....................\n..................oopohccccaaaaaaaaaaccccioqoo..................\n................oopohccccaaaaaCaaJaaaaaccccioqoo................\n..............oopohccccaaaaaCCBaaKJJaaaaaccccioroo..............\n............oopohccccaaaaaCCBBBaaKKKJJaaaaaccccqoroo............\n..........oopohccccaaaaaCCBBBBBaaKKKKKJJaaaaaccccqoroo..........\n........oopohccccaaaaaCCBBBBBBEaaMKKKKKKJJaaaaaccccqoroo........\n.......opohccccaaaaaCCBBBBBBEEEaaMMMKKKKKKJJaaaaaccccqoso.......\n.......ohccccaaaaaCCBBBBBBEEEECaaaMMMMKKKKKKJJaaaaaccccro.......\n.....ooccccaaaaaCCBBBBBBEEEECCBaaaaaMMMMKKKKKKJJaaaaaccccoo.....\n....occccaaaaaaDBBBBBBBBEEaDBBBaaaaaaaMMKKKKKKKKNaaaaaacccco....\n...oLbbddaaaaaaDDDBBBBBBCCaDDDBaaaaaaaJJKKKKKKNNNaaaaaaddbbRo...\n...oLLLbbddaaaaaDDDDBBBBBBCCDDDaaaaaJJKKKKKKNNNNaaaaaddbbRRRo...\n...oLLLLLbbddaaaaaDDDDBBBBBBCCDaaaJJKKKKKKNNNNaaaaaddbbRRRRRo...\n...oLLLLLLLbbddaaaaaDDDDBBBBBBCaaJKKKKKKNNNNaaaaaddbbRRRRRRRo...\n...oLLLLLLLLLbbddaaaaaDDDDBBBBBaaKKKKKNNNNaaaaaddbbRRRRRRRRRo...\n....oLLLLLLLLLLbbddaaaaaDDDDBBBaaKKKNNNNaaaaaddbbRRRRRRRRRRo....\n.....ooLLLLLLLLLLbbddaaaaaDDDDBaaKNNNNaaaaaddbbRRRRRRRRRRoo.....\n.......opLLLLLLLLLLbbddaaaaaDDDaaNNNaaaaaddbbRRRRRRRRRRso.......\n.......opopLLLLLLLLLLbbddaaaaaDaaNaaaaaddbbRRRRRRRRRRsoso.......\n.......opopopLLLLLLLLLLbbddaaaaaaaaaaddbbRRRRRRRRRRsososo.......\n.......ohopopopLLLLLLLLLLbbddaaaaaaddbbRRRRRRRRRRsososoro.......\n........oohopopoiLLLLLLLLLLbbddaaddbbRRRRRRRRRRsososoroo........\n..........oohopoioiLLLLLLLLLLbbddbbRRRRRRRRRRsososoroo..........\n............oohoioioiLLLLLLLLLLbbRRRRRRRRRRrososoroo............\n..............oopoioioiLLLLLLLLLRRRRRRRRRrorosoroo..............\n................oopoioioiLLLLLLLRRRRRRRrorororoo................\n..................oopoioioqLLLLLRRRRRrororoqoo..................\n....................oopoioqoqLLLRRRrororoqoo....................\n......................oopoqoqoqLRrororoqoo......................\n........................ooioqoqoororoqoo........................\n..........................ooioqooroqoo..........................\n............................ooiooqoo............................\n..............................o..o..............................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n................................................................\n",
		"signal-48": "\n................................................\n................................................\n................................................\n...........ooo..............o...................\n..........oWWWo...........oopoo.................\n.........oWoooWo.........opoKopo................\n........oWo...oWo........oKoKoKo................\n........oWo....oWo......ooKoKoKoo.o.o.o.o.......\n.......oWo.....oWo.....oWoKoKoKopoKoKoKopo......\n........o.......oWo....oWoo.o.o.ooKoKoKoo.......\n................oWo...oWo........oKoKoKo........\n.................oWoooWo.........opoKopo........\n..................oWWWo...........oopoo.........\n...................ooo..............o...........\n................................................\n......................o..o......................\n....................ooiooioo....................\n..................oopopccpoioo..................\n................oopohccccccpoqoo................\n..............oopohccccaaccccioqoo..............\n............oopohccccaaaaaaccccioqoo............\n..........oopohccccaaaCaaJaaaccccioroo..........\n........oopohccccaaaCCBaaKJJaaaccccqoroo........\n.......opohccccaaaCCBBBaaKKKJJaaaccccqoro.......\n.......ohccccaaaCCBBBBEaaMKKKKJJaaaccccqo.......\n.....ooccccaaaCCBBBBEEaaaaMMKKKKJJaaaccccoo.....\n....occccaaaCCBBBBEEaaCaaaaaMMKKKKJJaaacccco....\n...oLbbddaaDBBBBBBaaaDBaaaaaaaKKKKKKNaaddbbRo...\n...oLLLbbddaDDBBBBCCaaDaaaaaJJKKKKNNaddbbRRRo...\n...oLLLLLbbddaDDBBBBCCaaaaJJKKKKNNaddbbRRRRRo...\n...oLLLLLLLbbddaDDBBBBCaaJKKKKNNaddbbRRRRRRRo...\n....oLLLLLLLLbbddaDDBBBaaKKKNNaddbbRRRRRRRRo....\n.....ooLLLLLLLLbbddaDDBaaKNNaddbbRRRRRRRRoo.....\n.......opLLLLLLLLbbddaDaaNaddbbRRRRRRRRso.......\n.......opopLLLLLLLLbbddaaddbbRRRRRRRRsoso.......\n.......ohopoiLLLLLLLLbbddbbRRRRRRRRsosoro.......\n........oohoioiLLLLLLLLbbRRRRRRRRsosoroo........\n..........oopoioiLLLLLLLRRRRRRRsosoroo..........\n............oopoioiLLLLLRRRRRrosoroo............\n..............oopoioqLLLRRRrororoo..............\n................oopoqoqLRroroqoo................\n..................ooioqooroqoo..................\n....................ooiooqoo....................\n......................o..o......................\n................................................\n................................................\n................................................\n................................................\n",
		"chip-48": "\n................................................\n................................................\n................................................\n................................................\n................................................\n................................................\n................................................\n................................................\n................................................\n................................................\n......................o..o......................\n....................ooiooioo....................\n..................ooiopccpoioo..................\n................oopopccccccpoqoo................\n..............oopohccccaaccccioqoo..............\n............oopohccccaaaaaaccccioqoo............\n..........oopohccccaaaCaaJaaaccccioroo..........\n........oopohccccaaaCCBaaKJJaaaccccqoroo........\n.......opohccccaaaCCBBBaaKKKJJaaaccccqoro.......\n.......ohccccaaaCCBBBBEaaMKKKKJJaaaccccqo.......\n.....ooccccaaaCCBBBBEEaaaaMMKKKKJJaaaccccoo.....\n....occccaaaCCBBBBEEaaCaaaaaMMKKKKJJaaacccco....\n...oLbbddaaDBBBBBBaaaDBaaaaaaaKKKKKKNaaddbbRo...\n...oLLLbbddaDDBBBBCCaaDaaaaaJJKKKKNNaddbbRRRo...\n...oLLLLLbbddaDDBBBBCCaaaaJJKKKKNNaddbbRRRRRo...\n...oLLLLLLLbbddaDDBBBBCaaJKKKKNNaddbbRRRRRRRo...\n....oLLLLLLLLbbddaDDBBBaaKKKNNaddbbRRRRRRRRo....\n.....ooLLLLLLLLbbddaDDBaaKNNaddbbRRRRRRRRoo.....\n.......opLLLLLLLLbbddaDaaNaddbbRRRRRRRRso.......\n.......opopLLLLLLLLbbddaaddbbRRRRRRRRsoso.......\n.......ohopopLLLLLLLLbbddbbRRRRRRRRsosoro.......\n........oohopoiLLLLLLLLbbRRRRRRRRsosoroo........\n..........oohoioiLLLLLLLRRRRRRRrosoroo..........\n............oopoioiLLLLLRRRRRrororoo............\n..............oopoioqLLLRRRroroqoo..............\n................oopoqoqLRroroqoo................\n..................ooioqooroqoo..................\n....................ooiooqoo....................\n......................o..o......................\n................................................\n................................................\n................................................\n................................................\n................................................\n................................................\n................................................\n................................................\n................................................\n",
		"chip-32": "\n................................\n................................\n................................\n................................\n................................\n..............o..o..............\n............ooiooioo............\n..........oopopccpoqoo..........\n........oopohccaaccioqoo........\n......oopohccaaaaaaccioroo......\n....oopohccaaaCaaJaaaccqoroo....\n...opohccaaaCCBaaKJJaaaccqoso...\n...ohccaaaCCBBEaaMKKJJaaaccro...\n..occaaaCCBBEECaaaMMKKJJaaacco..\n.oLbbaaDBBBBaDBaaaaaKKKKNaabbRo.\n.oLLLbbaDDBBCCDaaaJJKKNNabbRRRo.\n.oLLLLLbbaDDBBCaaJKKNNabbRRRRRo.\n..oLLLLLLbbaDDBaaKNNabbRRRRRRo..\n...opLLLLLLbbaDaaNabbRRRRRRso...\n...opopLLLLLLbbaabbRRRRRRsoso...\n...ohopoiLLLLLLbbRRRRRRsosoro...\n....oohoioiLLLLLRRRRRrosoroo....\n......oopoioiLLLRRRrororoo......\n........oopoioqLRroroqoo........\n..........oopoqooroqoo..........\n............ooiooqoo............\n..............o..o..............\n................................\n................................\n................................\n................................\n................................\n",
		"diamond-32": "\n................................\n................................\n................................\n............o..oo..o............\n...........ohooccooho...........\n..........oohhcaacppoo..........\n.........ohoocaaaacoopo.........\n........oohhcaaCJaaciioo........\n.......ohoocaaCCJJaacoopo.......\n......oohhcaaCCCJJKaaciioo......\n.....ohoocaaCCCCJKKKaadooio.....\n....oohhcaaCCCCCKKKKKaadqqoo....\n...ohoocaaCCCCCaaKKKKKaadooio...\n...ohhcaaCCCCCaBaaKKKKKaadqqo...\n....ocaaCCCCCaBBaaaKKKMMaado....\n...ocaaCCCCCaBBBaaaaKMMMMaado...\n...ocaaCCCCBaBBBaaaaMMMMMaado...\n....ocaaCCBBBaBBaaaMMMMMaado....\n...oppcaaBBBBBaBaaMMMMMaadrro...\n...ohoocaaBBBBBaaMMMMMaadooqo...\n....ooiicaaBBBBBMMMMMaadrroo....\n.....opoocaaBBBDMMMMaadooqo.....\n......ooiidaaBDDMMMaadrroo......\n.......opoodaaDDMMaadooqo.......\n........ooqqdaaDMaadrroo........\n.........oioodaaaadooqo.........\n..........ooqqdaadrroo..........\n...........oiooddooqo...........\n............o..oo..o............\n................................\n................................\n................................\n",
		"diamond-16": "\n......o..o......\n.....ohooho.....\n....oohccpoo....\n...ohocBKcopo...\n...ohcBBKKcio...\n.ooocBBaaKKdooo.\nohhcBBaaaaKKdqio\n.ocBBaaBaaaKKdo.\n.ocBBaaBaaaKKdo.\nohpcBBaaaaKKdrqo\n.ooocBBaaKKdooo.\n...oidBBKKdro...\n...opodBKdoqo...\n....ooqddroo....\n.....oiooqo.....\n......o..o......\n",
		"chip-16": "\n................\n................\n......oooo......\n....oopaapoo....\n..oohaaaaaaioo..\n.ohaaCCaaJJaaqo.\noaaBBaBaaaaKKaao\nobbBBaBaaaaKKbbo\noLLbbDDaaMMbbRRo\n.opLLbbaabbRRso.\n.ohoiLLbbRRsoro.\n..oopoqLRroroo..\n....ooiooqoo....\n......o..o......\n................\n................\n",
		"flat-16": "\n....o.o..o.o....\n...ohohoohopo...\n...ohopoopoio...\n.oocccccccccaoo.\nohhcaaaCJaaadqio\n.oocaaCCJJaadoo.\nohpcaCCaaJJadqio\n.oocBBaBaaKKdoo.\n.oocBBaBaaKKdoo.\nohpcaBBaaKKadrqo\n.oocaaDDMMaadoo.\nopicaaaDMaaadrqo\n.ooadddddddddoo.\n...oqoqoororo...\n...oioiooqoqo...\n....o.o..o.o....\n"
	}
}, window.fsdsp56kCommandIcons = {
	palette: {
		Y: "#FFF3A0",
		z: "#0F243C",
		y: "#1B3A5C",
		k: "#1A1D22",
		l: "#2A2F37",
		m: "#3A414C",
		e: "#FF7A30",
		u: "#FF3B30"
	},
	icons: {
		"file-fs": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhCJhhhhio.\n.ohhhhCCKKhhhio.\n.ohhhCChhKKhhio.\n.ohhCChBhhKMhio.\n.ohhCBhBhhMMhio.\n.ohhhBBhhMMhhio.\n.ohhhhBBMMhhhio.\n.ohhhhhDMhhhhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.oiiiiiiiiiiiio.\n",
		"file-fsx": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhCJhhhhio.\n.ohhhhCCKKhhhio.\n.ohhhCChhKKhhio.\n.ohhCChBhhKMhio.\n.ohhCBhBhooooo..\n.ohhhBBhhoYo....\n.ohhhhBBMoYYo...\n.ohhhhhDMoYYYo..\n.ohhhhhhhoYYYo..\n.ohhhhhhhoYYo...\n.oiiiiiiioYo....\n",
		"file-fsproj": "\n................\nooooooo.........\niiiiiiio........\niiiiiiiooooooooo\nhhhhhhhhhhhhhhhh\npppppppCJppppppp\nppppppCCKKpppppp\npppppCCppKKppppp\nppppCCpBppKMpppp\nppppCBpBppMMpppp\npppppBBppMMppppp\nppppppBBMMpppppp\npppppppDMppppppp\npppppppppppppppp\niiiiiiiiiiiiiiii\noooooooooooooooo\n",
		kernel: "\n.......oo.......\n......oCJo......\n.....oCCJJo.....\n....oCCCJKKo....\n...oCCCCKKKKo...\n..oCCCCooKKKKo..\n.oCCCCoBooKKKMo.\noCCCCoBBo.oKMMMo\noCCCBoBBo.oMMMMo\n.oCBBBoBooMMMMo.\n..oBBBBooMMMMo..\n...oBBBBMMMMo...\n....oBBDMMMo....\n.....oDDMMo.....\n......oDMo......\n.......oo.......\n",
		"file-asm": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhhhhhhhio.\n.ohhDDDDDhhhhio.\n.ohhhhhhhhhhhio.\n.ohhhhBBBBBhhio.\n.ohhhhhhhhhhhio.\n.ohhhhBBBBhhhio.\n.ohhhhhhhhhhhio.\n.ohhDDDDDDhhhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.oiiiiiiiiiiiio.\n",
		"file-lst": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhhhhhhhio.\n.ohhqqhBBBBhhio.\n.ohhhhhhhhhhhio.\n.ohhqqhBBBBhhio.\n.ohhhhhhhhhhhio.\n.ohhqqhBBBhhhio.\n.ohhhhhhhhhhhio.\n.ohhqqhBBBBhhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.oiiiiiiiiiiiio.\n",
		"file-hex": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhhhhhhhio.\n.ohhKKKhKKKhhio.\n.ohhMMMhMMMhhio.\n.ohhhhhhhhhhhio.\n.ohhKKKhKKKhhio.\n.ohhMMMhMMMhhio.\n.ohhhhhhhhhhhio.\n.ohhKKKhKKKhhio.\n.ohhMMMhMMMhhio.\n.ohhhhhhhhhhhio.\n.oiiiiiiiiiiiio.\n",
		"file-mds": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhhhhhhhio.\n.ohhrrrrrrrrhio.\n.ohhrikeekirhio.\n.ohhrikkkkirhio.\n.ohhruiiiiirhio.\n.ohhrkikikirhio.\n.ohhrrrrrrrrhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.oiiiiiiiiiiiio.\n",
		"files-mds": "\n.....oooooooo...\n...oooooooohhi..\n...ohhhhhhhihii.\noooooooohhhiihio\nohhhhhhhihohioio\nohhhhhhhiihhioio\nohhhhhhohiohioio\nohhhhhhhhiohioio\norrrrrrrriohioio\norikeekiriohioio\noruiiiiiriohioio\norkikikiriohioio\norrrrrrrrioiiooo\nohhhhhhhhioooo..\noiiiiiiiiio.....\nooooooooooo.....\n",
		"file-syx": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhhhhhhhio.\n.ohhhhqqqqhhhio.\n.ohhhqkhhkrhhio.\n.ohhqkhkkhkrhio.\n.ohhqhkkkkhrhio.\n.ohhqkkkkkkrhio.\n.ohhhrkqqkrhhio.\n.ohhhhrrrrhhhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.oiiiiiiiiiiiio.\n",
		"file-json": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhhhhhhhio.\n.ohhhBhhhhBhhio.\n.ohhBhhhhhhBhio.\n.ohhBhhhhhhBhio.\n.ohBhhhqqhhhBio.\n.ohhBhhhhhhBhio.\n.ohhBhhhhhhBhio.\n.ohhhBhhhhBhhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.oiiiiiiiiiiiio.\n",
		"file-wav": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhhhhhhhio.\n.ohhhhKhhhhhhio.\n.ohhhKhKhhhhhio.\n.ohhhKhKhhhhhio.\n.ohhKhhhKhhhKio.\n.ohhhhhhKhhKhio.\n.ohhhhhhhKhKhio.\n.ohhhhhhhhKhhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.oiiiiiiiiiiiio.\n",
		"file-pbm": "\n.ohhhhhhhho.....\n.ohhhhhhhiho....\n.ohhhhhhhiiho...\n.ohhhhhhhiiiho..\n.ohhhhhhhiiiiio.\n.ohhhhhhhhhhhio.\n.ohhiikkiihhhio.\n.ohhikiikihhhio.\n.ohhkkkkkkhhhio.\n.ohhkikkikhhhio.\n.ohhkkkkkkhhhio.\n.ohhikiikihhhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.ohhhhhhhhhhhio.\n.oiiiiiiiiiiiio.\n",
		oracle: "\n......oooo......\n....ooKKKKoo....\n...oKKKzzKKKo...\n..oKKzzzzzzKMo..\n.oKKzzzzzzzzMMo.\n.oKzzzzzzzYYzMo.\noKKzzzzzzYYzzMMo\noKzzYzzzYYzzzzMo\noKzzYYzYYzzzzzMo\noKKzzYYYzzzzzMMo\n.oKzzzYzzzzzzMo.\n.oKKzzzzzzzzMMo.\n..oMMzzzzzzMMo..\n...oMMMzzMMMo...\n....ooMMMMoo....\n......oooo......\n",
		report: "\n................\noooooooooooooooo\nmqmqmmmmmmmmmmmm\nmmmmmmmmmmmmmmmm\nkkkkkkkkkkkkkkkk\nkkkkkkkkkkkkkkkk\nkkKkkkkkkkkkkkkk\nkkkKkkkkkkkkkkkk\nkkKkkYYYkkkkkkkk\nkkkkkkkkkkkkkkkk\nkkiiiiiiiiikkkkk\nkkkkkkkkkkkkkkkk\nkkiiiiiikkkkkkkk\nkkkkkkkkkkkkkkkk\noooooooooooooooo\n................\n",
		cycles: "\no.o.o.o.o.o.o.o.\nYoYoYoYoYoYoYoYo\no.o.o.o.o.ooo.o.\n.........oJMo...\n.........oKMo...\n....oo...oKMo...\n...oJMo..oKMooo.\n...oKMo..oKMoJMo\n...oKMooooKMoKMo\n.oooKMoJMoKMoKMo\noJMoKMoKMoKMoKMo\noKMoKMoKMoKMoKMo\noKMoKMoKMoKMoKMo\noKMoKMoKMoKMoKMo\noKMoKMoKMoKMoKMo\n.oo.oo.oo.oo.oo.\n",
		audio: "\n................\n.......o........\n......opo...o...\n.....oppo..oYo..\n....oippo...oYo.\n.oooiippo.o..oYo\noqqqiippooYo.oYo\noqqqiippo.oYooYo\noqqqiippo.oYooYo\noqqqiippooYo.oYo\n.oooiippo.o..oYo\n....oippo...oYo.\n.....oppo..oYo..\n......opo...o...\n.......o........\n................\n",
		midi: "\n................\n................\n................\n......oo......oo\noooooohhoooooohh\nhhhhhopqohhhhopq\npqpqqopqoqqqqopq\nqoqoqopqooooqopq\nqoqoqopqohooqopq\nqoqoqopqoqooqopq\nqoqoqopqophhqopq\nqoqoqoqqoqqqqoqq\no.o.o.oo.oooo.oo\n................\n................\n................\n",
		"midi-port": "\n................\n.....oooooo.....\n...ooiiiiiqoo...\n..oiippkkiiqqo..\n..oippkkkkiiqo..\nooippkkkkkkiiqoo\niiipkkkkkkkkirrr\nikipkikkkkikqrkr\nikipkkkkkkkkqrkr\niiiikkikkikkqrrr\nooqiikkkikkqqroo\n..oqiikkkkqqro..\n..oqqiiqqqqrro..\n...ooqrrrrroo...\n.....oooooo.....\n................\n",
		dsp56300: "\n....o.o..o.o....\n...opopoopopo...\n...opopoopopo...\n.oocccccccccdoo.\noppcaaaaaaaadqro\n.oocaKaaaaaadoo.\noppcaaaaaaaadqro\n.oocaaaaaaaadoo.\n.oocaaqqqqaadoo.\noppcaaaaaaaadqro\n.oocaarrraaadoo.\noppcaaaaaaaadqro\n.ooddddddddddoo.\n...oqoqooqoqo...\n...ororoororo...\n....o.o..o.o....\n",
		machinedrum: "\n................\n................\n................\n.oooooooooooooo.\nohhhhhhhhhhhhhho\noiiiiikkkkikikio\noikkiikeekiiiiio\noikkiikkkkikikio\noqqqqqqqqqqqqqqo\noiuiiiiiuiiiiiio\noikikikikikikiio\noqqqqqqqqqqqqqqo\n.oooooooooooooo.\n................\n................\n................\n",
		"machinedrum-32": "\n.oooooooooooooooooooooooooooooo.\nohhhhhhhhhhhhhhhhhhhhhhhhhhhhhqo\noiiiuirirpkkkkkkkkkpiiqqqqqqqiqo\noikiiriripkqqqqkkkkpiiiiiiiiiiqo\noiiiiiiiipkeeeeueekpikikikikikqo\noiiiikkiipkeuueeuekpiiiiiiiiiiqo\noikiklkkipkeeeeeeekpirikikikikqo\noikikkkkipkkkkkkkkkpiiiiiiiiiiqo\noiiiikkiipkkkkkkkkkpiiiiiiiiiiqo\noppppppppppppppppppppppppppppppo\noikikikikiiiiikiiiiiiiiiiiiiiiqo\noikikikikiiikikikiieeikkihhikkqo\noppppppppppppppppppppppppppppppo\noiuiriririuiriririuiririririiiqo\noikikikikikikikikikikikikikikkqo\noikikikikikikikikikikikikikikkqo\noqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqo\n.oooooooooooooooooooooooooooooo.\n"
	}
}, window.fsdsp56kAuditionIcons = {
	palette: {
		P: "#1292FF",
		S: "#5CB3FF",
		Q: "#0B5EB8",
		v: "#A6D6FF",
		F: "#FF8A4C",
		G: "#F74C00",
		H: "#A83200",
		V: "#8A78FF",
		X: "#654FF0",
		Z: "#4532B8",
		U: "#F7DF1E",
		T: "#C2AC00",
		1: "#659AD2",
		2: "#00599C",
		3: "#004482"
	},
	icons: {
		fable: "\n.....o.oo.......\n...ooSoSSoooooo.\n.ooSSPSPPSSSSSSo\noSSooQPPPPPPPQQo\nSoo..oPPPPQQQoo.\no....oPPPQooo...\n.....oPPPQooo...\n.....oQQQQSSSoo.\n....oQvvvvvvvPPo\n....oQSSSSSSSPQo\n....oQSSSSSSPQo.\n.....ooSSSSooo..\n......oPPQo.....\n......oPQo......\n.....oSQo.......\n.....oQo........\n",
		rust: "\n.....ooppoo.....\n....opopioio....\n...ooppiiiioo...\n..opppiiiiiiio..\n.ooppooooooiioo.\nopppoiiiiiooiqqo\noopiooiioiioqqoo\nppiiooiiiiooqqqq\npiiiooiioiooqqqq\nooiiooiioqqoqqoo\noiiioiiioqqoqqqo\n.ooiiooooooqqoo.\n..oiiiqqqqqqqo..\n...ooqqqqqqoo...\n....oqoqqoqo....\n.....ooqqoo.....\n",
		ferris: "\n................\n................\n.....o.oo.o.....\n....oFoFFoFo....\no.ooFGFGGFGFoo.o\nFoFoGGGGGGGHoFoF\nHFHoGGGGGGGHoHFH\noHoFGGGGGGGGFoHo\n.oFGGHHGGHHGGFo.\n.oGGGhkGGhkGGHo.\n.oHGGkkHHkkGGHo.\n..oHGFFkkFFGHo..\n...oHGHFFHGHo...\n..oFoHooooHoFo..\n.oFoFo....oFoFo.\n..o.o......o.o..\n",
		wasm: "\n.ooooo....ooooo.\noVVVVVo..oXXXXXo\noVVVVVVooXXXXXXo\noVVVVVXXXXXXXXXo\noVVVVXXXXXXXXXXo\noVVVXXXXXXXXXXXo\noVVXXXXXXXXXXXXo\noVXXXXXXXXXXXXXo\noXhXXXhXXXhhXXXo\noXhXXXhXXhXXhXXo\noXhXhXhXXhhhhXXo\noXhXhXhXXhXXhXZo\noXXhXhXXXhXXhZZo\noXXXXXXXXXXXZZZo\noXXXXXXXXXXZZZZo\n.oooooooooooooo.\n",
		javascript: "\n.oooooooooooooo.\noUUUUUUUUUUUUUTo\noUUUUUUUUUUUUUTo\noUUUUUUUUUUUUUTo\noUUUUUUUUUUUUUTo\noUUUUUUUUUUUUUTo\noUUUUUUUUUUUUUTo\noUUUUUUUUUUUUUTo\noUUUUUUUUkUUkkTo\noUUUUUUUUkUkUUTo\noUUUUUUUUkUUkUTo\noUUUUUUkUkUUUkTo\noUUUUUUUkUUkkUTo\noUUUUUUUUUUUUUTo\noTTTTTTTTTTTTTTo\n.oooooooooooooo.\n",
		webaudio: "\n................\noooooooooooooooo\nqquqYqKqqqqqqqqq\nqqqqqqqqqqqqqqqq\nkkkkkkkkkkkkkkkk\nkkkkkkkkkkkkkkkk\nkkkkWWkkkkkkkkkk\nkkkWkkWkkkkkkkkk\nkkWkkkkWkkkkkkkk\nkWkkkkkkkkkkkkWk\nkkkkkkkkWkkkkWkk\nkkkkkkkkkWkkWkkk\nkkkkkkkkkkWWkkkk\nkkkkkkkkkkkkkkkk\nkkkkkkkkkkkkkkkk\noooooooooooooooo\n",
		cpp: "\n.....oo11oo.....\n....o111111o....\n..oo11111111oo..\n.o111111111111o.\no11hhhhh1111112o\no1hhhhhh1111222o\no1hh11111112222o\no1hh111112h22h2o\no1hh11133hhhhhho\no1hh133333h22h2o\no1hhhhhh3333222o\no13hhhhh3333332o\n.o333333333333o.\n..oo33333333oo..\n....o333333o....\n.....oo33oo.....\n",
		emulator: "\n....o.o..o.o....\n...oMoMooMoMo...\n...oooooooooo...\n.ooKkKkKkKkKkoo.\noMokkkkkkkkkKoMo\n.ooKkKkkkkkkkoo.\noMokkkkkkkkkKoMo\n.ooKkkkkkkkkkoo.\n.ookkkMMMMkkKoo.\noMoKkkkkkkkkkoMo\n.ookkkMMMkkkKoo.\noMoKkkkkkkkkkoMo\n.ookKkKkKkKkKoo.\n...oooooooooo...\n...oMoMooMoMo...\n....o.o..o.o....\n"
	}
}, window.fsdsp56kCreditIcons = {
	palette: {
		0: "#D8D0BC",
		1: "#A99F88",
		2: "#E3BC52",
		3: "#A6842C",
		4: "#E6BD7E",
		5: "#A8773E",
		6: "#D5DCE5",
		7: "#A3AFBF",
		F: "#FF8A4C",
		G: "#F74C00",
		H: "#A83200"
	},
	icons: {
		motorola: "\n................\n.oooooooooooooo.\no00000000000000o\no0q011111111001o\no000hhhhhhhh001o\no00022222222001o\no00022222222001o\no00022222222001o\no00033333333001o\no11100000000111o\no11111111111111o\no11111111111111o\n.ohohohoohohoho.\n.oqoqoqooqoqoqo.\n.oqoqoqooqoqoqo.\n..o.o.o..o.o.o..\n",
		mdx: "\n................\n.oooooooooooooo.\nollllllllllllllo\nommkkkkkkkkkkmko\nommoooooooooomko\nommoeeeeeeeeomko\nommoekkeekkeomko\nommoeekkkkeeomko\nommoeeekkeeeomko\nommoeekkkkeeomko\nommoekkeekkeomko\nommoeeeeeeeeomko\nommoooooooooomko\nommllllllllllmko\nokkkkkkkkkkkkkko\n.oooooooooooooo.\n",
		"usual-suspects": "\noooooooooooooooo\no66666666666666o\no666666rr666666o\no77777rrrm77777o\no6rr666rr666666o\norrrm6mllk66rr6o\no7rr77mllk7rrrmo\nomllk6mllk66rr6o\nomllk6mllk6mllko\nomllk7mllk7mllko\nomllk6mllk6mllko\nomllk6mllk6mllko\nomllk7mllk7mllko\nomllk6mllk6mllko\nomllk6mllk6mllko\noooooooooooooooo\n",
		"dsp56300-rust": "\no.o..........o.o\nFoFo..o.oo..oFoF\nHFHoooFoFFoooHFH\noHooFFGFGGFFooHo\noHFFHHGGGGHHFoHo\n.oHGhkGGGGhkGFo.\n..oHFFHHHHFFHo..\n.oFoHooooooHoFo.\n.oooooooooooooo.\nopccccccccccccpo\n.oaaaaaaaaaaado.\nopaaaaaaaaaaaapo\n.oaaaaaaaaaaado.\nopaaaaaaaaaaaapo\n.oddddddddddddo.\n..oooooooooooo..\n",
		tx6: "\no4o..........o4o\n.o4o........o4o.\n..o4o......o4o..\n...o4oooooo4o...\n.ooih4hhhh4hioo.\noihhphpppphphhio\noqiiiiiiiiiiiiro\norqiiiiiiiiiiqro\noJqJJqJJJJqJJqJo\noKqKKqKKKKqKKqNo\noNqNNqNNNNqNNqNo\noiqiiqiiiiqiiqio\n.orrrrrrrrrrrro.\n..oooooooooooo..\n................\n................\n"
	}
}, window.fsdsp56kSynthIcons = {
	palette: {
		n: "#C42A2E",
		j: "#8E1B20",
		t: "#C9302C",
		w: "#B7BCC2",
		x: "#D9C56A",
		f: "#A8964A",
		g: "#3A6FB0",
		A: "#8FD14F",
		I: "#F2F2EE",
		O: "#E0442E",
		F: "#FF8A4C"
	},
	icons: {
		virus: "\noooooooooooooooooooooooooooooo\nolkknnnnnnnnnnnnnnnnnnnnnnkklo\nolkknknnknnknnknnknnknnknnkklo\nolkknnnnnnnnnnnnnnnnnnnnnnkklo\nolkknknnknnknnknnknnknnknnkklo\nolkknnnnnnnnnnnnnnnnnnnnnnkklo\nolkknnkknnnAAAAAAAAnnnkknnkklo\nolkknnkknnnAAAAAAAAnnnkknnkklo\nolkknnnnnnnnnnnnnnnnnnnnnnkklo\nolkkjjjjjjjjjjjjjjjjjjjjjjkklo\noooooooooooooooooooooooooooooo\n",
		nordlead: "\noooooooooooooooooooooooooooooooo\notttttttttttttttttttttttttttttto\notwwwwwwwwwwwwwwwwwwttttttttttto\notwkwwkwwkwwkwwkwwkwtttIIIIIttto\notwwwwwwwwwwwwwwwwwwttttttttttto\notttttttttttttttttttttttttttttto\notkktIkIkIIIkIkIkIIIkIkIIkIkIIko\notkktIkIkIIIkIkIkIIIkIkIIkIkIIko\notkktIpIpIpIpIpIpIpIpIpIpIpIpIpo\notkktIpIpIpIpIpIpIpIpIpIpIpIpIpo\notttttttttttttttttttttttttttttto\noooooooooooooooooooooooooooooooo\n",
		microq: "\noooooooooooooooooooooooooooooo\noxxxxxxxxxxxxxxxxxxxxxxxxxxxxo\noxxxOOOxxxxggggggggxxxxkxxkxko\noxxOFOOOxxxggggggggxxxxxxxxxxo\noxxOOOOOxxxxxxxxxxxxxxxxkxxkxo\noxxxOOOxxxxxkxxkxxkxxxxxxxxxxo\noxxxxxxxxxxxxxxxxxxxxxxxkxxkxo\noffffffffffffffffffffffffffffo\noooooooooooooooooooooooooooooo\n"
	}
}, window.fsdsp56kWindowGlyphs = {
	palette: {},
	icons: {
		"gadget-close": "\nhhhhhhhhr\nh.......r\nh.......r\nh..ooo..r\nh..oho..r\nh..ooo..r\nh.......r\nh.......r\nrrrrrrrrr\n",
		"gadget-depth": "\nhhhhhhhhhhr\nh.........r\nh.oooo....r\nh.o..o....r\nh.o.ooooo.r\nh.ooohhho.r\nh...ohhho.r\nh...ooooo.r\nrrrrrrrrrrr\n",
		"gadget-size": "\nhhhhhhhhr\nh.......r\nh.....o.r\nh....oo.r\nh...ooo.r\nh..oooo.r\nh.ooooo.r\nh.......r\nrrrrrrrrr\n"
	}
}, window.fsdsp56kFont = {
	height: 9,
	spacing: 1,
	glyphs: "\n= A\n.###.\n#...#\n#...#\n#####\n#...#\n#...#\n#...#\n.....\n.....\n= B\n####.\n#...#\n#...#\n####.\n#...#\n#...#\n####.\n.....\n.....\n= C\n.###.\n#...#\n#....\n#....\n#....\n#...#\n.###.\n.....\n.....\n= D\n####.\n#...#\n#...#\n#...#\n#...#\n#...#\n####.\n.....\n.....\n= E\n#####\n#....\n#....\n####.\n#....\n#....\n#####\n.....\n.....\n= F\n#####\n#....\n#....\n####.\n#....\n#....\n#....\n.....\n.....\n= G\n.###.\n#...#\n#....\n#.###\n#...#\n#...#\n.###.\n.....\n.....\n= H\n#...#\n#...#\n#...#\n#####\n#...#\n#...#\n#...#\n.....\n.....\n= I\n###\n.#.\n.#.\n.#.\n.#.\n.#.\n###\n...\n...\n= J\n..###\n...#.\n...#.\n...#.\n#..#.\n#..#.\n.##..\n.....\n.....\n= K\n#...#\n#..#.\n#.#..\n##...\n#.#..\n#..#.\n#...#\n.....\n.....\n= L\n#....\n#....\n#....\n#....\n#....\n#....\n#####\n.....\n.....\n= M\n#...#\n##.##\n#.#.#\n#.#.#\n#...#\n#...#\n#...#\n.....\n.....\n= N\n#...#\n#...#\n##..#\n#.#.#\n#..##\n#...#\n#...#\n.....\n.....\n= O\n.###.\n#...#\n#...#\n#...#\n#...#\n#...#\n.###.\n.....\n.....\n= P\n####.\n#...#\n#...#\n####.\n#....\n#....\n#....\n.....\n.....\n= Q\n.###.\n#...#\n#...#\n#...#\n#.#.#\n#..#.\n.##.#\n.....\n.....\n= R\n####.\n#...#\n#...#\n####.\n#.#..\n#..#.\n#...#\n.....\n.....\n= S\n.###.\n#...#\n#....\n.###.\n....#\n#...#\n.###.\n.....\n.....\n= T\n#####\n..#..\n..#..\n..#..\n..#..\n..#..\n..#..\n.....\n.....\n= U\n#...#\n#...#\n#...#\n#...#\n#...#\n#...#\n.###.\n.....\n.....\n= V\n#...#\n#...#\n#...#\n#...#\n#...#\n.#.#.\n..#..\n.....\n.....\n= W\n#...#\n#...#\n#...#\n#.#.#\n#.#.#\n#.#.#\n.#.#.\n.....\n.....\n= X\n#...#\n#...#\n.#.#.\n..#..\n.#.#.\n#...#\n#...#\n.....\n.....\n= Y\n#...#\n#...#\n.#.#.\n..#..\n..#..\n..#..\n..#..\n.....\n.....\n= Z\n#####\n....#\n...#.\n..#..\n.#...\n#....\n#####\n.....\n.....\n= a\n.....\n.....\n.###.\n....#\n.####\n#...#\n.####\n.....\n.....\n= b\n#....\n#....\n####.\n#...#\n#...#\n#...#\n####.\n.....\n.....\n= c\n....\n....\n.###\n#...\n#...\n#...\n.###\n....\n....\n= d\n....#\n....#\n.####\n#...#\n#...#\n#...#\n.####\n.....\n.....\n= e\n.....\n.....\n.###.\n#...#\n#####\n#....\n.###.\n.....\n.....\n= f\n..##\n.#..\n###.\n.#..\n.#..\n.#..\n.#..\n....\n....\n= g\n.....\n.....\n.####\n#...#\n#...#\n#...#\n.####\n....#\n.###.\n= h\n#....\n#....\n####.\n#...#\n#...#\n#...#\n#...#\n.....\n.....\n= i\n#\n.\n#\n#\n#\n#\n#\n.\n.\n= j\n..#\n...\n..#\n..#\n..#\n..#\n..#\n..#\n##.\n= k\n#...\n#...\n#..#\n#.#.\n##..\n#.#.\n#..#\n....\n....\n= l\n#.\n#.\n#.\n#.\n#.\n#.\n.#\n..\n..\n= m\n.....\n.....\n##.#.\n#.#.#\n#.#.#\n#.#.#\n#.#.#\n.....\n.....\n= n\n.....\n.....\n####.\n#...#\n#...#\n#...#\n#...#\n.....\n.....\n= o\n.....\n.....\n.###.\n#...#\n#...#\n#...#\n.###.\n.....\n.....\n= p\n.....\n.....\n####.\n#...#\n#...#\n#...#\n####.\n#....\n#....\n= q\n.....\n.....\n.####\n#...#\n#...#\n#...#\n.####\n....#\n....#\n= r\n....\n....\n#.##\n##..\n#...\n#...\n#...\n....\n....\n= s\n.....\n.....\n.####\n#....\n.###.\n....#\n####.\n.....\n.....\n= t\n....\n.#..\n###.\n.#..\n.#..\n.#..\n..##\n....\n....\n= u\n.....\n.....\n#...#\n#...#\n#...#\n#...#\n.####\n.....\n.....\n= v\n.....\n.....\n#...#\n#...#\n#...#\n.#.#.\n..#..\n.....\n.....\n= w\n.....\n.....\n#...#\n#...#\n#.#.#\n#.#.#\n.#.#.\n.....\n.....\n= x\n.....\n.....\n#...#\n.#.#.\n..#..\n.#.#.\n#...#\n.....\n.....\n= y\n.....\n.....\n#...#\n#...#\n#...#\n#...#\n.####\n....#\n.###.\n= z\n.....\n.....\n#####\n...#.\n..#..\n.#...\n#####\n.....\n.....\n= 0\n.###.\n#...#\n#..##\n#.#.#\n##..#\n#...#\n.###.\n.....\n.....\n= 1\n..#..\n.##..\n..#..\n..#..\n..#..\n..#..\n.###.\n.....\n.....\n= 2\n.###.\n#...#\n....#\n...#.\n..#..\n.#...\n#####\n.....\n.....\n= 3\n#####\n...#.\n..#..\n...#.\n....#\n#...#\n.###.\n.....\n.....\n= 4\n...#.\n..##.\n.#.#.\n#..#.\n#####\n...#.\n...#.\n.....\n.....\n= 5\n#####\n#....\n####.\n....#\n....#\n#...#\n.###.\n.....\n.....\n= 6\n..##.\n.#...\n#....\n####.\n#...#\n#...#\n.###.\n.....\n.....\n= 7\n#####\n....#\n...#.\n..#..\n.#...\n.#...\n.#...\n.....\n.....\n= 8\n.###.\n#...#\n#...#\n.###.\n#...#\n#...#\n.###.\n.....\n.....\n= 9\n.###.\n#...#\n#...#\n.####\n....#\n...#.\n.##..\n.....\n.....\n= space\n...\n...\n...\n...\n...\n...\n...\n...\n...\n= .\n.\n.\n.\n.\n.\n.\n#\n.\n.\n= ,\n..\n..\n..\n..\n..\n..\n.#\n#.\n..\n= :\n.\n.\n.\n#\n.\n.\n#\n.\n.\n= ;\n..\n..\n..\n.#\n..\n..\n.#\n#.\n..\n= ·\n....\n....\n....\n.##.\n.##.\n....\n....\n....\n....\n= -\n...\n...\n...\n...\n###\n...\n...\n...\n...\n= _\n.....\n.....\n.....\n.....\n.....\n.....\n.....\n#####\n.....\n= #\n.#.#.\n.#.#.\n#####\n.#.#.\n#####\n.#.#.\n.#.#.\n.....\n.....\n= /\n....#\n...#.\n...#.\n..#..\n.#...\n.#...\n#....\n.....\n.....\n= (\n..#\n.#.\n#..\n#..\n#..\n.#.\n..#\n...\n...\n= )\n#..\n.#.\n..#\n..#\n..#\n.#.\n#..\n...\n...\n= [\n##\n#.\n#.\n#.\n#.\n#.\n##\n..\n..\n= ]\n##\n.#\n.#\n.#\n.#\n.#\n##\n..\n..\n= <\n...#\n..#.\n.#..\n#...\n.#..\n..#.\n...#\n....\n....\n= >\n#...\n.#..\n..#.\n...#\n..#.\n.#..\n#...\n....\n....\n= '\n#\n#\n.\n.\n.\n.\n.\n.\n.\n= \"\n#.#\n#.#\n...\n...\n...\n...\n...\n...\n...\n= !\n#\n#\n#\n#\n#\n.\n#\n.\n.\n= ?\n.###.\n#...#\n....#\n...#.\n..#..\n.....\n..#..\n.....\n.....\n= +\n.....\n..#..\n..#..\n#####\n..#..\n..#..\n.....\n.....\n.....\n= =\n.....\n.....\n#####\n.....\n#####\n.....\n.....\n.....\n.....\n= *\n.....\n#.#.#\n.###.\n#####\n.###.\n#.#.#\n.....\n.....\n.....\n= ×\n...\n...\n...\n#.#\n.#.\n#.#\n...\n...\n...\n= &\n.##..\n#..#.\n#.#..\n.#...\n#.#.#\n#..#.\n.##.#\n.....\n.....\n= %\n##...\n##..#\n...#.\n..#..\n.#...\n#..##\n...##\n.....\n.....\n= $\n..#..\n.####\n#.#..\n.###.\n..#.#\n####.\n..#..\n.....\n.....\n= @\n.###.\n#...#\n#.###\n#.#.#\n#.###\n#....\n.###.\n.....\n.....\n= |\n#\n#\n#\n#\n#\n#\n#\n#\n.\n= #....\n.#...\n.#...\n..#..\n...#.\n...#.\n....#\n.....\n.....\n= {\n..##\n.#..\n.#..\n#...\n.#..\n.#..\n..##\n....\n....\n= }\n##..\n..#.\n..#.\n...#\n..#.\n..#.\n##..\n....\n....\n= ~\n.....\n.....\n.#..#\n#.##.\n.....\n.....\n.....\n.....\n.....\n"
};
// ../../../toolchain-logo/art/src/Dom.fs
var va = [
	["fsdsp56kIcon", "grids"],
	["fsdsp56kCommandIcons", "icons"],
	["fsdsp56kAuditionIcons", "icons"],
	["fsdsp56kCreditIcons", "icons"],
	["fsdsp56kSynthIcons", "icons"],
	["fsdsp56kWindowGlyphs", "icons"]
], ya = (() => {
	let e = window.fsdsp56kFont;
	return Ti(e.height, e.spacing, e.glyphs);
})(), ba = pi(2, I((e) => {
	let t = e[0];
	return [Yr(I((e) => [e[0][0], K(e[1])], Object.entries(window[t].palette || {})), { Compare: (e, t) => le(e, t) | 0 }), I((e) => [e[0], At(e[1].trim(), ["\n"], void 0, 0)], Object.entries(window[t][e[1]] || {}))];
}, va));
function xa(e, t) {
	e.width !== t.w && (e.width = t.w), e.height !== t.h && (e.height = t.h);
	let n = e.getContext("2d"), r = n.createImageData(t.w, t.h);
	r.data.set(t.px), n.putImageData(r, 0, 0);
}
function Sa() {
	return matchMedia("(prefers-reduced-motion: reduce)").matches;
}
// ../../../toolchain-logo/art/src/Area.fs
function Ca(e) {
	return e.closest("[data-aesthetic]");
}
function wa(e) {
	let t = Ca(e);
	return t == null ? ia : ma(t.getAttribute("data-aesthetic"));
}
function Ta(e) {
	switch (e.getAttribute("data-state")) {
		case "disabled": return $i.Disabled;
		case "selected": return new $i(2, [0]);
		default: return $i.Enabled;
	}
}
function Ea(e) {
	let t = wa(e), n = e.querySelectorAll("canvas[data-glyph]");
	for (let r = 0; r <= n.length - 1; r++) {
		let i, a = n.item(r), o = Ca(a), s;
		o == null ? s = 1 : (i = o, s = i === e ? 0 : 1), s === 0 && xa(a, _a(t, Ta(a), mi(ba, a.getAttribute("data-glyph"))));
	}
}
function Da(e, t) {
	let n = ma(t);
	e.setAttribute("data-aesthetic", n.name);
	let r = [
		["--ground", n.ground],
		["--panel", n.panel],
		["--line", n.line],
		["--edge-lit", n.edgeLit],
		["--edge-shade", n.edgeShade],
		["--fg", n.fg],
		["--dim", n.dim],
		["--accent", n.accent]
	];
	for (let t = 0; t <= r.length - 1; t++) {
		let n = L(t, r);
		e.style.setProperty(n[0], ti(n[1]));
	}
	e.style.setProperty("--font", n.font), e.style.setProperty("--radius", `${n.radius}px`), Ea(e), e.dispatchEvent(new CustomEvent("aestheticchange", { detail: n.name }));
}
function Oa(e, t) {
	t(wa(e)), Ca(e)?.addEventListener("aestheticchange", (n) => {
		t(wa(e));
	});
}
// ../../../toolchain-logo/art/src/Crt.fs
var ka = "\nattribute vec2 corner;\nvarying vec2 uv;\nvoid main() {\n  uv = vec2(corner.x * 0.5 + 0.5, 0.5 - corner.y * 0.5);\n  gl_Position = vec4(corner, 0.0, 1.0);\n}\n", Aa = "\nprecision mediump float;\nuniform sampler2D picture;\nuniform vec2 size;        // the picture, in its pixels\nuniform float time;       // seconds\nuniform float strength;   // 0 a flat screen, 1 the whole tube\nvarying vec2 uv;\n\n// the glass: the picture pushed out toward the corners\nvec2 bulge(vec2 p) {\n  p = p * 2.0 - 1.0;\n  vec2 o = abs(p.yx) / vec2(5.5, 4.0);\n  p += p * o * o * strength;\n  return p * 0.5 + 0.5;\n}\n\nvec3 at(vec2 q) { return texture2D(picture, q).rgb; }\n\nvoid main() {\n  vec2 q = bulge(uv);\n  if (q.x < 0.0 || q.x > 1.0 || q.y < 0.0 || q.y > 1.0) { gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0); return; }\n  vec2 texel = 1.0 / size;\n  // the beams a little apart: red to the right, blue to the left\n  vec3 c = vec3(at(q + vec2(0.4 * texel.x * strength, 0.0)).r, at(q).g, at(q - vec2(0.4 * texel.x * strength, 0.0)).b);\n  // light bleeding to the neighbours\n  vec3 bleed = (at(q + vec2(texel.x, 0.0)) + at(q - vec2(texel.x, 0.0)) + at(q + vec2(0.0, texel.y)) + at(q - vec2(0.0, texel.y))) * 0.25;\n  c += bleed * 0.22 * strength;\n  // a beam per row: brightest along its middle, dark between rows\n  float row = fract(q.y * size.y);\n  c *= mix(1.0, 0.55 + 0.45 * sin(row * 3.14159), strength);\n  // the aperture grille, one stripe of each colour every three screen pixels\n  float m = mod(gl_FragCoord.x, 3.0);\n  vec3 grille = vec3(m < 1.0 ? 1.0 : 0.72, m >= 1.0 && m < 2.0 ? 1.0 : 0.72, m >= 2.0 ? 1.0 : 0.72);\n  c *= mix(vec3(1.0), grille, 0.55 * strength);\n  // the corners falling off, a faint flicker, the brightness the dark rows took back\n  vec2 d = q - 0.5;\n  c *= 1.0 - strength * dot(d, d) * 1.1;\n  c *= 1.0 + strength * (0.012 * sin(time * 60.0) + 0.18);\n  gl_FragColor = vec4(c, 1.0);\n}\n", ja = class extends S {
	constructor(e, t, n, r) {
		super(), this.gl = e, this.size = t, this.time = n, this.strength = r;
	}
};
function Ma(e) {
	let t = e.getContext("webgl", {
		antialias: !1,
		premultipliedAlpha: !1
	});
	if (t != null) {
		let e = x(t), n = (t, n) => {
			let r = e.createShader(t);
			if (e.shaderSource(r, n), e.compileShader(r), e.getShaderParameter(r, 35713)) return ve(r);
			console.warn("crt: " + e.getShaderInfoLog(r));
		}, r = n(35633, ka), i = n(35632, Aa), a, o, s;
		switch (r == null || i == null ? a = 1 : (a = 0, o = x(i), s = x(r)), a) {
			case 0: {
				let t = e.createProgram();
				if (e.attachShader(t, s), e.attachShader(t, o), e.linkProgram(t), e.getProgramParameter(t, 35714)) {
					e.useProgram(t), e.bindBuffer(34962, e.createBuffer()), e.bufferData(34962, new Float32Array([
						-1,
						-1,
						1,
						-1,
						-1,
						1,
						1,
						1
					]), 35044);
					let n = e.getAttribLocation(t, "corner") | 0;
					e.enableVertexAttribArray(n), e.vertexAttribPointer(n, 2, 5126, !1, 0, 0), e.bindTexture(3553, e.createTexture());
					let r = [
						[10241, 9728],
						[10240, 9728],
						[10242, 33071],
						[10243, 33071]
					];
					for (let t = 0; t <= r.length - 1; t++) {
						let n = L(t, r);
						e.texParameteri(3553, n[0], n[1]);
					}
					return new ja(e, e.getUniformLocation(t, "size"), e.getUniformLocation(t, "time"), e.getUniformLocation(t, "strength"));
				}
				return;
			}
			default: return;
		}
	} else return;
}
function Na(e, t, n, r, i, a) {
	e.gl.viewport(0, 0, n, r), e.gl.texImage2D(3553, 0, 6408, 6408, 5121, t), e.gl.uniform2f(e.size, t.width, t.height), e.gl.uniform1f(e.time, i), e.gl.uniform1f(e.strength, a), e.gl.drawArrays(5, 0, 4);
}
// ../../../toolchain-logo/art/src/ConsoleView.fs
var Pa = class extends S {
	constructor(e, t) {
		super(), this.replay = e, this.tube = t;
	}
};
function Fa(e, t, n, r, i) {
	let a = Sa(), o = Yi(n, r), s = document.createElement("div");
	s.className = "fsdsp56k-console", e.appendChild(s);
	let c = document.createElement("canvas"), l = document.createElement("canvas");
	c.className = "picture", l.className = "screen", s.style.aspectRatio = `${o[0]} / ${o[1]}`, s.appendChild(c), s.appendChild(l);
	let u = Ma(l), d = new C(a ? Hi(zi(i)) : zi(i)), f = new C(ia), p = new C(u != null), m = performance.now(), h = () => {
		let e = (performance.now() - m) / 1e3;
		xa(c, ga(f.contents, Xi(ba, ya, t, n, r, d.contents, a || Bi(d.contents) ? !0 : e % 1.06 < .53)));
		let i, o;
		switch (u == null ? i = 1 : p.contents ? (i = 0, o = u) : i = 1, i) {
			case 0: {
				let t = s.getBoundingClientRect(), n = window.devicePixelRatio || 1, r = Math.floor(t.width * n), i = Math.floor(t.height * n);
				l.width !== r && (l.width = r), l.height !== i && (l.height = i), Na(o, c, r, i, e, a ? .85 : 1);
				break;
			}
		}
		l.style.visibility = p.contents && u != null ? "visible" : "hidden";
	}, g = new C(0), _ = new C(0), ee = (e) => {
		let t = P(0, F(.05, (e - _.contents) / 1e3));
		_.contents = e, d.contents = Vi(t, d.contents), h(), g.contents = Bi(d.contents) ? window.requestAnimationFrame(ee) : 0;
	}, te = () => {
		!a && g.contents === 0 && Bi(d.contents) && (_.contents = performance.now(), g.contents = window.requestAnimationFrame(ee));
	};
	return a || window.setInterval(() => {
		g.contents === 0 && h();
	}, 530), Oa(e, (e) => {
		f.contents = e, h();
	}), new ResizeObserver(h).observe(s), te(), new Pa(() => {
		d.contents = a ? Hi(zi(i)) : zi(i), h(), te();
	}, (e) => {
		p.contents = e && u != null, h();
	});
}
// Entry.fs
function Ia(e) {
	return en(e.command) ? en(e.output) ? new Pi(2, [e.pause]) : new Pi(1, [Se(e.output)]) : new Pi(0, [Se(e.command)]);
}
function La(e, t, n, r, i) {
	Da(e, fa.name);
	let a = Fa(e, t, n, r, I(Ia, i));
	return {
		replay: () => {
			a.replay();
		},
		tube: (e) => {
			a.tube(e);
		}
	};
}
window.fsdsp56kConsole = { mount: La }, window.dispatchEvent(new CustomEvent("fsdsp56k-console"));
