#!/usr/bin/env node
// F# source files in, the F# compiler's typed tree as JSON out, with Fable.Standalone's worker running in node (no dotnet).
//
//   node typedtree.mjs --dist <standalone dist> --metadata <fable-metadata lib> [--refs <dir of .dll> --ref Name ...] --out tree.json File.fs...
//
// --dist      the dist folder of a standalone built with fable-5.18.0-typed-tree.patch (worker.min.js, bundle.min.js)
// --metadata  the reference assemblies the compiler type-checks against (npm @fable-org/fable-metadata, its lib/)
// --refs/--ref  assemblies the source refers to (Dsp56k.Kernel.Intrinsics, Dsp56k.Kernel, Dsp56k, Dsp56k.Dsl for kernels): the
//             folder they are in, and their names without .dll
// The files are one project, in the order given. Exit 1 when the source does not type-check (errors are printed).
import fs from "node:fs"
import path from "node:path"
import { startHost } from "./host.mjs"

const args = process.argv.slice(2)
const opt = { ref: [] }
const files = []
for (let i = 0; i < args.length; i++) {
  if (args[i].startsWith("--")) {
    const k = args[i].slice(2), v = args[++i]
    if (k === "ref") opt.ref.push(v); else opt[k] = v
  } else files.push(args[i])
}
if (!opt.dist || !opt.metadata || !opt.out || files.length === 0) {
  console.error("usage: node typedtree.mjs --dist D --metadata M [--refs R --ref Name...] --out tree.json File.fs...")
  process.exit(2)
}
const refDirs = [opt.metadata, ...(opt.refs ? [opt.refs] : [])]
const host = await startHost({ standaloneDir: opt.dist, refDirs })
const checker = await host.createChecker({ extraRefs: opt.ref })
const r = await host.typedTree(files.map((f) => ({ Name: path.resolve(f), Content: fs.readFileSync(f, "utf8") })))
const hard = r.errors.filter((e) => !e.IsWarning)
console.log(`Fable ${checker.version}: ${files.length} files, ${hard.length} errors, ${r.json.length} bytes of JSON, ${Math.round(r.ms)} ms`)
for (const e of hard.slice(0, 10)) console.log(`  ${path.basename(e.FileName)}(${e.StartLine}): ${e.Message.split("\n")[0].slice(0, 200)}`)
if (r.json) fs.writeFileSync(opt.out, r.json)
process.exit(hard.length ? 1 : 0)
