/// Command line of the `fsdsp56k` tool (Argu). Pure DSP56300 commands (asm, check, disasm,
/// import of a listing) need nothing else; the `mds` and `run` commands and binary import are
/// about Machinedrum MDS packages.
module dsp56k.tool.Cli

open Argu

type AsmArgs =
  | [<MainCommand; ExactlyOnce>] Source of path: string
  | [<AltCommandLine("-I")>] Include of dir: string
  | [<AltCommandLine("-D")>] Define of name_value: string
  | [<AltCommandLine("-l")>] Listing of path: string
  | Hex of path: string
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Source _ -> "Motorola-syntax source file."
      | Include _ -> "extra include directory (repeatable)."
      | Define _ -> "predefined symbol NAME=VALUE (repeatable; VALUE decimal or $hex)."
      | Listing _ -> "write the listing here instead of stdout."
      | Hex _ -> "write P memory as one hex word per line, after an @origin line."

type CheckArgs =
  | [<MainCommand; ExactlyOnce>] Source of path: string
  | [<AltCommandLine("-I")>] Include of dir: string
  | [<AltCommandLine("-D")>] Define of name_value: string
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Source _ -> "Motorola-syntax source file."
      | Include _ -> "extra include directory (repeatable)."
      | Define _ -> "predefined symbol NAME=VALUE (repeatable)."

type DisasmArgs =
  | [<MainCommand; ExactlyOnce>] Input of path: string
  | Slot of int
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Input _ -> ".mds package or .syx dump."
      | Slot _ -> "slot (1..16) to take from a .syx holding several machines."

type ImportArgs =
  | [<MainCommand; ExactlyOnce>] Input of path: string
  | [<AltCommandLine("-m")>] Module of name: string
  | [<AltCommandLine("-o")>] Output of path: string
  | [<AltCommandLine("-I")>] Include of dir: string
  | [<AltCommandLine("-D")>] Define of name_value: string
  | Slot of int
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Input _ -> "Motorola-syntax listing, or .mds/.syx for a binary import."
      | Module _ -> "F# module name (default: from the file name)."
      | Output _ -> "write the F# source here instead of stdout."
      | Include _ -> "extra include directory for listings (repeatable)."
      | Define _ -> "predefined symbol NAME=VALUE for listings (repeatable)."
      | Slot _ -> "slot (1..16) to take from a .syx holding several machines."

type MdsInfoArgs =
  | [<MainCommand; ExactlyOnce>] Input of path: string
  interface IArgParserTemplate with
    member _.Usage = ".mds package or .syx dump."

type MdsPackArgs =
  | [<Mandatory; AltCommandLine("-j")>] Json of path: string
  | [<Mandatory; AltCommandLine("-s")>] Source of path: string
  | Sprite of pbm: string
  | [<AltCommandLine("-I")>] Include of dir: string
  | [<AltCommandLine("-D")>] Define of name_value: string
  | [<Mandatory; AltCommandLine("-o")>] Output of path: string
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Json _ -> "machine definition (the mds_pack.py JSON)."
      | Source _ -> "Motorola-syntax program."
      | Sprite _ -> "32x32 PBM sprite."
      | Include _ -> "extra include directory (repeatable)."
      | Define _ -> "predefined symbol NAME=VALUE (repeatable)."
      | Output _ -> ".mds file to write."

type MdsSyxArgs =
  | [<MainCommand; ExactlyOnce>] Input of path: string
  | Slot of int
  | [<Mandatory; AltCommandLine("-o")>] Output of path: string
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Input _ -> ".mds package."
      | Slot _ -> "destination slot 1..16 (default 1)."
      | Output _ -> ".syx file to write."

type MdsUnsyxArgs =
  | [<MainCommand; ExactlyOnce>] Input of path: string
  | [<AltCommandLine("-o")>] Output of dir: string
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Input _ -> ".syx dump."
      | Output _ -> "directory for the .mds files (default: current)."

type MdsArgs =
  | [<CliPrefix(CliPrefix.None)>] Info of ParseResults<MdsInfoArgs>
  | [<CliPrefix(CliPrefix.None)>] Pack of ParseResults<MdsPackArgs>
  | [<CliPrefix(CliPrefix.None)>] Syx of ParseResults<MdsSyxArgs>
  | [<CliPrefix(CliPrefix.None)>] Unsyx of ParseResults<MdsUnsyxArgs>
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Info _ -> "header, parameters, patches and checks of a package."
      | Pack _ -> "assemble a program and pack it with its machine definition."
      | Syx _ -> "package to an open-loop SysEx dump."
      | Unsyx _ -> "SysEx dump to packages."

type RunArgs =
  | [<MainCommand; ExactlyOnce>] Input of path: string
  | Slot of int
  | Settings of values: string
  | Blocks of int
  | Trig_Every of blocks: int
  | Sample of wav: string
  | Wav of path: string
  | Profile
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Input _ -> ".mds package or .syx dump."
      | Slot _ -> "slot (1..16) to take from a .syx holding several machines."
      | Settings _ -> "front-panel settings, comma separated (default: the parameters' defaults)."
      | Blocks _ -> "32-sample blocks to render (default 1378, about one second)."
      | Trig_Every _ -> "retrigger every N blocks (default: once, at the start)."
      | Sample _ -> "WAV loaded as the internal and userwave sample banks."
      | Wav _ -> "write the rendered output as a 24-bit WAV."
      | Profile -> "single-step the first INIT, TRIG and RENDER and report hot addresses."

type StressArgs =
  | [<MainCommand; ExactlyOnce>] Input of path: string
  | Slot of int
  | Sweep of how: string
  | Seed of int
  | Blocks of int
  | Sample of wav: string
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Input _ -> ".mds package or .syx dump."
      | Slot _ -> "slot (1..16) to take from a .syx holding several machines."
      | Sweep _ -> "exhaustive (each control over its range, then pairwise; default) or random:N."
      | Seed _ -> "seed of random:N (default 1)."
      | Blocks _ -> "RENDER blocks per case (default 16)."
      | Sample _ -> "WAV loaded as the internal and userwave sample banks."

type Command =
  | [<CliPrefix(CliPrefix.None)>] Asm of ParseResults<AsmArgs>
  | [<CliPrefix(CliPrefix.None)>] Check of ParseResults<CheckArgs>
  | [<CliPrefix(CliPrefix.None)>] Disasm of ParseResults<DisasmArgs>
  | [<CliPrefix(CliPrefix.None)>] Import of ParseResults<ImportArgs>
  | [<CliPrefix(CliPrefix.None)>] Mds of ParseResults<MdsArgs>
  | [<CliPrefix(CliPrefix.None)>] Run of ParseResults<RunArgs>
  | [<CliPrefix(CliPrefix.None)>] Stress of ParseResults<StressArgs>
  interface IArgParserTemplate with
    member a.Usage =
      match a with
      | Asm _ -> "assemble a Motorola-syntax source."
      | Check _ -> "assemble, then confirm every instruction against the dsp56300 oracle."
      | Disasm _ -> "recover a package's program as Motorola-syntax source."
      | Import _ -> "listing or package to F# DSL source."
      | Mds _ -> "MDS packages and SysEx dumps."
      | Run _ -> "run a machine on the emulator: output WAV and cycle report."
      | Stress _ -> "worst-case INIT, TRIG and RENDER cycles over many settings, as a table."
