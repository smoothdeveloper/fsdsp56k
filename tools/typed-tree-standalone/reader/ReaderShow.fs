/// The JSON reader run with no .NET behind it: the typed tree JSON of a kernel and the libraries it uses (given with the
/// kernel, so their members are in the tree) in, the kernel's three functions and record layouts out as text. The same file
/// is compiled by Fable (fable.fsproj, run in node by run.mjs) and by .NET (dotnet.fsproj): the two texts must be equal.
module ReaderShow

open dsp56k.kernel

/// A host that knows no values: the reader asks it only for module values and calls on constants, which a program that is
/// only read, not compiled, never needs.
let nowhere: FromJson.Host =
  { getStatic = fun owner _ name -> failwith ("no value of " + owner + "." + name)
    getInstance = fun _ name -> failwith ("no value of " + name)
    invoke = fun owner name _ _ -> failwith ("no call of " + owner + "." + name)
    isCase = fun union case _ -> failwith ("no case " + union + "." + case) }

let private fields (fs: Ir.IrField array) =
  fs |> Array.map (fun f -> f.name + ":" + string f.ty + (if f.xMemory then ":X" else "") + (match f.length with Some n -> ":[" + string n + "]" | None -> "")) |> String.concat " "

/// What the reader makes of the kernel `name`'s functions `init`, `trig`, `render` of the module `moduleName`.
let show (json: string) (moduleName: string) (name: string) (init: string) (trig: string) (render: string) : string =
  match FromJson.kernelRegistering nowhere (Json.parse json) moduleName name (init, trig, render) with
  | Error e -> "ERROR " + e
  | Ok k ->
    let body (m: Ir.Member) = match m.Definition.Value with Some e -> string e | None -> "(no body)"
    [ "kernel " + k.name
      "params " + string k.paramsType + ": " + fields k.parameters
      "state " + string k.stateType + ": " + (match k.state with Some fs -> fields fs | None -> "none")
      "init " + body k.init
      "trig " + body k.trig
      "render " + body k.render ]
    |> String.concat "\n"
