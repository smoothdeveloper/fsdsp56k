# Third-party notices

fsdsp56k ports DSP56300 machines that their authors published, with their assembly, for the Machinedrum's
MDS (Machine Dump Standard). The ports are F#: they reproduce the originals' program words or their
arithmetic, and are credited here and at the top of each file. The originals are not copied into this
repository; `scripts/fetch-fixtures.sh` fetches them, at the revisions below, for the tests.

Where an author published no licence, the port is here as credited study and test material. If you
are one of these authors and want your work credited differently, or removed, please open an issue.

## Sources

| Source | Authors | Revision | Licence | What it is |
|---|---|---|---|---|
| [etcetc000/kitbasher](https://github.com/etcetc000/kitbasher) | etcetc000 and contributors | `510bdbd` | GPL-3.0-or-later; the TX6 machines and examples/physical/ks carry their own MIT licences | A kit builder and a library of MDS machines. |
| [murdenihcam/murdenihcam-usr-machines](https://github.com/murdenihcam/murdenihcam-usr-machines) | murdenihcam | `ab92ccf` | no licence published | USR machines for the MDX X.20 firmware, released publicly with their assembly sources. |
| [jmamma/MDS](https://github.com/jmamma/MDS) | Justin Mammarella (jmamma) | `c446179` | no licence published | The Machine Dump Standard documentation and its example machines, published for anyone writing MDS machines. |

The TX6 kit (eight drums by Matthew Fecher (AnalogMatthew) and Fadedlimes, MIT) is the most comprehensive suite of
real machines the compiler is tested against; reproducing it word for word is what made the compiler robust. Many
thanks to its authors.

## Ported files

| Our files | Original | Authors | Licence |
|---|---|---|---|
| `tests/dsp56k.tests/Kernels/Sine.fs` | [MDS/machines/examples/sine/sine.asm56k](https://github.com/jmamma/MDS/blob/c446179/machines/examples/sine/sine.asm56k) | Justin Mammarella (jmamma) | no licence published |
| `tests/dsp56k.tests/Kernels/InputThru.fs` | [MDS/machines/examples/imports/input_thru.asm56k](https://github.com/jmamma/MDS/blob/c446179/machines/examples/imports/input_thru.asm56k) | Justin Mammarella (jmamma) | no licence published |
| `tests/dsp56k.tests/Kernels/MainTap.fs` | [MDS/machines/examples/imports/main_tap.asm56k](https://github.com/jmamma/MDS/blob/c446179/machines/examples/imports/main_tap.asm56k) | Justin Mammarella (jmamma) | no licence published |
| `tests/dsp56k.tests/Kernels/Pluck.fs` | [MDS/machines/examples/imports/pluck.asm56k](https://github.com/jmamma/MDS/blob/c446179/machines/examples/imports/pluck.asm56k) | Justin Mammarella (jmamma) | no licence published |
| `tests/dsp56k.tests/Kernels/Tx6Bd.fs`<br>`tests/dsp56k.tests/Kernels/Tx6BdTables.fs` | [kitbasher/mds/tx6/bd/bd.asm](https://github.com/etcetc000/kitbasher/blob/510bdbd/mds/tx6/bd/bd.asm)<br>after [Simple606](https://github.com/Fadedlimes/Simple606/tree/6aacda7a14a8c097d32836039fd356eb0976ba8e) | 2026 Matthew Fecher (AnalogMatthew) - Core DSP Engine<br>2026 Fadedlimes - Plugin Wrapper & Application | MIT |
| `tests/dsp56k.tests/Kernels/Tx6Sd.fs`<br>`tests/dsp56k.tests/Kernels/Tx6SdTables.fs` | [kitbasher/mds/tx6/sd/sd.asm](https://github.com/etcetc000/kitbasher/blob/510bdbd/mds/tx6/sd/sd.asm)<br>after [Simple606](https://github.com/Fadedlimes/Simple606/tree/6aacda7a14a8c097d32836039fd356eb0976ba8e) | 2026 Matthew Fecher (AnalogMatthew) - Core DSP Engine<br>2026 Fadedlimes - Plugin Wrapper & Application | MIT |
| `tests/dsp56k.tests/Kernels/Tx6Tom.fs`<br>`tests/dsp56k.tests/Kernels/Tx6TomTables.fs` | [kitbasher/mds/tx6/lt/lt.asm](https://github.com/etcetc000/kitbasher/blob/510bdbd/mds/tx6/lt/lt.asm)<br>[kitbasher/mds/tx6/ht/ht.asm](https://github.com/etcetc000/kitbasher/blob/510bdbd/mds/tx6/ht/ht.asm)<br>after [Simple606](https://github.com/Fadedlimes/Simple606/tree/6aacda7a14a8c097d32836039fd356eb0976ba8e) | 2026 Matthew Fecher (AnalogMatthew) - Core DSP Engine<br>2026 Fadedlimes - Plugin Wrapper & Application | MIT |
| `tests/dsp56k.tests/Kernels/Tx6Cp.fs`<br>`tests/dsp56k.tests/Kernels/Tx6CpTables.fs` | [kitbasher/mds/tx6/cp/cp.asm](https://github.com/etcetc000/kitbasher/blob/510bdbd/mds/tx6/cp/cp.asm)<br>after [Simple606](https://github.com/Fadedlimes/Simple606/tree/6aacda7a14a8c097d32836039fd356eb0976ba8e) | 2026 Matthew Fecher (AnalogMatthew) - Core DSP Engine<br>2026 Fadedlimes - Plugin Wrapper & Application | MIT |
| `tests/dsp56k.tests/Kernels/Tx6Metal.fs`<br>`tests/dsp56k.tests/Kernels/Tx6MetalTables.fs` | [kitbasher/mds/tx6/ch/ch.asm](https://github.com/etcetc000/kitbasher/blob/510bdbd/mds/tx6/ch/ch.asm)<br>[kitbasher/mds/tx6/oh/oh.asm](https://github.com/etcetc000/kitbasher/blob/510bdbd/mds/tx6/oh/oh.asm)<br>[kitbasher/mds/tx6/cy/cy.asm](https://github.com/etcetc000/kitbasher/blob/510bdbd/mds/tx6/cy/cy.asm)<br>after [Simple606](https://github.com/Fadedlimes/Simple606/tree/6aacda7a14a8c097d32836039fd356eb0976ba8e) | 2026 Matthew Fecher (AnalogMatthew) - Core DSP Engine<br>2026 Fadedlimes - Plugin Wrapper & Application | MIT |

## MIT licence of the ported MIT files

The TX6 machines are under the MIT licence; the copyright lines are in the table
above, and the notice is:

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Other third-party code and assets

| What | Where | Licence | Use |
|---|---|---|---|
| mborgerson/dsp56300 | https://github.com/mborgerson/dsp56300 (rev 2f99039, through native/Cargo.toml) | MIT | The native emulator, assembler and disassembler the tests use as an oracle. |
| cpal | https://github.com/RustAudio/cpal (0.18.2, through native/fsdsp56k-audio/Cargo.toml) | Apache-2.0 | The audio output `audition` plays through. |
| dsp56300 interpreter (The Usual Suspects) | https://github.com/dsp56300/dsp56300 (the revision checked out, wasm/build.fsx) | GPL-3.0 | Built to WebAssembly for the explorer's Play; not in this repository. A published build must ship with its licence (copied as LICENSE.dsp56300.md), the corresponding source and wasm/build.fsx's adaptations. |
