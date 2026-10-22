// fsdsp56k playground: reading a dropped or picked machine file, in the browser only (nothing is uploaded). A .mds is
// a package (an "MDS1" header); a .syx/.sysex is unpacked to its packages with the logic of `md unsyx`
// (machinedrum/Sysex.fs: the USR-machine dump messages, 7-bit unpacking, the checksums, one package per header ...
// end-of-dump run). Each package's header gives its program size and cycle budget (machinedrum/Mds.fs, Mds.read).
// window.fsdsp56kPackage.inspect(bytes: Uint8Array, name) returns { kind, packages: [{ slot, bytes, words, maxCycles,
// entry }], error }; the playground hands a package to the DSP56300 runtime's entry point once the dist has one.
(() => {
  const INIT = [0xf0, 0x00, 0x20, 0x3c, 0x02, 0x00];
  const u16 = (b, o) => (b[o] << 8) | b[o + 1];
  const u32 = (b, o) => ((u16(b, o) << 16) | u16(b, o + 2)) >>> 0;
  const isMds = b => b.length >= 128 && b[0] === 0x4d && b[1] === 0x44 && b[2] === 0x53 && b[3] === 0x31;   // "MDS1"

  function header(bytes) {
    if (!isMds(bytes)) return { error: 'not an MDS package (no MDS1 header)' };
    const n = u16(bytes, 22), offset = u32(bytes, 32);
    if (offset + 3 * n > bytes.length) return { error: 'truncated package' };
    return { words: n, maxCycles: u16(bytes, 70), entry: { init: u16(bytes, 64), trig: u16(bytes, 66), render: u16(bytes, 68) } };
  }
  function unpack7(p) {
    const out = [];
    for (let g = 0; g < p.length; g += 8) {
      const mask = p[g];
      for (let i = 1; i < 8 && g + i < p.length; i++) out.push(p[g + i] | (((mask >> (7 - i)) & 1) << 7));
    }
    return out;
  }
  // the USR-machine messages of a SysEx stream (other SysEx is skipped)
  function messages(b) {
    const out = [];
    for (let i = 0; i < b.length;) {
      if (b[i] !== 0xf0) { i++; continue; }
      const fin = b.indexOf(0xf7, i);
      if (fin < 0) return { error: 'malformed SysEx at byte ' + i };
      const m = b.subarray(i, fin + 1);
      if (m.length >= 19 && INIT.every((v, j) => m[j] === v) && m[6] === 0x74) {
        const checked = m.subarray(9, m.length - 5);
        const sum = checked.reduce((s, v) => s + v, 0) & 0x3fff;
        if (sum !== ((m[m.length - 5] << 7) | m[m.length - 4])) return { error: 'bad checksum at byte ' + i };
        out.push({ command: m[8], slot: m[11], body: unpack7(m.subarray(14, m.length - 5)) });
      }
      i = fin + 1;
    }
    return { messages: out };
  }
  function unsyx(b) {
    const r = messages(b);
    if (r.error) return r;
    const packages = [];
    let cur = null;
    for (const m of r.messages) {
      if (m.command === 0x01) cur = { slot: m.slot + 1, size: ((m.body[0] << 24) | (m.body[1] << 16) | (m.body[2] << 8) | m.body[3]) >>> 0, data: [] };
      else if (m.command === 0x02 && cur) cur.data.push(...m.body);
      else if (m.command === 0x7b && cur && cur.data.length === cur.size) { packages.push({ slot: cur.slot, bytes: Uint8Array.from(cur.data) }); cur = null; }
    }
    return packages.length ? { packages } : { error: 'no complete machine dump in this SysEx' };
  }
  function inspect(bytes, name) {
    const ext = (name.match(/\.[^.]+$/) || [''])[0].toLowerCase();
    let kind, r;
    if (isMds(bytes)) { kind = 'mds'; r = { packages: [{ slot: null, bytes }] }; }
    else if (bytes[0] === 0xf0 || ext === '.syx' || ext === '.sysex') { kind = 'sysex'; r = unsyx(bytes); }
    else if (ext === '.mds') { kind = 'mds'; r = { error: 'not an MDS package (no MDS1 header)' }; }
    else return { kind: 'unknown', packages: [], error: 'not a .mds package or a .syx/.sysex dump' };
    if (r.error) return { kind, packages: [], error: r.error };
    const packages = r.packages.map(p => Object.assign(p, header(p.bytes)));
    const bad = packages.find(p => p.error);
    return { kind, packages, error: bad ? bad.error : null };
  }
  window.fsdsp56kPackage = { inspect, unsyx, header };
})();
