# The kernel IR: the generator reads a tree, not quotations

The code generator (`Compile.fs`) reads its own expression tree
(`src/dsp56k.kernel/Ir.fs`); quotations are one front end that builds it (`FromQuotation.fs`); a tree
written by hand is another (`Ir.Build`, `tests/dsp56k.tests/IrFrontEndTests.fs`). Nothing the generator
emits changed: the golden file of the kernels here is as it was, and the emulator conformance
tests pass as before.

```
F# kernel ──quotations──► FromQuotation ─┐
by hand / Ir.Build ──────────────────────┼─► Ir.IrKernel ─► Compile.fs ─► items ─► Optimize.fs ─► words
(JSON: not built; typed tree: below)     ┘      (this tree)    (unchanged logic)
```

## Why

Compile.fs read `Microsoft.FSharp.Quotations.Expr` and .NET reflection (`MethodInfo`, attributes,
`typeof`, `Invoke`), which Fable cannot compile and which only exists where the F# compiler ran. A page
that compiles kernels needs the generator on something it can build itself. The tree has the shapes of a
quotation on purpose (same pattern names, `ExprShape`, `Substitute`, `GetFreeVars`), so Compile.fs changed
by its opens, its member and type accessors and a few quotation literals, not by its logic.

## What is in the tree (`Ir.fs`)

| | |
|---|---|
| `IrTy` | the types the generator tells apart: Frac, Acc, Long, Table, Region, Cell, Cursor, Output, int, bool..., a record or union by its full name |
| `Var` | identity of its own, ordered as F# orders quotation variables (name, then the type's metadata token, then creation): maps keyed by `Var` iterate in that order and it decides which local gets a register |
| `Member` | what the generator asks of a method or property: `Name`, `Owner`, `Declaring`, `ReturnType`, `Intrinsic`, `IsConst`, `Marks` (`Subroutine`, `NoUnswitch`, `Unroll`), `Key` (a subroutine's name), `Definition` (the inlinable body, lazy), `Invoke` (evaluate on constants), `Get` (read a property) |
| `Expr` | `Let`, `Call`, `PropertyGet`/`PropertySet`, `IfThenElse`, `ForIntegerRangeLoop`, `Sequential`, `VarSet`, `Lambda`, `Application`, `Value`, `UnionCaseTest`..., each with its type and, when known, its source `Range` (the debug range: the comments in the listing) |
| `IrKernel` | the record layouts (`IrField`: name, type, `[<XMemory>]`, `[<Length n>]`) and the `init`, `trig`, `render` members |
| registries | `Ir.intrinsic "acc.ofFrac"` (the calls the generator builds itself), `Ir.typeOperators` (what `a + b` means on kernel values), `Ir.zero`, `Ir.typeKeyOf`: filled by the front end before it hands over a kernel |

Constants stay .NET values in `Value(obj, ty)` (a `Frac`, an `Acc`, a `Table`, a `Region`): the generator
matches `:? Frac` as before. They are the Intrinsics types, not reflection.

## Writing another front end

1. Make a `Member` for every method or property the tree calls (`Ir.Build.property`, `Ir.Build.func`, or
   your own record), once each: members are compared by identity.
2. Register the intrinsics and operators the kernel uses (`FromQuotation.register` does it by reflection over
   the Intrinsics assembly; a front end without reflection builds the same table from a list), set
   `Ir.zero` and `Ir.typeKeyOf`.
3. Produce the nodes, with their types. Helpers called by the kernel are `Member`s whose `Definition` is a
   lambda over their parameters: the generator inlines them (or calls them, with the `Subroutine` mark).
4. Hand an `IrKernel` to `Compile.generateIr` / `compileIr` (and the passes of `Optimize` on its result).

`IrFrontEndTests.fs` is the worked example: Sine as a tree, no F# behind it, compiling to the program
Sine.fs compiles to, on every host.

## What stayed in Compile.fs

`normalize` (pipes, curried applications, `let b = cond`), `inlined` (helpers by substitution), `ordered`
and `touches` (evaluation order), `tryConst` (folding), `pureHelper`: they work on the tree, so every front
end gets them. A front end that already produces normal forms simply never meets the cases.

## Tests

- `tests/dsp56k.tests/golden/kernel-compile.tsv` (`KernelIrTests.fs`): every `Kernel.define` value of the
  test and sample assemblies, every host, by the generator alone: item count and a hash of the listing, or
  the refusal (the printed expression some refusals end with is cut: its text belongs to the tree's
  printer). `UPDATE_GOLDEN=1` rewrites it; `DUMP_LISTINGS=dir` writes every listing, to diff a change.
- `IrFrontEndTests.fs`: the by-hand Sine against Sine.fs.

## A second real front end: the F# compiler's typed tree (spike, `src/dsp56k.kernel.typedtree`)

`FromTypedTree.check files` runs the F# compiler (FSharp.Compiler.Service, `keepAssemblyContents`) over kernel
*source text*; `FromTypedTree.kernel checked "name" ("init", "trig", "render")` reads the three functions as an
`IrKernel`, with no quotation and no `[<ReflectedDefinition>]`. `tests/dsp56k.tests/TypedTreeFrontEndTests.fs` compiles
every kernel of `tests/dsp56k.tests/Kernels` (Sine, InputThru, MainTap, Pluck, the Tx6 family with their tables files) through it, on every
host, and requires the same program as the quotation front end. What the typed tree does differently, and the converter
absorbs: F#'s generic operators instead of the kernel types' own (`acc >>> 6`: looked up in `Ir.typeOperators` by operand
types); a unit argument on a property or a method with none; generic functions instantiated per call site (their return type is
the instantiated one); `match` as decision trees with targets; union case field reads.

Still on .NET reflection: `Member.Get` and `Member.Invoke` (module values: tables, voice records; pure helpers `tryConst` runs).
A browser build needs an evaluator for those (or a front end that folds them). FCS itself does not run in a browser; Fable.Standalone's
compiler (FCS compiled by Fable) does, and it holds the same typed tree. Its stock API only prints it (`FSharpAstToString`), so
`TypedTreeJson.fs` writes it as JSON and `tools/typed-tree-standalone` carries a 30-line Fable 5.18.0 patch that adds a worker
message for it (`GetTypedTree`): pasted source in, JSON out. Measured: 80 kernel files type-check in
13 s in node and give the same tree as .NET's compiler (63 differences in 9.9 MB, all in FSharp.Core/System member metadata).
The reader (`FromJson.fs`: JSON in, `IrKernel` out) is done: Sine, InputThru, MainTap, Pluck and the seven Tx6 kernels, read from
the JSON of the browser's compiler (in node), compile to the same program as the quotations on every host; and it compiles under
Fable (`Ir.fs`, `Json.fs`, `FromJson.fs`) and gives, in node, the same text as compiled by .NET. Still missing: the constants evaluator
(the reader asks a `Host` for module values and calls on constants; .NET reflection answers today) and `Compile.fs` under Fable.

## Not done

- The tree is in the Kernel project (it needs only `System`); to be built by Fable it moves to a project of
  its own, with `Compile.fs` and `Optimize.fs` checked for what Fable cannot do (`HashIdentity.Reference`,
  `Threading` for the big stack, `ConcurrentDictionary`, string formatting).
- A serialised form (JSON) of the tree: constants (module values, a voice's configuration record) must be
  folded or encoded, and `tryConst` evaluates pure helpers by calling them (`Member.Invoke`): a loaded tree
  has no .NET to call, so it needs the tree's own evaluator (or inlining and folding) first.
- The constants evaluator and the generator (`Compile.fs`, `Optimize.fs`) under Fable, with the kernel types' values ported (the
  generator matches `:? Frac`).
- `Host.fs` (a kernel run as .NET code) still reads the quotation `Kernel` by reflection; an interpreter
  of the tree would replace it where there is no .NET.
