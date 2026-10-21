# The grading cache

Grading a kernel runs it three times per scenario: the .NET kernel, the compiled program on the
emulator and, when there is one, the reference program on the emulator. Those runs are most of
a grade's time, and most of them give what they gave last time: the kernel did not change, the
compiler did not change what it emits, the reference never changes. `Dsp56k.Kernel.GradeCache`
records each run under a key made of what the run depends on, so a grade that would repeat a run
reuses it instead, in any process of the machine.

It is off unless `FSDSP56K_GRADE_CACHE` names the store's folder. `gradeAt` (so `gradeWith`,
`grade` and everything built on them) goes through it; nothing else changes for a caller.

## Keys

| Run | Key |
| --- | --- |
| .NET kernel | the kernel's name; the text of its init/trig/render reflected definitions and, transitively, of every reflected definition they call; the values of the script-level bindings they read; the files of the assemblies they reach (the intrinsics' .NET models among them) and of the host's (`Dsp56k.Kernel`); the parameter and state types (properties, types, attributes) |
| compiled program, reference | the emulator's P, X and Y memory and its registers as the session starts (the program as loaded, the target image, the tables, the harness's setup), the workset, the emulator library's file, the program's sizes and the state words read back |
| both | the harness's name, target and cycle limit; the scenario as the run sees it (settings turned into parameter words by the harness); this assembly's file; `FSDSP56K_GRADE_SALT` |

An assembly is hashed by its content, not its file: the metadata (the MVID blanked), the method
bodies, the fields' initial data and the resources, without the debug directory and F#'s
optimisation and signature data. Those carry the stamps of the referenced assemblies, so an
assembly rebuilt against a changed reference, its own code the same, keeps its hash.

The compiled program's key holds what the compiler emitted, not the compiler: a compiler change
that leaves a kernel's program as it was reuses its run. The .NET run's key holds the host's
assembly, so a change to `Dsp56k.Kernel` runs the .NET kernels again.

What a key cannot see is the harness's calling convention when it lives in a closure (the
registers a memory map sets on each call, its stub address). A caller that defines harnesses
in its own scripts should give a hash of that code as `FSDSP56K_GRADE_SALT`.

## Store

`entries/<2 hex>/<key>.bin` holds the run (gzip), `<key>.json` its manifest: every key part's
hash, the payload's hash, its size, the time the run took, the host, the process and the time.
Both are written to a temporary file and renamed, the manifest last; a record is used only when
the payload still has the manifest's hash.

A process about to run a key takes `<key>.lock` (created exclusively; host, process, time).
Another one that needs the same key waits and then reads the record. A lock is stale after
`FSDSP56K_GRADE_CACHE_STALE` seconds (default 1800) or, on the same host, once its process is
gone.

## Modes and statistics

`FSDSP56K_GRADE_CACHE_MODE`: `on` (default), `off`, or `verify`, which runs every unit again and
compares with the record. A difference (logged as `differs`, the new run kept beside the record)
is a nondeterministic run or an input the key misses.

`FSDSP56K_GRADE_CACHE_EVENTS` names a file each lookup appends a line to: time, host, process,
`hit` / `miss` / `waited` / `verified` / `differs`, `net` / `dsp`, the run's label, the key and
the milliseconds (the run's when it ran, the lookup's when it hit).

## Machines

The store is per machine: keys include the emulator library's bytes, which differ between
builds. Entries are plain files named by their key, so copying a store between machines with
the same builds (rsync of the folder) is safe; a key that does not match is simply not used.
