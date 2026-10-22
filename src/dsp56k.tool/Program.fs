module dsp56k.tool.Program

open Argu
open dsp56k.tool.Cli

[<EntryPoint>]
let main argv =
  let parser = ArgumentParser.Create<Command>(programName = "fsdsp56k")
  try
    let args = parser.ParseCommandLine(argv)
    match args.GetSubCommand() with
    | Asm a -> Commands.asm a
    | Check a -> Commands.check a
    | Disasm a -> Commands.disasm a
    | Import a -> Commands.import a
    | Mds a -> Commands.mds a
    | Run a -> Commands.run a
    | Stress a -> Commands.stress a
    | Audition a -> Commands.audition a
  with
  | :? ArguParseException as e ->
    eprintfn "%s" e.Message
    2
  | :? System.IO.IOException | :? System.UnauthorizedAccessException | :? System.ArgumentException as e ->
    eprintfn "error: %s" e.Message
    1
