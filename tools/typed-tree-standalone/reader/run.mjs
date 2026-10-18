#!/usr/bin/env node
// The JSON reader compiled by Fable, run in node: node run.mjs tree.json Module.Name kernel-name init trig render
// (build first: dotnet tool restore && dotnet fable fable.fsproj --outDir out)
import fs from "node:fs"
import path from "node:path"
import { fileURLToPath, pathToFileURL } from "node:url"
const here = path.dirname(fileURLToPath(import.meta.url))
const [json, moduleName, name, init, trig, render] = process.argv.slice(2)
const { show } = await import(pathToFileURL(path.join(here, "out", "ReaderShow.js")).href)
console.log(show(fs.readFileSync(json, "utf8"), moduleName, name, init, trig, render))
