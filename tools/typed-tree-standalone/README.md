# The typed tree in the browser's compiler (Fable.Standalone)

`src/dsp56k.kernel.typedtree/TypedTreeJson.fs` writes the F# compiler's typed tree as JSON using only the compiler's
symbol API. The same file runs on .NET (FSharp.Compiler.Service) and, compiled by Fable into Fable.Standalone, in a page or
node: pasted F# source in, its typed tree out.

- `fable-5.18.0-typed-tree.patch`: 30 lines against the Fable 5.18.0 tag: a worker message `GetTypedTree(files, options)`
  answered with `TypedTree(json, errors)` (`src/fable-standalone/src/Worker`), `IFableManager.FSharpAstToJson`, and the
  `Json.fs` and `TypedTreeJson.fs` included in `Fable.Standalone.fsproj` from the folder `TYPED_TREE_JSON_DIR`. (The standalone already had
  `FSharpAstToString`, a printed tree for debugging; this one is structured.)
- `build.sh <fable checkout at 5.18.0>`: applies the patch and builds the standalone into its `dist/` (about 7 minutes).
- `host.mjs`: runs the standalone's worker in a node `vm` (the Thoth message protocol a browser Web Worker speaks), with
  `typedTree(files)` added. `typedtree.mjs`: the command line over it.

```
node tools/typed-tree-standalone/typedtree.mjs --dist <fable>/src/fable-standalone/dist --metadata <fable-metadata>/lib \
  --refs <folder with Dsp56k.Kernel.Intrinsics.dll, Dsp56k.Kernel.dll, Dsp56k.dll, Dsp56k.Dsl.dll> \
  --ref Dsp56k.Kernel.Intrinsics --ref Dsp56k --ref Dsp56k.Dsl --ref Dsp56k.Kernel --out tree.json tests/dsp56k.tests/Kernels/Sine.fs
```

`--metadata` is the `lib/` of the npm package `@fable-org/fable-metadata` (2.1.1 goes with this standalone).

## The reader: JSON in, `IrKernel` out

`src/dsp56k.kernel.typedtree/FromJson.fs` reads the JSON into the kernel IR (it does what `FromTypedTree.fs` does with the
compiler's symbols, on the JSON, so it needs no F# compiler). What only the running program knows (a module value such as a
table, a function called on constants, a union case test) is asked through a `Host`: `ReflectionHost.fs` is the .NET one;
the intrinsics and operators are registered by .NET reflection, or from the tree itself when it holds the source of the
kernel libraries (`kernelRegistering`). `Json.fs` is its parser and the writer's printer, no System.Text.Json.

Proof, in `tests/dsp56k.tests/TypedTreeFrontEndTests.fs`: Sine, InputThru, MainTap, Pluck and the seven Tx6 kernels, on
every host, compile to the same program as the quotation front end when read
- from the JSON .NET's compiler writes (always run), and
- from the JSON the browser's compiler writes, run in node (`TYPED_TREE_JSON_DIR=<folder of Sine.json, Tx6Bd.json...>`;
  one JSON per kernel source, tables first: `node typedtree.mjs ... --out Tx6Bd.json Tx6BdTables.fs Tx6Bd.fs`).

Under Fable: `reader/` compiles `Ir.fs`, `Json.fs`, `FromJson.fs` and `ReaderShow.fs` to JavaScript
(`dotnet fable reader/fable.fsproj --outDir out`), and `reader/run.mjs` runs the reader in node on a tree that holds the
kernel libraries' source (`Intrinsics.fs`) and `Sine.fs`; its output (the layouts and the three functions as text) is
byte-identical to the one the same code gives compiled by .NET (`reader/dotnet.fsproj`). What does not run in a page yet is the
generator (`Compile.fs`): it matches .NET values (`:? Frac`) the page has no counterpart for until the intrinsics are ported.

## JSON shape

`{"version":1,"files":[{"file","name","decls":[...]}],"members":[...],"vars":[...]}`. A declaration is `entity` (name, fullName,
attributes, subs), `member` (index into `members`, parameter variables, `body`) or `init`. Members and variables are listed
once and referred to by number (a member: name, compiledName, owner, file/line/col, flags, parameter and return types,
attributes and its owner's). An expression is `{"k": kind, "t": type, "r": line, ...}`: `Const Var Member Lambda App Call
FieldGet FieldSet Let Seq If VarSet MemberSet For While CaseTest CaseGet Decision Success Coerce TupleGet Tuple NewRecord
NewCase NewArray Default`, and `Other` (with `children`) for the rest. An entity of a record carries `fields` (name, type, attributes) and one of a union its `cases`. Types: `{"t": fullName, "kind", "args"}`,
`{"fn": [a, r]}`, `{"tuple": [...]}`, `{"gp": name}`. Members the compiler generates for records and unions are left out.

## Measured (2026-10-18, standalone from Fable 5.18.0)

- `tests/dsp56k.tests/Kernels/Sine.fs`: 29,147 bytes from the standalone against 29,306 from .NET; 13 differences, none in an
  expression: FSharp.Core's own declaration file and line, and a unit constant that is `undefined` rather than `null`.
- 80 kernel files kept in a separate repository (all but its
  four grader-side ones): type-checked by the standalone in 13 s, 9.90 MB of JSON; against .NET 63 differences, all in the
  metadata of FSharp.Core and System members, plus `Frac.lsr` named ``lsr`` (use `compiledName`). No expression and no kernel
  declaration differs.
- What the compiler in the page cannot parse that .NET's can: `lsr` as a name (an OCaml-compat keyword): the ports now
  write ``Frac.``lsr`` `` (6 uses in 3 files) and `Intrinsics.fs` declares ``static member ``lsr`` ``.
- What Fable gets differently at run time: a number is any number to a type test, so constants are written by their declared
  type (`constantJson`); `Interlocked` and `LastIndexOfAny` are not Fable's, `Ir.fs` avoids them.
