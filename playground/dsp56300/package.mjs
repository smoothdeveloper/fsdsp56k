// fsdsp56k playground: an MDS machine package on the DSP56300 runtime, in the browser. Copied to
// dist/playground/dsp56300/package.mjs by tools/export-playground.fsx, next to host.mjs (the track harness it writes).
//
// loadPackage(bytes, info) reads the package as machinedrum/Mds.fs does (Mds.read: the MDS1 header, the program
// words, the relocation bitmap, the imports; Mds.machineOf: the MDM1 description's parameters), places it on the
// harness as machinedrum.run.Track does natively (the program at programBase, relocated, each import patched with its
// resource's address; the services and the stand-ins' data from host.mjs), and returns a kernel the playground plays
// on the DSP56300 runtime only: its parameters with their defaults and conversions (Track.convert), the steps of a
// one-second hit (INIT, the defaults, INIT, TRIG, the renders: the order the native reference at build used) and the
// image. X aliases P over the program, as on the machine: the program is placed in both. info.preset, [name, setting]
// pairs, goes over the machine's defaults. Nothing leaves the browser.
import { host } from './host.mjs';

const u16 = (b, o) => (b[o] << 8) | b[o + 1];
const u32 = (b, o) => ((u16(b, o) << 16) | u16(b, o + 2)) >>> 0;
const text = (b, o, n) => String.fromCharCode(...b.subarray(o, o + n)).replace(/\0[\s\S]*$/, '').trim();
const TRANSFORMS = ['raw14', 'unipolar', 'unipolar square', 'bipolar', 'pitch', 'boolean', 'index', 'decay'];
const words32 = s => { const bin = atob(s), u = new Uint8Array(bin.length); for (let i = 0; i < bin.length; i++) u[i] = bin.charCodeAt(i); return Array.from(new Int32Array(u.buffer)); };
let template = null;
const segments = () => template || (template = host.segments.map(s => ({ bank: s.bank, address: s.address, data: words32(s.data) })));

/// Mds.read
export function read(bytes) {
  if (bytes.length < 128 || text(bytes, 0, 4) !== 'MDS1') throw new Error('not an MDS package (no MDS1 header)');
  const n = u16(bytes, 22), offset = u32(bytes, 32), relocOffset = u32(bytes, 44), importOffset = u32(bytes, 48);
  const descSize = u16(bytes, 52), descOffset = u32(bytes, 56);
  if (offset + 3 * n > bytes.length || descOffset + descSize + 128 > bytes.length) throw new Error('truncated package');
  const program = new Array(n);
  for (let i = 0; i < n; i++) program[i] = (bytes[offset + 3 * i] << 16) | (bytes[offset + 3 * i + 1] << 8) | bytes[offset + 3 * i + 2];
  const relocations = [];
  for (let w = 0; w < n; w++) if (bytes[relocOffset + (w >> 3)] & (1 << (w & 7))) relocations.push(w);
  const imports = [];
  for (let i = 0; i < u16(bytes, 24); i++) { const o = importOffset + 4 * i; imports.push([u16(bytes, o), bytes[o + 2], bytes[o + 3]]); }
  return { program, relocations, imports, description: bytes.subarray(descOffset, descOffset + descSize),
           init: u16(bytes, 64), trig: u16(bytes, 66), render: u16(bytes, 68), maxCycles: u16(bytes, 70), version: [bytes[30], bytes[31]] };
}

/// Mds.machineOf: the name and the parameters
export function machineOf(pkg) {
  const d = pkg.description;
  const params = [];
  for (let i = 0; i < d[5]; i++) {
    const o = 64 + 8 * i;
    params.push({ name: text(d, o, 4), defaultValue: d[o + 4], min: d[o + 5], max: d[o + 6], transform: d[o + 7] & 15, control: d[o + 7] >> 4 });
  }
  return { id: text(d, 32, 32), name: text(d, 16, 16), category: text(d, 8, 4), label: text(d, 12, 3), params };
}

/// Track.convert: a front-panel setting (0..127) as the firmware hands it to the DSP
export function convert(p, s) {
  switch (p.transform) {
    case 0: return Math.round(s * 128);
    case 1: return Math.round(s * 65536);
    case 2: return Math.round((s / 128) ** 2 * 8388608);
    case 3: return Math.round((s - 64) * 131072) & 0xffffff;
    case 5: return s >= 0.5 ? 1 : 0;
    case 6: return s - p.min;
    default: return Math.round(s * 128);     // pitch and decay: their laws are not modelled
  }
}

/// Track.clearOfProgram: the track memory (import 2,6), X and Y the same storage, moved above a program it would start in
export function trackMemory(words) {
  const at = host.imports['2,6'], top = host.programBase + words;
  return at >= host.programBase && at < top ? (top + 0x3ff) & ~0x3ff : at;
}

/// The package relocated and patched at host.programBase (Track.Track's constructor)
export function place(pkg) {
  if (pkg.program.length > host.room)
    throw new Error(`the program is ${pkg.program.length} words; the track harness has room for ${host.room} from P:$${host.programBase.toString(16)}`);
  const words = pkg.program.slice();
  for (const w of pkg.relocations) words[w] = (words[w] + host.programBase) & 0xffffff;
  const memory = trackMemory(pkg.program.length);
  for (const [w, kind, symbol] of pkg.imports) {
    const at = kind + ',' + symbol === '2,6' ? memory : host.imports[kind + ',' + symbol] ?? host.imports[kind + ',*'] ?? 0;
    words[w] = (words[w] + at) & 0xffffff;
  }
  return words;
}

export function loadPackage(bytes, info = {}) {
  const pkg = read(bytes);
  const m = machineOf(pkg);
  const program = place(pkg);
  const entry = e => host.programBase + e;
  const image = {
    version: 1, words: host.words, frames: host.frames, sampleRate: host.sampleRate,
    segments: [...segments(), { bank: 0, address: host.programBase, data: program },
               ...(host.mirrorProgramInX ? [{ bank: 1, address: host.programBase, data: program }] : [])],
    registers: host.registers, initialize: [entry(pkg.init)], beforeRender: host.registers,
    render: entry(pkg.render), trig: entry(pkg.trig), outputBank: host.outputBank, output: host.output,
    inputBank: -1, input: 0, externalMemory: null, channels: 1 };
  const clamp = (p, s) => p.max > p.min ? Math.min(p.max, Math.max(p.min, s)) : s;
  const laws = m.params.map(p => Array.from({ length: 128 }, (_, s) => convert(p, clamp(p, s))));
  const own = m.params.map(p => p.defaultValue);
  const preset = (info.preset || []).filter(([name]) => m.params.some(p => p.name === name));
  const defaults = own.map((s, i) => { const hit = preset.find(([name]) => name === m.params[i].name); return hit ? clamp(m.params[i], hit[1]) : s; });
  const blocks = info.blocks || 1378;
  const steps = [['p', defaults.map((s, i) => laws[i][s] & 0xffffff)], ['i'], ['t'], ['r', blocks]];
  return {
    id: info.id || 'file:' + (info.name || m.id || 'package'), title: info.title || m.name || m.id || info.name || 'package',
    machine: m, params: m.params.map(p => p.name), defaults, ownDefaults: own, preset, laws,
    lawNote: 'the machine\'s own conversions (' + [...new Set(m.params.map(p => TRANSFORMS[p.transform] || 'raw14'))].join(', ') + '); pitch and decay laws are not modelled',
    phrase: { steps, blocks }, steps, programWords: pkg.program.length, maxCycles: pkg.maxCycles, image, emulatorOnly: true };
}
