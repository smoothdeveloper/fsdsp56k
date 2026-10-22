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
function b(e, t) {
	return e === t ? 0 : e < t ? -1 : e > t ? 1 : Number.isNaN(e) ? Number.isNaN(t) ? 0 : -1 : 1;
}
function le(e, t, n) {
	if (e == null) return t == null ? 0 : 1;
	if (t == null) return -1;
	if (e.length !== t.length) return e.length < t.length ? -1 : 1;
	for (let r = 0, i = 0; r < e.length; r++) if (i = n(e[r], t[r]), i !== 0) return i;
	return 0;
}
function ue(e, t) {
	return le(e, t, x);
}
function de(e, t) {
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return n.length < r.length ? -1 : 1;
	n.sort(), r.sort();
	for (let i = 0, a = 0; i < n.length; i++) {
		let o = n[i];
		if (o !== r[i]) return o < r[i] ? -1 : 1;
		if (a = x(e[o], t[o]), a !== 0) return a;
	}
	return 0;
}
function x(e, t) {
	return e === t ? 0 : e == null ? t == null ? 0 : -1 : t == null ? 1 : i(e) ? e.CompareTo(t) : n(e) ? n(t) ? ue(e, t) : -1 : typeof e == "object" ? e instanceof Date ? t instanceof Date ? ce(e, t) : -1 : Object.getPrototypeOf(e)?.constructor === Object ? de(e, t) : -1 : b(e, t);
}
function fe(e, t = 0) {
	let n = 10 ** t, r = +(t ? e * n : e).toFixed(8), i = Math.floor(r), a = r - i, o = a > .49999999 && a < .50000001 ? i % 2 == 0 ? i : i + 1 : Math.round(r);
	return t ? o / n : o;
}
function pe(e) {
	return e > 0 ? 1 : e < 0 ? -1 : 0;
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Option.js
var me = class e {
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
		return t == null ? 1 : x(this.value, t instanceof e ? t.value : t);
	}
};
function S(e) {
	if (e == null) throw new t("Option has no value");
	return e instanceof me ? e.value : e;
}
function he(e) {
	return e == null || e instanceof me ? new me(e) : e;
}
function ge(e, t) {
	return e == null ? t : S(e);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Types.js
function _e(e) {
	let t = 0, n = "[";
	for (let r of e) {
		if (t === 0) n += ve(r);
		else if (t === 100) {
			n += "; ...";
			break;
		} else n += "; " + ve(r);
		t++;
	}
	return n + "]";
}
function ve(e, t = 0) {
	return typeof e == "string" ? "\"" + e + "\"" : ye(e, t);
}
function ye(e, t = 0) {
	if (typeof e == "object" && e) {
		if (typeof e.toString == "function" && e.toString !== Object.prototype.toString) return e.toString();
		if (Symbol.iterator in e) return _e(e);
		{
			let n = Object.getPrototypeOf(e)?.constructor;
			return n === Object && t < 10 ? "{ " + Object.entries(e).map(([e, n]) => e + " = " + ve(n, t + 1)).join("\n  ") + " }" : n?.name ?? "";
		}
	}
	return String(e);
}
function be(e, t) {
	if (t.length === 0) return e;
	{
		let n, r = !0;
		return t.length === 1 ? (n = ve(t[0]), r = n.indexOf(" ") >= 0) : n = t.map((e) => ve(e)).join(", "), e + (r ? " (" : " ") + n + (r ? ")" : "");
	}
}
var xe = class {
	get name() {
		return this.cases()[this.tag];
	}
	toJSON() {
		return this.fields.length === 0 ? this.name : [this.name].concat(this.fields);
	}
	toString() {
		return be(this.name, this.fields);
	}
	GetHashCode() {
		let e = this.fields.map((e) => v(e));
		return e.splice(0, 0, ee(this.tag)), ne(e);
	}
	Equals(e) {
		return this === e ? !0 : u(this, e) ? this.tag === e.tag && oe(this.fields, e.fields) : !1;
	}
	CompareTo(e) {
		return this === e ? 0 : u(this, e) ? this.tag === e.tag ? ue(this.fields, e.fields) : this.tag < e.tag ? -1 : 1 : -1;
	}
};
function Se(e) {
	let t = {}, n = Object.keys(e);
	for (let r = 0; r < n.length; r++) t[n[r]] = e[n[r]];
	return t;
}
function Ce(e) {
	return "{ " + Object.entries(e).map(([e, t]) => e + " = " + ve(t)).join("\n  ") + " }";
}
function we(e) {
	return ne(Object.values(e).map((e) => v(e)));
}
function Te(e, t) {
	if (e === t) return !0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) if (!y(e[n[r]], t[n[r]])) return !1;
		return !0;
	}
	return !1;
}
function Ee(e, t) {
	if (e === t) return 0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) {
			let i = x(e[n[r]], t[n[r]]);
			if (i !== 0) return i;
		}
		return 0;
	}
	return -1;
}
var C = class {
	toJSON() {
		return Se(this);
	}
	toString() {
		return Ce(this);
	}
	GetHashCode() {
		return we(this);
	}
	Equals(e) {
		return Te(this, e);
	}
	CompareTo(e) {
		return Ee(this, e);
	}
}, w = class {
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
}, De = Symbol("numeric");
function Oe(e) {
	return typeof e == "number" || typeof e == "bigint" || e?.[De];
}
function ke(e) {
	return typeof e == "number" && Number.isInteger(e) || typeof e == "bigint";
}
function Ae(e, t) {
	return typeof e == "number" || typeof e == "bigint" ? e < t ? -1 : +(e > t) : e.CompareTo(t);
}
function je(e, t) {
	return typeof e == "number" ? e * t : typeof e == "bigint" ? e * BigInt(t) : e[De]().multiply(t);
}
function Me(e, t) {
	return typeof e == "number" ? e / t : typeof e == "bigint" ? Number(e) / t : e[De]().multiply(1 / t);
}
function Ne(e, t) {
	return typeof e == "number" ? e.toFixed(t) : typeof e == "bigint" ? e.toString() : e[De]().toFixed(t);
}
function Pe(e, t) {
	return typeof e == "number" ? e.toPrecision(t) : typeof e == "bigint" ? e.toString() : e[De]().toPrecision(t);
}
function Fe(e, t) {
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
function Ie(e, t) {
	return typeof e == "number" ? e.toExponential(t) : typeof e == "bigint" ? Fe(e, t) : e[De]().toExponential(t);
}
function Le(e) {
	return typeof e == "number" ? (Number(e) >>> 0).toString(16) : typeof e == "bigint" ? BigInt.asUintN(64, e).toString(16) : e[De]().toHex();
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Global.js
var Re = "Collection was empty.", ze = "Value cannot be null.", Be = " (Parameter '", Ve = "The given key was not present in the dictionary.", He = [
	"Sun",
	"Mon",
	"Tue",
	"Wed",
	"Thu",
	"Fri",
	"Sat"
], Ue = [
	"Sunday",
	"Monday",
	"Tuesday",
	"Wednesday",
	"Thursday",
	"Friday",
	"Saturday"
], We = [
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
], Ge = [
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
function Ke(e, t) {
	return t >= e.length - 1 ? -1 : e.charCodeAt(t + 1);
}
function qe(e, n) {
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
function Je(n, r, i) {
	let a = 0, o = 0, s = "", c = i ? lt(n.getTime(), e.Utc) : n;
	for (; a < r.length;) {
		let n = r[a];
		switch (n) {
			case "d":
				switch (o = T(r, a, "d"), a += o, o) {
					case 1:
						s += O(c);
						break;
					case 2:
						s += m(O(c), 2);
						break;
					case 3:
						s += He[ht(c)];
						break;
					default: s += Ue[ht(c)];
				}
				break;
			case "f":
				if (o = T(r, a, "f"), a += o, o <= 3) {
					let e = 10 ** (3 - o);
					s += m(Math.floor(ft(c) / e), o);
				} else if (o <= 7) s += ("" + ft(c)).padEnd(o, "0");
				else throw "Input string was not in a correct format.";
				break;
			case "F":
				if (o = T(r, a, "F"), a += o, o <= 3) {
					let e = 10 ** (3 - o), t = Math.floor(ft(c) / e);
					t != 0 && (s += m(t, o));
				} else if (o <= 7) {
					let e = ft(c);
					e != 0 && (s += m(e, 3));
				} else throw "Input string was not in a correct format.";
				break;
			case "g":
				o = T(r, a, "g"), a += o, s += "A.D.";
				break;
			case "h":
				switch (o = T(r, a, "h"), a += o, o) {
					case 1:
						let e = k(c) % 12;
						s += e || 12;
						break;
					default:
						let t = k(c) % 12;
						s += m(t || 12, 2);
				}
				break;
			case "H":
				switch (o = T(r, a, "H"), a += o, o) {
					case 1:
						s += k(c);
						break;
					default: s += m(k(c), 2);
				}
				break;
			case "K":
				if (o = T(r, a, "K"), a += o, o === 1) switch (Ye(c)) {
					case e.Utc:
						s += "Z";
						break;
					case e.Local:
						s += Xe(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified:
				}
				break;
			case "m":
				switch (o = T(r, a, "m"), a += o, o) {
					case 1:
						s += pt(c);
						break;
					default: s += m(pt(c), 2);
				}
				break;
			case "M":
				switch (o = T(r, a, "M"), a += o, o) {
					case 1:
						s += A(c);
						break;
					case 2:
						s += m(A(c), 2);
						break;
					case 3:
						s += We[A(c) - 1];
						break;
					default: s += Ge[A(c) - 1];
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
						s += j(c) % 100;
						break;
					case 2:
						s += m(j(c) % 100, 2);
						break;
					default: s += m(j(c), o);
				}
				break;
			case "z":
				o = T(r, a, "z"), a += o;
				let l = "";
				switch (Ye(c)) {
					case e.Utc:
						l = "+00:00";
						break;
					case e.Local:
						l = Xe(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified: l = Xe(dt(c).getTimezoneOffset() * -6e4);
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
				let [p, h] = qe(r, a);
				s += p, a += h;
				break;
			case "%":
				let g = Ke(r, a);
				if (g >= 0 && g !== 37) a += 2, s += Je(c, String.fromCharCode(g), i);
				else throw new t("Invalid format string");
				break;
			case "\\":
				let _ = Ke(r, a);
				if (_ >= 0) a += 2, s += String.fromCharCode(_);
				else throw new t("Invalid format string");
				break;
			default: a++, s += n;
		}
	}
	return s;
}
function Ye(t) {
	return t.kind ?? e.Unspecified;
}
function Xe(e) {
	let t = e < 0;
	e = Math.abs(e);
	let n = ~~(e / 36e5), r = e % 36e5 / 6e4;
	return (t ? "-" : "+") + m(n, 2) + ":" + m(r, 2);
}
function Ze(t, n) {
	if (n) return t.toISOString();
	{
		let n = t.kind == null || t.kind === e.Local;
		return m(t.getFullYear(), 4) + "-" + m(t.getMonth() + 1, 2) + "-" + m(t.getDate(), 2) + "T" + m(t.getHours(), 2) + ":" + m(t.getMinutes(), 2) + ":" + m(t.getSeconds(), 2) + "." + m(t.getMilliseconds(), 3) + (n ? Xe(t.getTimezoneOffset() * -6e4) : "");
	}
}
function Qe(e, t) {
	let n = e.toISOString();
	return n.substring(0, n.length - 1) + Xe(t);
}
function $e(n, r) {
	let i = new Date(n.getTime() + (n.offset ?? 0));
	if (typeof r != "string") return i.toISOString().replace(/\.\d+/, "").replace(/[A-Z]|\.\d+/g, " ") + Xe(n.offset ?? 0);
	if (r.length === 1) switch (r) {
		case "D": return E(i);
		case "d": return et(i);
		case "F": return E(i) + " " + D(i);
		case "f": return E(i) + " " + tt(i);
		case "G": return et(i) + " " + D(i);
		case "g": return et(i) + " " + tt(i);
		case "M":
		case "m": return at(i);
		case "O":
		case "o": return Qe(i, n.offset ?? 0);
		case "R":
		case "r": return nt(lt(n.getTime(), e.Utc));
		case "s": return rt(ut(i));
		case "T": return D(ut(i));
		case "t": return tt(ut(i));
		case "u": return it(lt(n.getTime(), e.Utc));
		case "U": {
			let t = lt(n.getTime(), e.Utc);
			return E(t) + " " + D(t);
		}
		case "Y":
		case "y": return ot(i);
		default: throw new t("Unrecognized Date print format");
	}
	else return Je(i, r, !0);
}
function E(e) {
	return Ue[ht(e)] + ", " + m(O(e), 2) + " " + Ge[A(e) - 1] + " " + j(e);
}
function et(e) {
	return m(A(e), 2) + "/" + m(O(e), 2) + "/" + j(e);
}
function D(e) {
	return m(k(e), 2) + ":" + m(pt(e), 2) + ":" + m(mt(e), 2);
}
function tt(e) {
	return m(k(e), 2) + ":" + m(pt(e), 2);
}
function nt(e) {
	let t = ut(e);
	return He[ht(t)] + ", " + m(O(t), 2) + " " + We[A(t) - 1] + " " + j(t) + " " + m(k(t), 2) + ":" + m(pt(t), 2) + ":" + m(mt(t), 2) + " GMT";
}
function rt(e) {
	return m(j(e), 4) + "-" + m(A(e), 2) + "-" + m(O(e), 2) + "T" + m(k(e), 2) + ":" + m(pt(e), 2) + ":" + m(mt(e), 2);
}
function it(e) {
	let t = ut(e);
	return m(j(t), 4) + "-" + m(A(t), 2) + "-" + m(O(t), 2) + " " + m(k(t), 2) + ":" + m(pt(t), 2) + ":" + m(mt(t), 2) + "Z";
}
function at(e) {
	return Ge[A(e) - 1] + " " + m(O(e), 2);
}
function ot(e) {
	return j(e) + " " + Ge[A(e) - 1];
}
function st(n, r) {
	let i = n.kind === e.Utc;
	if (typeof r != "string") return et(n) + " " + D(n);
	if (r.length === 1) switch (r) {
		case "D": return E(n);
		case "d": return et(n);
		case "F": return E(n) + " " + D(n);
		case "f": return E(n) + " " + tt(n);
		case "G": return et(n) + " " + D(n);
		case "g": return et(n) + " " + tt(n);
		case "M":
		case "m": return at(n);
		case "O":
		case "o": return Ze(n, i);
		case "R":
		case "r": return nt(n);
		case "s": return rt(n);
		case "T": return D(n);
		case "t": return tt(n);
		case "u": return it(n);
		case "U": return E(ut(n)) + " " + D(ut(n));
		case "Y":
		case "y": return ot(n);
		default: throw new t("Unrecognized Date print format");
	}
	else return Je(n, r, i);
}
function ct(e, t, n) {
	return e.offset == null ? st(e, t) : $e(e, t);
}
function lt(t, n) {
	let r = new Date(t);
	return r.kind = n ?? e.Unspecified, r;
}
function ut(t) {
	return t.kind === e.Utc ? t : lt(t.getTime(), e.Utc);
}
function dt(t) {
	return t.kind === e.Local ? t : lt(t.getTime(), e.Local);
}
function O(t) {
	return t.kind === e.Utc ? t.getUTCDate() : t.getDate();
}
function k(t) {
	return t.kind === e.Utc ? t.getUTCHours() : t.getHours();
}
function ft(t) {
	return t.kind === e.Utc ? t.getUTCMilliseconds() : t.getMilliseconds();
}
function pt(t) {
	return t.kind === e.Utc ? t.getUTCMinutes() : t.getMinutes();
}
function A(t) {
	return (t.kind === e.Utc ? t.getUTCMonth() : t.getMonth()) + 1;
}
function mt(t) {
	return t.kind === e.Utc ? t.getUTCSeconds() : t.getSeconds();
}
function j(t) {
	return t.kind === e.Utc ? t.getUTCFullYear() : t.getFullYear();
}
function ht(t) {
	return t.kind === e.Utc ? t.getUTCDay() : t.getDay();
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/RegExp.js
function gt(e, n = 0) {
	if (n & -284) throw new t("RegexOptions only supports: IgnoreCase, Multiline, Compiled, Singleline and ECMAScript");
	let r = "gu";
	return r += n & 1 ? "i" : "", r += n & 2 ? "m" : "", r += n & 16 ? "s" : "", new RegExp(e, r);
}
function _t(e) {
	return e.replace(/[$()*+.?[\\\^{|}\]]/g, "\\$&");
}
function vt(e, t, n, r, i = 0) {
	function a() {
		let e = arguments[0];
		if (r) {
			r--;
			let t = [], i = arguments.length, a = typeof arguments[i - 1] != "string", o = a ? i - 3 : i - 2;
			for (let e = 0; e < o; e++) t.push(arguments[e]);
			t.index = arguments[o++], t.input = arguments[o++], a && (t.groups = arguments[o]), e = n(t);
		}
		return e;
	}
	if (typeof e == "string") {
		let n = e;
		e = gt(t, r ?? 0), t = n, r = void 0;
	}
	if (typeof n == "string") {
		let e = n;
		n = (t) => e.replace(/\$(?:(\$)|(&)|(`)|(')|(\d+)|\{(\d+)\}|\{([^}]+)\})/g, (e, n, r, i, a, o, s, c) => {
			if (n != null) return "$";
			if (r != null) return t[0];
			if (i != null) return t.input.substring(0, t.index);
			if (a != null) return t.input.substring(t.index + t[0].length);
			let l = o ?? s;
			if (l != null) {
				let n = parseInt(l, 10);
				return n < t.length ? t[n] ?? "" : e;
			}
			return t.groups != null && c in t.groups ? t.groups[c] ?? "" : e;
		});
	}
	return r ??= -1, t.substring(0, i) + t.substring(i).replace(e, a);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/String.js
var yt = Symbol.for("Fable.DateTimeFormattable");
function bt(e) {
	return e != null && typeof e[yt] == "function";
}
function xt(e, t) {
	return e instanceof Date ? ct(e, t) : e[yt](t);
}
var St = /\{(\d+)(,-?\d+)?(?:\:([a-zA-Z])(\d{0,2})|\:(.+?))?\}/g;
function Ct(e, t) {
	return Ae(e, t) < 0;
}
function wt(e) {
	let [t, n] = e.split(".");
	return n === void 0 && (n = ""), {
		integral: t,
		decimal: n
	};
}
function Tt(e) {
	return e.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}
function Et(e, ...n) {
	let r;
	return typeof e == "object" ? (r = String(n[0]), n.shift()) : r = e, r.replace(St, (e, r, i, a, o, s) => {
		if (r < 0 || r >= n.length) throw new t("Index must be greater or equal to zero and less than the arguments' length.");
		let c = n[r], l;
		if (Oe(c)) switch (o = o == "" ? null : parseInt(o, 10), a) {
			case "b":
			case "B":
				if (!ke(c)) throw new t("Format specifier was invalid.");
				c = (c >>> 0).toString(2).replace(/^0+/, "").padStart(o || 1, "0");
				break;
			case "c":
			case "C":
				let e = Ct(c, 0);
				Ct(c, 0) && (c = je(c, -1)), o ??= 2, c = Ne(c, o), l = wt(c), c = o > 0 ? "¤" + Tt(l.integral) + "." + jt(l.decimal, o, "0") : "¤" + Tt(l.integral), e && (c = "(" + c + ")");
				break;
			case "d":
			case "D":
				if (!ke(c)) throw new t("Format specifier was invalid.");
				c = String(c), o != null && (c = c.startsWith("-") ? "-" + At(c.substring(1), o, "0") : At(c, o, "0"));
				break;
			case "e":
			case "E": {
				o ??= 6, c = String(Ie(c, o));
				let e = c.indexOf("e");
				c = c.slice(0, e) + a + c[e + 1] + At(c.slice(e + 2), 3, "0");
				break;
			}
			case "f":
			case "F":
				o ??= 2, c = Ne(c, o), o > 0 && (l = wt(c), c = l.integral + "." + jt(l.decimal, o, "0"));
				break;
			case "g":
			case "G": {
				c = o == null ? Pe(c) : Pe(c, o);
				let e = c.indexOf("e");
				if (e >= 0) {
					let t = Pt(Pt(c.slice(0, e), "0"), "."), n = c[e + 1], r = c.slice(e + 2), i = r.length < 2 ? "0" + r : r;
					c = t + (a === "G" ? "E" : "e") + n + i;
				} else c.indexOf(".") >= 0 && (c = Pt(Pt(c, "0"), "."));
				break;
			}
			case "n":
			case "N":
				o ??= 2, c = Ne(c, o), l = wt(c), c = o > 0 ? Tt(l.integral) + "." + jt(l.decimal, o, "0") : Tt(l.integral);
				break;
			case "p":
			case "P":
				o ??= 2, c = Ne(je(c, 100), o), l = wt(c), c = o > 0 ? Tt(l.integral) + "." + jt(l.decimal, o, "0") + " %" : Tt(l.integral) + " %";
				break;
			case "r":
			case "R": throw new t("The round-trip format is not supported by Fable");
			case "x":
			case "X":
				if (!ke(c)) throw new t("Format specifier was invalid.");
				o ??= 1, c = At(Le(c), o, "0"), a === "X" && (c = c.toUpperCase());
				break;
			default:
				if (a) throw new t("Format specifier was invalid.");
				if (s) {
					let e = "", t = s, n = (t.match(/%/g) ?? []).length;
					n > 0 && (c = je(c, 100 ** n)), c = t.replace(/([0#,]+)(\.[0#]+)?(,*)/, (t, n, r) => {
						let i = /,*$/.exec(n)[0].length;
						i > 0 && (n = n.substring(0, n.length - i), c = Me(c, 1e3 ** i)), Ct(c, 0) && (c = je(c, -1), e = "-"), r = r == null ? "" : r.substring(1), c = Ne(c, Math.max(r.length, 0));
						let [a, o] = c.split(".");
						o ||= "";
						let s = n.replace(/,/g, "").replace(/^#+/, "").length;
						a = At(a, s, "0");
						let l = r.replace(/#+$/, "").length;
						if (l > o.length ? o = jt(o, l, "0") : l < o.length && (o = o.substring(0, l) + o.substring(l).replace(/0+$/, "")), n.indexOf(",") > 0) {
							let e = a.length % 3, t = Math.floor(a.length / 3), n = e > 0 ? a.substr(0, e) + (t > 0 ? "," : "") : "";
							for (let r = 0; r < t; r++) n += a.substr(e + r * 3, 3) + (r < t - 1 ? "," : "");
							a = n;
						}
						return o.length > 0 ? a + "." + o : a;
					}), c = e + c;
				}
		}
		else c = c instanceof Date || bt(c) ? xt(c, s || a) : ye(c);
		return i = parseInt((i || " ").substring(1), 10), isNaN(i) || (c = kt(String(c), Math.abs(i), " ", i < 0)), c;
	});
}
function Dt(e) {
	return typeof e != "string" || e.length === 0;
}
function Ot(e, t) {
	return Array.isArray(t) ? t.join(e) : Array.from(t).join(e);
}
function kt(e, t, n, r) {
	n ||= " ", t -= e.length;
	for (let i = 0; i < t; i++) e = r ? e + n : n + e;
	return e;
}
function At(e, t, n) {
	return kt(e, t, n);
}
function jt(e, t, n) {
	return kt(e, t, n, !0);
}
function Mt(e, t, n) {
	return e.replace(new RegExp(_t(t), "g"), n);
}
function Nt(e, n, r, i) {
	if (r = typeof r == "number" ? r : void 0, i = typeof i == "number" ? i : 0, r && r < 0) throw new t("Count cannot be less than zero");
	if (r === 0) return [];
	let a = (i & 1) == 1, o = (i & 2) == 2;
	n ||= [], n = n.filter((e) => e).map(_t), n = n.length > 0 ? n : ["\\s"];
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
function Pt(e, ...t) {
	return t.length === 0 ? e.trimEnd() : e.replace(RegExp("[" + _t(t.join("")) + "]+$"), "");
}
function Ft(e, n, r) {
	if (n + (r || 0) > e.length) throw new t("Invalid startIndex and/or length");
	return r == null ? e.substr(n) : e.substr(n, r);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/System.js
var It = class extends t {
	constructor(e) {
		super(e);
	}
};
function Lt(e) {
	return new It(e);
}
var Rt = class extends t {
	constructor(e) {
		super(e);
	}
};
function zt(e) {
	return new Rt(e);
}
var Bt = class extends t {
	constructor(e, t, n) {
		super(Dt(t) ? e : e + Be + t + "')", n), this.paramName = t;
	}
};
function Vt(e, t, n) {
	return new Bt(e, t, n);
}
function Ht(e) {
	return Vt(e, "", l());
}
var Ut = class extends Bt {
	constructor(e, t) {
		super(t, e, l());
	}
};
function Wt(e, t) {
	return new Ut(e, t);
}
function Gt(e) {
	return Wt(e, ze);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Native.js
function Kt(e, t) {
	if (e == null) return Array(t);
	{
		let n = e;
		return typeof n == "function" ? new n(t) : Array(t);
	}
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Double.js
function M(e, t) {
	return Math.max(e, t);
}
function N(e, t) {
	return Math.min(e, t);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Array.js
function qt(e, t, n) {
	let r = e.length | 0, i = t.length | 0, a = Kt(n, r + i);
	for (let t = 0; t <= r - 1; t++) I(a, t, F(t, e));
	for (let e = 0; e <= i - 1; e++) I(a, e + r, F(e, t));
	return a;
}
function Jt(e, t, n, r) {
	let i = t | 0;
	return e.fill(r, i, i + n);
}
function P(e, t, n) {
	let r = t.length | 0, i = Kt(n, r);
	for (let n = 0; n <= r - 1; n++) I(i, n, e(F(n, t)));
	return i;
}
function Yt(e, t) {
	let n = M(0, e) | 0;
	return t.slice(0, 0 + n);
}
function Xt(e, t) {
	let n = Array.isArray(e) ? e : Array.from(e);
	switch (n.length | 0) {
		case 0: return Kt(t, 0);
		case 1: return F(0, n);
		default: {
			let e = 0, r = 0;
			for (let e = 0; e <= n.length - 1; e++) {
				let t = F(e, n);
				r = r + t.length | 0;
			}
			let i = Kt(t, r);
			for (let t = 0; t <= n.length - 1; t++) {
				let r = F(t, n);
				for (let t = 0; t <= r.length - 1; t++) I(i, e, F(t, r)), e = e + 1 | 0;
			}
			return i;
		}
	}
}
function Zt(e, t, n) {
	return Xt(P(e, t, l()), n);
}
function Qt(e, t) {
	let n = Kt(t, 1);
	return I(n, 0, e), n;
}
function $t(e, n, r) {
	if (e < 0) throw new t("The input must be non-negative (Parameter 'count')");
	let i = Kt(r, e);
	for (let t = 0; t <= e - 1; t++) I(i, t, n(t));
	return i;
}
function en(e) {
	if (e.length < 2) return [];
	{
		let t = e.length - 1 | 0, n = Array(t);
		for (let r = 0; r <= t - 1; r++) I(n, r, [F(r, e), F(r + 1, e)]);
		return n;
	}
}
function tn(e) {
	return e.slice().reverse();
}
function nn(e, t) {
	return t.find(e);
}
function rn(e, t) {
	let n = t.findIndex(e) | 0;
	if (n > -1) return n;
}
function an(e, t, n) {
	let r = e;
	return n.reduce(r, t);
}
function F(e, n) {
	if (e < 0 || e >= n.length) throw new t("Index was outside the bounds of the array. (Parameter 'index')");
	return n[e];
}
function I(e, n, r) {
	if (n < 0 || n >= e.length) throw new t("Index was outside the bounds of the array. (Parameter 'index')");
	e[n] = r;
}
function on(e, n) {
	if (n.length === 0) throw new t("The input array was empty");
	let r = e;
	return n.reduce(r);
}
function sn(e, t) {
	return on((e, n) => t.Compare(n, e) > 0 ? n : e, e);
}
function cn(e, t) {
	return on((e, n) => t.Compare(n, e) > 0 ? e : n, e);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/FSharp.Core.js
function ln(e) {
	return !!y(e, l());
}
function un(e, t) {
	if (y(t, l())) throw Gt(e);
	return t;
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/List.js
var dn = class e extends C {
	constructor(e, t) {
		super(), this.head = e, this.tail = t;
	}
	toString() {
		let e = this, t = "[", n = !0, r = f(e);
		try {
			for (; r["System.Collections.IEnumerator.MoveNext"]();) {
				let e, i, a, o = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
				t = (n ? t : t + "; ") + (e = o, i = e, typeof i == "string" ? (a = i, "\"" + a + "\"") : ye(e)), n = !1;
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
						let o = S(i), s = S(a);
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
					let s = S(o);
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
						let o = S(i), s = S(a), c = x(n.head, r.head) | 0;
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
		return pn(this);
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return f(this);
	}
}, fn = class {
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
			let n = S(t);
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
function pn(e) {
	return new fn(e);
}
function mn() {
	return new dn(l(), void 0);
}
function hn(e, t) {
	return new dn(e, t);
}
function gn(e) {
	return e.tail == null;
}
function _n(e) {
	return ((e, t) => {
		loop: for (;;) {
			let n = e, r = t.tail;
			if (r != null) {
				e = n + 1, t = S(r);
				continue loop;
			}
			return n | 0;
		}
	})(0, e) | 0;
}
function vn(e) {
	if (e.tail != null) return e.head;
	throw new t(Re + " (Parameter 'list')");
}
function yn(e) {
	let n = e.tail;
	if (n != null) return S(n);
	throw new t(Re + " (Parameter 'list')");
}
function bn() {
	return mn();
}
function xn(e, t) {
	return hn(e, t);
}
function Sn(e) {
	return hn(e, mn());
}
function Cn(e) {
	return gn(e);
}
function L(e) {
	return vn(e);
}
function wn(e) {
	return yn(e);
}
function Tn(e) {
	let t = _n(e) | 0, n = Jt(Array(t), 0, t, null);
	return ((e, t) => {
		loop: for (;;) {
			let r = e, i = t;
			if (!gn(i)) {
				I(n, r, vn(i)), e = r + 1, t = yn(i);
				continue loop;
			}
			break;
		}
	})(0, e), n;
}
function En(e, t, n) {
	let r = t, i = n;
	for (; !gn(i);) r = e(r, L(i)), i = yn(i);
	return r;
}
function Dn(e, t) {
	let n = t;
	for (let t = e.length - 1; t >= 0; t--) n = hn(F(t, e), n);
	return n;
}
function On(e) {
	return Dn(e, mn());
}
function kn(e) {
	if (n(e)) return On(e);
	if (e instanceof dn) return e;
	{
		let t = mn(), n = t, r = f(e);
		try {
			for (; r["System.Collections.IEnumerator.MoveNext"]();) {
				let e, t, i = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
				n = (e = n, t = new dn(i, void 0), e.tail = t, t);
			}
		} finally {
			c(r);
		}
		let i = n;
		return i.tail = mn(), yn(t);
	}
}
function An(e, t) {
	return ((t) => {
		loop: for (;;) {
			let n = t;
			if (gn(n)) return;
			{
				let r = e(vn(n));
				if (r == null) {
					t = yn(n);
					continue loop;
				}
				return r;
			}
		}
	})(t);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Seq.js
var jn = "Enumeration already finished.", Mn = "Enumeration has not started. Call MoveNext.", Nn = "Reset is not supported on this enumerator.";
function Pn() {
	throw zt(Nn);
}
function Fn() {
	throw Lt(Mn);
}
function In() {
	throw Lt(jn);
}
var Ln = class {
	constructor(e) {
		this.f = e;
	}
	toString() {
		let e = this, t = 0, n = "seq [", r = f(e);
		try {
			for (; t < 4 && r["System.Collections.IEnumerator.MoveNext"]();) t > 0 && (n += "; "), n += ye(r["System.Collections.Generic.IEnumerator`1.get_Current"]()), t = t + 1 | 0;
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
function Rn(e) {
	return new Ln(e);
}
var zn = class {
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
		Pn();
	}
	Dispose() {
		this.dispose();
	}
};
function Bn(e, t, n) {
	return new zn(e, t, n);
}
function Vn(e) {
	let t, n, r = !1, i = !1, a, o = () => {
		if (i = !0, n != null) {
			let e = S(n);
			try {
				c(e);
			} finally {
				n = void 0;
			}
		}
		if (t != null) {
			let e = S(t);
			try {
				c(e);
			} finally {
				t = void 0;
			}
		}
	};
	return Bn(() => (r ? i && In() : Fn(), a == null ? In() : S(a)), () => {
		if (r ||= !0, i) return !1;
		{
			let r;
			for (; r == null;) {
				let i, s = t, l = n;
				if (s != null) {
					if (l != null) {
						let e = S(l);
						if (e["System.Collections.IEnumerator.MoveNext"]()) a = he(e["System.Collections.Generic.IEnumerator`1.get_Current"]()), r = !0;
						else try {
							c(e);
						} finally {
							n = void 0;
						}
					} else {
						let e = S(s);
						e["System.Collections.IEnumerator.MoveNext"]() ? n = (i = e["System.Collections.Generic.IEnumerator`1.get_Current"](), f(i)) : (o(), r = !1);
					}
				} else t = f(e);
			}
			return S(r);
		}
	}, () => {
		i || o();
	});
}
function Hn(e, t, n) {
	let r = !1, i, a = he(e()), o = () => {
		if (a != null) {
			let e = S(a);
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
	return Bn(() => (r || Fn(), i == null ? In() : S(i)), () => {
		if (r ||= !0, a != null) {
			let e = S(a), n;
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
function Un(e, t) {
	let n, r = t;
	return Bn(() => {
		if (n != null) {
			let e = S(n)[0];
			return S(n)[1], e;
		}
		return Fn();
	}, () => (n = e(r), n != null && (S(n)[0], r = S(n)[1], !0)), () => {});
}
function Wn(e) {
	return Rn(e);
}
function Gn(e) {
	return f(un("source", e));
}
function R(e) {
	return Wn(() => f(e()));
}
function Kn(e) {
	return Wn(() => Vn(e));
}
function qn(e, t) {
	return Wn(() => Un(e, t));
}
function Jn() {
	return R(() => []);
}
function Yn(e) {
	return R(() => Qt(e));
}
function z(e) {
	return e instanceof dn ? Tn(e) : Array.from(e);
}
function Xn(e) {
	return n(e) ? On(e) : e instanceof dn ? e : kn(e);
}
function Zn(e, t, n) {
	return Wn(() => Hn(e, t, n));
}
function Qn(e, t) {
	return Kn([e, t]);
}
function $n(e, t) {
	return Zn(() => Gn(t), (t) => {
		let n;
		for (; n == null && t["System.Collections.IEnumerator.MoveNext"]();) n = e(t["System.Collections.Generic.IEnumerator`1.get_Current"]());
		return n;
	}, (e) => {
		c(e);
	});
}
function er(e, t, n) {
	let r = Gn(t);
	try {
		let t = Gn(n);
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
function tr(e, t) {
	return Kn(qn((n) => e() ? [t, n + 1] : void 0, 0));
}
function nr(e, t) {
	return $n((t) => {
		if (e(t)) return he(t);
	}, t);
}
function rr(e, t) {
	let n = Gn(t);
	try {
		let t = !1;
		for (; !t && n["System.Collections.IEnumerator.MoveNext"]();) t = e(n["System.Collections.Generic.IEnumerator`1.get_Current"]());
		return t;
	} finally {
		c(n);
	}
}
function ir(e, t, n) {
	let r = Gn(n);
	try {
		let n = t;
		for (; r["System.Collections.IEnumerator.MoveNext"]();) n = e(n, r["System.Collections.Generic.IEnumerator`1.get_Current"]());
		return n;
	} finally {
		c(r);
	}
}
function ar(e, t) {
	return !rr((t) => !e(t), t);
}
function or(e, t) {
	ir((t, n) => {
		e(n);
	}, void 0, t);
}
function sr(e, t) {
	ir((t, n) => (e(t, n), t + 1 | 0), 0, t);
}
function B(e, t) {
	return Zn(() => Gn(t), (t) => t["System.Collections.IEnumerator.MoveNext"]() ? he(e(t["System.Collections.Generic.IEnumerator`1.get_Current"]())) : void 0, (e) => {
		c(e);
	});
}
function V(e, t) {
	return R(() => Kn(B(e, t)));
}
function cr(e, t, n) {
	return ir((t, r) => n.Add(t, e(r)), n.GetZero(), t);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/System.Collections.Generic.js
var lr = class extends t {
	constructor(e) {
		super(e);
	}
};
function ur(e) {
	return new lr(e);
}
function dr() {
	return ur(Ve);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Map.js
var fr = class {
	constructor(e, t) {
		this.k = e, this.v = t;
	}
};
function pr(e, t) {
	return new fr(e, t);
}
function H(e) {
	return e.k;
}
function U(e) {
	return e.v;
}
var W = class extends fr {
	constructor(e, t, n, r, i) {
		super(e, t), this.left = n, this.right = r, this.h = i | 0;
	}
};
function mr(e, t, n, r, i) {
	return new W(e, t, n, r, i);
}
function G(e) {
	return e.left;
}
function K(e) {
	return e.right;
}
function hr(e) {
	return e.h | 0;
}
function gr(e, t) {
	MapTreeModule_sizeAux: for (;;) {
		let n = e, r = t;
		if (r != null) {
			let i = S(r);
			if (i instanceof W) {
				let r = i;
				e = gr(n + 1, G(r)), t = K(r);
				continue MapTreeModule_sizeAux;
			}
			return n + 1 | 0;
		}
		return n | 0;
	}
}
function _r(e) {
	return gr(0, e) | 0;
}
function q(e, t, n, r) {
	let i, a, o, s = e;
	if (s != null) {
		let e = S(s);
		o = e instanceof W ? (i = e, hr(i)) : 1;
	} else o = 0;
	let c, l = r;
	if (l != null) {
		let e = S(l);
		c = e instanceof W ? (a = e, hr(a)) : 1;
	} else c = 0;
	let u = (o < c ? c : o) | 0;
	return u === 0 ? pr(t, n) : mr(t, n, e, r, u + 1);
}
function vr(e, n, r, i) {
	let a, o, s, c, l, u, d, f, p, m = e;
	if (m != null) {
		let e = S(m);
		p = e instanceof W ? (a = e, hr(a)) : 1;
	} else p = 0;
	let h, g = i;
	if (g != null) {
		let e = S(g);
		h = e instanceof W ? (o = e, hr(o)) : 1;
	} else h = 0;
	if (h > p + 2) {
		let a = S(i);
		if (a instanceof W) {
			let i = a;
			if (s = G(i), (s == null ? 0 : (c = S(s), c instanceof W ? (l = c, hr(l)) : 1)) > p + 1) {
				let a = S(G(i));
				if (a instanceof W) {
					let t = a;
					return q(q(e, n, r, G(t)), H(t), U(t), q(K(t), H(i), U(i), K(i)));
				}
				throw new t("internal error: Map.rebalance");
			}
			return q(q(e, n, r, G(i)), H(i), U(i), K(i));
		}
		throw new t("internal error: Map.rebalance");
	}
	if (p > h + 2) {
		let a = S(e);
		if (a instanceof W) {
			let e = a;
			if (u = K(e), (u == null ? 0 : (d = S(u), d instanceof W ? (f = d, hr(f)) : 1)) > h + 1) {
				let a = S(K(e));
				if (a instanceof W) {
					let t = a;
					return q(q(G(e), H(e), U(e), G(t)), H(t), U(t), q(K(t), n, r, i));
				}
				throw new t("internal error: Map.rebalance");
			}
			return q(G(e), H(e), U(e), q(K(e), n, r, i));
		}
		throw new t("internal error: Map.rebalance");
	}
	return q(e, n, r, i);
}
function yr(e, t, n, r) {
	if (r != null) {
		let i = S(r), a = e.Compare(t, H(i)) | 0;
		if (i instanceof W) {
			let r = i;
			return a < 0 ? vr(yr(e, t, n, G(r)), H(r), U(r), K(r)) : a === 0 ? mr(t, n, G(r), K(r), hr(r)) : vr(G(r), H(r), U(r), yr(e, t, n, K(r)));
		}
		return a < 0 ? mr(t, n, void 0, r, 2) : a === 0 ? pr(t, n) : mr(t, n, r, void 0, 2);
	}
	return pr(t, n);
}
function br(e, t, n) {
	MapTreeModule_tryFind: for (;;) {
		let r = e, i = t, a = n;
		if (a != null) {
			let o = S(a), s = r.Compare(i, H(o)) | 0;
			if (s === 0) return he(U(o));
			if (o instanceof W) {
				let a = o;
				e = r, t = i, n = s < 0 ? G(a) : K(a);
				continue MapTreeModule_tryFind;
			}
			return;
		}
		return;
	}
}
function xr(e, t, n) {
	let r = br(e, t, n);
	if (r == null) throw dr();
	return S(r);
}
function Sr(e, t, n) {
	MapTreeModule_mem: for (;;) {
		let r = e, i = t, a = n;
		if (a != null) {
			let o = S(a), s = r.Compare(i, H(o)) | 0;
			if (o instanceof W) {
				let a = o;
				if (s < 0) {
					e = r, t = i, n = G(a);
					continue MapTreeModule_mem;
				}
				if (s === 0) return !0;
				e = r, t = i, n = K(a);
				continue MapTreeModule_mem;
			}
			return s === 0;
		}
		return !1;
	}
}
function Cr(e, t) {
	MapTreeModule_iterOpt: for (;;) {
		let n = e, r = t;
		if (r != null) {
			let i = S(r);
			if (i instanceof W) {
				let r = i;
				Cr(n, G(r)), n(H(r), U(r)), e = n, t = K(r);
				continue MapTreeModule_iterOpt;
			}
			n(H(i), U(i));
		}
		break;
	}
}
function wr(e, t) {
	Cr(e, t);
}
function Tr(e, t, n) {
	MapTreeModule_foldOpt: for (;;) {
		let r = e, i = t, a = n;
		if (a != null) {
			let o = S(a);
			if (o instanceof W) {
				let a = o;
				e = r, t = r(Tr(r, i, G(a)), H(a), U(a)), n = K(a);
				continue MapTreeModule_foldOpt;
			}
			return r(i, H(o), U(o));
		}
		return i;
	}
}
function Er(e, t, n) {
	return Tr(e, t, n);
}
function Dr(e, t, n) {
	let r = n;
	wr((e, n) => {
		I(t, r, [e, n]), r = r + 1 | 0;
	}, e);
}
function Or(e, t) {
	return En((t, n) => yr(e, n[0], n[1], t), void 0, t);
}
function kr(e, t, n) {
	MapTreeModule_mkFromEnumerator: for (;;) {
		let r = e, i = t, a = n;
		if (a["System.Collections.IEnumerator.MoveNext"]()) {
			let o = a["System.Collections.Generic.IEnumerator`1.get_Current"]();
			e = r, t = yr(r, o[0], o[1], i), n = a;
			continue MapTreeModule_mkFromEnumerator;
		}
		return i;
	}
}
function Ar(e, t) {
	let n;
	for (let r = 0; r <= t.length - 1; r++) {
		let i = F(r, t);
		n = yr(e, i[0], i[1], n);
	}
	return n;
}
function jr(e, t) {
	if (n(t)) return Ar(e, t);
	if (t instanceof dn) return Or(e, t);
	{
		let n = f(t);
		try {
			return kr(e, void 0, n);
		} finally {
			c(n);
		}
	}
}
var Mr = class extends C {
	constructor(e, t) {
		super(), this.stack = e, this.started = t;
	}
};
function Nr(e) {
	MapTreeModule_collapseLHS: for (;;) {
		let t = e;
		if (Cn(t)) return bn();
		{
			let n = wn(t), r = L(t);
			if (r != null) {
				let i = S(r);
				if (i instanceof W) {
					let t = i;
					e = Dn([
						G(t),
						pr(H(t), U(t)),
						K(t)
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
function Pr(e) {
	return new Mr(Nr(Sn(e)), !1);
}
function Fr() {
	throw new t("enumeration not started");
}
function Ir() {
	throw new t("enumeration already finished");
}
function Lr(e) {
	if (e.started) {
		let n = e.stack;
		if (Cn(n)) return Ir();
		if (L(n) != null) {
			let e = S(L(n));
			if (e instanceof W) throw new t("Please report error: Map iterator, unexpected stack for current");
			return [H(e), U(e)];
		}
		throw new t("Please report error: Map iterator, unexpected stack for current");
	}
	return Fr();
}
function Rr(e) {
	if (e.started) {
		let n = e.stack;
		if (Cn(n)) return !1;
		if (L(n) != null) {
			if (S(L(n)) instanceof W) throw new t("Please report error: Map iterator, unexpected stack for moveNext");
			return e.stack = Nr(wn(n)), !Cn(e.stack);
		}
		throw new t("Please report error: Map iterator, unexpected stack for moveNext");
	}
	return e.started = !0, !Cn(e.stack);
}
function zr(e) {
	let t = Pr(e);
	return {
		"System.Collections.Generic.IEnumerator`1.get_Current"() {
			return Lr(t);
		},
		"System.Collections.IEnumerator.get_Current"() {
			return Lr(t);
		},
		"System.Collections.IEnumerator.MoveNext"() {
			return Rr(t);
		},
		"System.Collections.IEnumerator.Reset"() {
			t = Pr(e);
		},
		Dispose() {}
	};
}
var Br = class e {
	constructor(e, t) {
		this.comparer = e, this.tree = t;
	}
	GetHashCode() {
		return Yr(this) | 0;
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
		return "map [" + Ot("; ", B((e) => Et("({0}, {1})", e[0], e[1]), this)) + "]";
	}
	get [Symbol.toStringTag]() {
		return "FSharpMap";
	}
	toJSON() {
		let e = this;
		return Array.from(e);
	}
	GetEnumerator() {
		return zr(this.tree);
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return zr(this.tree);
	}
	CompareTo(t) {
		let n, r = this;
		return (t instanceof e ? (n = t, er((e, t) => {
			let n = r.comparer.Compare(e[0], t[0]) | 0;
			return (n === 0 ? x(e[1], t[1]) : n) | 0;
		}, r, n)) : 1) | 0;
	}
	"System.Collections.Generic.ICollection`1.Add2B595"(e) {
		throw zt("Map cannot be mutated");
	}
	"System.Collections.Generic.ICollection`1.Clear"() {
		throw zt("Map cannot be mutated");
	}
	"System.Collections.Generic.ICollection`1.Remove2B595"(e) {
		throw zt("Map cannot be mutated");
	}
	"System.Collections.Generic.ICollection`1.Contains2B595"(e) {
		let t = this;
		return qr(t, e[0]) && y(Gr(t, e[0]), e[1]);
	}
	"System.Collections.Generic.ICollection`1.CopyToZ3B4C077E"(e, t) {
		Dr(this.tree, e, t);
	}
	"System.Collections.Generic.ICollection`1.get_IsReadOnly"() {
		return !0;
	}
	"System.Collections.Generic.ICollection`1.get_Count"() {
		return Kr(this) | 0;
	}
	"System.Collections.Generic.IReadOnlyCollection`1.get_Count"() {
		return Kr(this) | 0;
	}
	get size() {
		return Kr(this) | 0;
	}
	clear() {
		throw new t("Map cannot be mutated");
	}
	delete(e) {
		throw new t("Map cannot be mutated");
	}
	entries() {
		return B((e) => [e[0], e[1]], this);
	}
	get(e) {
		return Gr(this, e);
	}
	has(e) {
		return qr(this, e);
	}
	keys() {
		return B((e) => e[0], this);
	}
	set(e, n) {
		throw new t("Map cannot be mutated");
	}
	values() {
		return B((e) => e[1], this);
	}
	forEach(e, t) {
		let n = this;
		or((t) => {
			e(t[1], t[0], n);
		}, n);
	}
};
function Vr(e, t) {
	return new Br(e, t);
}
function Hr(e) {
	return Vr(e, void 0);
}
function Ur(e) {
	return e.tree;
}
function Wr(e, t, n) {
	return Vr(e.comparer, yr(e.comparer, t, n, e.tree));
}
function Gr(e, t) {
	return xr(e.comparer, t, e.tree);
}
function Kr(e) {
	return _r(e.tree) | 0;
}
function qr(e, t) {
	return Sr(e.comparer, t, e.tree);
}
function Jr(e, t) {
	return br(e.comparer, t, e.tree);
}
function Yr(e) {
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
function Xr(e, t, n) {
	return Wr(n, e, t);
}
function Zr(e, t) {
	return Gr(t, e);
}
function Qr(e, t, n) {
	return Er(e, t, Ur(n));
}
function $r(e, t) {
	return Vr(t, jr(t, e));
}
function ei(e, t) {
	return Vr(t, jr(t, e));
}
function ti(e) {
	return Hr(e);
}
var ni = class extends C {
	constructor(e, t, n) {
		super(), this.r = e, this.g = t, this.b = n;
	}
}, ri = "0123456789ABCDEF";
function ii(e) {
	return ri.indexOf(e.toUpperCase());
}
function ai(e, t) {
	return ii(e[t]) * 16 + ii(e[t + 1]);
}
function J(e) {
	return new ni(ai(e, 1), ai(e, 3), ai(e, 5));
}
function Y(e, t, n) {
	return new ni(e.r + (t.r - e.r) * n, e.g + (t.g - e.g) * n, e.b + (t.b - e.b) * n);
}
var oi = J("#FFFFFF"), si = J("#000000");
function ci(e, t) {
	return Y(e, oi, t);
}
function X(e) {
	return .2126 * e.r + .7152 * e.g + .0722 * e.b;
}
function li(e) {
	let t = X(e);
	return new ni(t, t, t);
}
function ui(e) {
	return (M(e.r, M(e.g, e.b)) - N(e.r, N(e.g, e.b))) / 255;
}
function di(e) {
	let t = M(e.r, M(e.g, e.b)), n = N(e.r, N(e.g, e.b)), r = t;
	if (r === n) return 0;
	{
		let t = r - n;
		return ((r === e.r ? (e.g - e.b) / t : r === e.g ? 2 + (e.b - e.r) / t : 4 + (e.r - e.g) / t) * 60 + 360) % 360;
	}
}
var fi = P((e) => (e + .5) / 16, [
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
function pi(e, t) {
	return F((t & 3) * 4 + (e & 3), fi);
}
function mi(e, t, n) {
	return ~~Math.floor(n) + +(n - Math.floor(n) > pi(e, t)) | 0;
}
var hi = class extends C {
	constructor(e, t) {
		super(), this.rows = e, this.palette = t;
	}
}, gi = class extends C {
	constructor(e) {
		super(), this.glyphs = e;
	}
};
function _i(e, t) {
	return Qr((e, t, n) => Xr(t, n, e), e, t);
}
function vi(e, t) {
	let n, r = P((e) => e[0], Yt(e, t));
	return n = an(_i, ti({ Compare: (e, t) => b(e, t) | 0 }), r), new gi(ei(Zt((e) => {
		let t = _i(n, e[0]);
		return P((e) => [e[0], new hi(e[1], t)], e[1]);
	}, t), { Compare: (e, t) => b(e, t) | 0 }));
}
function yi(e, t) {
	return Zr(t, e.glyphs);
}
var bi = class extends C {
	constructor(e, t, n) {
		super(), this.w = e | 0, this.h = t | 0, this.px = n;
	}
};
function xi(e, t) {
	return new bi(e, t, Jt(Array(e * t * 4), 0, e * t * 4, 0));
}
function Si(e, t, n) {
	return t >= 0 && n >= 0 && t < e.w && n < e.h;
}
function Ci(e, t, n) {
	return Si(e, t, n) ? F(4 * (n * e.w + t) + 3, e.px) > 0 : !1;
}
function Z(e, t, n, r, i) {
	if (Si(e, t, n)) {
		let a = 4 * (n * e.w + t) | 0, o = F(a + 3, e.px) / 255, s = i + o * (1 - i);
		s > 0 && (e.px[a] = (r.r * i + F(a, e.px) * o * (1 - i)) / s, e.px[a + 1] = (r.g * i + F(a + 1, e.px) * o * (1 - i)) / s, e.px[a + 2] = (r.b * i + F(a + 2, e.px) * o * (1 - i)) / s), e.px[a + 3] = s * 255;
	}
}
function wi(e, t, n, r) {
	if (Si(e, t, n)) {
		let i = 4 * (n * e.w + t) | 0;
		e.px[i] = r.r, e.px[i + 1] = r.g, e.px[i + 2] = r.b, e.px[i + 3] = 255;
	}
}
function Ti(e, t, n) {
	let r = 4 * (n * e.w + t) | 0;
	if (Ci(e, t, n)) return new ni(F(r, e.px), F(r + 1, e.px), F(r + 2, e.px));
}
function Ei(e, t, n, r, i) {
	for (let a = 0; a <= t.rows.length - 1; a++) for (let o = 0; o <= F(a, t.rows).length - 1; o++) {
		let s = Jr(t.palette, F(a, t.rows)[o]);
		if (s != null) {
			let t = i(n + o, r + a, s);
			if (t != null) {
				let i = t;
				Z(e, n + o, r + a, i[0], i[1]);
			}
		}
	}
}
function Di(e, t, n, r) {
	Ei(e, t, n, r, (e, t, n) => [n, 1]);
}
function Oi(e, t, n) {
	for (let r = 0; r <= e.w - 1; r++) Z(e, r, 0, t, n), Z(e, r, e.h - 1, t, n);
	for (let r = 1; r <= e.h - 2; r++) Z(e, 0, r, t, n), Z(e, e.w - 1, r, t, n);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Int32.js
var ki = { AllowHexSpecifier: 512 };
function Ai(e, t) {
	let [, n, r, i] = e;
	return {
		sign: n || "",
		prefix: r || "",
		digits: i,
		radix: t
	};
}
function ji(e, n) {
	switch (n) {
		case 8: return e ? [0, 255] : [-128, 127];
		case 16: return e ? [0, 65535] : [-32768, 32767];
		case 32: return e ? [0, 4294967295] : [-2147483648, 2147483647];
		default: throw new t("Invalid bit size.");
	}
}
function Mi(e) {
	switch (e) {
		case 2: return /[^0-1]/;
		case 8: return /[^0-7]/;
		case 10: return /[^0-9]/;
		case 16: return /[^0-9a-fA-F]/;
		default: throw new t("Invalid Base.");
	}
}
function Ni(e, t) {
	if (t & ki.AllowHexSpecifier) return 16;
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
function Pi(e, t, n) {
	let r = /^\s*([\+\-])?(0[xXoObB])?([0-9a-fA-F]+)\s*$/.exec(e.replace(/_/g, ""));
	if (r != null) {
		let [, , e, i] = r;
		if (n ||= Ni(e, t), !Mi(n).test(i)) return Ai(r, n);
	}
	return null;
}
function Fi(e, n, r, i, a) {
	let o = Pi(e, n, a);
	if (o != null) {
		let e = Number.parseInt(o.sign + o.digits, o.radix);
		if (!Number.isNaN(e)) {
			let [t, n] = ji(!0, i);
			!r && o.radix !== 10 && e >= t && e <= n && (e = e << 32 - i >> 32 - i);
			let [a, s] = ji(r, i);
			if (e >= a && e <= s) return e;
		}
	}
	throw new t(`The input string ${e} was not in a correct format.`);
}
function Ii(e, t, n, r, i) {
	try {
		return i.contents = Fi(e, t, n, r), !0;
	} catch {
		return !1;
	}
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/MapUtil.js
function Li(e, t, n) {
	return e.has(t) ? (n.contents = e.get(t), !0) : !1;
}
function Ri(e, t) {
	return !t.has(e) && (t.add(e), !0);
}
function zi(e, n, r) {
	if (e.has(n)) throw new t("An item with the same key has already been added. Key: " + n);
	e.set(n, r);
}
function Bi(e, n) {
	if (e.has(n)) return e.get(n);
	throw new t(`The given key '${n}' was not present in the dictionary.`);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/MutableSet.js
var Vi = class {
	constructor(e, t) {
		let n = new w(l());
		this.comparer = t, n.contents = this, this.hashMap = /* @__PURE__ */ new Map([]), this["init@9"] = 1;
		let r = f(e);
		try {
			for (; r["System.Collections.IEnumerator.MoveNext"]();) {
				let e = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
				Gi(n.contents, e);
			}
		} finally {
			c(r);
		}
	}
	get [Symbol.toStringTag]() {
		return "HashSet";
	}
	toJSON() {
		let e = this;
		return Array.from(e);
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return f(this);
	}
	GetEnumerator() {
		return f(Kn(this.hashMap.values()));
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.Generic.ICollection`1.Add2B595"(e) {
		Gi(this, e);
	}
	"System.Collections.Generic.ICollection`1.Clear"() {
		Ui(this);
	}
	"System.Collections.Generic.ICollection`1.Contains2B595"(e) {
		return Ki(this, e);
	}
	"System.Collections.Generic.ICollection`1.CopyToZ3B4C077E"(e, t) {
		sr((n, r) => {
			I(e, t + n, r);
		}, this);
	}
	"System.Collections.Generic.ICollection`1.get_Count"() {
		return Wi(this) | 0;
	}
	"System.Collections.Generic.ICollection`1.get_IsReadOnly"() {
		return !1;
	}
	"System.Collections.Generic.ICollection`1.Remove2B595"(e) {
		return qi(this, e);
	}
	get size() {
		return Wi(this) | 0;
	}
	add(e) {
		let t = this;
		return Gi(t, e), t;
	}
	clear() {
		Ui(this);
	}
	delete(e) {
		return qi(this, e);
	}
	has(e) {
		return Ki(this, e);
	}
	keys() {
		return B((e) => e, this);
	}
	values() {
		return B((e) => e, this);
	}
	entries() {
		return B((e) => [e, e], this);
	}
	forEach(e, t) {
		let n = this;
		or((t) => {
			e(t, t, n);
		}, n);
	}
};
function Hi(e, t) {
	let n = e.comparer.GetHashCode(t) | 0, r, i = l();
	return r = [Li(e.hashMap, n, new w(() => i, (e) => {
		i = e;
	})), i], r[0] ? [
		!0,
		n,
		r[1].findIndex((n) => e.comparer.Equals(t, n))
	] : [
		!1,
		n,
		-1
	];
}
function Ui(e) {
	e.hashMap.clear();
}
function Wi(e) {
	let t = 0, n = f(e.hashMap.values());
	try {
		for (; n["System.Collections.IEnumerator.MoveNext"]();) {
			let e = n["System.Collections.Generic.IEnumerator`1.get_Current"]();
			t = t + e.length | 0;
		}
	} finally {
		c(n);
	}
	return t | 0;
}
function Gi(e, t) {
	let n = Hi(e, t);
	return n[0] ? n[2] > -1 ? !1 : (Bi(e.hashMap, n[1]).push(t), !0) : (e.hashMap.set(n[1], [t]), !0);
}
function Ki(e, t) {
	let n = Hi(e, t), r;
	switch (r = n[0] && n[2] > -1 ? 0 : 1, r) {
		case 0: return !0;
		default: return !1;
	}
}
function qi(e, t) {
	let n = Hi(e, t), r;
	switch (r = n[0] && n[2] > -1 ? 0 : 1, r) {
		case 0: return Bi(e.hashMap, n[1]).splice(n[2], 1), !0;
		default: return !1;
	}
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Range.js
function Ji(e, n, r, i) {
	let a = x(e, r) | 0;
	if (a === 0) throw new t("The step of a range cannot be zero");
	let o = a > 0;
	return (t) => {
		let r = x(t, n) | 0;
		return o && r <= 0 || !o && r >= 0 ? [t, i(t, e)] : void 0;
	};
}
function Yi(e, t, n, r, i) {
	let a = Ji(t, n, r, i);
	return R(() => qn(a, e));
}
function Xi(e, t, n) {
	return Yi(e, t, n, 0, (e, t) => e + t);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/MutableMap.js
var Zi = class {
	constructor(e, t) {
		let n = new w(l());
		this.comparer = t, n.contents = this, this.hashMap = /* @__PURE__ */ new Map([]), this["init@9"] = 1;
		let r = f(e);
		try {
			for (; r["System.Collections.IEnumerator.MoveNext"]();) {
				let e = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
				ia(n.contents, e[0], e[1]);
			}
		} finally {
			c(r);
		}
	}
	get [Symbol.toStringTag]() {
		return "Dictionary";
	}
	toJSON() {
		let e = this;
		return Array.from(e);
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return f(this);
	}
	GetEnumerator() {
		return f(Kn(this.hashMap.values()));
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.Generic.ICollection`1.Add2B595"(e) {
		ia(this, e[0], e[1]);
	}
	"System.Collections.Generic.ICollection`1.Clear"() {
		ea(this);
	}
	"System.Collections.Generic.ICollection`1.Contains2B595"(e) {
		let t = $i(this, e[0]), n;
		switch (t == null ? n = 1 : y(S(t)[1], e[1]) ? (n = 0, S(t)) : n = 1, n) {
			case 0: return !0;
			default: return !1;
		}
	}
	"System.Collections.Generic.ICollection`1.CopyToZ3B4C077E"(e, t) {
		sr((n, r) => {
			I(e, t + n, r);
		}, this);
	}
	"System.Collections.Generic.ICollection`1.get_Count"() {
		return ta(this) | 0;
	}
	"System.Collections.Generic.ICollection`1.get_IsReadOnly"() {
		return !1;
	}
	"System.Collections.Generic.ICollection`1.Remove2B595"(e) {
		let t = this, n = $i(t, e[0]), r;
		switch (n == null ? r = 1 : y(S(n)[1], e[1]) ? (r = 0, S(n)) : r = 1, r) {
			case 0: return oa(t, e[0]);
			default: return !1;
		}
	}
	"System.Collections.Generic.IDictionary`2.Add5BDDA1"(e, t) {
		ia(this, e, t);
	}
	"System.Collections.Generic.IDictionary`2.ContainsKey2B595"(e) {
		return aa(this, e);
	}
	"System.Collections.Generic.IDictionary`2.get_Item2B595"(e) {
		return na(this, e);
	}
	"System.Collections.Generic.IDictionary`2.set_Item5BDDA1"(e, t) {
		ra(this, e, t);
	}
	"System.Collections.Generic.IDictionary`2.get_Keys"() {
		let e = this;
		return z(R(() => B((e) => e[0], e)));
	}
	"System.Collections.Generic.IDictionary`2.Remove2B595"(e) {
		return oa(this, e);
	}
	"System.Collections.Generic.IDictionary`2.TryGetValue6DC89625"(e, t) {
		let n = $i(this, e);
		return n != null && (t.contents = S(n)[1], !0);
	}
	"System.Collections.Generic.IDictionary`2.get_Values"() {
		let e = this;
		return z(R(() => B((e) => e[1], e)));
	}
	get size() {
		return ta(this) | 0;
	}
	clear() {
		ea(this);
	}
	delete(e) {
		return oa(this, e);
	}
	entries() {
		return B((e) => [e[0], e[1]], this);
	}
	get(e) {
		return na(this, e);
	}
	has(e) {
		return aa(this, e);
	}
	keys() {
		return B((e) => e[0], this);
	}
	set(e, t) {
		let n = this;
		return ra(n, e, t), n;
	}
	values() {
		return B((e) => e[1], this);
	}
	forEach(e, t) {
		let n = this;
		or((t) => {
			e(t[1], t[0], n);
		}, n);
	}
};
function Qi(e, t) {
	let n = e.comparer.GetHashCode(t) | 0, r, i = l();
	return r = [Li(e.hashMap, n, new w(() => i, (e) => {
		i = e;
	})), i], r[0] ? [
		!0,
		n,
		r[1].findIndex((n) => e.comparer.Equals(t, n[0]))
	] : [
		!1,
		n,
		-1
	];
}
function $i(e, t) {
	let n = Qi(e, t), r;
	switch (r = n[0] && n[2] > -1 ? 0 : 1, r) {
		case 0: return F(n[2], Bi(e.hashMap, n[1]));
		default: return;
	}
}
function ea(e) {
	e.hashMap.clear();
}
function ta(e) {
	let t = 0, n = f(e.hashMap.values());
	try {
		for (; n["System.Collections.IEnumerator.MoveNext"]();) {
			let e = n["System.Collections.Generic.IEnumerator`1.get_Current"]();
			t = t + e.length | 0;
		}
	} finally {
		c(n);
	}
	return t | 0;
}
function na(e, t) {
	let n = $i(e, t);
	if (n != null) return S(n)[1];
	throw ur("The item was not found in collection");
}
function ra(e, t, n) {
	let r = Qi(e, t);
	r[0] ? r[2] > -1 ? I(Bi(e.hashMap, r[1]), r[2], [t, n]) : Bi(e.hashMap, r[1]).push([t, n]) : e.hashMap.set(r[1], [[t, n]]);
}
function ia(e, t, n) {
	let r = Qi(e, t);
	if (r[0]) {
		if (r[2] > -1) throw Ht(Et("An item with the same key has already been added. Key: {0}", t));
		Bi(e.hashMap, r[1]).push([t, n]);
	} else e.hashMap.set(r[1], [[t, n]]);
}
function aa(e, t) {
	let n = Qi(e, t), r;
	switch (r = n[0] && n[2] > -1 ? 0 : 1, r) {
		case 0: return !0;
		default: return !1;
	}
}
function oa(e, t) {
	let n = Qi(e, t), r;
	switch (r = n[0] && n[2] > -1 ? 0 : 1, r) {
		case 0: return Bi(e.hashMap, n[1]).splice(n[2], 1), !0;
		default: return !1;
	}
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Seq2.js
function sa(e, t) {
	return R(() => {
		let n = new Vi([], t);
		return nr((e) => Ri(e, n), e);
	});
}
function ca(e, t, n) {
	return R(() => {
		let r = new Zi([], n), i = [], a = f(t);
		try {
			for (; a["System.Collections.IEnumerator.MoveNext"]();) {
				let t = a["System.Collections.Generic.IEnumerator`1.get_Current"](), n = e(t), o, s = l();
				o = [Li(r, n, new w(() => s, (e) => {
					s = e;
				})), s], o[0] ? o[1].push(t) : (zi(r, n, [t]), i.push(n));
			}
		} finally {
			c(a);
		}
		return B((e) => [e, Bi(r, e)], i);
	});
}
function la(e, t) {
	return z(sa(e, t));
}
function ua(e, t, n) {
	return z(B((e) => [e[0], z(e[1])], ca(e, t, n)));
}
var da = class extends C {
	constructor(e, t, n) {
		super(), this.height = e | 0, this.spacing = t | 0, this.glyphs = n;
	}
};
function fa(e, t, n) {
	return new da(e, t, $r(an((e, t) => {
		let n, r, i, a;
		switch (t.startsWith("= ") ? n = 0 : Cn(e) || t === "" ? n = 2 : (n = 1, r = L(e)[0], i = wn(e), a = L(e)[1]), n) {
			case 0: {
				let n = Ft(t, 2);
				return xn([n === "space" ? " " : n[0], []], e);
			}
			case 1: return xn([r, qt(a, [t])], i);
			default: return e;
		}
	}, bn(), P((e) => Pt(e, "\r"), Nt(n, ["\n"], void 0, 0))), { Compare: (e, t) => b(e, t) | 0 }));
}
var pa = ei(Zt((e) => z(R(() => B((t) => [t, e[1]], e[0].split("")))), [
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
]), { Compare: (e, t) => b(e, t) | 0 });
function ma(e) {
	return an((e, t) => Mt(e, t[0], t[1]), e, [
		["—", "--"],
		["→", "->"],
		["⇒", "=>"],
		["←", "<-"],
		["⇐", "<="],
		["…", "..."]
	]);
}
function ha(e, t) {
	let n = Jr(e.glyphs, t);
	if (n == null) {
		let n, r = Jr(pa, t);
		return n = r == null ? void 0 : Jr(e.glyphs, r), n ?? Gr(e.glyphs, "?");
	}
	return n;
}
function ga(e, t) {
	return F(0, ha(e, t)).length | 0;
}
function _a(e, t) {
	return t === "" ? 0 : cr((t) => ga(e, t) + e.spacing | 0, t.split(""), {
		GetZero: () => 0,
		Add: (e, t) => e + t | 0
	}) - e.spacing | 0;
}
var va = class extends C {
	constructor(e, t, n) {
		super(), this.fg = e, this.bg = t, this.accent = n;
	}
};
function ya(e) {
	let t = e.trim();
	if (t.startsWith("#") && t.length === 7) return J(t);
	if (t.startsWith("#") && t.length === 4) return J([
		"#",
		t[1],
		t[1],
		t[2],
		t[2],
		t[3],
		t[3]
	].join(""));
	if (t.startsWith("rgb") && t.indexOf("(") >= 0) {
		let e = Nt(Pt(Ft(t, t.indexOf("(") + 1), ")"), [
			",",
			" ",
			"/"
		], void 0, 1), n = (t) => {
			let n, r = 0;
			return n = [Ii(F(0, Nt(F(t, e), ["."], void 0, 0)), 511, !1, 32, new w(() => r | 0, (e) => {
				r = e | 0;
			})), r], n[0] ? n[1] : 0;
		};
		return e.length < 3 || e.length > 3 && F(3, e).trim() === "0" ? void 0 : new ni(n(0), n(1), n(2));
	}
}
function ba(e) {
	let t = (e) => {
		let t = e / 255;
		return t <= .03928 ? t / 12.92 : ((t + .055) / 1.055) ** 2.4;
	};
	return .2126 * t(e.r) + .7152 * t(e.g) + .0722 * t(e.b);
}
function xa(e, t) {
	let n = ba(e), r = ba(t), i = n;
	return (M(i, r) + .05) / (N(i, r) + .05);
}
function Sa(e, t, n, r) {
	return ((i) => {
		go: for (;;) {
			let a = i, o = Y(r, t, a);
			if (a >= 1 || xa(o, e) >= n) return o;
			i = a + .04;
			continue go;
		}
	})(0);
}
var Ca = [
	"metal",
	"text",
	"accent"
];
function wa(e, t) {
	let n = X(e.bg) < X(e.fg), r = n ? oi : si;
	return P((t) => Sa(e.bg, r, 4.5, t), t === "text" ? $t(6, (t) => t <= 3 ? Y(e.fg, e.bg, .36 * (3 - t) / 3) : Y(e.fg, r, .3 * (t - 3))) : t === "accent" ? $t(6, (t) => t <= 3 ? Y(e.accent, e.fg, t / 3) : Y(e.fg, r, .3 * (t - 3))) : P(J, n ? [
		"#64748B",
		"#94A3B8",
		"#CBD5E1",
		"#E2E8F0",
		"#F1F5F9",
		"#FFFFFF"
	] : [
		"#94A3B8",
		"#64748B",
		"#475569",
		"#334155",
		"#1E293B",
		"#0F172A"
	]));
}
var Ta = J("#B4703C"), Ea = [
	J("#E2E8F0"),
	J("#94A3B8"),
	J("#64748B")
], Da = [[
	J("#4C8FBF"),
	J("#2F6E9A"),
	J("#1F4F72")
], J("#163A55")], Oa = J("#05080D"), ka = [
	"virus",
	"nordlead",
	"microq",
	"machinedrum-32"
];
function Aa(e) {
	return ir((e, t) => e * 31 + ~~t.charCodeAt(0) & 2147483647, 7, e.split("")) | 0;
}
function ja(e) {
	let t = new w((e | 1) >>> 0);
	return (e) => {
		let n = t.contents;
		return n = (n ^ n << 13 >>> 0) >>> 0, n = (n ^ n >>> 17) >>> 0, n = (n ^ n << 5 >>> 0) >>> 0, t.contents = n, ~~(n % (M(1, e) >>> 0)) | 0;
	};
}
function Ma(e, t) {
	return [
		[e + 1, t],
		[e - 1, t],
		[e, t + 1],
		[e, t - 1]
	];
}
function Na(e, t, n) {
	let r = new Vi([], {
		Equals: oe,
		GetHashCode: (e) => ie(e) | 0
	}), i = f(t);
	try {
		for (; i["System.Collections.IEnumerator.MoveNext"]();) {
			let e = i["System.Collections.Generic.IEnumerator`1.get_Current"](), n = e[0][1] | 0, a = e[0][0] | 0;
			t.has([a + 1, n + 1]) || Ri([a + 1, n + 1], r);
		}
	} finally {
		c(i);
	}
	let a = f(Qn(t.keys(), r));
	try {
		for (; a["System.Collections.IEnumerator.MoveNext"]();) {
			let n = a["System.Collections.Generic.IEnumerator`1.get_Current"](), i = Ma(n[0], n[1]);
			for (let n = 0; n <= i.length - 1; n++) {
				let a, o = F(n, i), s = o[1] | 0, c = o[0] | 0;
				a = [c, s], !(t.has(a) || r.has(a)) && wi(e, c, s, Oa);
			}
		}
	} finally {
		c(a);
	}
	let o = f(r);
	try {
		for (; o["System.Collections.IEnumerator.MoveNext"]();) {
			let t = o["System.Collections.Generic.IEnumerator`1.get_Current"]();
			wi(e, t[0], t[1], n);
		}
	} finally {
		c(o);
	}
	let s = f(t);
	try {
		for (; s["System.Collections.IEnumerator.MoveNext"]();) {
			let t = s["System.Collections.Generic.IEnumerator`1.get_Current"]();
			wi(e, t[0][0], t[0][1], t[1]);
		}
	} finally {
		c(s);
	}
}
function Pa(e, t) {
	return z(R(() => Qn(V((t) => {
		let n = t[1][1] | 0, r = t[0][1] | 0, i = t[1][0] | 0, a = t[0][0] | 0, o = pe(i - a) | 0, s = pe(n - r) | 0, c = o | 0;
		return V((t) => {
			let n = r + t * s | 0, i = a + t * c | 0;
			return wi(e, i, n, Ta), Qn((c !== 0 && s !== 0 && wi(e, i + c, n, Ta), Jn()), R(() => Si(e, i, n) ? Yn(n * e.w + i) : Jn()));
		}, Xi(0, 1, M(Math.abs(i - a), Math.abs(n - r)) - 1));
	}, en(t)), R(() => {
		let n = F(t.length - 1, t), r = n[1] | 0, i = n[0] | 0;
		return wi(e, i, r, Ta), Si(e, i, r) ? Yn(r * e.w + i) : Jn();
	}))));
}
function Fa(e, t, n) {
	for (let r = -1; r <= 1; r++) for (let i = -1; i <= 1; i++) (i !== 0 || r !== 0) && wi(e, t + i, n + r, i + r < 0 ? F(0, Ea) : i + r > 0 ? F(2, Ea) : F(1, Ea));
}
function Ia(e, t, n) {
	let r = e - .5, i = (t) => z(R(() => V((n) => V((e) => t(e < r)(Math.abs(e - r) + Math.abs(n - r)) ? Yn([e, n]) : Jn(), Xi(0, 1, 2 * e - 1)), Xi(0, 1, 2 * e - 1))));
	return [
		i((n) => ((r) => n && r <= e && r > t)),
		i((e) => ((t) => e && t <= n)),
		i((n) => ((r) => !n && r <= e && r > t))
	];
}
function La(e, t) {
	turn: for (;;) {
		let n = e, r = t;
		if (r === 0) return n;
		{
			let i = sn(P((e) => e[1] | 0, n), { Compare: (e, t) => b(e, t) | 0 }) + 1 | 0;
			e = P((e) => [i - 1 - e[1], e[0]], n), t = r - 1;
			continue turn;
		}
	}
}
function Ra(e, t, n, r) {
	let i = cn(P((e) => e[0] | 0, t), { Compare: (e, t) => b(e, t) | 0 }) | 0, a = cn(P((e) => e[1] | 0, t), { Compare: (e, t) => b(e, t) | 0 }) | 0, o = i | 0, s = sn(P((e) => e[0] | 0, t), { Compare: (e, t) => b(e, t) | 0 }) - o + sn(P((e) => e[1] | 0, t), { Compare: (e, t) => b(e, t) | 0 }) - a + 1, c = new Zi([], {
		Equals: oe,
		GetHashCode: (e) => ie(e) | 0
	});
	for (let e = 0; e <= t.length - 1; e++) {
		let i = F(e, t), l = i[1] | 0, u = i[0] | 0, d = (u - o + l - a) / s;
		c.set([n + u - o, r + l - a], F(d < .34 ? 0 : d > .66 ? 2 : 1, Da[0]));
	}
	Na(e, c, Da[1]);
}
function za(e, t, n, r, i) {
	return ar((t) => !Ci(e, t[0], t[1]), R(() => V((e) => B((t) => [t, e], Xi(t, 1, t + r - 1)), Xi(n, 1, n + i - 1))));
}
function Ba(e, t, n, r) {
	let i = Ia(t, ~~(t / 2), ~~(t / 3)), a = i[1], o = i[0];
	return Ra(e, o, n - 3, r + 1), Ra(e, a, n - 1 + cn(P((e) => e[0] | 0, a), { Compare: (e, t) => b(e, t) | 0 }), r + cn(P((e) => e[1] | 0, a), { Compare: (e, t) => b(e, t) | 0 })), Ra(e, i[2], n + 2 + t, r - 1), ei(P((e) => [r + 1 + e[0], n - 3 + cn(P((e) => e[0] | 0, e[1]), { Compare: (e, t) => b(e, t) | 0 })], ua((e) => e[1] | 0, o, {
		Equals: (e, t) => e === t,
		GetHashCode: (e) => ee(e) | 0
	})), { Compare: (e, t) => b(e, t) | 0 });
}
function Va(e, t, n, r, i, a, o) {
	let s = Ia(4, 2, 1), c = s[2], l = s[0];
	((u, d) => {
		drop: for (;;) {
			let f = u, p = d;
			if (f < n && p < 300) {
				let n = La(F(t(5), [
					l,
					c,
					l,
					c,
					s[1]
				]), t(4)), m = sn(P((e) => e[0] | 0, n), { Compare: (e, t) => b(e, t) | 0 }) - cn(P((e) => e[0] | 0, n), { Compare: (e, t) => b(e, t) | 0 }) + 1 | 0, h = sn(P((e) => e[1] | 0, n), { Compare: (e, t) => b(e, t) | 0 }) - cn(P((e) => e[1] | 0, n), { Compare: (e, t) => b(e, t) | 0 }) + 1 | 0, g = r + t(M(1, a - m)) | 0, _ = i + t(M(1, o - h)) | 0, ee = g | 0;
				if (za(e, ee - 2, _ - 2, m + 4, h + 4)) {
					Ra(e, n, ee, _), u = f + 1, d = p + 1;
					continue drop;
				}
				u = f, d = p + 1;
				continue drop;
			}
			break;
		}
	})(0, 0);
}
var Ha = class extends C {
	constructor(e, t, n, r, i, a, o, s, c) {
		super(), this.w = e | 0, this.h = t | 0, this.letters = n, this.tops = r, this.shadow = i, this.board = a, this.signal = o, this.text = s, this.level = c | 0;
	}
};
function Ua(e) {
	return e >= 6 ? 3 : ~~(e * 4 / 7) | 0;
}
function Wa(e, t, n, r, i) {
	let a, o = ma(n), s = (r <= 2 ? 2 : 1) | 0, c = _a(t, o) * s | 0, l = t.height * s | 0, u = M(i, c + 8) | 0, d = (r <= 2 ? 44 : 20) | 0, f = xi(u, d), p = Aa(o) | 0, m = ja(p), h = 2 + l + 3 | 0, g;
	switch (r) {
		case 1: {
			let t = yi(e, F(p % ka.length, ka)), n = F(0, t.rows).length | 0;
			Di(f, t, 1, h + 1);
			let r = u - 14 - 6 | 0, i = Ba(f, 7, r, h), a = n + 3 | 0, o = n + 3 + ~~((r - n) / 3) | 0, s = [h + 5, h + 8];
			for (let e = 0; e <= s.length - 1; e++) {
				let t = F(e, s) | 0, n = t + 3 | 0, r = Jr(i, n);
				r == null ? (Pa(f, [
					[a, t],
					[o, t],
					[o + 3, n],
					[o + 3 + 12, n]
				]), Fa(f, o + 3 + 14, n)) : Pa(f, [
					[a, t],
					[o, t],
					[o + 3, n],
					[r - 2, n]
				]);
			}
			let l = M(a + 4, N(o - 6, c - 8)) | 0, d = Pa(f, [
				[a, h + 2],
				[l, h + 2],
				[l + 3, h - 1]
			]);
			Fa(f, l + 3, h - 2), g = d;
			break;
		}
		case 2: {
			let t = yi(e, F(~~(p / 7) % ka.length, ka)), n = u - F(0, t.rows).length - 2 | 0;
			Di(f, t, n, h + 1);
			let r = h + 1 | 0, i = 30 + m(M(1, ~~(n / 3))) | 0;
			Fa(f, 2, r);
			let a = Pa(f, [
				[4, r],
				[i, r],
				[i + 4, r + 4],
				[n - 6, r + 4]
			]);
			Fa(f, n - 4, r + 4), Va(f, m, 3, 4, r + 5, i - 8, d - r - 6), g = tn(a);
			break;
		}
		default: {
			let e = h + 1 | 0, t = Pa(f, [[1, e], [c + 10, e]]);
			Fa(f, c + 12, e);
			let n = Ia(3, 1, 1);
			Ra(f, p % 2 == 0 ? n[0] : n[2], c + 16, e - 3), g = tn(t);
		}
	}
	let _ = /* @__PURE__ */ new Map([]);
	ir((e, n) => {
		let r = ha(t, n);
		for (let t = 0; t <= r.length - 1; t++) for (let n = 0; n <= F(t, r).length - 1; n++) if (F(t, r)[n] === "#") for (let r = 0; r <= s - 1; r++) for (let i = 0; i <= s - 1; i++) {
			let a = 2 + t * s + r | 0, o = 1 + (e + n) * s + i | 0;
			Si(f, o, a) && _.set(a * u + o, Ua(t));
		}
		return e + ga(t, n) + t.spacing | 0;
	}, 0, o.split(""));
	let te = Array.from(_.keys());
	return new Ha(u, d, z(R(() => V((e) => {
		let t = e;
		return Yn([t[0], t[1]]);
	}, _))), te.filter((e) => !_.has(e - u)), la((a = P((e) => e + u + 1 | 0, te), a.filter((e) => e % u !== 0 && e < u * d && !_.has(e))), {
		Equals: (e, t) => e === t,
		GetHashCode: (e) => ee(e) | 0
	}), z(R(() => V((e) => {
		let t = Ti(f, e % u, ~~(e / u));
		return t == null ? Jn() : Yn([e, t]);
	}, Xi(0, 1, u * d - 1)))), g, [
		1,
		2,
		c,
		l
	], r);
}
var Ga = class e extends xe {
	constructor(e, t) {
		super(), this.tag = e, this.fields = t;
	}
	cases() {
		return [
			"Idle",
			"Pulse",
			"Lit"
		];
	}
	static Idle = new e(0, []);
	static Lit = new e(2, []);
}, Ka = .3, qa = .15;
function Ja(e, t, n, r) {
	let i = xi(n.w, n.h), a = wa(e, t), o = (e, t) => {
		wi(i, e % n.w, ~~(e / n.w), t);
	}, s = n.board;
	for (let t = 0; t <= s.length - 1; t++) {
		let n = F(t, s);
		o(n[0], Y(e.bg, n[1], .45));
	}
	let c, l, u;
	switch (r.tag) {
		case 1:
			r.fields[0] < .3 ? (l = 0, u = r.fields[0]) : r.fields[0] < .44999999999999996 ? (l = 1, r.fields[0]) : l = 3;
			break;
		case 2:
			l = 2;
			break;
		default: l = 3;
	}
	switch (l) {
		case 0: {
			let e = ~~(u / Ka * (n.signal.length - 1)) | 0;
			for (let t = 0; t <= 3; t++) e - t >= 0 && o(F(e - t, n.signal), F(5 - t, a));
			c = 0;
			break;
		}
		case 1:
			c = 1;
			break;
		case 2:
			c = 1;
			break;
		default: c = 0;
	}
	let d = Y(e.bg, e.fg, .22), f = n.shadow;
	for (let e = 0; e <= f.length - 1; e++) o(F(e, f), d);
	let p = n.letters;
	for (let e = 0; e <= p.length - 1; e++) {
		let t = F(e, p);
		o(t[0], F(N(5, 4 - t[1] + c), a));
	}
	let m = n.tops;
	for (let e = 0; e <= m.length - 1; e++) o(F(e, m), F(5, a));
	return i;
}
// fsdsp56k-art/fsdsp56k-pixel-font.js
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
var Ya = [
	["fsdsp56kIcon", "grids"],
	["fsdsp56kCommandIcons", "icons"],
	["fsdsp56kAuditionIcons", "icons"],
	["fsdsp56kCreditIcons", "icons"],
	["fsdsp56kSynthIcons", "icons"],
	["fsdsp56kWindowGlyphs", "icons"]
], Xa = (() => {
	let e = window.fsdsp56kFont;
	return fa(e.height, e.spacing, e.glyphs);
})(), Za = vi(2, P((e) => {
	let t = e[0];
	return [ei(P((e) => [e[0][0], J(e[1])], Object.entries(window[t].palette || {})), { Compare: (e, t) => b(e, t) | 0 }), P((e) => [e[0], Nt(e[1].trim(), ["\n"], void 0, 0)], Object.entries(window[t][e[1]] || {}))];
}, Ya));
function Qa(e, t) {
	e.width !== t.w && (e.width = t.w), e.height !== t.h && (e.height = t.h);
	let n = e.getContext("2d"), r = n.createImageData(t.w, t.h);
	r.data.set(t.px), n.putImageData(r, 0, 0);
}
function $a() {
	return matchMedia("(prefers-reduced-motion: reduce)").matches;
}
(class e extends xe {
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
});
var eo = class extends C {
	constructor(e, t, n, r, i, a, o, s, c, l, u, d, f) {
		super(), this.name = e, this.ground = t, this.panel = n, this.line = r, this.edgeLit = i, this.edgeShade = a, this.fg = o, this.dim = s, this.accent = c, this.font = l, this.radius = u | 0, this.ink = d, this.select = f;
	}
}, to = J("#3A4A62"), no = J("#A6B4C6"), ro = J("#30B9DB"), io = new eo("house", J("#060A11"), J("#0D1726"), J("#1A2A40"), J("#24395A"), J("#1A2A40"), J("#DFE7F1"), J("#7F90A7"), ro, "-apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif", 10, (e, t, n, r, i) => {
	let a;
	switch (e.tag) {
		case 0:
		case 2:
			a = 3;
			break;
		default: a = X(i) < 24 ? 0 : (n + r) % 2 == 1 ? 1 : 2;
	}
	switch (a) {
		case 0: return [i, 1];
		case 1: return;
		case 2: return [Y(to, no, X(i) / 255), 1];
		default: return [ci(i, .45 * t), 1];
	}
}, (e, t) => {
	Oi(e, ro, 1);
	for (let t = 0; t <= e.w - 1; t++) pi(t, 1) < .5 && Z(e, t, 1, ro, .35), pi(t, e.h - 2) < .5 && Z(e, t, e.h - 2, ro, .35);
	let n = (t * .6 % 1.6 - .3) * e.w;
	for (let t = 2; t <= e.h - 3; t++) for (let r = 0; r <= e.w - 1; r++) {
		let i = r - n + t * .7;
		i > -1.5 && i < 1.5 && Ci(e, r, t) && Z(e, r, t, oi, .45);
	}
}), ao = J("#060A11");
function oo(e) {
	return Y(Y(e, li(e), .7), ao, .2);
}
var so = new eo("muted", io.ground, J("#0A111C"), J("#152234"), J("#1B2B42"), J("#152234"), J("#C9D4E2"), J("#66768C"), J("#5F8FA8"), io.font, 6, (e, t, n, r, i) => {
	if (X(i) < 16) return [i, 1];
	switch (e.tag) {
		case 2: return [Y(oo(i), i, .6 + .4 * t), 1];
		case 0: return [Y(oo(i), i, .7 * t), 1];
		default: return (n + r) % 2 == 1 ? void 0 : [Y(oo(i), ao, .35), 1];
	}
}, (e, t) => {
	Oi(e, J("#5F8FA8"), .8);
}), Q = [
	J("#0055AA"),
	J("#FFFFFF"),
	J("#000022"),
	J("#FF8800")
];
function co(e) {
	if (ui(e) > .3) {
		let t = di(e);
		return t < 70 || t > 330;
	}
	return !1;
}
function lo(e) {
	if (ui(e) > .12) {
		let t = di(e);
		return t > 170 && t < 260;
	}
	return !1;
}
function uo(e, t, n, r) {
	if (X(r) < 12) return 2;
	{
		let i = N(2, (X(r) / 255 + .35 * n) * 2.4);
		switch (mi(e, t, i) | 0) {
			case 0: return lo(r) ? 0 : 2;
			case 1: return co(r) ? 3 : 0;
			default: return co(r) && i < 1.85 ? 3 : 1;
		}
	}
}
function fo(e, t, n) {
	let r = Ti(e, t, n);
	if (r == null) return 0;
	{
		let e = r;
		return ge(rn((t) => y(t, e), Q), 0) | 0;
	}
}
var po = new eo("workbench", F(0, Q), F(0, Q), F(1, Q), F(1, Q), F(2, Q), F(1, Q), F(1, Q), F(3, Q), "'Topaz', 'Topaz a500a1', ui-monospace, Menlo, monospace", 0, (e, t, n, r, i) => {
	let a;
	switch (a = e.tag === 1 && (n + r) % 2 == 0 ? 0 : 1, a) {
		case 0: return [F(0, Q), 1];
		default: return [F(uo(n, r, t, i), Q), 1];
	}
}, (e, t) => {
	for (let t = 0; t <= e.h - 1; t++) for (let n = 0; n <= e.w - 1; n++) Z(e, n, t, F(3 - fo(e, n, t), Q), 1);
}), $ = [
	J("#031008"),
	J("#0B3B1B"),
	J("#1C7F3D"),
	J("#3FD46E"),
	J("#A8FFC4")
], mo = [
	io,
	so,
	po,
	new eo("phosphor", J("#020A05"), J("#04140A"), F(1, $), F(2, $), F(1, $), J("#7DFFA8"), F(2, $), F(4, $), "ui-monospace, Menlo, monospace", 2, (e, t, n, r, i) => {
		let a = e.tag === 2 ? X(i) / 255 + .15 + .3 * t : e.tag === 0 ? X(i) / 255 + .3 * t : X(i) / 255 * .5, o;
		switch (o = X(i) < 16 ? 0 : e.tag === 1 && (n + r) % 2 == 1 ? 1 : 2, o) {
			case 0: return [F(0, $), 1];
			case 1: return;
			default: return [F(N(4, mi(n, r, N(4, a * 4.2))), $), 1];
		}
	}, (e, t) => {
		Oi(e, F(3, $), 1);
		for (let t = 1; t <= e.w - 2; t++) pi(t, 1) < .5 && Z(e, t, 1, F(2, $), 1), pi(t, e.h - 2) < .5 && Z(e, t, e.h - 2, F(2, $), 1);
	})
];
function ho(e) {
	return ge(nn((t) => t.name === e, mo), io);
}
function go(e) {
	return e.closest("[data-aesthetic]");
}
function _o(e) {
	let t = go(e);
	return t == null ? io : ho(t.getAttribute("data-aesthetic"));
}
function vo(e, t) {
	t(_o(e)), go(e)?.addEventListener("aestheticchange", (n) => {
		t(_o(e));
	});
}
var yo = "\n.fsdsp56k-heading-art { display: block; max-width: 100%; height: auto; image-rendering: pixelated; }\na.fsdsp56k-heading-art { width: fit-content; }\n.fsdsp56k-heading-words { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); clip-path: inset(50%); white-space: nowrap; }\n.fsdsp56k-heading:has(.fsdsp56k-heading-words :focus-visible) .fsdsp56k-heading-art { outline: 2px solid currentColor; outline-offset: 3px; }\n@media (forced-colors: active), print {\n  .fsdsp56k-heading-art { display: none !important; }\n  .fsdsp56k-heading-words { position: static !important; width: auto !important; height: auto !important; overflow: visible !important;\n                            clip: auto !important; clip-path: none !important; white-space: normal !important; }\n}\n";
function bo() {
	if (ln(document.getElementById("fsdsp56k-heading-css"))) {
		let e = document.createElement("style");
		e.id = "fsdsp56k-heading-css", e.textContent = yo, document.head.appendChild(e);
	}
}
function xo(e) {
	let t = ge(ya(getComputedStyle(e).color), J("#DFE7F1"));
	return new va(t, ((e) => {
		ground: for (;;) {
			let t = e;
			if (ln(t)) return J("#FFFFFF");
			{
				let n = ya(getComputedStyle(t).backgroundColor);
				if (n == null) {
					e = t.parentElement;
					continue ground;
				}
				return n;
			}
		}
	})(e), ge(An((t) => ya(getComputedStyle(e).getPropertyValue(t)), On(["--accent", "--cyan"])), t));
}
var So = class extends C {
	constructor(e, t, n, r, i, a, o) {
		super(), this.heading = e, this.text = t, this.level = n | 0, this.canvas = r, this.plan = i, this.light = a, this.started = o;
	}
};
function Co(e) {
	let t = window.devicePixelRatio || 1;
	return M(1, fe((e === 1 ? 3 : 2) * t)) / t;
}
function wo(e) {
	let t = e.querySelectorAll(".anchor"), n = Xn(R(() => B((e) => t.item(e).textContent, Xi(0, 1, t.length - 1))));
	return vt(En((e, t) => t === "" ? e : Mt(e, t, ""), e.textContent, n), "\\s+", " ").trim();
}
function To(e, t) {
	bo();
	let n = $a(), r = e.querySelectorAll(t), i = z(R(() => V((e) => {
		let t = r.item(e), n = wo(t);
		if (n !== "") {
			let e, r = t.tagName;
			e = r === "H1" ? 1 : r === "H2" ? 2 : 3;
			let i = document.createElement("canvas"), a = document.createElement("span");
			return a.className = "fsdsp56k-heading-words", Qn(tr(() => !ln(t.firstChild), R(() => (a.appendChild(t.firstChild), Jn()))), R(() => {
				let r = a.querySelector("a[href]"), o;
				if (ln(r)) o = i;
				else {
					let e = document.createElement("a");
					e.setAttribute("href", r.getAttribute("href")), e.setAttribute("tabindex", "-1"), e.appendChild(i), o = e;
				}
				return o.classList.add("fsdsp56k-heading-art"), i.setAttribute("aria-hidden", "true"), o.setAttribute("aria-hidden", "true"), t.classList.add("fsdsp56k-heading"), t.appendChild(o), t.appendChild(a), Yn(new So(t, n, e, i, new w(Wa(Za, Xa, n, e, 1)), new w(Ga.Idle), new w(0)));
			}));
		}
		return Jn();
	}, Xi(0, 1, r.length - 1)))), a = (t) => {
		let n;
		Qa(t.canvas, Ja(xo(t.heading), (n = e.getAttribute("data-heading-style"), n === l() || n === "" ? F(0, Ca) : n), t.plan.contents, t.light.contents));
	}, o = (e) => {
		let t = Co(e.level), n = ~~(e.heading.clientWidth / t) | 0;
		n !== e.plan.contents.w && (e.plan.contents = Wa(Za, Xa, e.text, e.level, n)), e.canvas.style.width = `${e.plan.contents.w * t}px`, a(e);
	}, s = new w(0), c = (e) => {
		let t = !1;
		for (let e = 0; e <= i.length - 1; e++) {
			let n = F(e, i);
			if (n.light.contents.tag === 1) {
				let e = (performance.now() - n.started.contents) / 1e3;
				n.light.contents = e >= .3 + .15 ? Ga.Idle : new Ga(1, [e]), t = t ? !0 : e < Ka + qa, a(n);
			}
		}
		s.contents = t ? window.requestAnimationFrame(c) : 0;
	}, u = (e) => {
		n ? (e.light.contents = Ga.Lit, a(e)) : e.light.contents.tag === 1 || (e.started.contents = performance.now(), e.light.contents = new Ga(1, [0]), a(e), s.contents === 0 && (s.contents = window.requestAnimationFrame(c)));
	}, d = (e) => {
		n && (e.light.contents = Ga.Idle, a(e));
	};
	for (let e = 0; e <= i.length - 1; e++) {
		let t = F(e, i);
		t.heading.addEventListener("pointerenter", (e) => {
			u(t);
		}), t.heading.addEventListener("pointerleave", (e) => {
			d(t);
		}), t.heading.addEventListener("focusin", (e) => {
			u(t);
		}), t.heading.addEventListener("focusout", (e) => {
			d(t);
		});
	}
	for (let e = 0; e <= i.length - 1; e++) {
		let t = F(e, i);
		new ResizeObserver(() => {
			o(t);
		}).observe(t.heading);
	}
	let f = () => {
		matchMedia("(resolution: " + (window.devicePixelRatio || 1).toString() + "dppx)").addEventListener("change", (() => {
			for (let e = 0; e <= i.length - 1; e++) o(F(e, i));
			f();
		}), { once: !0 });
	};
	f(), new MutationObserver(() => {
		for (let e = 0; e <= i.length - 1; e++) a(F(e, i));
	}).observe(e, { attributes: !0 }), new MutationObserver(() => {
		for (let e = 0; e <= i.length - 1; e++) a(F(e, i));
	}).observe(document.documentElement, { attributes: !0 }), vo(e, (e) => {
		for (let e = 0; e <= i.length - 1; e++) o(F(e, i));
	});
}
function Eo(e, t) {
	To(e, t);
}
var Do = document.querySelectorAll("[data-headings]");
for (let e = 0; e <= Do.length - 1; e++) {
	let t = Do.item(e);
	Eo(t, t.getAttribute("data-headings"));
}
window.fsdsp56kHeadings = { mount: (e, t) => {
	Eo(e, t);
} }, window.dispatchEvent(new CustomEvent("fsdsp56k-headings"));
