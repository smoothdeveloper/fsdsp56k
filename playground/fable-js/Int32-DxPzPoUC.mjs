//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Util.js
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
function ne(t) {
	let n = t;
	return typeof n.offset == "number" ? n.offset : t.kind === e.Utc ? 0 : t.getTimezoneOffset() * -6e4;
}
var p = class e {
	static id(t) {
		return e.idMap.has(t) || e.idMap.set(t, ++e.count), e.idMap.get(t);
	}
};
p.idMap = /* @__PURE__ */ new WeakMap(), p.count = 0;
function m(e) {
	let t = 0, n = 5381, r = e.length;
	for (; t < r;) n = n * 33 ^ e.charCodeAt(t++);
	return n;
}
function re(e) {
	return e * 2654435761 | 0;
}
function ie(e) {
	return m(e.toString(32));
}
function h(e) {
	let t = 0, n = e.length;
	for (let r = 0; r < n; r++) {
		let n = e[r];
		t = (t << 5) + t ^ n;
	}
	return t;
}
function ae(e) {
	return e.getTime();
}
function oe(e) {
	let t = e.length, n = Array(t);
	for (let r = 0; r < t; r++) n[r] = g(e[r]);
	return h(n);
}
function g(e) {
	if (e == null) return 0;
	switch (typeof e) {
		case "boolean": return +!!e;
		case "number": return re(e);
		case "bigint": return ie(e);
		case "string": return m(e);
		default: return o(e) ? e.GetHashCode() : n(e) ? oe(e) : e instanceof Date ? ae(e) : Object.getPrototypeOf(e)?.constructor === Object ? h(Object.values(e).map((e) => g(e))) : re(p.id(e));
	}
}
function se(e, t, n) {
	if (e == null) return t == null;
	if (t == null || e.length !== t.length) return !1;
	for (let r = 0; r < e.length; r++) if (!n(e[r], t[r])) return !1;
	return !0;
}
function ce(e, t) {
	return se(e, t, _);
}
function le(e, t) {
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return !1;
	n.sort(), r.sort();
	for (let i = 0; i < n.length; i++) if (n[i] !== r[i] || !_(e[n[i]], t[r[i]])) return !1;
	return !0;
}
function _(e, t) {
	return e === t ? !0 : e == null ? t == null : t == null ? !1 : a(e) ? e.Equals(t) : n(e) ? n(t) && ce(e, t) : typeof e == "object" ? e instanceof Date ? t instanceof Date && ue(e, t) === 0 : Object.getPrototypeOf(e)?.constructor === Object && le(e, t) : !1;
}
function ue(e, t) {
	let n, r;
	return "offset" in e && "offset" in t ? (n = e.getTime(), r = t.getTime()) : (n = e.getTime() + ne(e), r = t.getTime() + ne(t)), n === r ? 0 : n < r ? -1 : 1;
}
function de(e, t) {
	return e === t ? 0 : e < t ? -1 : e > t ? 1 : Number.isNaN(e) ? Number.isNaN(t) ? 0 : -1 : 1;
}
function fe(e, t, n) {
	if (e == null) return t == null ? 0 : 1;
	if (t == null) return -1;
	if (e.length !== t.length) return e.length < t.length ? -1 : 1;
	for (let r = 0, i = 0; r < e.length; r++) if (i = n(e[r], t[r]), i !== 0) return i;
	return 0;
}
function pe(e, t) {
	return fe(e, t, v);
}
function me(e, t) {
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return n.length < r.length ? -1 : 1;
	n.sort(), r.sort();
	for (let i = 0, a = 0; i < n.length; i++) {
		let o = n[i];
		if (o !== r[i]) return o < r[i] ? -1 : 1;
		if (a = v(e[o], t[o]), a !== 0) return a;
	}
	return 0;
}
function v(e, t) {
	return e === t ? 0 : e == null ? t == null ? 0 : -1 : t == null ? 1 : i(e) ? e.CompareTo(t) : n(e) ? n(t) ? pe(e, t) : -1 : typeof e == "object" ? e instanceof Date ? t instanceof Date ? ue(e, t) : -1 : Object.getPrototypeOf(e)?.constructor === Object ? me(e, t) : -1 : de(e, t);
}
function he(e, t = 0) {
	let n = 10 ** t, r = +(t ? e * n : e).toFixed(8), i = Math.floor(r), a = r - i, o = a > .49999999 && a < .50000001 ? i % 2 == 0 ? i : i + 1 : Math.round(r);
	return t ? o / n : o;
}
function ge(e) {
	n(e) ? e.splice(0) : e.clear();
}
var _e = /* @__PURE__ */ new WeakMap();
function ve(e) {
	return _e.get(e) ?? ((t) => (n) => e(t, n));
}
function ye(e, t, n, r, i) {
	if (ArrayBuffer.isView(e) && ArrayBuffer.isView(n)) n.set(e.subarray(t, t + i), r);
	else for (let a = 0; a < i; ++a) n[r + a] = e[t + a];
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Types.js
function be(e) {
	let t = 0, n = "[";
	for (let r of e) {
		if (t === 0) n += y(r);
		else if (t === 100) {
			n += "; ...";
			break;
		} else n += "; " + y(r);
		t++;
	}
	return n + "]";
}
function y(e, t = 0) {
	return typeof e == "string" ? "\"" + e + "\"" : b(e, t);
}
function b(e, t = 0) {
	if (typeof e == "object" && e) {
		if (typeof e.toString == "function" && e.toString !== Object.prototype.toString) return e.toString();
		if (Symbol.iterator in e) return be(e);
		{
			let n = Object.getPrototypeOf(e)?.constructor;
			return n === Object && t < 10 ? "{ " + Object.entries(e).map(([e, n]) => e + " = " + y(n, t + 1)).join("\n  ") + " }" : n?.name ?? "";
		}
	}
	return String(e);
}
function xe(e, t) {
	if (t.length === 0) return e;
	{
		let n, r = !0;
		return t.length === 1 ? (n = y(t[0]), r = n.indexOf(" ") >= 0) : n = t.map((e) => y(e)).join(", "), e + (r ? " (" : " ") + n + (r ? ")" : "");
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
		let e = this.fields.map((e) => g(e));
		return e.splice(0, 0, re(this.tag)), h(e);
	}
	Equals(e) {
		return this === e ? !0 : u(this, e) ? this.tag === e.tag && ce(this.fields, e.fields) : !1;
	}
	CompareTo(e) {
		return this === e ? 0 : u(this, e) ? this.tag === e.tag ? pe(this.fields, e.fields) : this.tag < e.tag ? -1 : 1 : -1;
	}
};
function Ce(e) {
	let t = {}, n = Object.keys(e);
	for (let r = 0; r < n.length; r++) t[n[r]] = e[n[r]];
	return t;
}
function we(e) {
	return "{ " + Object.entries(e).map(([e, t]) => e + " = " + y(t)).join("\n  ") + " }";
}
function Te(e) {
	return h(Object.values(e).map((e) => g(e)));
}
function Ee(e, t) {
	if (e === t) return !0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) if (!_(e[n[r]], t[n[r]])) return !1;
		return !0;
	}
	return !1;
}
function De(e, t) {
	if (e === t) return 0;
	if (u(e, t)) {
		let n = Object.keys(e);
		for (let r = 0; r < n.length; r++) {
			let i = v(e[n[r]], t[n[r]]);
			if (i !== 0) return i;
		}
		return 0;
	}
	return -1;
}
var Oe = class {
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
}, ke = class {
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
}, x = Symbol("numeric");
function Ae(e) {
	return typeof e == "number" || typeof e == "bigint" || e?.[x];
}
function je(e) {
	return typeof e == "number" && Number.isInteger(e) || typeof e == "bigint";
}
function Me(e, t) {
	return typeof e == "number" || typeof e == "bigint" ? e < t ? -1 : +(e > t) : e.CompareTo(t);
}
function S(e, t) {
	return typeof e == "number" ? e * t : typeof e == "bigint" ? e * BigInt(t) : e[x]().multiply(t);
}
function Ne(e, t) {
	return typeof e == "number" ? e / t : typeof e == "bigint" ? Number(e) / t : e[x]().multiply(1 / t);
}
function C(e, t) {
	return typeof e == "number" ? e.toFixed(t) : typeof e == "bigint" ? e.toString() : e[x]().toFixed(t);
}
function w(e, t) {
	return typeof e == "number" ? e.toPrecision(t) : typeof e == "bigint" ? e.toString() : e[x]().toPrecision(t);
}
function Pe(e, t) {
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
function Fe(e, t) {
	return typeof e == "number" ? e.toExponential(t) : typeof e == "bigint" ? Pe(e, t) : e[x]().toExponential(t);
}
function Ie(e) {
	return typeof e == "number" ? (Number(e) >>> 0).toString(16) : typeof e == "bigint" ? BigInt.asUintN(64, e).toString(16) : e[x]().toHex();
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Option.js
var Le = class e {
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
		return g(this.value);
	}
	Equals(t) {
		return t != null && _(this.value, t instanceof e ? t.value : t);
	}
	CompareTo(t) {
		return t == null ? 1 : v(this.value, t instanceof e ? t.value : t);
	}
};
function Re(e) {
	if (e == null) throw new t("Option has no value");
	return e instanceof Le ? e.value : e;
}
function ze(e) {
	return e == null || e instanceof Le ? new Le(e) : e;
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Native.js
function T(e, t) {
	if (e == null) return Array(t);
	{
		let n = e;
		return typeof n == "function" ? new n(t) : Array(t);
	}
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Global.js
var Be = "Collection was empty.", Ve = "Value cannot be null.", He = " (Parameter '", Ue = [
	"Sun",
	"Mon",
	"Tue",
	"Wed",
	"Thu",
	"Fri",
	"Sat"
], We = [
	"Sunday",
	"Monday",
	"Tuesday",
	"Wednesday",
	"Thursday",
	"Friday",
	"Saturday"
], Ge = [
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
], E = [
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
function D(e, t, n) {
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
function O(n, r, i) {
	let a = 0, o = 0, s = "", c = i ? P(n.getTime(), e.Utc) : n;
	for (; a < r.length;) {
		let n = r[a];
		switch (n) {
			case "d":
				switch (o = D(r, a, "d"), a += o, o) {
					case 1:
						s += I(c);
						break;
					case 2:
						s += f(I(c), 2);
						break;
					case 3:
						s += Ue[U(c)];
						break;
					default: s += We[U(c)];
				}
				break;
			case "f":
				if (o = D(r, a, "f"), a += o, o <= 3) {
					let e = 10 ** (3 - o);
					s += f(Math.floor(R(c) / e), o);
				} else if (o <= 7) s += ("" + R(c)).padEnd(o, "0");
				else throw "Input string was not in a correct format.";
				break;
			case "F":
				if (o = D(r, a, "F"), a += o, o <= 3) {
					let e = 10 ** (3 - o), t = Math.floor(R(c) / e);
					t != 0 && (s += f(t, o));
				} else if (o <= 7) {
					let e = R(c);
					e != 0 && (s += f(e, 3));
				} else throw "Input string was not in a correct format.";
				break;
			case "g":
				o = D(r, a, "g"), a += o, s += "A.D.";
				break;
			case "h":
				switch (o = D(r, a, "h"), a += o, o) {
					case 1:
						let e = L(c) % 12;
						s += e || 12;
						break;
					default:
						let t = L(c) % 12;
						s += f(t || 12, 2);
				}
				break;
			case "H":
				switch (o = D(r, a, "H"), a += o, o) {
					case 1:
						s += L(c);
						break;
					default: s += f(L(c), 2);
				}
				break;
			case "K":
				if (o = D(r, a, "K"), a += o, o === 1) switch (Je(c)) {
					case e.Utc:
						s += "Z";
						break;
					case e.Local:
						s += k(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified:
				}
				break;
			case "m":
				switch (o = D(r, a, "m"), a += o, o) {
					case 1:
						s += z(c);
						break;
					default: s += f(z(c), 2);
				}
				break;
			case "M":
				switch (o = D(r, a, "M"), a += o, o) {
					case 1:
						s += B(c);
						break;
					case 2:
						s += f(B(c), 2);
						break;
					case 3:
						s += Ge[B(c) - 1];
						break;
					default: s += E[B(c) - 1];
				}
				break;
			case "s":
				switch (o = D(r, a, "s"), a += o, o) {
					case 1:
						s += V(c);
						break;
					default: s += f(V(c), 2);
				}
				break;
			case "t":
				switch (o = D(r, a, "t"), a += o, o) {
					case 1:
						s += c.getHours() < 12 ? "A" : "P";
						break;
					default: s += c.getHours() < 12 ? "AM" : "PM";
				}
				break;
			case "y":
				switch (o = D(r, a, "y"), a += o, o) {
					case 1:
						s += H(c) % 100;
						break;
					case 2:
						s += f(H(c) % 100, 2);
						break;
					default: s += f(H(c), o);
				}
				break;
			case "z":
				o = D(r, a, "z"), a += o;
				let l = "";
				switch (Je(c)) {
					case e.Utc:
						l = "+00:00";
						break;
					case e.Local:
						l = k(c.getTimezoneOffset() * -6e4);
						break;
					case e.Unspecified: l = k(at(c).getTimezoneOffset() * -6e4);
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
				let [te, ne] = qe(r, a);
				s += te, a += ne;
				break;
			case "%":
				let p = Ke(r, a);
				if (p >= 0 && p !== 37) a += 2, s += O(c, String.fromCharCode(p), i);
				else throw new t("Invalid format string");
				break;
			case "\\":
				let m = Ke(r, a);
				if (m >= 0) a += 2, s += String.fromCharCode(m);
				else throw new t("Invalid format string");
				break;
			default: a++, s += n;
		}
	}
	return s;
}
function Je(t) {
	return t.kind ?? e.Unspecified;
}
function k(e) {
	let t = e < 0;
	e = Math.abs(e);
	let n = ~~(e / 36e5), r = e % 36e5 / 6e4;
	return (t ? "-" : "+") + f(n, 2) + ":" + f(r, 2);
}
function Ye(t, n) {
	if (n) return t.toISOString();
	{
		let n = t.kind == null || t.kind === e.Local;
		return f(t.getFullYear(), 4) + "-" + f(t.getMonth() + 1, 2) + "-" + f(t.getDate(), 2) + "T" + f(t.getHours(), 2) + ":" + f(t.getMinutes(), 2) + ":" + f(t.getSeconds(), 2) + "." + f(t.getMilliseconds(), 3) + (n ? k(t.getTimezoneOffset() * -6e4) : "");
	}
}
function Xe(e, t) {
	let n = e.toISOString();
	return n.substring(0, n.length - 1) + k(t);
}
function Ze(n, r) {
	let i = new Date(n.getTime() + (n.offset ?? 0));
	if (typeof r != "string") return i.toISOString().replace(/\.\d+/, "").replace(/[A-Z]|\.\d+/g, " ") + k(n.offset ?? 0);
	if (r.length === 1) switch (r) {
		case "D": return A(i);
		case "d": return j(i);
		case "F": return A(i) + " " + M(i);
		case "f": return A(i) + " " + N(i);
		case "G": return j(i) + " " + M(i);
		case "g": return j(i) + " " + N(i);
		case "M":
		case "m": return tt(i);
		case "O":
		case "o": return Xe(i, n.offset ?? 0);
		case "R":
		case "r": return Qe(P(n.getTime(), e.Utc));
		case "s": return $e(F(i));
		case "T": return M(F(i));
		case "t": return N(F(i));
		case "u": return et(P(n.getTime(), e.Utc));
		case "U": {
			let t = P(n.getTime(), e.Utc);
			return A(t) + " " + M(t);
		}
		case "Y":
		case "y": return nt(i);
		default: throw new t("Unrecognized Date print format");
	}
	else return O(i, r, !0);
}
function A(e) {
	return We[U(e)] + ", " + f(I(e), 2) + " " + E[B(e) - 1] + " " + H(e);
}
function j(e) {
	return f(B(e), 2) + "/" + f(I(e), 2) + "/" + H(e);
}
function M(e) {
	return f(L(e), 2) + ":" + f(z(e), 2) + ":" + f(V(e), 2);
}
function N(e) {
	return f(L(e), 2) + ":" + f(z(e), 2);
}
function Qe(e) {
	let t = F(e);
	return Ue[U(t)] + ", " + f(I(t), 2) + " " + Ge[B(t) - 1] + " " + H(t) + " " + f(L(t), 2) + ":" + f(z(t), 2) + ":" + f(V(t), 2) + " GMT";
}
function $e(e) {
	return f(H(e), 4) + "-" + f(B(e), 2) + "-" + f(I(e), 2) + "T" + f(L(e), 2) + ":" + f(z(e), 2) + ":" + f(V(e), 2);
}
function et(e) {
	let t = F(e);
	return f(H(t), 4) + "-" + f(B(t), 2) + "-" + f(I(t), 2) + " " + f(L(t), 2) + ":" + f(z(t), 2) + ":" + f(V(t), 2) + "Z";
}
function tt(e) {
	return E[B(e) - 1] + " " + f(I(e), 2);
}
function nt(e) {
	return H(e) + " " + E[B(e) - 1];
}
function rt(n, r) {
	let i = n.kind === e.Utc;
	if (typeof r != "string") return j(n) + " " + M(n);
	if (r.length === 1) switch (r) {
		case "D": return A(n);
		case "d": return j(n);
		case "F": return A(n) + " " + M(n);
		case "f": return A(n) + " " + N(n);
		case "G": return j(n) + " " + M(n);
		case "g": return j(n) + " " + N(n);
		case "M":
		case "m": return tt(n);
		case "O":
		case "o": return Ye(n, i);
		case "R":
		case "r": return Qe(n);
		case "s": return $e(n);
		case "T": return M(n);
		case "t": return N(n);
		case "u": return et(n);
		case "U": return A(F(n)) + " " + M(F(n));
		case "Y":
		case "y": return nt(n);
		default: throw new t("Unrecognized Date print format");
	}
	else return O(n, r, i);
}
function it(e, t, n) {
	return e.offset == null ? rt(e, t) : Ze(e, t);
}
function P(t, n) {
	let r = new Date(t);
	return r.kind = n ?? e.Unspecified, r;
}
function F(t) {
	return t.kind === e.Utc ? t : P(t.getTime(), e.Utc);
}
function at(t) {
	return t.kind === e.Local ? t : P(t.getTime(), e.Local);
}
function I(t) {
	return t.kind === e.Utc ? t.getUTCDate() : t.getDate();
}
function L(t) {
	return t.kind === e.Utc ? t.getUTCHours() : t.getHours();
}
function R(t) {
	return t.kind === e.Utc ? t.getUTCMilliseconds() : t.getMilliseconds();
}
function z(t) {
	return t.kind === e.Utc ? t.getUTCMinutes() : t.getMinutes();
}
function B(t) {
	return (t.kind === e.Utc ? t.getUTCMonth() : t.getMonth()) + 1;
}
function V(t) {
	return t.kind === e.Utc ? t.getUTCSeconds() : t.getSeconds();
}
function H(t) {
	return t.kind === e.Utc ? t.getUTCFullYear() : t.getFullYear();
}
function U(t) {
	return t.kind === e.Utc ? t.getUTCDay() : t.getDay();
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/RegExp.js
function ot(e) {
	return e.replace(/[$()*+.?[\\\^{|}\]]/g, "\\$&");
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/String.js
var st = Symbol.for("Fable.DateTimeFormattable");
function ct(e) {
	return e != null && typeof e[st] == "function";
}
function lt(e, t) {
	return e instanceof Date ? it(e, t) : e[st](t);
}
var W = /(^|[^%])%([0+\- ]*)(\*|\d+)?(?:\.(\d+))?(\w)/g, ut = /\{(\d+)(,-?\d+)?(?:\:([a-zA-Z])(\d{0,2})|\:(.+?))?\}/g;
function G(e, t) {
	return Me(e, t) < 0;
}
function dt(e) {
	return {
		input: e,
		cont: gt(e)
	};
}
function ft(e, t) {
	return typeof t == "string" ? e(t) : t.cont(e);
}
function pt(e) {
	return ft((e) => console.log(e), e);
}
function mt(e, t, n, r, i) {
	let a = "";
	if (t ||= "", i ||= "", Ae(e)) switch (i.toLowerCase() !== "x" && (G(e, 0) ? (e = S(e, -1), a = "-") : t.indexOf(" ") >= 0 ? a = " " : t.indexOf("+") >= 0 && (a = "+")), r = r == null ? null : parseInt(r, 10), i) {
		case "f":
		case "F":
			r ??= 6, e = C(e, r);
			break;
		case "g":
		case "G":
			e = r == null ? w(e) : w(e, r);
			break;
		case "e":
		case "E":
			e = r == null ? Fe(e) : Fe(e, r);
			break;
		case "x":
			e = Ie(e);
			break;
		case "X":
			e = Ie(e).toUpperCase();
			break;
		default: e = String(e);
	}
	else e = e instanceof Date || ct(e) ? lt(e, void 0) : i === "A" && typeof e == "string" ? "\"" + e + "\"" : b(e);
	if (n = typeof n == "number" ? n : parseInt(n, 10), isNaN(n)) e = a + e;
	else {
		let r = t.indexOf("0") >= 0, i = t.indexOf("-") >= 0, o = i || !r ? " " : "0";
		o === "0" ? (e = J(e, n - a.length, o, i), e = a + e) : e = J(a + e, n, o, i);
	}
	return e;
}
function ht(e, n, r, i = "", a = -1) {
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
			s += c[0], s += mt(e, n, u, i, o), c.splice(0, 1), l.splice(0, 1);
		}
		return l.length === 0 ? (s += c[0], e(s)) : ht(e, c, l, s, a);
	};
}
function gt(e) {
	return (t) => {
		W.lastIndex = 0;
		let n = [], r = [], i = 0, a = W.exec(e);
		for (; a;) {
			let t = a.index + (a[1] || "").length;
			n.push(e.substring(i, t).replace(/%%/g, "%")), r.push(a), i = W.lastIndex, --W.lastIndex, a = W.exec(e);
		}
		return n.length === 0 ? t(e.replace(/%%/g, "%")) : (n.push(e.substring(i).replace(/%%/g, "%")), ht(t, n, r));
	};
}
function K(e) {
	let [t, n] = e.split(".");
	return n === void 0 && (n = ""), {
		integral: t,
		decimal: n
	};
}
function q(e) {
	return e.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}
function _t(e, ...n) {
	let r;
	return typeof e == "object" ? (r = String(n[0]), n.shift()) : r = e, r.replace(ut, (e, r, i, a, o, s) => {
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
				let e = G(c, 0);
				G(c, 0) && (c = S(c, -1)), o ??= 2, c = C(c, o), l = K(c), c = o > 0 ? "¤" + q(l.integral) + "." + X(l.decimal, o, "0") : "¤" + q(l.integral), e && (c = "(" + c + ")");
				break;
			case "d":
			case "D":
				if (!je(c)) throw new t("Format specifier was invalid.");
				c = String(c), o != null && (c = c.startsWith("-") ? "-" + Y(c.substring(1), o, "0") : Y(c, o, "0"));
				break;
			case "e":
			case "E": {
				o ??= 6, c = String(Fe(c, o));
				let e = c.indexOf("e");
				c = c.slice(0, e) + a + c[e + 1] + Y(c.slice(e + 2), 3, "0");
				break;
			}
			case "f":
			case "F":
				o ??= 2, c = C(c, o), o > 0 && (l = K(c), c = l.integral + "." + X(l.decimal, o, "0"));
				break;
			case "g":
			case "G": {
				c = o == null ? w(c) : w(c, o);
				let e = c.indexOf("e");
				if (e >= 0) {
					let t = Z(Z(c.slice(0, e), "0"), "."), n = c[e + 1], r = c.slice(e + 2), i = r.length < 2 ? "0" + r : r;
					c = t + (a === "G" ? "E" : "e") + n + i;
				} else c.indexOf(".") >= 0 && (c = Z(Z(c, "0"), "."));
				break;
			}
			case "n":
			case "N":
				o ??= 2, c = C(c, o), l = K(c), c = o > 0 ? q(l.integral) + "." + X(l.decimal, o, "0") : q(l.integral);
				break;
			case "p":
			case "P":
				o ??= 2, c = C(S(c, 100), o), l = K(c), c = o > 0 ? q(l.integral) + "." + X(l.decimal, o, "0") + " %" : q(l.integral) + " %";
				break;
			case "r":
			case "R": throw new t("The round-trip format is not supported by Fable");
			case "x":
			case "X":
				if (!je(c)) throw new t("Format specifier was invalid.");
				o ??= 1, c = Y(Ie(c), o, "0"), a === "X" && (c = c.toUpperCase());
				break;
			default:
				if (a) throw new t("Format specifier was invalid.");
				if (s) {
					let e = "", t = s, n = (t.match(/%/g) ?? []).length;
					n > 0 && (c = S(c, 100 ** n)), c = t.replace(/([0#,]+)(\.[0#]+)?(,*)/, (t, n, r) => {
						let i = /,*$/.exec(n)[0].length;
						i > 0 && (n = n.substring(0, n.length - i), c = Ne(c, 1e3 ** i)), G(c, 0) && (c = S(c, -1), e = "-"), r = r == null ? "" : r.substring(1), c = C(c, Math.max(r.length, 0));
						let [a, o] = c.split(".");
						o ||= "";
						let s = n.replace(/,/g, "").replace(/^#+/, "").length;
						a = Y(a, s, "0");
						let l = r.replace(/#+$/, "").length;
						if (l > o.length ? o = X(o, l, "0") : l < o.length && (o = o.substring(0, l) + o.substring(l).replace(/0+$/, "")), n.indexOf(",") > 0) {
							let e = a.length % 3, t = Math.floor(a.length / 3), n = e > 0 ? a.substr(0, e) + (t > 0 ? "," : "") : "";
							for (let r = 0; r < t; r++) n += a.substr(e + r * 3, 3) + (r < t - 1 ? "," : "");
							a = n;
						}
						return o.length > 0 ? a + "." + o : a;
					}), c = e + c;
				}
		}
		else c = c instanceof Date || ct(c) ? lt(c, s || a) : b(c);
		return i = parseInt((i || " ").substring(1), 10), isNaN(i) || (c = J(String(c), Math.abs(i), " ", i < 0)), c;
	});
}
function vt(e) {
	return typeof e != "string" || e.length === 0;
}
function yt(...e) {
	return e.map((e) => String(e)).join("");
}
function J(e, t, n, r) {
	n ||= " ", t -= e.length;
	for (let i = 0; i < t; i++) e = r ? e + n : n + e;
	return e;
}
function Y(e, t, n) {
	return J(e, t, n);
}
function X(e, t, n) {
	return J(e, t, n, !0);
}
function bt(e, n, r, i) {
	if (r = typeof r == "number" ? r : void 0, i = typeof i == "number" ? i : 0, r && r < 0) throw new t("Count cannot be less than zero");
	if (r === 0) return [];
	let a = (i & 1) == 1, o = (i & 2) == 2;
	n ||= [], n = n.filter((e) => e).map(ot), n = n.length > 0 ? n : ["\\s"];
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
function Z(e, ...t) {
	return t.length === 0 ? e.trimEnd() : e.replace(RegExp("[" + ot(t.join("")) + "]+$"), "");
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/System.js
var xt = class extends t {
	constructor(e) {
		super(e);
	}
};
function St(e) {
	return new xt(e);
}
var Ct = class extends t {
	constructor(e) {
		super(e);
	}
};
function wt(e) {
	return new Ct(e);
}
var Tt = class extends t {
	constructor(e, t, n) {
		super(vt(t) ? e : e + He + t + "')", n), this.paramName = t;
	}
}, Et = class extends Tt {
	constructor(e, t) {
		super(t, e, l());
	}
};
function Dt(e, t) {
	return new Et(e, t);
}
function Ot(e) {
	return Dt(e, Ve);
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/FSharp.Core.js
function kt(e) {
	return !!_(e, l());
}
function At(e, t) {
	if (_(t, l())) throw Ot(e);
	return t;
}
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Array.js
function jt() {
	throw new t("An index satisfying the predicate was not found in the collection.");
}
function Mt(e, t, n, r) {
	let i = t | 0;
	return e.fill(r, i, i + n);
}
function Nt(e) {
	if (e.length === 0) throw new t("The input array was empty (Parameter 'array')");
	return Q(e.length - 1, e);
}
function Pt(e, t, n) {
	let r = t.length | 0, i = T(n, r);
	for (let n = 0; n <= r - 1; n++) $(i, n, e(Q(n, t)));
	return i;
}
function Ft(e) {
	let t = e.length | 0, n = Array(t);
	for (let r = 0; r <= t - 1; r++) $(n, r, [r, Q(r, e)]);
	return n;
}
function It(e, t) {
	let n = T(t, 1);
	return $(n, 0, e), n;
}
function Lt(e, n, r) {
	if (e < 0) throw new t("The input must be non-negative (Parameter 'count')");
	let i = T(r, e);
	for (let t = 0; t <= e - 1; t++) $(i, t, n(t));
	return i;
}
function Rt(e) {
	return e.slice();
}
function zt(e, t, n, r, i) {
	ye(e, t, n, r, i);
}
function Bt(e, t) {
	let n = t.find(e);
	return n == null ? jt() : Re(n);
}
function Vt(e, t) {
	return t.find(e);
}
function Ht(e, t) {
	let n = t.findIndex(e) | 0;
	return n > -1 ? n | 0 : (jt(), -1);
}
function Ut(e, t) {
	let n = e.slice();
	return n.sort((e, n) => t.Compare(e, n) | 0), n;
}
function Wt(e, t, n) {
	if (kt(t)) return !!kt(n);
	if (kt(n)) return !1;
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
//#endregion
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Int32.js
var Gt = { AllowHexSpecifier: 512 };
function Kt(e, t) {
	let [, n, r, i] = e;
	return {
		sign: n || "",
		prefix: r || "",
		digits: i,
		radix: t
	};
}
function qt(e, n) {
	switch (n) {
		case 8: return e ? [0, 255] : [-128, 127];
		case 16: return e ? [0, 65535] : [-32768, 32767];
		case 32: return e ? [0, 4294967295] : [-2147483648, 2147483647];
		default: throw new t("Invalid bit size.");
	}
}
function Jt(e) {
	switch (e) {
		case 2: return /[^0-1]/;
		case 8: return /[^0-7]/;
		case 10: return /[^0-9]/;
		case 16: return /[^0-9a-fA-F]/;
		default: throw new t("Invalid Base.");
	}
}
function Yt(e, t) {
	if (t & Gt.AllowHexSpecifier) return 16;
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
function Xt(e, t, n) {
	let r = /^\s*([\+\-])?(0[xXoObB])?([0-9a-fA-F]+)\s*$/.exec(e.replace(/_/g, ""));
	if (r != null) {
		let [, , e, i] = r;
		if (n ||= Yt(e, t), !Jt(n).test(i)) return Kt(r, n);
	}
	return null;
}
function Zt(e, n, r, i, a) {
	let o = Xt(e, n, a);
	if (o != null) {
		let e = Number.parseInt(o.sign + o.digits, o.radix);
		if (!Number.isNaN(e)) {
			let [t, n] = qt(!0, i);
			!r && o.radix !== 10 && e >= t && e <= n && (e = e << 32 - i >> 32 - i);
			let [a, s] = qt(r, i);
			if (e >= a && e <= s) return e;
		}
	}
	throw new t(`The input string ${e} was not in a correct format.`);
}
//#endregion
export { Se as A, ee as B, bt as C, Re as D, ze as E, de as F, m as H, ve as I, l as L, t as M, ge as N, ke as O, v as P, c as R, dt as S, Be as T, g as U, he as V, te as W, At as _, Mt as a, yt as b, Ft as c, Nt as d, Pt as f, Vt as g, Ut as h, Wt as i, b as j, Oe as k, Lt as l, It as m, Rt as n, Bt as o, $ as p, zt as r, Ht as s, Zt as t, Q as u, St as v, pt as w, _t as x, wt as y, _ as z };
