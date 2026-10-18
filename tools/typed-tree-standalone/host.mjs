// Runs Fable's standalone worker (dist/worker.min.js) inside a node `vm` context, speaking the same
// Thoth-encoded message protocol a browser Web Worker speaks (src/fable-standalone/src/Worker/Shared.fs).
// The worker needs four browser globals only: self, importScripts, postMessage and fetch.
//
//   const host = await startHost({ standaloneDir, refDirs: [metadataDir, myLibDir] })
//   await host.createChecker({ extraRefs: ["MyLib"], precompiled })
//   const r = await host.compileFiles([{ Name: "A.fs", Content: "..." }], { filesToEmit: ["A.fs"] })

import fs from "node:fs"
import path from "node:path"
import vm from "node:vm"

export async function startHost({ standaloneDir, refDirs }) {
  const workerPath = path.join(standaloneDir, "worker.min.js")
  if (!fs.existsSync(workerPath)) throw new Error(`no worker.min.js in ${standaloneDir}`)

  let onMessage = null
  const answers = []
  const fetched = []        // every assembly the worker asked for, for the size report

  const findAssembly = (url) => {
    const base = path.basename(url)
    for (const dir of refDirs) {
      const file = path.join(dir, base)
      if (fs.existsSync(file)) return file
    }
    return null
  }

  const ctx = {
    console,
    setTimeout, clearTimeout, setInterval, clearInterval, queueMicrotask,
    performance, crypto, TextDecoder, TextEncoder,
    addEventListener: (kind, fn) => { if (kind === "message") onMessage = fn },
    postMessage: (msg) => answers.push(msg),
    importScripts: (p) => vm.runInContext(fs.readFileSync(path.join(standaloneDir, p), "utf8"), ctx, { filename: p }),
    fetch: async (url) => {
      const file = findAssembly(url)
      if (!file) return { ok: false, status: 404, statusText: `no such assembly: ${url}` }
      const b = fs.readFileSync(file)
      fetched.push({ name: path.basename(file), bytes: b.length })
      return { ok: true, arrayBuffer: async () => b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength) }
    },
  }
  ctx.self = ctx
  ctx.globalThis = ctx
  vm.createContext(ctx)
  vm.runInContext(fs.readFileSync(workerPath, "utf8"), ctx, { filename: "worker.min.js" })

  const post = (request) => onMessage({ data: JSON.stringify(request) })

  async function answer(expected) {
    for (;;) {
      while (answers.length) {
        const parsed = JSON.parse(answers.shift())
        if (parsed[0] === expected) return parsed
        if (parsed[0] === "CompilerCrashed") throw new Error(`CompilerCrashed: ${parsed[1]}`)
        if (parsed[0] === "LoadFailed") throw new Error("LoadFailed (see the console error above)")
      }
      await new Promise((r) => setTimeout(r, 0))
    }
  }

  return {
    fetched,
    async createChecker({ extraRefs = [], precompiled = null, suffix = null, options = [] } = {}) {
      const t0 = performance.now()
      post(["CreateChecker", "file:///assemblies", extraRefs, suffix, options, precompiled])
      const [, version] = await answer("Loaded")
      return { version, ms: performance.now() - t0 }
    },
    /** files: [{Name, Content}], filesToEmit: names (empty: all) -> { modules, errors, stats, ms } */
    async compileFiles(files, { filesToEmit = [], language = "javascript", options = [] } = {}) {
      const t0 = performance.now()
      post(["CompileFiles", files, filesToEmit, language, options])
      const [, modules, , errors, stats] = await answer("CompilationsFinished")
      return { modules, errors: errors ?? [], stats, ms: performance.now() - t0 }
    },
    /** files: [{Name, Content}] -> { json (the typed tree as text, "" when there are errors), errors, ms } */
    async typedTree(files, options = []) {
      const t0 = performance.now()
      post(["GetTypedTree", files, options])
      const [, json, errors] = await answer("TypedTree")
      return { json, errors: errors ?? [], ms: performance.now() - t0 }
    },
    async parseFile(file, files, options = []) {
      post(["ParseFile", file, files, options])
      const [, errors] = await answer("ParsedCode")
      return errors
    },
  }
}
