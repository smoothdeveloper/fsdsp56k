import { B as e, I as t, M as n, j as r } from "./Array.mjs";
//#region node_modules/.pnpm/@fable-org+fable-library-js@2.7.1/node_modules/@fable-org/fable-library-js/Reflection.js
var i = class {
	constructor(e, t, n, r) {
		this.declaringType = e, this.tag = t, this.name = n, this.fields = r;
	}
}, a = class {
	constructor(e, t, n, r, i, a, o) {
		this.fullname = e, this.generics = t, this.construct = n, this.parent = r, this.fields = i, this.cases = a, this.enumCases = o;
	}
	toString() {
		return g(this);
	}
	GetHashCode() {
		return s(this);
	}
	Equals(e) {
		return c(this, e);
	}
};
function o(e) {
	return e.generics == null ? [] : e.generics;
}
function s(t) {
	let r = e(t.fullname), i = o(t).map(s);
	return n([r, ...i]);
}
function c(e, n) {
	return e.fullname === "" ? n.fullname === "" && t(v(e), v(n), ([e, t], [n, r]) => e === n && c(t, r)) : e.fullname === n.fullname && t(o(e), o(n), c);
}
function l(e, t, n, r) {
	return new a(e, t, n, r);
}
function u(e, t, n, r) {
	return new a(e, t, n, void 0, r);
}
function d(e) {
	let t = new a("Microsoft.FSharp.Core.FSharpOption`1", [e], void 0, void 0, void 0, () => [new i(t, 0, "None"), new i(t, 1, "Some", [["value", e]])]);
	return t;
}
function f(e) {
	return new a("[]", [e]);
}
new a("System.Object"), new a("Microsoft.FSharp.Core.Unit"), new a("System.Char");
var p = new a("System.String"), m = new a("System.Boolean");
new a("System.SByte"), new a("System.Byte"), new a("System.Int16"), new a("System.UInt16");
var h = new a("System.Int32");
new a("System.UInt32"), new a("System.Int64"), new a("System.UInt64"), new a("System.Int128"), new a("System.UInt128"), new a("System.IntPtr"), new a("System.UIntPtr"), new a("System.Half"), new a("System.Single"), new a("System.Double"), new a("System.Decimal"), new a("System.Numerics.BigInteger");
function g(e) {
	let t = _(e);
	return t == null ? e.generics == null || e.generics.length === 0 ? e.fullname : e.fullname + "[" + e.generics.map((e) => g(e)).join(",") + "]" : g(t) + "[]";
}
function _(e) {
	return e.fullname === "[]" && e.generics?.length === 1 ? e.generics[0] : void 0;
}
function v(e) {
	if (e.fields != null) return e.fields();
	throw new r(`${e.fullname} is not an F# record type`);
}
//#endregion
export { d as a, h as i, m as n, u as o, l as r, p as s, f as t };
