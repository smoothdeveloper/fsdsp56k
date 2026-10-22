<p align="center">
  <img src="docs/fsdsp56k-logo.svg" alt="" width="240">
</p>

<h1 align="center">fsdsp56k</h1>

<p align="center"><b><a href="https://smoothdeveloper.github.io/fsdsp56k/">Website, playground and full documentation</a></b></p>

F# tooling for the Motorola DSP56300 family, and on top of it, a workflow for writing Elektron
Machinedrum machines: MDS packages for X.20, the MDX custom firmware.

You prototype a machine in F# as three functions (`init`, `trig`, `render`) over fixed-point types
that behave like the DSP's own arithmetic, and run it as ordinary code on .NET, or through
[Fable](https://fable.io) as JavaScript and Rust. The same code then compiles to DSP56300 assembly,
graded word for word against the prototype on the DSP56300 emulator, with every call's cycles
measured against the track's limit. That allows you to audition and render your kernels long
before you spend effort on hand optimisation or on the real machine.

The `fsdsp56k` command line also assembles, checks, disassembles, packs and converts hand-written
machines (MDS packages and SysEx dumps), and runs them with a WAV and a cycle report.

The [website](https://smoothdeveloper.github.io/fsdsp56k/) has the commands, the kernel pipeline,
the DSL, and a playground where the shipped kernels and public MDS machines play in your browser.

## Build and test

    dotnet tool restore && dotnet paket install
    (cd native && cargo build --release)      # oracle, emulator, import, run
    ./scripts/fetch-fixtures.sh               # shipped machines the tests compare against
    dotnet run --project tests/dsp56k.tests

`dotnet run --project src/dsp56k.tool -- <command> --help` lists each command's options.

## Credits

fsdsp56k stands on other people's work:
[Motorola](https://www.nxp.com) for the DSP56300 and `asm56300`;
[Elektron](https://www.elektron.se) for the Machinedrum;
Justin Mammarella and Yatao Li for [MDX](https://github.com/jmamma/MDX), and Justin Mammarella for
[MDS](https://github.com/jmamma/MDS);
[The Usual Suspects](https://dsp56300.com) for their [dsp56300](https://github.com/dsp56300/dsp56300)
emulator, which runs the browser playground;
Matt Borgerson for [dsp56300](https://github.com/mborgerson/dsp56300), the oracle, assembler and
emulator behind every grade;
Matthew Fecher (AnalogMatthew) and Fadedlimes for the TX6 kit in
[kitbasher](https://github.com/etcetc000/kitbasher).
The full credits are on the website.

## Licence

AGPL-3.0-or-later ([LICENSE](LICENSE)). Ported machines keep their own licences and attributions
([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)); dependencies keep theirs.
