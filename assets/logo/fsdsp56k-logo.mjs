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
var m = class {
	constructor(e) {
		this.factory = e, this.isValueCreated = !1;
	}
	get Value() {
		return this.isValueCreated ||= (this.createdValue = this.factory(), !0), this.createdValue;
	}
	get IsValueCreated() {
		return this.isValueCreated;
	}
};
function h(e, t) {
	return e.toString(10).padStart(t, "0");
}
function g(t) {
	let n = t;
	return typeof n.offset == "number" ? n.offset : t.kind === e.Utc ? 0 : t.getTimezoneOffset() * -6e4;
}
function _(e, t) {
	return e = e < 0 && t != null && t !== 10 ? 4294967295 + e + 1 : e, e.toString(t);
}
var v = class e {
	static id(t) {
		return e.idMap.has(t) || e.idMap.set(t, ++e.count), e.idMap.get(t);
	}
};
v.idMap = /* @__PURE__ */ new WeakMap(), v.count = 0;
function ee(e) {
	let t = 0, n = 5381, r = e.length;
	for (; t < r;) n = n * 33 ^ e.charCodeAt(t++);
	return n;
}
function te(e) {
	return e * 2654435761 | 0;
}
function ne(e) {
	return ee(e.toString(32));
}
function re(e) {
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
	for (let r = 0; r < t; r++) n[r] = oe(e[r]);
	return re(n);
}
function oe(e) {
	if (e == null) return 0;
	switch (typeof e) {
		case "boolean": return +!!e;
		case "number": return te(e);
		case "bigint": return ne(e);
		case "string": return ee(e);
		default: return o(e) ? e.GetHashCode() : n(e) ? ae(e) : e instanceof Date ? ie(e) : Object.getPrototypeOf(e)?.constructor === Object ? re(Object.values(e).map((e) => oe(e))) : te(v.id(e));
	}
}
function se(e, t, n) {
	if (e == null) return t == null;
	if (t == null || e.length !== t.length) return !1;
	for (let r = 0; r < e.length; r++) if (!n(e[r], t[r])) return !1;
	return !0;
}
function ce(e, t) {
	return se(e, t, y);
}
function le(e, t) {
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return !1;
	n.sort(), r.sort();
	for (let i = 0; i < n.length; i++) if (n[i] !== r[i] || !y(e[n[i]], t[r[i]])) return !1;
	return !0;
}
function y(e, t) {
	return e === t ? !0 : e == null ? t == null : t == null ? !1 : a(e) ? e.Equals(t) : n(e) ? n(t) && ce(e, t) : typeof e == "object" ? e instanceof Date ? t instanceof Date && ue(e, t) === 0 : Object.getPrototypeOf(e)?.constructor === Object && le(e, t) : !1;
}
function ue(e, t) {
	let n, r;
	return "offset" in e && "offset" in t ? (n = e.getTime(), r = t.getTime()) : (n = e.getTime() + g(e), r = t.getTime() + g(t)), n === r ? 0 : n < r ? -1 : 1;
}
function b(e, t) {
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
	return de(e, t, me);
}
function pe(e, t) {
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return n.length < r.length ? -1 : 1;
	n.sort(), r.sort();
	for (let i = 0, a = 0; i < n.length; i++) {
		let o = n[i];
		if (o !== r[i]) return o < r[i] ? -1 : 1;
		if (a = me(e[o], t[o]), a !== 0) return a;
	}
	return 0;
}
function me(e, t) {
	return e === t ? 0 : e == null ? t == null ? 0 : -1 : t == null ? 1 : i(e) ? e.CompareTo(t) : n(e) ? n(t) ? fe(e, t) : -1 : typeof e == "object" ? e instanceof Date ? t instanceof Date ? ue(e, t) : -1 : Object.getPrototypeOf(e)?.constructor === Object ? pe(e, t) : -1 : b(e, t);
}
function he(e, t = 0) {
	let n = 10 ** t, r = +(t ? e * n : e).toFixed(8), i = Math.floor(r), a = r - i, o = a > .49999999 && a < .50000001 ? i % 2 == 0 ? i : i + 1 : Math.round(r);
	return t ? o / n : o;
}
function ge(e) {
	return e > 0 ? 1 : e < 0 ? -1 : 0;
}
function _e(e, t, n, r, i) {
	if (ArrayBuffer.isView(e) && ArrayBuffer.isView(n)) n.set(e.subarray(t, t + i), r);
	else for (let a = 0; a < i; ++a) n[r + a] = e[t + a];
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Types.js
function ve(e) {
	let t = 0, n = "[";
	for (let r of e) {
		if (t === 0) n += ye(r);
		else if (t === 100) {
			n += "; ...";
			break;
		} else n += "; " + ye(r);
		t++;
	}
	return n + "]";
}
function ye(e, t = 0) {
	return typeof e == "string" ? "\"" + e + "\"" : be(e, t);
}
function be(e, t = 0) {
	if (typeof e == "object" && e) {
		if (typeof e.toString == "function" && e.toString !== Object.prototype.toString) return e.toString();
		if (Symbol.iterator in e) return ve(e);
		{
			let n = Object.getPrototypeOf(e)?.constructor;
			return n === Object && t < 10 ? "{ " + Object.entries(e).map(([e, n]) => e + " = " + ye(n, t + 1)).join("\n  ") + " }" : n?.name ?? "";
		}
	}
	return String(e);
}
function xe(e, t) {
	if (t.length === 0) return e;
	{
		let n, r = !0;
		return t.length === 1 ? (n = ye(t[0]), r = n.indexOf(" ") >= 0) : n = t.map((e) => ye(e)).join(", "), e + (r ? " (" : " ") + n + (r ? ")" : "");
	}
}
var Se = class {
	get name() {
		return this.cases()[this.tag];
	}
	toJSON() {
		return this.fields.length === 0 ? this.name : [this.name].concat(this.fields);
	}
	toString() {
		return xe(this.name, this.fields);
	}
	GetHashCode() {
		let e = this.fields.map((e) => oe(e));
		return e.splice(0, 0, te(this.tag)), re(e);
	}
	Equals(e) {
		return this === e ? !0 : u(this, e) ? this.tag === e.tag && ce(this.fields, e.fields) : !1;
	}
	CompareTo(e) {
		return this === e ? 0 : u(this, e) ? this.tag === e.tag ? fe(this.fields, e.fields) : this.tag < e.tag ? -1 : 1 : -1;
	}
};
function Ce(e) {
	let t = {}, n = Object.keys(e);
	for (let r = 0; r < n.length; r++) t[n[r]] = e[n[r]];
	return t;
}
function we(e) {
	return "{ " + Object.entries(e).map(([e, t]) => e + " = " + ye(t)).join("\n  ") + " }";
}
function Te(e) {
	return re(Object.values(e).map((e) => oe(e)));
}
function Ee(e, t) {
	if (e === t) return !0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) if (!y(e[n[r]], t[n[r]])) return !1;
		return !0;
	}
	return !1;
}
function De(e, t) {
	if (e === t) return 0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) {
			let i = me(e[n[r]], t[n[r]]);
			if (i !== 0) return i;
		}
		return 0;
	}
	return -1;
}
var x = class {
	toJSON() {
		return Ce(this);
	}
	toString() {
		return we(this);
	}
	GetHashCode() {
		return Te(this);
	}
	Equals(e) {
		return Ee(this, e);
	}
	CompareTo(e) {
		return De(this, e);
	}
}, Oe = class {
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
}, ke = Symbol("numeric");
function Ae(e) {
	return typeof e == "number" || typeof e == "bigint" || e?.[ke];
}
function je(e) {
	return typeof e == "number" && Number.isInteger(e) || typeof e == "bigint";
}
function Me(e, t) {
	return typeof e == "number" || typeof e == "bigint" ? e < t ? -1 : +(e > t) : e.CompareTo(t);
}
function Ne(e, t) {
	return typeof e == "number" ? e * t : typeof e == "bigint" ? e * BigInt(t) : e[ke]().multiply(t);
}
function Pe(e, t) {
	return typeof e == "number" ? e / t : typeof e == "bigint" ? Number(e) / t : e[ke]().multiply(1 / t);
}
function Fe(e, t) {
	return typeof e == "number" ? e.toFixed(t) : typeof e == "bigint" ? e.toString() : e[ke]().toFixed(t);
}
function Ie(e, t) {
	return typeof e == "number" ? e.toPrecision(t) : typeof e == "bigint" ? e.toString() : e[ke]().toPrecision(t);
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
	return typeof e == "number" ? e.toExponential(t) : typeof e == "bigint" ? Le(e, t) : e[ke]().toExponential(t);
}
function ze(e) {
	return typeof e == "number" ? (Number(e) >>> 0).toString(16) : typeof e == "bigint" ? BigInt.asUintN(64, e).toString(16) : e[ke]().toHex();
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Option.js
var Be = class e {
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
		return oe(this.value);
	}
	Equals(t) {
		return t != null && y(this.value, t instanceof e ? t.value : t);
	}
	CompareTo(t) {
		return t == null ? 1 : me(this.value, t instanceof e ? t.value : t);
	}
};
function S(e) {
	if (e == null) throw new t("Option has no value");
	return e instanceof Be ? e.value : e;
}
function Ve(e) {
	return e == null || e instanceof Be ? new Be(e) : e;
}
function He(e, t) {
	return e == null ? t : S(e);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Global.js
var Ue = "Collection was empty.", We = "Specified argument was out of the range of valid values.", Ge = "Value cannot be null.", Ke = " (Parameter '", qe = "The given key was not present in the dictionary.", Je = [
	"Sun",
	"Mon",
	"Tue",
	"Wed",
	"Thu",
	"Fri",
	"Sat"
], Ye = [
	"Sunday",
	"Monday",
	"Tuesday",
	"Wednesday",
	"Thursday",
	"Friday",
	"Saturday"
], Xe = [
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
], Ze = [
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
function C(e, t, n) {
	let r = 0, i = t;
	for (; i < e.length && e[i] === n;) i++, r++;
	return r;
}
function Qe(e, t) {
	return t >= e.length - 1 ? -1 : e.charCodeAt(t + 1);
}
function $e(e, n) {
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
function et(n, r, i) {
	let a = 0, o = 0, s = "", c = i ? _t(n.getTime(), e.Utc) : n;
	for (; a < r.length;) {
		let n = r[a];
		switch (n) {
			case "d":
				switch (o = C(r, a, "d"), a += o, o) {
					case 1:
						s += bt(c);
						break;
					case 2:
						s += h(bt(c), 2);
						break;
					case 3:
						s += Je[Et(c)];
						break;
					default: s += Ye[Et(c)];
				}
				break;
			case "f":
				if (o = C(r, a, "f"), a += o, o <= 3) {
					let e = 10 ** (3 - o);
					s += h(Math.floor(St(c) / e), o);
				} else if (o <= 7) s += ("" + St(c)).padEnd(o, "0");
				else throw "Input string was not in a correct format.";
				break;
			case "F":
				if (o = C(r, a, "F"), a += o, o <= 3) {
					let e = 10 ** (3 - o), t = Math.floor(St(c) / e);
					t != 0 && (s += h(t, o));
				} else if (o <= 7) {
					let e = St(c);
					e != 0 && (s += h(e, 3));
				} else throw "Input string was not in a correct format.";
				break;
			case "g":
				o = C(r, a, "g"), a += o, s += "A.D.";
				break;
			case "h":
				switch (o = C(r, a, "h"), a += o, o) {
					case 1:
						let e = xt(c) % 12;
						s += e || 12;
						break;
					default:
						let t = xt(c) % 12;
						s += h(t || 12, 2);
				}
				break;
			case "H":
				switch (o = C(r, a, "H"), a += o, o) {
					case 1:
						s += xt(c);
						break;
					default: s += h(xt(c), 2);
				}
				break;
			case "K":
				if (o = C(r, a, "K"), a += o, o === 1) switch (tt(c)) {
					case e.Utc:
						s += "Z";
						break;
					case e.Local:
						s += nt(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified:
				}
				break;
			case "m":
				switch (o = C(r, a, "m"), a += o, o) {
					case 1:
						s += Ct(c);
						break;
					default: s += h(Ct(c), 2);
				}
				break;
			case "M":
				switch (o = C(r, a, "M"), a += o, o) {
					case 1:
						s += w(c);
						break;
					case 2:
						s += h(w(c), 2);
						break;
					case 3:
						s += Xe[w(c) - 1];
						break;
					default: s += Ze[w(c) - 1];
				}
				break;
			case "s":
				switch (o = C(r, a, "s"), a += o, o) {
					case 1:
						s += wt(c);
						break;
					default: s += h(wt(c), 2);
				}
				break;
			case "t":
				switch (o = C(r, a, "t"), a += o, o) {
					case 1:
						s += c.getHours() < 12 ? "A" : "P";
						break;
					default: s += c.getHours() < 12 ? "AM" : "PM";
				}
				break;
			case "y":
				switch (o = C(r, a, "y"), a += o, o) {
					case 1:
						s += Tt(c) % 100;
						break;
					case 2:
						s += h(Tt(c) % 100, 2);
						break;
					default: s += h(Tt(c), o);
				}
				break;
			case "z":
				o = C(r, a, "z"), a += o;
				let l = "";
				switch (tt(c)) {
					case e.Utc:
						l = "+00:00";
						break;
					case e.Local:
						l = nt(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified: l = nt(yt(c).getTimezoneOffset() * -6e4);
				}
				let u = l[0] === "-" ? "-" : "+", d = parseInt(l.substring(1, 3), 10), f = parseInt(l.substring(4, 6), 10);
				switch (o) {
					case 1:
						s += `${u}${d}`;
						break;
					case 2:
						s += `${u}${h(d, 2)}`;
						break;
					default: s += `${u}${h(d, 2)}:${h(f, 2)}`;
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
				let [p, m] = $e(r, a);
				s += p, a += m;
				break;
			case "%":
				let g = Qe(r, a);
				if (g >= 0 && g !== 37) a += 2, s += et(c, String.fromCharCode(g), i);
				else throw new t("Invalid format string");
				break;
			case "\\":
				let _ = Qe(r, a);
				if (_ >= 0) a += 2, s += String.fromCharCode(_);
				else throw new t("Invalid format string");
				break;
			default: a++, s += n;
		}
	}
	return s;
}
function tt(t) {
	return t.kind ?? e.Unspecified;
}
function nt(e) {
	let t = e < 0;
	e = Math.abs(e);
	let n = ~~(e / 36e5), r = e % 36e5 / 6e4;
	return (t ? "-" : "+") + h(n, 2) + ":" + h(r, 2);
}
function rt(t, n) {
	if (n) return t.toISOString();
	{
		let n = t.kind == null || t.kind === e.Local;
		return h(t.getFullYear(), 4) + "-" + h(t.getMonth() + 1, 2) + "-" + h(t.getDate(), 2) + "T" + h(t.getHours(), 2) + ":" + h(t.getMinutes(), 2) + ":" + h(t.getSeconds(), 2) + "." + h(t.getMilliseconds(), 3) + (n ? nt(t.getTimezoneOffset() * -6e4) : "");
	}
}
function it(e, t) {
	let n = e.toISOString();
	return n.substring(0, n.length - 1) + nt(t);
}
function at(n, r) {
	let i = new Date(n.getTime() + (n.offset ?? 0));
	if (typeof r != "string") return i.toISOString().replace(/\.\d+/, "").replace(/[A-Z]|\.\d+/g, " ") + nt(n.offset ?? 0);
	if (r.length === 1) switch (r) {
		case "D": return ot(i);
		case "d": return st(i);
		case "F": return ot(i) + " " + ct(i);
		case "f": return ot(i) + " " + lt(i);
		case "G": return st(i) + " " + ct(i);
		case "g": return st(i) + " " + lt(i);
		case "M":
		case "m": return pt(i);
		case "O":
		case "o": return it(i, n.offset ?? 0);
		case "R":
		case "r": return ut(_t(n.getTime(), e.Utc));
		case "s": return dt(vt(i));
		case "T": return ct(vt(i));
		case "t": return lt(vt(i));
		case "u": return ft(_t(n.getTime(), e.Utc));
		case "U": {
			let t = _t(n.getTime(), e.Utc);
			return ot(t) + " " + ct(t);
		}
		case "Y":
		case "y": return mt(i);
		default: throw new t("Unrecognized Date print format");
	}
	else return et(i, r, !0);
}
function ot(e) {
	return Ye[Et(e)] + ", " + h(bt(e), 2) + " " + Ze[w(e) - 1] + " " + Tt(e);
}
function st(e) {
	return h(w(e), 2) + "/" + h(bt(e), 2) + "/" + Tt(e);
}
function ct(e) {
	return h(xt(e), 2) + ":" + h(Ct(e), 2) + ":" + h(wt(e), 2);
}
function lt(e) {
	return h(xt(e), 2) + ":" + h(Ct(e), 2);
}
function ut(e) {
	let t = vt(e);
	return Je[Et(t)] + ", " + h(bt(t), 2) + " " + Xe[w(t) - 1] + " " + Tt(t) + " " + h(xt(t), 2) + ":" + h(Ct(t), 2) + ":" + h(wt(t), 2) + " GMT";
}
function dt(e) {
	return h(Tt(e), 4) + "-" + h(w(e), 2) + "-" + h(bt(e), 2) + "T" + h(xt(e), 2) + ":" + h(Ct(e), 2) + ":" + h(wt(e), 2);
}
function ft(e) {
	let t = vt(e);
	return h(Tt(t), 4) + "-" + h(w(t), 2) + "-" + h(bt(t), 2) + " " + h(xt(t), 2) + ":" + h(Ct(t), 2) + ":" + h(wt(t), 2) + "Z";
}
function pt(e) {
	return Ze[w(e) - 1] + " " + h(bt(e), 2);
}
function mt(e) {
	return Tt(e) + " " + Ze[w(e) - 1];
}
function ht(n, r) {
	let i = n.kind === e.Utc;
	if (typeof r != "string") return st(n) + " " + ct(n);
	if (r.length === 1) switch (r) {
		case "D": return ot(n);
		case "d": return st(n);
		case "F": return ot(n) + " " + ct(n);
		case "f": return ot(n) + " " + lt(n);
		case "G": return st(n) + " " + ct(n);
		case "g": return st(n) + " " + lt(n);
		case "M":
		case "m": return pt(n);
		case "O":
		case "o": return rt(n, i);
		case "R":
		case "r": return ut(n);
		case "s": return dt(n);
		case "T": return ct(n);
		case "t": return lt(n);
		case "u": return ft(n);
		case "U": return ot(vt(n)) + " " + ct(vt(n));
		case "Y":
		case "y": return mt(n);
		default: throw new t("Unrecognized Date print format");
	}
	else return et(n, r, i);
}
function gt(e, t, n) {
	return e.offset == null ? ht(e, t) : at(e, t);
}
function _t(t, n) {
	let r = new Date(t);
	return r.kind = n ?? e.Unspecified, r;
}
function vt(t) {
	return t.kind === e.Utc ? t : _t(t.getTime(), e.Utc);
}
function yt(t) {
	return t.kind === e.Local ? t : _t(t.getTime(), e.Local);
}
function bt(t) {
	return t.kind === e.Utc ? t.getUTCDate() : t.getDate();
}
function xt(t) {
	return t.kind === e.Utc ? t.getUTCHours() : t.getHours();
}
function St(t) {
	return t.kind === e.Utc ? t.getUTCMilliseconds() : t.getMilliseconds();
}
function Ct(t) {
	return t.kind === e.Utc ? t.getUTCMinutes() : t.getMinutes();
}
function w(t) {
	return (t.kind === e.Utc ? t.getUTCMonth() : t.getMonth()) + 1;
}
function wt(t) {
	return t.kind === e.Utc ? t.getUTCSeconds() : t.getSeconds();
}
function Tt(t) {
	return t.kind === e.Utc ? t.getUTCFullYear() : t.getFullYear();
}
function Et(t) {
	return t.kind === e.Utc ? t.getUTCDay() : t.getDay();
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/RegExp.js
function Dt(e) {
	return e.replace(/[$()*+.?[\\\^{|}\]]/g, "\\$&");
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/String.js
var Ot = Symbol.for("Fable.DateTimeFormattable");
function kt(e) {
	return e != null && typeof e[Ot] == "function";
}
function At(e, t) {
	return e instanceof Date ? gt(e, t) : e[Ot](t);
}
var jt = /\{(\d+)(,-?\d+)?(?:\:([a-zA-Z])(\d{0,2})|\:(.+?))?\}/g;
function Mt(e, t) {
	return Me(e, t) < 0;
}
function Nt(e) {
	let [t, n] = e.split(".");
	return n === void 0 && (n = ""), {
		integral: t,
		decimal: n
	};
}
function Pt(e) {
	return e.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}
function Ft(e, ...n) {
	let r;
	return typeof e == "object" ? (r = String(n[0]), n.shift()) : r = e, r.replace(jt, (e, r, i, a, o, s) => {
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
				let e = Mt(c, 0);
				Mt(c, 0) && (c = Ne(c, -1)), o ??= 2, c = Fe(c, o), l = Nt(c), c = o > 0 ? "¤" + Pt(l.integral) + "." + Bt(l.decimal, o, "0") : "¤" + Pt(l.integral), e && (c = "(" + c + ")");
				break;
			case "d":
			case "D":
				if (!je(c)) throw new t("Format specifier was invalid.");
				c = String(c), o != null && (c = c.startsWith("-") ? "-" + zt(c.substring(1), o, "0") : zt(c, o, "0"));
				break;
			case "e":
			case "E": {
				o ??= 6, c = String(Re(c, o));
				let e = c.indexOf("e");
				c = c.slice(0, e) + a + c[e + 1] + zt(c.slice(e + 2), 3, "0");
				break;
			}
			case "f":
			case "F":
				o ??= 2, c = Fe(c, o), o > 0 && (l = Nt(c), c = l.integral + "." + Bt(l.decimal, o, "0"));
				break;
			case "g":
			case "G": {
				c = o == null ? Ie(c) : Ie(c, o);
				let e = c.indexOf("e");
				if (e >= 0) {
					let t = Ht(Ht(c.slice(0, e), "0"), "."), n = c[e + 1], r = c.slice(e + 2), i = r.length < 2 ? "0" + r : r;
					c = t + (a === "G" ? "E" : "e") + n + i;
				} else c.indexOf(".") >= 0 && (c = Ht(Ht(c, "0"), "."));
				break;
			}
			case "n":
			case "N":
				o ??= 2, c = Fe(c, o), l = Nt(c), c = o > 0 ? Pt(l.integral) + "." + Bt(l.decimal, o, "0") : Pt(l.integral);
				break;
			case "p":
			case "P":
				o ??= 2, c = Fe(Ne(c, 100), o), l = Nt(c), c = o > 0 ? Pt(l.integral) + "." + Bt(l.decimal, o, "0") + " %" : Pt(l.integral) + " %";
				break;
			case "r":
			case "R": throw new t("The round-trip format is not supported by Fable");
			case "x":
			case "X":
				if (!je(c)) throw new t("Format specifier was invalid.");
				o ??= 1, c = zt(ze(c), o, "0"), a === "X" && (c = c.toUpperCase());
				break;
			default:
				if (a) throw new t("Format specifier was invalid.");
				if (s) {
					let e = "", t = s, n = (t.match(/%/g) ?? []).length;
					n > 0 && (c = Ne(c, 100 ** n)), c = t.replace(/([0#,]+)(\.[0#]+)?(,*)/, (t, n, r) => {
						let i = /,*$/.exec(n)[0].length;
						i > 0 && (n = n.substring(0, n.length - i), c = Pe(c, 1e3 ** i)), Mt(c, 0) && (c = Ne(c, -1), e = "-"), r = r == null ? "" : r.substring(1), c = Fe(c, Math.max(r.length, 0));
						let [a, o] = c.split(".");
						o ||= "";
						let s = n.replace(/,/g, "").replace(/^#+/, "").length;
						a = zt(a, s, "0");
						let l = r.replace(/#+$/, "").length;
						if (l > o.length ? o = Bt(o, l, "0") : l < o.length && (o = o.substring(0, l) + o.substring(l).replace(/0+$/, "")), n.indexOf(",") > 0) {
							let e = a.length % 3, t = Math.floor(a.length / 3), n = e > 0 ? a.substr(0, e) + (t > 0 ? "," : "") : "";
							for (let r = 0; r < t; r++) n += a.substr(e + r * 3, 3) + (r < t - 1 ? "," : "");
							a = n;
						}
						return o.length > 0 ? a + "." + o : a;
					}), c = e + c;
				}
		}
		else c = c instanceof Date || kt(c) ? At(c, s || a) : be(c);
		return i = parseInt((i || " ").substring(1), 10), isNaN(i) || (c = Rt(String(c), Math.abs(i), " ", i < 0)), c;
	});
}
function It(e) {
	return typeof e != "string" || e.length === 0;
}
function Lt(...e) {
	return e.map((e) => String(e)).join("");
}
function T(e, t) {
	return Array.isArray(t) ? t.join(e) : Array.from(t).join(e);
}
function Rt(e, t, n, r) {
	n ||= " ", t -= e.length;
	for (let i = 0; i < t; i++) e = r ? e + n : n + e;
	return e;
}
function zt(e, t, n) {
	return Rt(e, t, n);
}
function Bt(e, t, n) {
	return Rt(e, t, n, !0);
}
function Vt(e, n, r, i) {
	if (r = typeof r == "number" ? r : void 0, i = typeof i == "number" ? i : 0, r && r < 0) throw new t("Count cannot be less than zero");
	if (r === 0) return [];
	let a = (i & 1) == 1, o = (i & 2) == 2;
	n ||= [], n = n.filter((e) => e).map(Dt), n = n.length > 0 ? n : ["\\s"];
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
function Ht(e, ...t) {
	return t.length === 0 ? e.trimEnd() : e.replace(RegExp("[" + Dt(t.join("")) + "]+$"), "");
}
function Ut(e, n, r) {
	if (n + (r || 0) > e.length) throw new t("Invalid startIndex and/or length");
	return r == null ? e.substr(n) : e.substr(n, r);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/System.js
var Wt = class extends t {
	constructor(e) {
		super(e);
	}
};
function Gt(e) {
	return new Wt(e);
}
var Kt = class extends t {
	constructor(e) {
		super(e);
	}
};
function qt(e) {
	return new Kt(e);
}
var Jt = class extends t {
	constructor(e, t, n) {
		super(It(t) ? e : e + Ke + t + "')", n), this.paramName = t;
	}
}, Yt = class extends Jt {
	constructor(e, t) {
		super(t, e, l());
	}
};
function Xt(e, t) {
	return new Yt(e, t);
}
function Zt(e) {
	return Xt(e, Ge);
}
var Qt = class extends Jt {
	constructor(e, t) {
		super(t, e, l());
	}
};
function $t(e, t) {
	return new Qt(e, t);
}
function en(e) {
	return $t(e, We);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Native.js
function E(e, t) {
	if (e == null) return Array(t);
	{
		let n = e;
		return typeof n == "function" ? new n(t) : Array(t);
	}
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Double.js
function D(e, t) {
	return Math.max(e, t);
}
function O(e, t) {
	return Math.min(e, t);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Array.js
function tn() {
	throw new t("An index satisfying the predicate was not found in the collection.");
}
function nn() {
	throw new t("Arrays had different lengths");
}
function k(e, t, n) {
	let r = e.length | 0, i = t.length | 0, a = E(n, r + i);
	for (let t = 0; t <= r - 1; t++) M(a, t, j(t, e));
	for (let e = 0; e <= i - 1; e++) M(a, e + r, j(e, t));
	return a;
}
function rn(e, t, n, r) {
	let i = t | 0;
	return e.fill(r, i, i + n);
}
function an(e, t, n) {
	let r = t.length | 0, i = E(n, r);
	for (let n = 0; n <= r - 1; n++) M(i, n, e(n, j(n, t)));
	return i;
}
function A(e, t, n) {
	let r = t.length | 0, i = E(n, r);
	for (let n = 0; n <= r - 1; n++) M(i, n, e(j(n, t)));
	return i;
}
function on(e) {
	let t = e.length | 0, n = Array(t);
	for (let r = 0; r <= t - 1; r++) M(n, r, [r, j(r, e)]);
	return n;
}
function sn(e, t) {
	let n = D(0, e) | 0;
	return t.slice(0, 0 + n);
}
function cn(e, t) {
	let n = Array.isArray(e) ? e : Array.from(e);
	switch (n.length | 0) {
		case 0: return E(t, 0);
		case 1: return j(0, n);
		default: {
			let e = 0, r = 0;
			for (let e = 0; e <= n.length - 1; e++) {
				let t = j(e, n);
				r = r + t.length | 0;
			}
			let i = E(t, r);
			for (let t = 0; t <= n.length - 1; t++) {
				let r = j(t, n);
				for (let t = 0; t <= r.length - 1; t++) M(i, e, j(t, r)), e = e + 1 | 0;
			}
			return i;
		}
	}
}
function ln(e, t, n) {
	return cn(A(e, t, l()), n);
}
function un(e, t) {
	let n = E(t, 1);
	return M(n, 0, e), n;
}
function dn(e, n, r) {
	if (e < 0) throw new t("The input must be non-negative (Parameter 'count')");
	let i = E(r, e);
	for (let t = 0; t <= e - 1; t++) M(i, t, n(t));
	return i;
}
function fn(e) {
	if (e.length < 2) return [];
	{
		let t = e.length - 1 | 0, n = Array(t);
		for (let r = 0; r <= t - 1; r++) M(n, r, [j(r, e), j(r + 1, e)]);
		return n;
	}
}
function pn(e) {
	return e.slice();
}
function mn(e, t, n, r, i) {
	_e(e, t, n, r, i);
}
function hn(e) {
	return e.slice().reverse();
}
function gn(e, t, n, r) {
	let i = E(r, n.length + 1);
	M(i, 0, t);
	for (let t = 0; t <= n.length - 1; t++) M(i, t + 1, e(j(t, i), j(t, n)));
	return i;
}
function _n(e, n, r) {
	if (e > n.length) throw new t("count is greater than array length (Parameter 'count')");
	if (e === n.length) return E(r, 0);
	{
		let t = (e < 0 ? 0 : e) | 0;
		return n.slice(t);
	}
}
function vn(e, t, n) {
	let r = t.length | 0, i = E(n, r), a = E(n, r), o = 0, s = 0;
	for (let n = 0; n <= r - 1; n++) e(j(n, t)) ? (M(i, o, j(n, t)), o = o + 1 | 0) : (M(a, s, j(n, t)), s = s + 1 | 0);
	return [sn(o, i), sn(s, a)];
}
function yn(e, t) {
	return t.find(e);
}
function bn(e, t) {
	let n = t.findIndex(e) | 0;
	if (n > -1) return n;
}
function xn(e, t) {
	return ((n) => {
		loop: for (;;) {
			let r = n;
			if (r < 0) return tn(), -1;
			if (e(j(r, t))) return r | 0;
			n = r - 1;
			continue loop;
		}
	})(t.length - 1) | 0;
}
function Sn(e, t, n) {
	let r = e;
	return n.reduce(r, t);
}
function Cn(e, t) {
	for (let n = 0; n <= t.length - 1; n++) e(n, j(n, t));
}
function wn(e, t) {
	let n = e.slice();
	return n.sort((e, n) => t.Compare(e, n) | 0), n;
}
function Tn(e, t, n) {
	let r = t.slice();
	return r.sort((t, r) => n.Compare(e(t), e(r)) * -1 | 0), r;
}
function En(e, t) {
	e.length !== t.length && nn();
	let n = Array(e.length);
	for (let r = 0; r <= e.length - 1; r++) M(n, r, [j(r, e), j(r, t)]);
	return n;
}
function j(e, n) {
	if (e < 0 || e >= n.length) throw new t("Index was outside the bounds of the array. (Parameter 'index')");
	return n[e];
}
function M(e, n, r) {
	if (n < 0 || n >= e.length) throw new t("Index was outside the bounds of the array. (Parameter 'index')");
	e[n] = r;
}
function Dn(e, n) {
	if (n.length === 0) throw new t("The input array was empty");
	let r = e;
	return n.reduce(r);
}
function On(e, t, n) {
	let r = n.GetZero();
	for (let i = 0; i <= t.length - 1; i++) r = n.Add(r, e(j(i, t)));
	return r;
}
function kn(e, t, n) {
	return Dn((t, r) => n.Compare(e(r), e(t)) > 0 ? r : t, t);
}
function An(e, t) {
	return Dn((e, n) => t.Compare(n, e) > 0 ? n : e, e);
}
function jn(e, t) {
	return Dn((e, n) => t.Compare(n, e) > 0 ? e : n, e);
}
function Mn(e, n, r) {
	if (n.length === 0) throw new t("The input array was empty (Parameter 'array')");
	let i = r.GetZero();
	for (let t = 0; t <= n.length - 1; t++) i = r.Add(i, e(j(t, n)));
	return r.DivideByInt(i, n.length);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/FSharp.Core.js
function Nn(e, t) {
	if (y(t, l())) throw Zt(e);
	return t;
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/List.js
var Pn = class e extends x {
	constructor(e, t) {
		super(), this.head = e, this.tail = t;
	}
	toString() {
		let e = this, t = "[", n = !0, r = f(e);
		try {
			for (; r["System.Collections.IEnumerator.MoveNext"]();) {
				let e, i, a, o = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
				t = (n ? t : t + "; ") + (e = o, i = e, typeof i == "string" ? (a = i, "\"" + a + "\"") : be(e)), n = !1;
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
					e = r + 1, t = (i << 1) + oe(a.head) + 631 * r, n = s;
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
						let o = S(i), s = S(a), c = me(n.head, r.head) | 0;
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
		return In(this);
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return f(this);
	}
}, Fn = class {
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
function In(e) {
	return new Fn(e);
}
function Ln() {
	return new Pn(l(), void 0);
}
function Rn(e, t) {
	return new Pn(e, t);
}
function zn(e) {
	return e.tail == null;
}
function Bn(e) {
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
function Vn(e) {
	if (e.tail != null) return e.head;
	throw new t(Ue + " (Parameter 'list')");
}
function Hn(e) {
	let n = e.tail;
	if (n != null) return S(n);
	throw new t(Ue + " (Parameter 'list')");
}
function Un() {
	return Ln();
}
function Wn(e, t) {
	return Rn(e, t);
}
function Gn(e) {
	return Rn(e, Ln());
}
function Kn(e) {
	return zn(e);
}
function qn(e) {
	return Vn(e);
}
function Jn(e) {
	return Hn(e);
}
function Yn(e) {
	let t = Bn(e) | 0, n = rn(Array(t), 0, t, null);
	return ((e, t) => {
		loop: for (;;) {
			let r = e, i = t;
			if (!zn(i)) {
				M(n, r, Vn(i)), e = r + 1, t = Hn(i);
				continue loop;
			}
			break;
		}
	})(0, e), n;
}
function Xn(e, t, n) {
	let r = t, i = n;
	for (; !zn(i);) r = e(r, qn(i)), i = Hn(i);
	return r;
}
function Zn(e, t) {
	let n = t;
	for (let t = e.length - 1; t >= 0; t--) n = Rn(j(t, e), n);
	return n;
}
function Qn(e) {
	return Zn(e, Ln());
}
function $n(e) {
	if (n(e)) return Qn(e);
	if (e instanceof Pn) return e;
	{
		let t = Ln(), n = t, r = f(e);
		try {
			for (; r["System.Collections.IEnumerator.MoveNext"]();) {
				let e, t, i = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
				n = (e = n, t = new Pn(i, void 0), e.tail = t, t);
			}
		} finally {
			c(r);
		}
		let i = n;
		return i.tail = Ln(), Hn(t);
	}
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Seq.js
var er = "Enumeration already finished.", tr = "Enumeration has not started. Call MoveNext.", nr = "Reset is not supported on this enumerator.";
function rr() {
	throw qt(nr);
}
function ir() {
	throw Gt(tr);
}
function ar() {
	throw Gt(er);
}
var or = class {
	constructor(e) {
		this.f = e;
	}
	toString() {
		let e = this, t = 0, n = "seq [", r = f(e);
		try {
			for (; t < 4 && r["System.Collections.IEnumerator.MoveNext"]();) t > 0 && (n += "; "), n += be(r["System.Collections.Generic.IEnumerator`1.get_Current"]()), t = t + 1 | 0;
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
function sr(e) {
	return new or(e);
}
var cr = class {
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
		rr();
	}
	Dispose() {
		this.dispose();
	}
};
function lr(e, t, n) {
	return new cr(e, t, n);
}
function ur(e) {
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
	return lr(() => (r ? i && ar() : ir(), a == null ? ar() : S(a)), () => {
		if (r ||= !0, i) return !1;
		{
			let r;
			for (; r == null;) {
				let i, s = t, l = n;
				if (s != null) {
					if (l != null) {
						let e = S(l);
						if (e["System.Collections.IEnumerator.MoveNext"]()) a = Ve(e["System.Collections.Generic.IEnumerator`1.get_Current"]()), r = !0;
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
function dr(e, t, n) {
	let r = !1, i, a = Ve(e()), o = () => {
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
	return lr(() => (r || ir(), i == null ? ar() : S(i)), () => {
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
function fr(e, t) {
	let n, r = t;
	return lr(() => {
		if (n != null) {
			let e = S(n)[0];
			return S(n)[1], e;
		}
		return ir();
	}, () => (n = e(r), n != null && (S(n)[0], r = S(n)[1], !0)), () => {});
}
function pr(e) {
	return sr(e);
}
function mr(e) {
	return f(Nn("source", e));
}
function N(e) {
	return pr(() => f(e()));
}
function hr(e) {
	return pr(() => ur(e));
}
function gr(e, t) {
	return pr(() => fr(e, t));
}
function _r() {
	return N(() => []);
}
function P(e) {
	return N(() => un(e));
}
function F(e) {
	return e instanceof Pn ? Yn(e) : Array.from(e);
}
function vr(e) {
	return n(e) ? Qn(e) : e instanceof Pn ? e : $n(e);
}
function yr(e, t, n) {
	return pr(() => dr(e, t, n));
}
function I(e, t) {
	return hr([e, t]);
}
function br(e, t) {
	return yr(() => mr(t), (t) => {
		let n;
		for (; n == null && t["System.Collections.IEnumerator.MoveNext"]();) n = e(t["System.Collections.Generic.IEnumerator`1.get_Current"]());
		return n;
	}, (e) => {
		c(e);
	});
}
function xr(e, t, n) {
	let r = mr(t);
	try {
		let t = mr(n);
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
function Sr(e, t) {
	return hr(gr((n) => e() ? [t, n + 1] : void 0, 0));
}
function Cr(e, t) {
	return br((t) => {
		if (e(t)) return Ve(t);
	}, t);
}
function wr(e, t, n) {
	let r = mr(n);
	try {
		let n = t;
		for (; r["System.Collections.IEnumerator.MoveNext"]();) n = e(n, r["System.Collections.Generic.IEnumerator`1.get_Current"]());
		return n;
	} finally {
		c(r);
	}
}
function Tr(e, t) {
	wr((t, n) => {
		e(n);
	}, void 0, t);
}
function Er(e, t) {
	wr((t, n) => (e(t, n), t + 1 | 0), 0, t);
}
function L(e, t) {
	return yr(() => mr(t), (t) => t["System.Collections.IEnumerator.MoveNext"]() ? Ve(e(t["System.Collections.Generic.IEnumerator`1.get_Current"]())) : void 0, (e) => {
		c(e);
	});
}
function R(e, t) {
	return N(() => hr(L(e, t)));
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/System.Collections.Generic.js
var Dr = class extends t {
	constructor(e) {
		super(e);
	}
};
function Or(e) {
	return new Dr(e);
}
function kr() {
	return Or(qe);
}
var Ar = class {
	constructor(e, t) {
		this.contents = e, this.count = t | 0, this.head = 0, this.tail = (t === this.contents.length ? 0 : t) | 0;
	}
	GetEnumerator() {
		return f(Vr(this));
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return f(this);
	}
};
function jr(e, t) {
	return new Ar(e, t);
}
function Mr(e) {
	if (e < 0) throw en("capacity is less than 0");
	return jr(rn(Array(e), 0, e, null), 0);
}
function Nr() {
	return Mr(4);
}
function Pr(e) {
	let t = Array.from(e);
	return jr(t, t.length);
}
function Fr(e) {
	return e.count | 0;
}
function Ir(e, t) {
	e.count === Rr(e) && Br(e, e.count + 1), e.contents[e.tail] = t, e.tail = (e.tail + 1) % Rr(e) | 0, e.count = e.count + 1 | 0;
}
function Lr(e) {
	if (e.count === 0) throw new t("Queue is empty");
	let n = j(e.head, e.contents);
	return e.head = (e.head + 1) % Rr(e) | 0, e.count = e.count - 1 | 0, n;
}
function Rr(e) {
	return e.contents.length | 0;
}
function zr(e, t) {
	return (e.head + t) % Rr(e) | 0;
}
function Br(e, t) {
	let n = rn(Array(t), 0, t, null);
	e.head < e.tail ? mn(e.contents, e.head, n, 0, e.count) : (mn(e.contents, e.head, n, 0, Rr(e) - e.head), mn(e.contents, 0, n, Rr(e) - e.head, e.tail)), e.head = 0, e.contents = n, e.tail = (e.count === Rr(e) ? 0 : e.count) | 0;
}
function Vr(e) {
	return N(() => {
		let t = 0;
		return Sr(() => t < e.count, N(() => I(P(j(zr(e, t), e.contents)), N(() => (t = t + 1 | 0, _r())))));
	});
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Map.js
var Hr = class {
	constructor(e, t) {
		this.k = e, this.v = t;
	}
};
function Ur(e, t) {
	return new Hr(e, t);
}
function z(e) {
	return e.k;
}
function B(e) {
	return e.v;
}
var V = class extends Hr {
	constructor(e, t, n, r, i) {
		super(e, t), this.left = n, this.right = r, this.h = i | 0;
	}
};
function Wr(e, t, n, r, i) {
	return new V(e, t, n, r, i);
}
function H(e) {
	return e.left;
}
function U(e) {
	return e.right;
}
function Gr(e) {
	return e.h | 0;
}
function Kr(e, t) {
	MapTreeModule_sizeAux: for (;;) {
		let n = e, r = t;
		if (r != null) {
			let i = S(r);
			if (i instanceof V) {
				let r = i;
				e = Kr(n + 1, H(r)), t = U(r);
				continue MapTreeModule_sizeAux;
			}
			return n + 1 | 0;
		}
		return n | 0;
	}
}
function qr(e) {
	return Kr(0, e) | 0;
}
function W(e, t, n, r) {
	let i, a, o, s = e;
	if (s != null) {
		let e = S(s);
		o = e instanceof V ? (i = e, Gr(i)) : 1;
	} else o = 0;
	let c, l = r;
	if (l != null) {
		let e = S(l);
		c = e instanceof V ? (a = e, Gr(a)) : 1;
	} else c = 0;
	let u = (o < c ? c : o) | 0;
	return u === 0 ? Ur(t, n) : Wr(t, n, e, r, u + 1);
}
function Jr(e, n, r, i) {
	let a, o, s, c, l, u, d, f, p, m = e;
	if (m != null) {
		let e = S(m);
		p = e instanceof V ? (a = e, Gr(a)) : 1;
	} else p = 0;
	let h, g = i;
	if (g != null) {
		let e = S(g);
		h = e instanceof V ? (o = e, Gr(o)) : 1;
	} else h = 0;
	if (h > p + 2) {
		let a = S(i);
		if (a instanceof V) {
			let i = a;
			if (s = H(i), (s == null ? 0 : (c = S(s), c instanceof V ? (l = c, Gr(l)) : 1)) > p + 1) {
				let a = S(H(i));
				if (a instanceof V) {
					let t = a;
					return W(W(e, n, r, H(t)), z(t), B(t), W(U(t), z(i), B(i), U(i)));
				}
				throw new t("internal error: Map.rebalance");
			}
			return W(W(e, n, r, H(i)), z(i), B(i), U(i));
		}
		throw new t("internal error: Map.rebalance");
	}
	if (p > h + 2) {
		let a = S(e);
		if (a instanceof V) {
			let e = a;
			if (u = U(e), (u == null ? 0 : (d = S(u), d instanceof V ? (f = d, Gr(f)) : 1)) > h + 1) {
				let a = S(U(e));
				if (a instanceof V) {
					let t = a;
					return W(W(H(e), z(e), B(e), H(t)), z(t), B(t), W(U(t), n, r, i));
				}
				throw new t("internal error: Map.rebalance");
			}
			return W(H(e), z(e), B(e), W(U(e), n, r, i));
		}
		throw new t("internal error: Map.rebalance");
	}
	return W(e, n, r, i);
}
function Yr(e, t, n, r) {
	if (r != null) {
		let i = S(r), a = e.Compare(t, z(i)) | 0;
		if (i instanceof V) {
			let r = i;
			return a < 0 ? Jr(Yr(e, t, n, H(r)), z(r), B(r), U(r)) : a === 0 ? Wr(t, n, H(r), U(r), Gr(r)) : Jr(H(r), z(r), B(r), Yr(e, t, n, U(r)));
		}
		return a < 0 ? Wr(t, n, void 0, r, 2) : a === 0 ? Ur(t, n) : Wr(t, n, r, void 0, 2);
	}
	return Ur(t, n);
}
function Xr(e, t, n) {
	MapTreeModule_tryFind: for (;;) {
		let r = e, i = t, a = n;
		if (a != null) {
			let o = S(a), s = r.Compare(i, z(o)) | 0;
			if (s === 0) return Ve(B(o));
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
function Zr(e, t, n) {
	let r = Xr(e, t, n);
	if (r == null) throw kr();
	return S(r);
}
function Qr(e, t, n) {
	MapTreeModule_mem: for (;;) {
		let r = e, i = t, a = n;
		if (a != null) {
			let o = S(a), s = r.Compare(i, z(o)) | 0;
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
function $r(e, t) {
	MapTreeModule_iterOpt: for (;;) {
		let n = e, r = t;
		if (r != null) {
			let i = S(r);
			if (i instanceof V) {
				let r = i;
				$r(n, H(r)), n(z(r), B(r)), e = n, t = U(r);
				continue MapTreeModule_iterOpt;
			}
			n(z(i), B(i));
		}
		break;
	}
}
function ei(e, t) {
	$r(e, t);
}
function ti(e, t, n) {
	MapTreeModule_foldOpt: for (;;) {
		let r = e, i = t, a = n;
		if (a != null) {
			let o = S(a);
			if (o instanceof V) {
				let a = o;
				e = r, t = r(ti(r, i, H(a)), z(a), B(a)), n = U(a);
				continue MapTreeModule_foldOpt;
			}
			return r(i, z(o), B(o));
		}
		return i;
	}
}
function ni(e, t, n) {
	return ti(e, t, n);
}
function ri(e, t, n) {
	let r = n;
	ei((e, n) => {
		M(t, r, [e, n]), r = r + 1 | 0;
	}, e);
}
function ii(e, t) {
	return Xn((t, n) => Yr(e, n[0], n[1], t), void 0, t);
}
function ai(e, t, n) {
	MapTreeModule_mkFromEnumerator: for (;;) {
		let r = e, i = t, a = n;
		if (a["System.Collections.IEnumerator.MoveNext"]()) {
			let o = a["System.Collections.Generic.IEnumerator`1.get_Current"]();
			e = r, t = Yr(r, o[0], o[1], i), n = a;
			continue MapTreeModule_mkFromEnumerator;
		}
		return i;
	}
}
function oi(e, t) {
	let n;
	for (let r = 0; r <= t.length - 1; r++) {
		let i = j(r, t);
		n = Yr(e, i[0], i[1], n);
	}
	return n;
}
function si(e, t) {
	if (n(t)) return oi(e, t);
	if (t instanceof Pn) return ii(e, t);
	{
		let n = f(t);
		try {
			return ai(e, void 0, n);
		} finally {
			c(n);
		}
	}
}
var ci = class extends x {
	constructor(e, t) {
		super(), this.stack = e, this.started = t;
	}
};
function li(e) {
	MapTreeModule_collapseLHS: for (;;) {
		let t = e;
		if (Kn(t)) return Un();
		{
			let n = Jn(t), r = qn(t);
			if (r != null) {
				let i = S(r);
				if (i instanceof V) {
					let t = i;
					e = Zn([
						H(t),
						Ur(z(t), B(t)),
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
function ui(e) {
	return new ci(li(Gn(e)), !1);
}
function di() {
	throw new t("enumeration not started");
}
function fi() {
	throw new t("enumeration already finished");
}
function pi(e) {
	if (e.started) {
		let n = e.stack;
		if (Kn(n)) return fi();
		if (qn(n) != null) {
			let e = S(qn(n));
			if (e instanceof V) throw new t("Please report error: Map iterator, unexpected stack for current");
			return [z(e), B(e)];
		}
		throw new t("Please report error: Map iterator, unexpected stack for current");
	}
	return di();
}
function mi(e) {
	if (e.started) {
		let n = e.stack;
		if (Kn(n)) return !1;
		if (qn(n) != null) {
			if (S(qn(n)) instanceof V) throw new t("Please report error: Map iterator, unexpected stack for moveNext");
			return e.stack = li(Jn(n)), !Kn(e.stack);
		}
		throw new t("Please report error: Map iterator, unexpected stack for moveNext");
	}
	return e.started = !0, !Kn(e.stack);
}
function hi(e) {
	let t = ui(e);
	return {
		"System.Collections.Generic.IEnumerator`1.get_Current"() {
			return pi(t);
		},
		"System.Collections.IEnumerator.get_Current"() {
			return pi(t);
		},
		"System.Collections.IEnumerator.MoveNext"() {
			return mi(t);
		},
		"System.Collections.IEnumerator.Reset"() {
			t = ui(e);
		},
		Dispose() {}
	};
}
var gi = class e {
	constructor(e, t) {
		this.comparer = e, this.tree = t;
	}
	GetHashCode() {
		return wi(this) | 0;
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
		return "map [" + T("; ", L((e) => Ft("({0}, {1})", e[0], e[1]), this)) + "]";
	}
	get [Symbol.toStringTag]() {
		return "FSharpMap";
	}
	toJSON() {
		let e = this;
		return Array.from(e);
	}
	GetEnumerator() {
		return hi(this.tree);
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.IEnumerable.GetEnumerator"() {
		return hi(this.tree);
	}
	CompareTo(t) {
		let n, r = this;
		return (t instanceof e ? (n = t, xr((e, t) => {
			let n = r.comparer.Compare(e[0], t[0]) | 0;
			return (n === 0 ? me(e[1], t[1]) : n) | 0;
		}, r, n)) : 1) | 0;
	}
	"System.Collections.Generic.ICollection`1.Add2B595"(e) {
		throw qt("Map cannot be mutated");
	}
	"System.Collections.Generic.ICollection`1.Clear"() {
		throw qt("Map cannot be mutated");
	}
	"System.Collections.Generic.ICollection`1.Remove2B595"(e) {
		throw qt("Map cannot be mutated");
	}
	"System.Collections.Generic.ICollection`1.Contains2B595"(e) {
		let t = this;
		return Ci(t, e[0]) && y(xi(t, e[0]), e[1]);
	}
	"System.Collections.Generic.ICollection`1.CopyToZ3B4C077E"(e, t) {
		ri(this.tree, e, t);
	}
	"System.Collections.Generic.ICollection`1.get_IsReadOnly"() {
		return !0;
	}
	"System.Collections.Generic.ICollection`1.get_Count"() {
		return Si(this) | 0;
	}
	"System.Collections.Generic.IReadOnlyCollection`1.get_Count"() {
		return Si(this) | 0;
	}
	get size() {
		return Si(this) | 0;
	}
	clear() {
		throw new t("Map cannot be mutated");
	}
	delete(e) {
		throw new t("Map cannot be mutated");
	}
	entries() {
		return L((e) => [e[0], e[1]], this);
	}
	get(e) {
		return xi(this, e);
	}
	has(e) {
		return Ci(this, e);
	}
	keys() {
		return L((e) => e[0], this);
	}
	set(e, n) {
		throw new t("Map cannot be mutated");
	}
	values() {
		return L((e) => e[1], this);
	}
	forEach(e, t) {
		let n = this;
		Tr((t) => {
			e(t[1], t[0], n);
		}, n);
	}
};
function _i(e, t) {
	return new gi(e, t);
}
function vi(e) {
	return _i(e, void 0);
}
function yi(e) {
	return e.tree;
}
function bi(e, t, n) {
	return _i(e.comparer, Yr(e.comparer, t, n, e.tree));
}
function xi(e, t) {
	return Zr(e.comparer, t, e.tree);
}
function Si(e) {
	return qr(e.tree) | 0;
}
function Ci(e, t) {
	return Qr(e.comparer, t, e.tree);
}
function wi(e) {
	let t = (e, t) => (e << 1) + t + 631 | 0, n = 0, r = f(e);
	try {
		for (; r["System.Collections.IEnumerator.MoveNext"]();) {
			let e = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
			n = t(n, oe(e[0])) | 0, n = t(n, oe(e[1])) | 0;
		}
	} finally {
		c(r);
	}
	return n | 0;
}
function Ti(e, t, n) {
	return bi(n, e, t);
}
function Ei(e, t, n) {
	return ni(e, t, yi(n));
}
function Di(e, t) {
	return _i(t, si(t, e));
}
function Oi(e, t) {
	return _i(t, si(t, e));
}
function ki(e) {
	return vi(e);
}
// ../../../toolchain-logo/art/src/Pixels.fs
var Ai = class extends x {
	constructor(e, t, n) {
		super(), this.r = e, this.g = t, this.b = n;
	}
}, ji = "0123456789ABCDEF";
function Mi(e) {
	return ji.indexOf(e.toUpperCase());
}
function Ni(e, t) {
	return Mi(e[t]) * 16 + Mi(e[t + 1]);
}
function G(e) {
	return new Ai(Ni(e, 1), Ni(e, 3), Ni(e, 5));
}
function Pi(e) {
	let t = (e) => {
		let t = ~~D(0, O(255, he(e))) | 0;
		return ji[~~(t / 16)] + ji[t % 16];
	};
	return "#" + t(e.r) + t(e.g) + t(e.b);
}
function K(e, t, n) {
	return new Ai(e.r + (t.r - e.r) * n, e.g + (t.g - e.g) * n, e.b + (t.b - e.b) * n);
}
var Fi = G("#FFFFFF");
G("#000000");
function Ii(e, t) {
	return K(e, Fi, t);
}
function Li(e) {
	return .2126 * e.r + .7152 * e.g + .0722 * e.b;
}
function Ri(e) {
	let t = Li(e);
	return new Ai(t, t, t);
}
function zi(e) {
	return (D(e.r, D(e.g, e.b)) - O(e.r, O(e.g, e.b))) / 255;
}
function Bi(e) {
	let t = D(e.r, D(e.g, e.b)), n = O(e.r, O(e.g, e.b)), r = t;
	if (r === n) return 0;
	{
		let t = r - n;
		return ((r === e.r ? (e.g - e.b) / t : r === e.g ? 2 + (e.b - e.r) / t : 4 + (e.r - e.g) / t) * 60 + 360) % 360;
	}
}
var Vi = A((e) => (e + .5) / 16, [
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
function Hi(e, t) {
	return j((t & 3) * 4 + (e & 3), Vi);
}
function Ui(e, t, n) {
	return ~~Math.floor(n) + +(n - Math.floor(n) > Hi(e, t)) | 0;
}
var Wi = class extends x {
	constructor(e, t) {
		super(), this.rows = e, this.palette = t;
	}
}, Gi = class extends x {
	constructor(e) {
		super(), this.glyphs = e;
	}
};
function Ki(e, t) {
	return Ei((e, t, n) => Ti(t, n, e), e, t);
}
function qi(e, t) {
	let n, r = A((e) => e[0], sn(e, t));
	return n = Sn(Ki, ki({ Compare: (e, t) => b(e, t) | 0 }), r), new Gi(Oi(ln((e) => {
		let t = Ki(n, e[0]);
		return A((e) => [e[0], new Wi(e[1], t)], e[1]);
	}, t), { Compare: (e, t) => b(e, t) | 0 }));
}
var Ji = class extends x {
	constructor(e, t, n) {
		super(), this.w = e | 0, this.h = t | 0, this.px = n;
	}
};
function Yi(e, t) {
	return new Ji(e, t, rn(Array(e * t * 4), 0, e * t * 4, 0));
}
function Xi(e, t, n) {
	return t >= 0 && n >= 0 && t < e.w && n < e.h;
}
function Zi(e, t, n) {
	return Xi(e, t, n) ? j(4 * (n * e.w + t) + 3, e.px) > 0 : !1;
}
function Qi(e, t, n, r, i) {
	if (Xi(e, t, n)) {
		let a = 4 * (n * e.w + t) | 0, o = j(a + 3, e.px) / 255, s = i + o * (1 - i);
		s > 0 && (e.px[a] = (r.r * i + j(a, e.px) * o * (1 - i)) / s, e.px[a + 1] = (r.g * i + j(a + 1, e.px) * o * (1 - i)) / s, e.px[a + 2] = (r.b * i + j(a + 2, e.px) * o * (1 - i)) / s), e.px[a + 3] = s * 255;
	}
}
function q(e, t, n, r) {
	if (Xi(e, t, n)) {
		let i = 4 * (n * e.w + t) | 0;
		e.px[i] = r.r, e.px[i + 1] = r.g, e.px[i + 2] = r.b, e.px[i + 3] = 255;
	}
}
function $i(e, t, n) {
	let r = 4 * (n * e.w + t) | 0;
	if (Zi(e, t, n)) return new Ai(j(r, e.px), j(r + 1, e.px), j(r + 2, e.px));
}
function ea(e, t, n) {
	for (let r = 0; r <= e.w - 1; r++) Qi(e, r, 0, t, n), Qi(e, r, e.h - 1, t, n);
	for (let r = 1; r <= e.h - 2; r++) Qi(e, 0, r, t, n), Qi(e, e.w - 1, r, t, n);
}
// ../../../toolchain-logo/art/src/Font.fs
var ta = class extends x {
	constructor(e, t, n) {
		super(), this.height = e | 0, this.spacing = t | 0, this.glyphs = n;
	}
};
function na(e, t, n) {
	return new ta(e, t, Di(Sn((e, t) => {
		let n, r, i, a;
		switch (t.startsWith("= ") ? n = 0 : Kn(e) || t === "" ? n = 2 : (n = 1, r = qn(e)[0], i = Jn(e), a = qn(e)[1]), n) {
			case 0: {
				let n = Ut(t, 2);
				return Wn([n === "space" ? " " : n[0], []], e);
			}
			case 1: return Wn([r, k(a, [t])], i);
			default: return e;
		}
	}, Un(), A((e) => Ht(e, "\r"), Vt(n, ["\n"], void 0, 0))), { Compare: (e, t) => b(e, t) | 0 }));
}
Oi(ln((e) => F(N(() => L((t) => [t, e[1]], e[0].split("")))), [
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
]), { Compare: (e, t) => b(e, t) | 0 }), window.fsdsp56kIcon = {
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
}, (() => {
	let e = window.fsdsp56kFont;
	return na(e.height, e.spacing, e.glyphs);
})(), qi(2, A((e) => {
	let t = e[0];
	return [Oi(A((e) => [e[0][0], G(e[1])], Object.entries(window[t].palette || {})), { Compare: (e, t) => b(e, t) | 0 }), A((e) => [e[0], Vt(e[1].trim(), ["\n"], void 0, 0)], Object.entries(window[t][e[1]] || {}))];
}, [
	["fsdsp56kIcon", "grids"],
	["fsdsp56kCommandIcons", "icons"],
	["fsdsp56kAuditionIcons", "icons"],
	["fsdsp56kCreditIcons", "icons"],
	["fsdsp56kSynthIcons", "icons"],
	["fsdsp56kWindowGlyphs", "icons"]
]));
function ra(e, t) {
	e.width !== t.w && (e.width = t.w), e.height !== t.h && (e.height = t.h);
	let n = e.getContext("2d"), r = n.createImageData(t.w, t.h);
	r.data.set(t.px), n.putImageData(r, 0, 0);
}
function ia() {
	return matchMedia("(prefers-reduced-motion: reduce)").matches;
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Range.js
function aa(e, n, r, i) {
	let a = me(e, r) | 0;
	if (a === 0) throw new t("The step of a range cannot be zero");
	let o = a > 0;
	return (t) => {
		let r = me(t, n) | 0;
		return o && r <= 0 || !o && r >= 0 ? [t, i(t, e)] : void 0;
	};
}
function oa(e, t, n, r, i) {
	let a = aa(t, n, r, i);
	return N(() => gr(a, e));
}
function J(e, t, n) {
	return oa(e, t, n, 0, (e, t) => e + t);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/MapUtil.js
function sa(e, t, n) {
	return e.has(t) ? (n.contents = e.get(t), !0) : !1;
}
function ca(e, t) {
	return !t.has(e) && (t.add(e), !0);
}
function la(e, n) {
	if (e.has(n)) return e.get(n);
	throw new t(`The given key '${n}' was not present in the dictionary.`);
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/MutableSet.js
var ua = class {
	constructor(e, t) {
		let n = new Oe(l());
		this.comparer = t, n.contents = this, this.hashMap = /* @__PURE__ */ new Map([]), this["init@9"] = 1;
		let r = f(e);
		try {
			for (; r["System.Collections.IEnumerator.MoveNext"]();) {
				let e = r["System.Collections.Generic.IEnumerator`1.get_Current"]();
				ma(n.contents, e);
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
		return f(hr(this.hashMap.values()));
	}
	[Symbol.iterator]() {
		return p(f(this));
	}
	"System.Collections.Generic.ICollection`1.Add2B595"(e) {
		ma(this, e);
	}
	"System.Collections.Generic.ICollection`1.Clear"() {
		fa(this);
	}
	"System.Collections.Generic.ICollection`1.Contains2B595"(e) {
		return ha(this, e);
	}
	"System.Collections.Generic.ICollection`1.CopyToZ3B4C077E"(e, t) {
		Er((n, r) => {
			M(e, t + n, r);
		}, this);
	}
	"System.Collections.Generic.ICollection`1.get_Count"() {
		return pa(this) | 0;
	}
	"System.Collections.Generic.ICollection`1.get_IsReadOnly"() {
		return !1;
	}
	"System.Collections.Generic.ICollection`1.Remove2B595"(e) {
		return ga(this, e);
	}
	get size() {
		return pa(this) | 0;
	}
	add(e) {
		let t = this;
		return ma(t, e), t;
	}
	clear() {
		fa(this);
	}
	delete(e) {
		return ga(this, e);
	}
	has(e) {
		return ha(this, e);
	}
	keys() {
		return L((e) => e, this);
	}
	values() {
		return L((e) => e, this);
	}
	entries() {
		return L((e) => [e, e], this);
	}
	forEach(e, t) {
		let n = this;
		Tr((t) => {
			e(t, t, n);
		}, n);
	}
};
function da(e, t) {
	let n = e.comparer.GetHashCode(t) | 0, r, i = l();
	return r = [sa(e.hashMap, n, new Oe(() => i, (e) => {
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
function fa(e) {
	e.hashMap.clear();
}
function pa(e) {
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
function ma(e, t) {
	let n = da(e, t);
	return n[0] ? n[2] > -1 ? !1 : (la(e.hashMap, n[1]).push(t), !0) : (e.hashMap.set(n[1], [t]), !0);
}
function ha(e, t) {
	let n = da(e, t), r;
	switch (r = n[0] && n[2] > -1 ? 0 : 1, r) {
		case 0: return !0;
		default: return !1;
	}
}
function ga(e, t) {
	let n = da(e, t), r;
	switch (r = n[0] && n[2] > -1 ? 0 : 1, r) {
		case 0: return la(e.hashMap, n[1]).splice(n[2], 1), !0;
		default: return !1;
	}
}
// node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Seq2.js
function _a(e, t) {
	return N(() => {
		let n = new ua([], t);
		return Cr((e) => ca(e, n), e);
	});
}
function va(e, t) {
	return F(_a(e, t));
}
// ../../../toolchain-logo/art/src/Logo.fs
var ya = 6.283185307179586, ba = F(N(() => L((e) => 123.75 + e * 17.6, J(0, 1, 15)))), xa = class extends x {
	constructor(e, t, n, r) {
		super(), this.side = e | 0, this.tip = t, this.edge = n, this.cell = r;
	}
};
function Sa(e) {
	let t = ~~(e / 16) | 0, n = (t < 2 ? e % 16 : 15 - e % 16) | 0, r = j(n, ba);
	switch (t) {
		case 0: return new xa(t, [r, 50], [r, 100], [n, 0]);
		case 1: return new xa(t, [462, r], [412, r], [15, n]);
		case 2: return new xa(t, [r, 462], [r, 412], [n, 15]);
		default: return new xa(t, [50, r], [100, r], [0, n]);
	}
}
function Ca(e, t) {
	return yn((n) => {
		let r = Sa(n).tip, i = r[1], a = r[0], o, s = ~~(n / 16) | 0;
		o = s === 0 ? [
			a - 8.8,
			44,
			17.6,
			58
		] : s === 1 ? [
			412,
			i - 8.8,
			58,
			17.6
		] : s === 2 ? [
			a - 8.8,
			410,
			17.6,
			58
		] : [
			44,
			i - 8.8,
			58,
			17.6
		];
		let c = o[1], l = o[0];
		return e >= l && e < l + o[2] && t >= c && t < c + o[3];
	}, F(J(0, 1, 63)));
}
function wa(e, t, n) {
	return [
		[e + n, e],
		[t - n, e],
		[t, e + n],
		[t, t - n],
		[t - n, t],
		[e + n, t],
		[e, t - n],
		[e, e + n]
	];
}
var Ta = wa(88, 424, 18), Ea = wa(89, 423, 17), Da = wa(92, 420, 16), Oa = wa(96, 416, 16), ka = [[
	[5, 63],
	[61, 7],
	[61, 35],
	[33, 63],
	[61, 91],
	[61, 119]
], [
	[41, 63],
	[61, 43],
	[61, 83]
]], Aa = [[
	[123, 63],
	[65, 7],
	[65, 35],
	[93, 63],
	[65, 91],
	[65, 119]
]];
function ja(e, t, n, r) {
	return 256 - t * Math.sin(ya * n * (e - 96) / 160 - r);
}
var Ma = F(N(() => L((e) => 256 + 10 * e, J(0, 1, 16)))), Na = [
	G("#183454"),
	G("#0F243C"),
	G("#081422")
], Pa = [
	G("#222834"),
	G("#181E27"),
	G("#11151D"),
	G("#0B0E14")
], Fa = [G("#475569"), G("#1E293B")], Ia = [G("#0F172A"), G("#020617")], La = G("#263345"), Ra = A(G, [
	"#F8FAFC",
	"#E2E8F0",
	"#CBD5E1",
	"#94A3B8",
	"#64748B",
	"#475569",
	"#334155"
]), za = G("#B46A3C"), Ba = G("#DF9B68"), Va = G("#08121E"), Ha = A(G, [
	"#4FA6D6",
	"#4498C9",
	"#378BBA",
	"#2A76A1",
	"#1E6189"
]), Ua = A(G, [
	"#6BE6FF",
	"#56E0FE",
	"#30B9DB",
	"#2399BF",
	"#1788A4"
]), Wa = G("#00E5FF"), Ga = G("#0B121A"), Ka = G("#30B9DB"), qa = G("#FFF3A0");
function Ja(e) {
	return e < 0 ? -((-e) ** (1 / 3)) : e ** (1 / 3);
}
function Ya(e) {
	return e <= .04045 ? e / 12.92 : ((e + .055) / 1.055) ** 2.4;
}
function Xa(e) {
	return e <= .0031308 ? 12.92 * e : 1.055 * e ** (1 / 2.4) - .055;
}
function Za(e) {
	let t = Ya(e.r / 255), n = Ya(e.g / 255), r = Ya(e.b / 255), i = Ja(.4122214708 * t + .5363325363 * n + .0514459929 * r), a = Ja(.2119034982 * t + .6806995451 * n + .1073969566 * r), o = Ja(.0883024619 * t + .2817188376 * n + .6299787005 * r), s = 1.9779984951 * i - 2.428592205 * a + .4505937099 * o, c = .0259040371 * i + .7827717662 * a - .808675766 * o;
	return [
		.2104542553 * i + .793617785 * a - .0040720468 * o,
		Math.sqrt(s * s + c * c),
		Math.atan2(c, s)
	];
}
function Qa(e, t, n) {
	let r = e, i = t, a = i * Math.cos(n), o = i * Math.sin(n), s = a, c = (r + .3963377774 * s + .2158037573 * o) ** 3, l = (r - .1055613458 * s - .0638541728 * o) ** 3, u = (r - .0894841775 * s - 1.291485548 * o) ** 3, d = (e) => 255 * Xa(O(1, D(0, e)));
	return new Ai(d(4.0767416621 * c - 3.3077115913 * l + .2309699292 * u), d(-1.2684380046 * c + 2.6097574011 * l - .3413193965 * u), d(-.0041960863 * c - .7034186147 * l + 1.707614701 * u));
}
function $a(e) {
	let t = Za(qa), n = t[0], r = t[2], i = t[1], a = Za(Ka), o, s = a[2] - r;
	o = s > 3.141592653589793 ? s - ya : s < -3.141592653589793 ? s + ya : s;
	let c = Math.sin(3.141592653589793 * e), l = n + (a[0] - n) * e + .05 * c, u = (i + (a[1] - i) * e) * (1 - .35 * c), d = r + o * e;
	return [Qa(l, u, d), Qa(l + (1 - l) * .65, u * .35, d)];
}
var eo = [
	[1, 0],
	[-1, 0],
	[0, 1],
	[0, -1]
];
function to(e, t, n, r, i) {
	let a = [t, n], o = [r, i], s = (e) => e[0] * 16 + e[1] | 0, c = /* @__PURE__ */ new Set([s(a)]), l = (t, n) => Pr(A((e) => e[0], Tn((e) => e[1], A((r) => {
		let i = r[0] | 0, a = r[1] | 0;
		return [[i, a], e() + ((o[0] - t[0]) * i + (o[1] - t[1]) * a > 0 ? .9 : 0) + (y(n, [i, a]) ? .6 : 0)];
	}, eo), { Compare: (e, t) => b(e, t) | 0 }))), u = [[a, l(a, void 0)]], d, f = 4e3;
	for (; d == null && u.length > 0 && f > 0;) {
		let e, t, n, r;
		f = f - 1 | 0;
		let i = j(u.length - 1, u), a = i[1], p = i[0];
		if (ce(p, o)) d = F(N(() => R((e) => P(e[0]), u)));
		else if (Fr(a) === 0) c.delete(s(p)), u.splice(u.length - 1, 1);
		else {
			let i = Lr(a), o = i[1] | 0, d = i[0] | 0, f = [p[0] + d, p[1] + o];
			e = f, t = p, n = e[0] | 0, r = e[1] | 0, n >= 0 && n < 16 && r >= 0 && r < 16 && !c.has(s([n, r])) && eo.every((e) => {
				let i = e[0] | 0, a = e[1] | 0;
				return n + i === t[0] && r + a === t[1] || !c.has(s([n + i, r + a]));
			}) && (ca(s(f), c), u.push([f, l(f, [d, o])]));
		}
	}
	return d;
}
var no = class extends x {
	constructor(e, t, n, r, i, a) {
		super(), this.x0 = e, this.y0 = t, this.dx = n, this.dy = r, this.from = i, this.len = a;
	}
}, ro = class extends x {
	constructor(e, t) {
		super(), this.runs = e, this.total = t;
	}
};
function io(e) {
	let t, n = _n(1, gn((e, t) => new no(t.x0, t.y0, t.dx, t.dy, e.from + e.len, t.len), new no(0, 0, 0, 0, 0, 0), A((e) => {
		let t = e[0], n = e[1], r = t[1], i = t[0], a = n[1], o = n[0];
		return new no(i, r, ge(o - i), ge(a - r), 0, Math.abs(o - i) + Math.abs(a - r));
	}, (t = fn(e), t.filter((e) => {
		let t = e[0], n = e[1];
		return t[0] !== n[0] || t[1] !== n[1];
	})))));
	return new ro(n, On((e) => e.len, n, {
		GetZero: () => 0,
		Add: (e, t) => e + t
	}));
}
function ao(e, t) {
	let n = D(0, O(e.total, t)), r = He(yn((e) => n <= e.from + e.len, e.runs), j(e.runs.length - 1, e.runs)), i = n - r.from;
	return [
		r.x0 + r.dx * i,
		r.y0 + r.dy * i,
		r.dx,
		r.dy
	];
}
var oo = 1.8, so = 2.6, co = 1.5, lo = .75;
function uo(e, t, n, r) {
	let i = ao(e, t), a = n - t, o = oo * D(0, 1 - a / 70) * Math.sin(ya * a / 28 - r);
	return [i[0] - i[3] * o, i[1] + i[2] * o];
}
function fo(e) {
	let t;
	return A((e) => e[1], (t = on(e), t.filter((t) => {
		let n = t[0] | 0, r = t[1], i = r[1] | 0, a = r[0] | 0;
		if (n === 0 || n === e.length - 1) return !0;
		{
			let t = j(n - 1, e), r = j(n + 1, e);
			return (a - t[0]) * (r[1] - i) !== (i - t[1]) * (r[0] - a);
		}
	})));
}
var po = class extends x {
	constructor(e, t, n, r, i, a, o, s, c, l, u) {
		super(), this.path = e, this.tip = t, this.strength = n, this.hue = r, this.colour = i, this.white = a, this.born = o, this.duration = s, this.phase = c, this.kicked = l, this.met = u;
	}
};
function mo(e, t, n, r, i) {
	let a = (t) => ~~Math.floor(e() * t) | 0, o = Sa(r), s = ((t) => {
		attempt: for (;;) {
			let n = t, r = a(64);
			for (; ~~(r / 16) === o.side;) r = a(64) | 0;
			let i = Sa(r), s, c = o.cell, l = i.cell;
			if (s = to(e, c[0], c[1], l[0], l[1]), s == null) {
				if (n < 5) {
					t = n + 1;
					continue attempt;
				}
				return [i, va([
					o.cell,
					[i.cell[0], o.cell[1]],
					i.cell
				], {
					Equals: ce,
					GetHashCode: (e) => ae(e) | 0
				})];
			}
			return [i, s];
		}
	})(0), c = s[0], l = io(cn([
		[o.tip, o.edge],
		A((e) => [j(e[0], ba), j(e[1], ba)], fo(s[1])),
		[c.edge, c.tip]
	])), u = n.length === 0 ? e() : kn((e) => jn(A((t) => Math.abs(t.hue - e), n), { Compare: (e, t) => b(e, t) | 0 }), dn(6, (t) => e()), { Compare: (e, t) => b(e, t) | 0 }), d = $a(u);
	return new po(l, c.tip, i, u, d[0], d[1], t, .5 + l.total * .0016, e() * ya, !1, void 0);
}
function ho(e, t) {
	let n = O(1, (e - t.born) / t.duration), r = (n < .5 ? 2 * n * n : 1 - (-2 * n + 2) ** 2 / 2) * t.path.total;
	return [
		n,
		r,
		n < 1 ? r : t.path.total + (e - t.born - t.duration) * 300
	];
}
var go = .6, _o = .9;
function vo(e) {
	return e * e * (3 - 2 * e);
}
function yo(e) {
	return .5 - Math.sin(Math.asin(1 - 2 * e) / 3);
}
var bo = class extends x {
	constructor(e, t, n, r, i, a, o, s, c, l, u, d, f, p, m, h) {
		super(), this.awake = e, this.hover = t, this.ramp = n, this.target = r, this.clock = i, this.phi = a, this.breath = o, this.burst = s, this.sx = c, this.sv = l, this.rx = u, this.rv = d, this.hum = f, this.comets = p, this.queued = m, this.pulses = h;
	}
}, Y = new bo(!1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1.5, [], [], []);
function xo(e) {
	return new bo(!0, e.hover, yo(O(1, D(0, e.hover))), 1, e.clock, e.phi, e.breath, e.burst, e.sx, e.sv, e.rx, e.rv, e.hum, e.comets, e.queued, e.pulses);
}
function So(e) {
	return new bo(!0, e.hover, e.ramp, 0, e.clock, e.phi, e.breath, e.burst, e.sx, e.sv, e.rx, e.rv, e.hum, e.comets, e.queued, e.pulses);
}
function Co(e, t, n) {
	if (t == null) {
		let t = Math.sin(n.breath) >= 0 || n.hover < .05;
		return new bo(!0, n.hover, n.ramp, n.target, n.clock, n.phi, n.breath, O(5, n.burst + 2.5), n.sx, n.sv + (t ? 14 : -10), n.rx, n.rv + (t ? e() < .5 ? -70 : 70 : 0), n.hum, n.comets, n.queued, n.pulses);
	}
	{
		let r = t | 0;
		return new bo(!0, n.hover, n.ramp, n.target, n.clock, n.phi, n.breath, n.burst, n.sx, n.sv, n.rx, n.rv, n.hum, n.comets, k(n.queued, F(N(() => L((e) => [
			n.clock + .14 * e,
			r,
			1
		], J(0, 1, ~~(e() * 3)))))), n.pulses);
	}
}
function wo(e) {
	return 1 + .35 * (.5 - .5 * Math.cos(e.clock * ya / 7.7)) + e.burst;
}
function To(e, t, n) {
	let r = n.target === 1 ? O(1, n.ramp + t / 2) : n.ramp, i = n.target === 1 ? vo(r) : n.hover - n.hover * (1 - Math.exp(-t / .45)), a = n.clock + t, o = n.breath + t * ya / 3.2, s = n.sv + (-60 * n.sx - 5 * n.sv) * t, c = n.rv + (-60 * n.rx - 5 * n.rv) * t, l = D(n.sx + s * t, -3 - 1.25 * (1 - Math.cos(o)) * i), u = n.rx + c * t, d = n.target === 1 ? n.hum - t > 0 ? [n.hum - t, []] : [1.2 + e() * 2, [[
		a,
		~~(e() * 64),
		.35
	]]] : [n.hum, []], f = vn((e) => e[0] <= a, k(n.queued, d[1])), p = Sn((t, n) => k(t, [mo(e, a, t, n[1], n[2])]), n.comets, f[0]), m = new bo(n.awake, i, r, n.target, a, n.phi + t * ya * .9 * i, o, n.burst * Math.exp(-t / .7), l, s, u, c, d[0], n.comets, f[1], n.pulses), h = wo(m), g = Sn((e, n) => {
		let r, i, o = e[0], s = e[1], c = new po(n.path, n.tip, n.strength, n.hue, n.colour, n.white, n.born, n.duration, n.phase + t * ya * so * (.7 + .3 * h), n.kicked, n.met), l = ho(a, c), u = l[2], d;
		if (l[0] < 1) {
			let e = uo(c.path, l[1], u, c.phase), t = e[0];
			d = c.kicked || Math.sqrt((r = t - 256, r ** 2 + (i = e[1] - 250, i ** 2))) >= 60 ? [c, s] : [new po(c.path, c.tip, c.strength, c.hue, c.colour, c.white, c.born, c.duration, c.phase, !0, c.met), new bo(s.awake, s.hover, s.ramp, s.target, s.clock, s.phi, s.breath, s.burst, s.sx, s.sv + 6 * c.strength, s.rx, s.rv + (t < 256 ? -45 : 45) * c.strength, s.hum, s.comets, s.queued, k(s.pulses, [[a, .6 * c.strength]]))];
		} else d = c.met == null ? [new po(c.path, c.tip, c.strength, c.hue, c.colour, c.white, c.born, c.duration, c.phase, c.kicked, a), new bo(s.awake, s.hover, s.ramp, s.target, s.clock, s.phi, s.breath, s.burst, s.sx, s.sv, s.rx, s.rv, s.hum, s.comets, s.queued, k(s.pulses, [[a, c.strength]]))] : [c, s];
		let f = d[0];
		return [u - 160 > f.path.total && a - f.born > f.duration + .75 ? o : k(o, [f]), d[1]];
	}, [[], m], p), _ = g[1], v = new bo(_.awake, _.hover, _.ramp, _.target, _.clock, _.phi, _.breath, _.burst, _.sx, _.sv, _.rx, _.rv, _.hum, g[0], _.queued, _.pulses.filter((e) => a - e[0] < _o));
	return v.comets.length === 0 && v.queued.length === 0 && v.pulses.length === 0 && v.target === 0 && v.hover < .002 && v.burst < .01 && Math.abs(v.sx) + Math.abs(v.sv) + Math.abs(v.rx) + Math.abs(v.rv) < .01 ? new bo(Y.awake, Y.hover, Y.ramp, Y.target, Y.clock, Y.phi, Y.breath, Y.burst, Y.sx, Y.sv, Y.rx, Y.rv, v.hum, Y.comets, Y.queued, Y.pulses) : v;
}
function Eo(e) {
	let t = D(e.hover, O(1, e.burst / 2.5)), n = 40 * (1 + .22 * Math.sin(e.clock * ya / 5.3) + .1 * Math.sin(e.clock * ya / 2.9 + 1)), r = wo(e);
	return (i) => ja(i, 40, 1, 0) + t * (ja(i, n, r, e.phi) - ja(i, 40, 1, 0));
}
function Do(e) {
	return e.hover * 1.25 * (1 - Math.cos(e.breath)) + e.sx;
}
function Oo(e) {
	return e.rx;
}
var ko = class extends x {
	constructor(e, t, n, r, i, a) {
		super(), this.level = e, this.blue = t, this.yellow = n, this.scale = r, this.pulse = i, this.pulseScale = a;
	}
};
function Ao(e) {
	let t = (t) => .5 - .5 * Math.cos(3.141592653589793 * (e.clock + t) / 5 + 3.141592653589793 / 2), n = Sn((e, t) => {
		let n = e[0], r = t[0];
		return r > n ? [r, t[1]] : [n, e[1]];
	}, [0, 1], A((t) => {
		let n, r = t[1], i = 1 - (n = 1 - O(1, (e.clock - t[0]) / _o), n ** 2);
		return i < .25 ? [.8 * r * i / .25, .96 + .1 * i / .25] : [.8 * r * (1 - (i - .25) / .75), 1.06 + .06 * (i - .25) / .75];
	}, e.pulses));
	return new ko(go + .4 * e.hover, .35 + .3 * t(0), .25 + .25 * t(-3), .97 + .07 * t(0), n[0], n[1]);
}
var jo = class extends x {
	constructor(e, t, n, r, i, a, o) {
		super(), this.tail = e, this.head = t, this.flicker = n, this.colour = r, this.white = i, this.strength = a, this.meet = o;
	}
};
function Mo(e) {
	return A((t) => {
		let n, r, i = ho(e.clock, t), a = i[2], o = O(a, t.path.total), s = D(0, a - 160), c = o;
		return new jo(dn(c <= s ? 0 : ~~Math.ceil((c - s) / co) + 1, (e) => {
			let n = D(s, c - e * co), r = uo(t.path, n, a, t.phase);
			return [
				r[0],
				r[1],
				(a - n) / 160
			];
		}), i[0] < 1 ? uo(t.path, i[1], a, t.phase) : void 0, .75 + .5 * (.5 + .5 * Math.sin(t.phase * 7.3)), t.colour, t.white, t.strength, (n = t.met, n == null ? void 0 : (r = (e.clock - n) / lo, r < 1 ? [t.tip, r] : void 0)));
	}, e.comets);
}
// ../../../toolchain-logo/art/src/Aesthetic.fs
var No = class e extends Se {
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
}, Po = class extends x {
	constructor(e, t, n, r, i, a, o, s, c, l, u, d, f) {
		super(), this.name = e, this.ground = t, this.panel = n, this.line = r, this.edgeLit = i, this.edgeShade = a, this.fg = o, this.dim = s, this.accent = c, this.font = l, this.radius = u | 0, this.ink = d, this.select = f;
	}
}, Fo = G("#3A4A62"), Io = G("#A6B4C6"), Lo = G("#30B9DB"), Ro = new Po("house", G("#060A11"), G("#0D1726"), G("#1A2A40"), G("#24395A"), G("#1A2A40"), G("#DFE7F1"), G("#7F90A7"), Lo, "-apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif", 10, (e, t, n, r, i) => {
	let a;
	switch (e.tag) {
		case 0:
		case 2:
			a = 3;
			break;
		default: a = Li(i) < 24 ? 0 : (n + r) % 2 == 1 ? 1 : 2;
	}
	switch (a) {
		case 0: return [i, 1];
		case 1: return;
		case 2: return [K(Fo, Io, Li(i) / 255), 1];
		default: return [Ii(i, .45 * t), 1];
	}
}, (e, t) => {
	ea(e, Lo, 1);
	for (let t = 0; t <= e.w - 1; t++) Hi(t, 1) < .5 && Qi(e, t, 1, Lo, .35), Hi(t, e.h - 2) < .5 && Qi(e, t, e.h - 2, Lo, .35);
	let n = (t * .6 % 1.6 - .3) * e.w;
	for (let t = 2; t <= e.h - 3; t++) for (let r = 0; r <= e.w - 1; r++) {
		let i = r - n + t * .7;
		i > -1.5 && i < 1.5 && Zi(e, r, t) && Qi(e, r, t, Fi, .45);
	}
}), zo = G("#060A11");
function Bo(e) {
	return K(K(e, Ri(e), .7), zo, .2);
}
var Vo = new Po("muted", Ro.ground, G("#0A111C"), G("#152234"), G("#1B2B42"), G("#152234"), G("#C9D4E2"), G("#66768C"), G("#5F8FA8"), Ro.font, 6, (e, t, n, r, i) => {
	if (Li(i) < 16) return [i, 1];
	switch (e.tag) {
		case 2: return [K(Bo(i), i, .6 + .4 * t), 1];
		case 0: return [K(Bo(i), i, .7 * t), 1];
		default: return (n + r) % 2 == 1 ? void 0 : [K(Bo(i), zo, .35), 1];
	}
}, (e, t) => {
	ea(e, G("#5F8FA8"), .8);
}), X = [
	G("#0055AA"),
	G("#FFFFFF"),
	G("#000022"),
	G("#FF8800")
];
function Ho(e) {
	if (zi(e) > .3) {
		let t = Bi(e);
		return t < 70 || t > 330;
	}
	return !1;
}
function Uo(e) {
	if (zi(e) > .12) {
		let t = Bi(e);
		return t > 170 && t < 260;
	}
	return !1;
}
function Wo(e, t, n, r) {
	if (Li(r) < 12) return 2;
	{
		let i = O(2, (Li(r) / 255 + .35 * n) * 2.4);
		switch (Ui(e, t, i) | 0) {
			case 0: return Uo(r) ? 0 : 2;
			case 1: return Ho(r) ? 3 : 0;
			default: return Ho(r) && i < 1.85 ? 3 : 1;
		}
	}
}
function Go(e, t, n) {
	let r = $i(e, t, n);
	if (r == null) return 0;
	{
		let e = r;
		return He(bn((t) => y(t, e), X), 0) | 0;
	}
}
var Ko = new Po("workbench", j(0, X), j(0, X), j(1, X), j(1, X), j(2, X), j(1, X), j(1, X), j(3, X), "'Topaz', 'Topaz a500a1', ui-monospace, Menlo, monospace", 0, (e, t, n, r, i) => {
	let a;
	switch (a = e.tag === 1 && (n + r) % 2 == 0 ? 0 : 1, a) {
		case 0: return [j(0, X), 1];
		default: return [j(Wo(n, r, t, i), X), 1];
	}
}, (e, t) => {
	for (let t = 0; t <= e.h - 1; t++) for (let n = 0; n <= e.w - 1; n++) Qi(e, n, t, j(3 - Go(e, n, t), X), 1);
}), qo = [
	G("#031008"),
	G("#0B3B1B"),
	G("#1C7F3D"),
	G("#3FD46E"),
	G("#A8FFC4")
], Jo = [
	Ro,
	Vo,
	Ko,
	new Po("phosphor", G("#020A05"), G("#04140A"), j(1, qo), j(2, qo), j(1, qo), G("#7DFFA8"), j(2, qo), j(4, qo), "ui-monospace, Menlo, monospace", 2, (e, t, n, r, i) => {
		let a = e.tag === 2 ? Li(i) / 255 + .15 + .3 * t : e.tag === 0 ? Li(i) / 255 + .3 * t : Li(i) / 255 * .5, o;
		switch (o = Li(i) < 16 ? 0 : e.tag === 1 && (n + r) % 2 == 1 ? 1 : 2, o) {
			case 0: return [j(0, qo), 1];
			case 1: return;
			default: return [j(O(4, Ui(n, r, O(4, a * 4.2))), qo), 1];
		}
	}, (e, t) => {
		ea(e, j(3, qo), 1);
		for (let t = 1; t <= e.w - 2; t++) Hi(t, 1) < .5 && Qi(e, t, 1, j(2, qo), 1), Hi(t, e.h - 2) < .5 && Qi(e, t, e.h - 2, j(2, qo), 1);
	})
];
function Yo(e) {
	return He(yn((t) => t.name === e, Jo), Ro);
}
var Xo = /* @__PURE__ */ new Map([]);
function Zo(e, t) {
	if (e.name === Ro.name) return t;
	{
		Xo.has(e.name) || Xo.set(e.name, /* @__PURE__ */ new Map([]));
		let n = la(Xo, e.name);
		n.size > 1e5 && n.clear();
		let r = Yi(t.w, t.h);
		for (let i = 0; i <= t.h - 1; i++) for (let a = 0; a <= t.w - 1; a++) {
			let o = $i(t, a, i);
			if (o != null) {
				let t = o, s = ((~~t.r * 256 + ~~t.g) * 256 + ~~t.b) * 16 + (i & 3) * 4 + (a & 3) | 0, c, u = l();
				if (c = [sa(n, s, new Oe(() => u, (e) => {
					u = e;
				})), u], c[0]) q(r, a, i, c[1]);
				else {
					let o, c = e.ink(No.Enabled, 0, a, i, t);
					o = c == null ? t : c[0], n.set(s, o), q(r, a, i, o);
				}
			}
		}
		return r;
	}
}
// ../../../toolchain-logo/art/src/LogoVector.fs
function Z(e) {
	return (he(e * 100) / 100).toString();
}
function Q(e, t) {
	if (e.name === Ro.name) return Pi(t);
	{
		let n = F(N(() => R((n) => L((r) => {
			let i = e.ink(No.Enabled, 0, r, n, t);
			return i == null ? t : i[0];
		}, J(0, 1, 3)), J(0, 1, 3))));
		return Pi(new Ai(Mn((e) => e.r, n, {
			GetZero: () => 0,
			Add: (e, t) => e + t,
			DivideByInt: (e, t) => e / t
		}), Mn((e) => e.g, n, {
			GetZero: () => 0,
			Add: (e, t) => e + t,
			DivideByInt: (e, t) => e / t
		}), Mn((e) => e.b, n, {
			GetZero: () => 0,
			Add: (e, t) => e + t,
			DivideByInt: (e, t) => e / t
		})));
	}
}
function Qo(e, t) {
	return " " + e + "=\"" + t + "\"";
}
function $(e, t) {
	return "<" + e + T("", A((e) => Qo(e[0], e[1]), t)) + "/>";
}
function $o(e, t) {
	return "<g" + T("", A((e) => Qo(e[0], e[1]), e)) + ">" + T("", t) + "</g>";
}
function es(e) {
	return T(" ", A((e) => Z(e[0]) + "," + Z(e[1]), e));
}
function ts(e) {
	return "M " + T(" L ", A((e) => Z(e[0]) + "," + Z(e[1]), e)) + " Z";
}
function ns(e, t, n, r, i) {
	return "<linearGradient" + Qo("id", t) + Qo("x1", "0") + Qo("y1", "0") + Qo("x2", Z(n)) + Qo("y2", Z(r)) + ">" + T("", A((t) => {
		let n = t[2];
		return $("stop", F(N(() => I(P(["offset", Z(t[0])]), N(() => I(P(["stop-color", Q(e, t[1])]), N(() => n < 1 ? P(["stop-opacity", Z(n)]) : _r())))))));
	}, i)) + "</linearGradient>";
}
function rs(e) {
	let t = Do(e), n = Oo(e);
	return Math.abs(t) < .01 && Math.abs(n) < .01 ? ["", ""] : ["translate(" + Z(-t) + " 0) rotate(" + Z(-n) + " 33 63)", "translate(" + Z(t) + " 0) rotate(" + Z(n) + " 95 63)"];
}
function is(e, t) {
	let n = (e) => "url(#" + t + "-" + e + ")", r = (e) => j(e, Ra), i = (e) => [
		0,
		e,
		1
	], a = F(N(() => I(P("<radialGradient" + Qo("id", t + "-board") + Qo("cx", "0.5") + Qo("cy", "0.5") + Qo("r", "0.75") + ">" + $("stop", [["offset", "0"], ["stop-color", Q(e, j(0, Na))]]) + $("stop", [["offset", "0.5"], ["stop-color", Q(e, j(1, Na))]]) + $("stop", [["offset", "1"], ["stop-color", Q(e, j(2, Na))]]) + "</radialGradient>"), N(() => I(P(ns(e, t + "-body", 1, 1, [
		i(j(0, Pa)),
		[
			.4,
			j(1, Pa),
			1
		],
		[
			.8,
			j(2, Pa),
			1
		],
		[
			1,
			j(3, Pa),
			1
		]
	])), N(() => I(P(ns(e, t + "-lit", 1, 1, [[
		0,
		j(0, Fa),
		.9
	], [
		1,
		j(1, Fa),
		.3
	]])), N(() => I(P(ns(e, t + "-shaded", 1, 1, [[
		0,
		j(0, Ia),
		.4
	], [
		1,
		j(1, Ia),
		.95
	]])), N(() => I(R((n) => {
		let a = n[1];
		return P(ns(e, t + "-" + n[0], a[0], a[1], [
			i(r(6)),
			[
				.2,
				r(4),
				1
			],
			[
				.5,
				r(2),
				1
			],
			[
				.75,
				r(3),
				1
			],
			[
				1,
				r(5),
				1
			]
		]));
	}, [["lead-v", [1, 0]], ["lead-h", [0, 1]]]), N(() => I(R((n) => {
		let a = n[1];
		return P(ns(e, t + "-" + n[0], a[0], a[1], [
			i(r(0)),
			[
				.4,
				r(1),
				1
			],
			[
				.8,
				r(3),
				1
			],
			[
				1,
				r(5),
				1
			]
		]));
	}, [["foot-top", [0, 1]], ["foot-left", [1, 0]]]), N(() => R((n) => {
		let a = n[1];
		return P(ns(e, t + "-" + n[0], a[0], a[1], [
			i(r(5)),
			[
				.2,
				r(3),
				1
			],
			[
				.6,
				r(1),
				1
			],
			[
				1,
				r(0),
				1
			]
		]));
	}, [["foot-bottom", [0, 1]], ["foot-right", [1, 0]]]))))))))))))))), o = (e, t, n, r, i) => $("rect", [
		["x", Z(e)],
		["y", Z(t)],
		["width", Z(n)],
		["height", Z(r)],
		["rx", Z(i)]
	]), s = (t, i, a) => [
		$o([["fill", "#000000"], ["opacity", "0.7"]], vr(N(() => L((e) => a(e - 3.75)(0)(7.5)(48)(1.5), ba)))),
		$o([["fill", n(t)]], vr(N(() => L((e) => a(e - 3.5)(2)(7)(38)(1), ba)))),
		$o([
			["fill", n(i)],
			["stroke", Q(e, r(4))],
			["stroke-width", "0.5"]
		], vr(N(() => L((e) => a(e - 4.25)(38)(8.5)(12)(2), ba))))
	], c = cn([
		s("lead-v", "foot-top", (e) => ((t) => ((n) => ((r) => ((i) => o(e, 100 - t - r, n, r, i)))))),
		s("lead-v", "foot-bottom", (e) => ((t) => ((n) => ((r) => ((i) => o(e, 412 + t, n, r, i)))))),
		s("lead-h", "foot-left", (e) => ((t) => ((n) => ((r) => ((i) => o(100 - t - r, e, r, n, i)))))),
		s("lead-h", "foot-right", (e) => ((t) => ((n) => ((r) => ((i) => o(412 + t, e, r, n, i))))))
	]), l = F(N(() => R((t) => {
		let n = Sa(t).tip, r = n[1], i = n[0];
		return I(P($("circle", [
			["cx", Z(i)],
			["cy", Z(r)],
			["r", "4.5"],
			["fill", Q(e, za)],
			["stroke", Q(e, Ba)],
			["stroke-width", "1.2"]
		])), N(() => P($("circle", [
			["cx", Z(i)],
			["cy", Z(r)],
			["r", "1.8"],
			["fill", Q(e, Va)]
		]))));
	}, J(0, 1, 63))));
	return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 512 512\" aria-hidden=\"true\"><defs>" + T("", a) + "</defs>" + $("rect", [
		["width", "512"],
		["height", "512"],
		["fill", n("board")]
	]) + T("", l) + T("", c) + $("polygon", [["points", es(Ta)], ["fill", n("lit")]]) + $("polygon", [["points", es(Ea)], ["fill", n("shaded")]]) + $("polygon", [["points", es(Da)], ["fill", n("body")]]) + $("polygon", [
		["points", es(Oa)],
		["fill", "none"],
		["stroke", Q(e, La)],
		["stroke-width", "1.2"],
		["opacity", "0.6"]
	]) + $o([["class", "comets"]], []) + "</svg>";
}
function as(e, t) {
	let n = Eo(t), r = "M 96 " + Z(n(96)) + T("", F(N(() => L((e) => " L " + _(e) + " " + Z(n(e)), J(98, 2, 256))))), i = Q(e, Ga), a = Q(e, Wa), o = [
		["fill", "none"],
		["stroke-linecap", "round"],
		["stroke-linejoin", "round"]
	];
	return $("path", k(o, [
		["d", r],
		["stroke", i],
		["stroke-width", "6"]
	])) + $("path", k(o, [
		["d", r],
		["stroke", a],
		["stroke-width", "3"],
		["opacity", "0.92"]
	])) + T("", F(N(() => R((t) => {
		let r = Z(n(t));
		return P($("line", [
			["x1", Z(t)],
			["y1", "256"],
			["x2", Z(t)],
			["y2", r],
			["stroke", i],
			["stroke-width", "5"],
			["stroke-linecap", "round"]
		]) + $("line", [
			["x1", Z(t)],
			["y1", "256"],
			["x2", Z(t)],
			["y2", r],
			["stroke", a],
			["stroke-width", "2.4"],
			["stroke-linecap", "round"]
		]) + $("circle", [
			["cx", Z(t)],
			["cy", r],
			["r", "5"],
			["fill", i]
		]) + $("circle", [
			["cx", Z(t)],
			["cy", r],
			["r", "3.8"],
			["fill", a],
			["stroke", Q(e, Fi)],
			["stroke-width", "1.2"]
		]));
	}, Ma))));
}
function os(e, t, n) {
	let r = rs(n), i = (e, n) => $("path", [["d", ts(n)], ["fill", "url(#" + t + "-" + e + ")"]]), a = (e) => e === "" ? [] : [["transform", e]];
	return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 512 512\" role=\"img\" aria-label=\"fsdsp56k logo\"><defs>" + ns(e, t + "-blue", 1, 1, [
		[
			0,
			j(1, Ha),
			1
		],
		[
			.5,
			j(2, Ha),
			1
		],
		[
			1,
			j(4, Ha),
			1
		]
	]) + ns(e, t + "-cyan", 1, 1, [
		[
			0,
			j(1, Ua),
			1
		],
		[
			.5,
			j(2, Ua),
			1
		],
		[
			1,
			j(4, Ua),
			1
		]
	]) + "</defs>" + $o([["transform", "translate(256, 250) scale(1.7) translate(-64, -63)"]], [$o(k([["class", "left"]], a(r[0])), vr(N(() => L((e) => i("blue", e), ka)))), $o(k([["class", "right"]], a(r[1])), vr(N(() => L((e) => i("cyan", e), Aa))))]) + $("line", [
		["x1", "256"],
		["y1", "256"],
		["x2", "416"],
		["y2", "256"],
		["stroke", Q(e, Ga)],
		["stroke-width", "3"],
		["stroke-dasharray", "3 3"]
	]) + $("line", [
		["x1", "256"],
		["y1", "256"],
		["x2", "416"],
		["y2", "256"],
		["stroke", Q(e, Wa)],
		["stroke-width", "1.6"],
		["stroke-dasharray", "3 3"],
		["opacity", "0.85"]
	]) + $o([["class", "live"]], [as(e, n)]) + "</svg>";
}
function ss(e, t, n) {
	let r = e.length | 0, i = an((i, a) => {
		let o, s, c = a[0], l = a[1], u = j(D(0, i - 2), e), d = u[1], f = u[0], p = j(O(r - 1, i + 2), e), m = p[1], h = p[0], g, _ = Math.sqrt((o = f - h, o ** 2 + (s = d - m, s ** 2)));
		g = _ === 0 ? 1 : _;
		let v = (d - m) / g, ee = (f - h) / g, te = t * (1 - O(1, a[2])) ** n;
		return [[c - v * te, l + ee * te], [c + v * te, l - ee * te]];
	}, e);
	return ts(k(A((e) => e[0], i), hn(A((e) => e[1], i))));
}
function cs(e, t) {
	return T("", A((t) => {
		let n = Q(e, t.colour), r = Q(e, t.white), i = n, a = (e, t, n, r, i) => $("circle", [
			["cx", Z(e)],
			["cy", Z(t)],
			["r", Z(n)],
			["fill", r],
			["opacity", Z(i)]
		]);
		return $o([["opacity", Z(t.strength)]], vr(N(() => I(t.tail.length > 1 ? I(P($("path", [
			["d", ss(t.tail, 4.2, 1.2)],
			["fill", i],
			["opacity", "0.3"]
		])), N(() => P($("path", [
			["d", ss(t.tail, 1.5, 1.8)],
			["fill", i],
			["opacity", "0.95"]
		])))) : _r(), N(() => {
			let e, n, o;
			return I((e = t.head, e == null ? _r() : (n = e[1], o = e[0], I(P(a(o, n, 5.5 * t.flicker, i, .35)), N(() => P(a(o, n, 2, r, 1)))))), N(() => {
				let e = t.meet;
				if (e == null) return _r();
				{
					let t = e[0][1], n = e[0][0], o = e[1], s = o < .3 ? [.3 + .7 * o / .3, .55 * o / .3] : [1 + .8 * (o - .3) / .7, .55 * (1 - (o - .3) / .7)], c = O(1, o / .6);
					return I(P(a(n, t, 12 * s[0], i, s[1])), N(() => P(a(n, t, 4 * (.6 + .8 * c), r, 1 - c))));
				}
			}));
		})))));
	}, Mo(t)));
}
function ls(e) {
	let t = Ao(e);
	return [
		[
			Ka,
			7,
			t.blue
		],
		[
			qa,
			2.5,
			t.yellow
		],
		[
			qa,
			6,
			t.pulse
		]
	];
}
function us(e, t, n, r) {
	let i = D(0, O(1, t)) * (e.length - 1), a = ~~Math.floor(i) | 0, o = i - a;
	return j(O(e.length - 1, a + +(o > .65 && (o - .65) / .35 > Hi(n, r))), e);
}
function ds(e, t) {
	return dn(e, (n) => {
		let r = n / (e - 1), i = O(t.length - 2, xn((e) => e[0] <= r, t)) | 0, a = j(i, t), o = j(i + 1, t), s = a[0];
		return K(a[1], o[1], (r - s) / (o[0] - s));
	});
}
function fs(e) {
	let t = A((e) => e[1], e);
	return F(N(() => R((t) => {
		let n = t + .5, r = wn(F(N(() => R((t) => {
			let r = j(t, e), i = j((t + 1) % e.length, e), a = i[1], o = r[1], s = r[0];
			return o <= n && a > n || a <= n && o > n ? P(s + (n - o) * (i[0] - s) / (a - o)) : _r();
		}, J(0, 1, e.length - 1)))), { Compare: (e, t) => b(e, t) | 0 });
		return R((e) => L((e) => t * 256 + e | 0, J(D(0, ~~Math.ceil(j(e, r) - .5)), 1, O(255, ~~Math.floor(j(e + 1, r) - .5)))), J(0, 2, r.length - 2));
	}, J(D(0, ~~Math.floor(jn(t, { Compare: (e, t) => b(e, t) | 0 }))), 1, O(255, ~~Math.ceil(An(t, { Compare: (e, t) => b(e, t) | 0 })))))));
}
function ps(e, t, n, r) {
	let i = e * 3.141592653589793 / 180, a = Math.cos(i), o = Math.sin(i), s = a;
	return A((e) => {
		let r = e[0], i = e[1];
		return [t + (r - t) * s - (i - n) * o, n + (r - t) * o + (i - n) * s];
	}, r);
}
function ms(e) {
	return ~~Math.floor(e + .5) | 0;
}
var hs = G("#04080E"), gs = G("#07090D"), _s = [
	G("#3E4C5F"),
	G("#1A2230"),
	G("#070B14"),
	G("#04070D")
], vs = G("#222C3B"), ys = [
	G("#1F5770"),
	G("#184158"),
	G("#132D40")
], bs = [[
	[78, 125],
	[126, 77],
	[126, 101],
	[102, 125],
	[126, 149],
	[126, 173]
], [
	[108, 125],
	[126, 107],
	[126, 143]
]], xs = [
	[177, 125],
	[129, 77],
	[129, 101],
	[153, 125],
	[129, 149],
	[129, 173]
];
function Ss(e, t, n) {
	return wa(e, t, n);
}
function Cs(e, t, n) {
	let r, i, a = ms((Math.sqrt((r = t + 40, r ** 2 + (i = n + 40, i ** 2))) - 120) / 230 * 2) - 1 | 0, o = (e) => j(D(0, O(6, e + a)), Ra), s = [
		4,
		2,
		3,
		5
	], c = [
		0,
		0,
		1,
		2,
		3,
		5
	];
	for (let t = 1; t <= 13; t++) for (let n = 0; n <= 3; n++) e(n, t, o(j(n, s)));
	for (let t = 14; t <= 19; t++) for (let n = 0; n <= 3; n++) e(n, t, o(j(19 - t, c) + +(n === 3)));
	for (let t = 1; t <= 19; t++) e(4, t, hs);
}
function ws(e, t, n) {
	for (let r = -2; r <= 2; r++) for (let i = -2; i <= 2; i++) {
		let a = Math.sqrt(i * i + r * r);
		a <= 2.4 && q(e, t + i, n + r, a < .9 ? Va : i + r < 0 ? Ba : za);
	}
}
var Ts = new m(() => fs(Ss(46, 210, 8))), Es = new m(() => {
	let e = Yi(256, 256), t = ds(5, [
		[0, j(0, Na)],
		[.5, j(1, Na)],
		[1, j(2, Na)]
	]);
	for (let n = 0; n <= 255; n++) for (let r = 0; r <= 255; r++) {
		let i, a;
		q(e, r, n, us(t, Math.sqrt((i = r + .5 - 128, i ** 2 + (a = n + .5 - 128, a ** 2))) / 192, r, n));
	}
	for (let t = 0; t <= 15; t++) {
		let n = 59 + 9 * t | 0;
		ws(e, n + 2, 25), ws(e, n + 2, 231), ws(e, 25, n + 2), ws(e, 231, n + 2);
	}
	for (let t = 0; t <= 15; t++) {
		let n = 59 + 9 * t | 0, r = n + 2;
		Cs((t, r, i) => {
			q(e, n + t, 44 - r, i);
		}, r, 30), Cs((t, r, i) => {
			q(e, n + t, 211 + r, i);
		}, r, 226), Cs((t, r, i) => {
			q(e, 44 - r, n + t, i);
		}, 30, r), Cs((t, r, i) => {
			q(e, 211 + r, n + t, i);
		}, 226, r);
	}
	let n = (t, n) => {
		q(e, t % 256, ~~(t / 256), n(t % 256)(~~(t / 256)));
	}, r = fs(Ss(45, 214, 9));
	for (let e = 0; e <= r.length - 1; e++) n(j(e, r), (e) => ((e) => hs));
	let i = fs(Ss(44, 212, 9));
	for (let e = 0; e <= i.length - 1; e++) n(j(e, i), (e) => ((t) => e + t < 256 ? j(0, _s) : j(2, _s)));
	let a = fs(Ss(45, 211, 9));
	for (let e = 0; e <= a.length - 1; e++) n(j(e, a), (e) => ((t) => e + t < 256 ? j(1, _s) : j(3, _s)));
	let o = ds(5, [
		[0, j(0, Pa)],
		[.4, j(1, Pa)],
		[.8, j(2, Pa)],
		[1, j(3, Pa)]
	]), s = Ts.Value;
	for (let e = 0; e <= s.length - 1; e++) n(j(e, s), (e) => ((t) => us(o, (e - 46 + t - 46) / 328 * 1.1, e, t)));
	let l = new Set(fs(Ss(48, 208, 8))), u = f(l);
	try {
		for (; u["System.Collections.IEnumerator.MoveNext"]();) {
			let e = u["System.Collections.Generic.IEnumerator`1.get_Current"]() | 0;
			l.has(e - 1) && l.has(e + 1) && l.has(e - 256) && l.has(e + 256) || n(e, (e) => ((e) => vs));
		}
	} finally {
		c(u);
	}
	return e;
});
function Ds(e, t, n) {
	return F(N(() => R((r) => R((i) => {
		let a, o;
		return i >= 0 && r >= 0 && i < 256 && r < 256 && (a = i + .5 - e, a ** 2 + (o = r + .5 - t, o ** 2) <= n * n) ? P(r * 256 + i) : _r();
	}, J(~~Math.floor(e - n), 1, ~~Math.ceil(e + n))), J(~~Math.floor(t - n), 1, ~~Math.ceil(t + n)))));
}
function Os(e, t, n, r) {
	let i = f(t);
	try {
		for (; i["System.Collections.IEnumerator.MoveNext"]();) {
			let t = i["System.Collections.Generic.IEnumerator`1.get_Current"]() | 0, a = ~~(t / 256) | 0, o = t % 256 | 0;
			Hi(o, a) < r && q(e, o, a, n);
		}
	} finally {
		c(i);
	}
}
var ks = [
	".ddd.",
	"dwswd",
	"dsssd",
	"dwswd",
	".ddd."
];
function As(e) {
	let t, n = Es.Value;
	t = new Ji(n.w, n.h, pn(Es.Value.px));
	let r = Ao(e), i = ms(Do(e) * .85), a = (e, t) => A((t) => [t[0] + e, t[1]], t), o = k(A((t) => [fs(ps(-Oo(e), 102 - i, 125, a(-i, t))), Ha], bs), [[fs(ps(Oo(e), 155 + i, 125, a(i, xs))), Ua]]), s = /* @__PURE__ */ new Set([]);
	for (let e = 0; e <= o.length - 1; e++) {
		let t = j(e, o)[0];
		for (let e = 0; e <= t.length - 1; e++) ca(j(e, t), s);
	}
	let l = rn(Array(65536), 0, 65536, 99), u = Nr(), d = f(s);
	try {
		for (; d["System.Collections.IEnumerator.MoveNext"]();) {
			let e = d["System.Collections.Generic.IEnumerator`1.get_Current"]() | 0;
			M(l, e, 0), Ir(u, e);
		}
	} finally {
		c(d);
	}
	for (; Fr(u) > 0;) {
		let e = Lr(u) | 0, t = j(e, l) | 0;
		if (t < 12) {
			let n = [
				e - 1,
				e + 1,
				e - 256,
				e + 256
			];
			for (let e = 0; e <= n.length - 1; e++) {
				let r = j(e, n) | 0;
				r >= 0 && r < 65536 && j(r, l) > t + 1 && (M(l, r, t + 1 | 0), Ir(u, r));
			}
		}
	}
	let p = r.level * (1 + .6 * (r.blue - .5)) + .6 * r.pulse, m = Ts.Value;
	for (let e = 0; e <= m.length - 1; e++) {
		let n = j(e, m) | 0, r = j(n, l) | 0;
		if (r > 0 && r <= 12) {
			let e = ~~(n / 256) | 0, i = n % 256 | 0, a = p * Math.exp(-(r - 1) / 4.5);
			a > .55 ? q(t, i, e, j(0, ys)) : a > .3 + .25 * Hi(i, e) ? q(t, i, e, j(1, ys)) : a > .08 + .3 * Hi(i, e) && q(t, i, e, j(2, ys));
		}
	}
	let h = Mo(e);
	for (let e = 0; e <= h.length - 1; e++) {
		let n = j(e, h), r = /* @__PURE__ */ new Set([]), i = /* @__PURE__ */ new Set([]), a = n.tail;
		for (let e = 0; e <= a.length - 1; e++) {
			let t, n, o = j(e, a), s = o[1], c = o[0], l = o[2], u = Ds(c / 2, s / 2, D(.5, 2.1 * (t = 1 - O(1, l), t ** 1.2)));
			for (let e = 0; e <= u.length - 1; e++) ca(j(e, u), r);
			let d = Ds(c / 2, s / 2, .75 * (n = 1 - O(1, l), n ** 1.8));
			for (let e = 0; e <= d.length - 1; e++) ca(j(e, d), i);
			let f = ~~Math.floor(c / 2) | 0, p = ~~Math.floor(s / 2) | 0, m = f | 0;
			l < .75 && m >= 0 && p >= 0 && m < 256 && p < 256 && ca(p * 256 + m, i);
		}
		let o = K(j(2, Pa), n.colour, .45 + .55 * n.strength), s = K(j(2, Pa), n.colour, .25 + .45 * n.strength);
		Os(t, Cr((e) => !i.has(e), r), s, .5), Os(t, i, o, 1);
		let c = n.head;
		if (c != null) {
			let e = c[1], r = c[0];
			Os(t, Ds(r / 2, e / 2, 2.75 * n.flicker), s, .5), Os(t, Ds(r / 2, e / 2, 1.1), K(j(2, Pa), n.white, .5 + .5 * n.strength), 1);
		}
		let l = n.meet;
		if (l != null) {
			let e = l[0][1], r = l[0][0], i = l[1], a = i < .3 ? [.3 + .7 * i / .3, .55 * i / .3] : [1 + .8 * (i - .3) / .7, .55 * (1 - (i - .3) / .7)];
			Os(t, Ds(r / 2, e / 2, 6 * a[0]), n.colour, a[1] * n.strength);
			let o = O(1, i / .6);
			Os(t, Ds(r / 2, e / 2, 2 * (.6 + .8 * o)), n.white, (1 - o) * n.strength);
		}
	}
	let g = f(s);
	try {
		for (; g["System.Collections.IEnumerator.MoveNext"]();) {
			let e = g["System.Collections.Generic.IEnumerator`1.get_Current"]() | 0, n = ~~(e / 256) | 0, r = e % 256 | 0;
			for (let i = 1; i <= 3; i++) !s.has(e + i * 256) && (i < 3 || Hi(r, n + i) < .5) && q(t, r, n + i, gs);
		}
	} finally {
		c(g);
	}
	for (let e = 0; e <= o.length - 1; e++) {
		let n = j(e, o), r = n[1], i = n[0], a = new Set(i), s = A((e) => e % 256 | 0, i), c = A((e) => ~~(e / 256) | 0, i), l = s, u = jn(l, { Compare: (e, t) => b(e, t) | 0 }) | 0, d = An(l, { Compare: (e, t) => b(e, t) | 0 }) | 0, f = jn(c, { Compare: (e, t) => b(e, t) | 0 }) | 0, p = An(c, { Compare: (e, t) => b(e, t) | 0 }) | 0, m = f | 0, h = u | 0;
		for (let e = 0; e <= i.length - 1; e++) {
			let n = j(e, i) | 0, o = ~~(n / 256) | 0, s = n % 256 | 0, c = ((s - h) / D(1, d - h) + (o - m) / D(1, p - m)) / 2;
			q(t, s, o, (!a.has(n - 256) || !a.has(n - 1)) && c < .75 ? j(0, r) : us(r.slice(1, r.length), c, s, o));
		}
	}
	let _ = Eo(e), v = rn(Array(65536), 0, 65536, 1e9);
	fn(F(N(() => L((e) => [e / 2, _(e) / 2], J(96, 1, 256))))).forEach((e) => {
		let t = e[0], n = e[1], r = t[0], i = t[1], a = n[0], o = n[1];
		for (let e = ~~Math.floor(O(i, o) - 2.2); e <= ~~Math.ceil(D(i, o) + 2.2); e++) for (let t = ~~Math.floor(O(r, a) - 2.2); t <= ~~Math.ceil(D(r, a) + 2.2); t++) {
			let n, s, c;
			if (t >= 0 && e >= 0 && t < 256 && e < 256) {
				let l = e + .5 - i, u = t + .5 - r, d = o - i, f = a - r, p = D(0, O(1, (u * f + l * d) / (n = f * f + d * d, n === 0 ? 1 : n))), m = (s = u - p * f, s ** 2 + (c = l - p * d, c ** 2));
				m < j(e * 256 + t, v) && M(v, e * 256 + t, m);
			}
		}
	});
	for (let e = 0; e <= 65535; e++) j(e, v) <= 3.0625 && q(t, e % 256, ~~(e / 256), j(e, v) <= .64 ? Wa : Ga);
	let ee = f(J(128, 2, 208));
	try {
		for (; ee["System.Collections.IEnumerator.MoveNext"]();) {
			let e = ee["System.Collections.Generic.IEnumerator`1.get_Current"]() | 0;
			q(t, e, 127, Ga), q(t, e, 129, Ga), q(t, e, 128, Wa);
		}
	} finally {
		c(ee);
	}
	for (let e = 0; e <= 16; e++) {
		let n = 128 + 5 * e | 0, r = ms(_(256 + 10 * e) / 2) | 0;
		if (r !== 128) for (let e = O(128, r); e <= D(128, r); e++) q(t, n - 1, e, Ga), q(t, n + 1, e, Ga), q(t, n, e, Wa);
		Cn((e, i) => {
			Er((i, a) => {
				switch (a) {
					case "d":
						q(t, n - 2 + i, r - 2 + e, Ga);
						break;
					case "s":
						q(t, n - 2 + i, r - 2 + e, Wa);
						break;
					case "w": q(t, n - 2 + i, r - 2 + e, Fi);
				}
			}, i.split(""));
		}, ks);
	}
	return t;
}
// ../../../toolchain-logo/art/src/Area.fs
function js(e) {
	return e.closest("[data-aesthetic]");
}
function Ms(e) {
	let t = js(e);
	return t == null ? Ro : Yo(t.getAttribute("data-aesthetic"));
}
function Ns(e, t) {
	t(Ms(e)), js(e)?.addEventListener("aestheticchange", (n) => {
		t(Ms(e));
	});
}
// ../../../toolchain-logo/art/src/LogoView.fs
var Ps = new Oe(0);
function Fs(e, t) {
	Ps.contents = Ps.contents + 1 | 0;
	let n = `logo${Ps.contents}`, r = ia(), i = document.createElement("div");
	i.className = "fsdsp56k-logo", e.appendChild(i);
	let a = new Oe(Y), o = new Oe(Ro), s = new Oe(t), c = new Oe(0), l = [[
		"left",
		ka,
		[203.3, 250]
	], [
		"right",
		Aa,
		[308.7, 250]
	]], u = [
		"blue",
		"yellow",
		"pulse"
	], d = (e) => i.querySelector(e), f = () => {
		let e = i.getBoundingClientRect().width;
		if (e > 0) {
			let t = e / 512, n = t * (window.devicePixelRatio || 1);
			d(".glow").style.transform = `scale(${t})`;
			for (let e = 0; e <= l.length - 1; e++) {
				let t = j(e, l), r = En(ls(Y), u);
				for (let e = 0; e <= r.length - 1; e++) {
					let i = j(e, r), a = d(Lt(".", t[0], " .", i[1]));
					a.width = Math.floor(512 * n), a.height = Math.floor(512 * n);
					let s = 1.7 * n;
					((e) => {
						e.clearRect(0, 0, a.width, a.height), e.filter = `blur(${i[0][1] * s}px)`, e.setTransform(s, 0, 0, s, n * 147.2, n * 142.9), e.fillStyle = Q(o.contents, i[0][0]);
						for (let n of F(N(() => L((e) => new Path2D(ts(e)), t[1])))) e.fill(n);
					})(a.getContext("2d"));
				}
			}
		}
	}, p = () => d(".pixels"), m = () => {
		let e = a.contents, t = rs(e), n = [["left", t[0]], ["right", t[1]]];
		for (let e = 0; e <= n.length - 1; e++) {
			let t = j(e, n), r = t[1], i = d("svg g." + t[0]);
			r === "" ? i.removeAttribute("transform") : i.setAttribute("transform", r);
		}
		d("g.live").innerHTML = as(o.contents, e), d("g.comets").innerHTML = cs(o.contents, e);
		let r = Ao(e), i = Do(e);
		d(".glow").style.opacity = r.level.toString();
		let l = [["left", -1], ["right", 1]];
		for (let t = 0; t <= l.length - 1; t++) {
			let n = j(t, l), a = n[1], o = n[0];
			d(Lt(".half.", o)).style.transform = "translate(" + (a * 1.7 * i).toString() + "px, 0) rotate(" + (a * Oo(e)).toString() + "deg)";
			let s = En(ls(e), u);
			for (let e = 0; e <= s.length - 1; e++) {
				let t = j(e, s), n = t[1], i = d(Lt(".", o, " .", n));
				i.style.opacity = t[0][2].toString(), i.style.transform = "scale(" + (n === "pulse" ? r.pulseScale : r.scale).toString() + ")";
			}
		}
		(s.contents === "pixel" || c.contents > 0) && ra(p(), Zo(o.contents, As(e)));
	}, h = new Oe(0), g = new Oe(0), _ = (e) => {
		let t = D(0, O(.05, (e - g.contents) / 1e3));
		g.contents = e, a.contents = To(() => Math.random(), t, a.contents), m(), h.contents = a.contents.awake ? window.requestAnimationFrame(_) : 0;
	}, v = (e) => {
		a.contents = e(a.contents), !r && h.contents === 0 && (g.contents = performance.now(), h.contents = window.requestAnimationFrame(_));
	};
	return Ns(e, (e) => {
		o.contents = e, i.innerHTML = is(o.contents, n) + "<div class=\"glow\">" + T("", F(N(() => R((e) => {
			let t = e[2][1], n = e[2][0];
			return P(`<div class="half ${e[0]}" style="transform-origin: ${n}px ${t}px">` + T("", F(N(() => L((e) => `<canvas class="${e}" style="transform-origin: ${n}px ${t}px"></canvas>`, u)))) + "</div>");
		}, l)))) + "</div>" + os(o.contents, n, a.contents) + "<canvas class=\"pixels\" width=\"256\" height=\"256\"></canvas>", p().style.clipPath = s.contents === "pixel" ? "inset(0 0 0 0)" : "inset(0 0 0 100%)", f(), m();
	}), new ResizeObserver(f).observe(i), r || (i.addEventListener("pointerenter", (e) => {
		v(xo);
	}), i.addEventListener("pointerleave", (e) => {
		v(So);
	}), i.addEventListener("click", (e) => {
		let t, n, r, a;
		v((t = (n = (r = e, a = i.getBoundingClientRect(), [(r.clientX - a.left) / a.width * 512, (r.clientY - a.top) / a.height * 512]), Ca(n[0], n[1])), (e) => Co(() => Math.random(), t, e)));
	}), i.addEventListener("mousedown", (e) => {
		e.detail >= 1 && e.preventDefault();
	})), (e) => {
		if (e !== s.contents) {
			s.contents = e, c.contents = c.contents + 1 | 0, m();
			let t = e === "pixel" ? ["inset(0 100% 0 0)", "inset(0 0 0 0)"] : ["inset(0 0 0 0)", "inset(0 0 0 100%)"], n = t[1];
			p().style.clipPath = n, p().animate([{ clipPath: t[0] }, { clipPath: n }], {
				duration: 700,
				easing: "ease-in-out"
			});
			let r = (e) => {
				m(), e > 0 ? window.requestAnimationFrame((t) => {
					r(e - 1);
				}) : c.contents = c.contents - 1 | 0;
			};
			r(45);
		}
	};
}
// Entry.fs
function Is(e) {
	let t = Fs(e, "vector");
	return {
		switchTo: (e) => {
			t(e);
		},
		pixels: e.querySelector(".fsdsp56k-logo .pixels")
	};
}
window.fsdsp56kLogo = { mount: Is }, window.dispatchEvent(new CustomEvent("fsdsp56k-logo"));
