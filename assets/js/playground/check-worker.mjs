// fsdsp56k playground: renders a kernel's phrase through one runtime, off the page's thread, and checks each block
// word for word against its reference (an FNV-1a hash a block, from tools/export-playground.fsx: the .NET render of an
// F# kernel, or the native emulator's render of an original package referenced from GitHub). A dropped package has no
// reference: its blocks are rendered and measured only (kernel.blocks, no hashes). A fresh worker
// for each run: the Fable kernels keep their stand-in memories in module globals, so a run never inherits another's.

function fnv(words, from, count) {
  let h = 0x811c9dc5;
  for (let i = from; i < from + count; i++) {
    const w = words[i];
    for (let s = 0; s <= 16; s += 8) { h ^= (w >>> s) & 0xff; h = Math.imul(h, 0x01000193) >>> 0; }
  }
  return h >>> 0;
}

// --- the three runtimes, one interface: parameters(words), init(), trig(), render() -> 32 words, cycles() ---
async function fable(kernel, urls) {
  const { create } = await import(urls.js);
  const k = create(kernel.id);
  return { parameters: w => k.parameters(w), init: () => k.init(), trig: () => k.trig(), render: () => k.render(), cycles: () => null };
}

async function rust(kernel, module) {
  const { exports: x } = await WebAssembly.instantiate(module, {});
  x.dsp_select(kernel.rustIndex);
  const out = new Int32Array(32);
  return {
    parameters: w => { for (let i = 0; i < 8; i++) x.dsp_parameter(i, i < w.length ? w[i] : 0); },
    init: () => x.dsp_init(), trig: () => x.dsp_trig(),
    render: () => { const n = x.dsp_render(); for (let i = 0; i < n; i++) out[i] = x.dsp_output(i); return out; },
    cycles: () => null };
}

// the dsp56300 interpreter (wasm/runtime.cpp's C ABI), driven as the toolchain's wasm Kernel module's Instance drives it
async function dsp(kernel, module, urls) {
  const { default: create } = await import(urls.glue);
  const x = await create({ locateFile: p => p, instantiateWasm: (imports, receive) => {
    const instance = new WebAssembly.Instance(module, imports); receive(instance); return instance.exports; } });
  const img = kernel.image;
  const check = (what, r) => { if (r < 0) throw new Error(`DSP ${what} failed (${r})`); return r; };
  check('create', x._dsp_create(img.words));
  for (const s of img.segments) for (let i = 0; i < s.data.length; i++) check('write', x._dsp_write(s.bank, s.address + i, s.data[i] & 0xffffff));
  const regs = list => { for (const r of list) check('register', x._dsp_reg_write(r.register, r.word)); };
  const call = entry => check('call', x._dsp_call(entry, 1000000));
  regs(img.registers);
  for (const e of img.initialize) call(e);
  const workset = img.registers.find(r => r.register === 6).word;
  const out = new Int32Array(img.frames);
  let last = 0;
  return {
    parameters: w => { w.forEach((word, i) => check('write', x._dsp_write(2, workset + 1 + i, word & 0xffffff))); },
    init: () => { regs(img.beforeRender); for (const e of img.initialize) call(e); },
    trig: () => { regs(img.beforeRender); if (img.trig != null) call(img.trig); },
    render: () => {
      regs(img.beforeRender);
      const c0 = x._dsp_cycles();
      call(img.render);
      last = x._dsp_cycles() - c0;
      for (let i = 0; i < img.frames; i++) out[i] = check('read', x._dsp_read(img.outputBank, img.output + i));
      return out;
    },
    cycles: () => last };
}

onmessage = async ({ data }) => {
  const { runtime, kernel, module, urls } = data;
  try {
    const started = performance.now();
    const k = runtime === 'js' ? await fable(kernel, urls) : runtime === 'rust' ? await rust(kernel, module) : await dsp(kernel, module, urls);
    const ready = performance.now();
    const hashes = kernel.hashes || null, blocks = hashes ? hashes.length : kernel.blocks;
    const words = new Int32Array(blocks * 32);
    let block = 0, matched = 0, first = -1, cyc = 0, cmax = 0, rendering = 0, peak = 0;
    for (const step of kernel.steps) {
      switch (step[0]) {
        case 'p': k.parameters(step[1]); break;
        case 'i': k.init(); break;
        case 't': k.trig(); break;
        case 'r':
          for (let n = 0; n < step[1]; n++, block++) {
            const t0 = performance.now();
            const out = k.render();
            rendering += performance.now() - t0;
            for (let i = 0; i < 32; i++) { words[block * 32 + i] = out[i]; peak = Math.max(peak, Math.abs((out[i] << 8) >> 8)); }
            if (hashes) { if (fnv(words, block * 32, 32) === hashes[block]) matched++; else if (first < 0) first = block; }
            const c = k.cycles();
            if (c != null) { cyc += c; cmax = Math.max(cmax, c); }
          }
      }
    }
    postMessage({ ok: true, runtime, kernel: kernel.id, blocks, matched: hashes ? matched : null, first, peak, reference: !!hashes,
                  cycles: runtime === 'dsp56300' ? { mean: cyc / block, max: cmax } : null,
                  loadMs: ready - started, renderMs: rendering, audioMs: blocks * 32 / 44.1, words }, [words.buffer]);
  } catch (e) {
    postMessage({ ok: false, runtime, kernel: kernel.id, error: String(e && e.message || e) });
  }
};
