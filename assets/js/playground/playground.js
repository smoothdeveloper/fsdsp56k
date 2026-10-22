// fsdsp56k splash: the playground. Pick a kernel and a runtime; audition it live (pad, keyboard, Web MIDI) or render
// its phrase. Runtimes: the .NET reference (pre-rendered WAVs: it cannot run in a browser), JS (Fable), Rust -> wasm
// (Fable) and DSP56300 (wasm: the dsp56300 emulator running the compiled kernel's program).
//
// What it can play comes from dist/playground: manifest.js (written by `dotnet fsi build.fsx playground-manifest`, from
// the files present) names each kernel's data script and each runtime's artefacts; the loader below loads those, and on
// http also confirms each artefact is really there. A runtime whose artefacts are missing shows "not available", as
// does every "word for word" result that has not been checked in this browser: nothing is shown before a check has run.
//
// The runtime picker is separate from the rest: whatever sits in #pg-runtimes announces a choice with an
// `auditionmode` event (detail: dotnet, js, rust or dsp56300), the event the art project's audition picker fires, and
// can read window.fsdsp56kPlayground.runtimes() (and listen to `fsdsp56k:runtimes`) to grey what is not available.
// With data-picker="external" on #pg-runtimes, the built-in chips are not drawn.
(() => {
  // where the playground's data is: dist/playground/ from the working folder, playground/ in the release (dist target)
  const DIST = document.documentElement.dataset.playground || 'dist/playground/';
  const KERNELS = [];
  let MANIFEST = null;
  let CATALOGUE = [];      // every listed machine, shipped or held (dist/playground/machines.js, from machines.tsv)
  const b64 = (s, T) => { const bin = atob(s), u = new Uint8Array(bin.length); for (let i = 0; i < bin.length; i++) u[i] = bin.charCodeAt(i); return new T(u.buffer); };
  const api = window.fsdsp56kPlayground = {
    manifest(m) { MANIFEST = m; },
    catalogue(list) { CATALOGUE = list; },
    register(k) {
      k.hashes = b64(k.phrase.hashes, Uint32Array);
      k.steps = k.phrase.steps;
      if (k.image) k.image.segments = k.image.segments.map(s => ({ bank: s.bank, address: s.address, data: Array.from(b64(s.data, Int32Array)) }));
      KERNELS.push(k);
    }
  };
  const script = src => new Promise(ok => { const e = document.createElement('script'); e.src = src; e.onload = () => ok(true); e.onerror = () => ok(false); document.head.appendChild(e); });

  const RUNTIMES = [
    { id: 'dotnet',   label: '.NET reference',  sub: 'pre-rendered',          icons: ['runtime-dotnet'] },
    { id: 'js',       label: 'JS (Fable)',      sub: 'F# → JavaScript',       icons: ['runtime-fable', 'runtime-javascript'] },
    { id: 'rust',     label: 'Rust → wasm',     sub: 'F# → Rust (Fable)',     icons: ['runtime-rust', 'runtime-wasm'] },
    { id: 'dsp56300', label: 'DSP56300 (wasm)', sub: 'compiled, on dsp56300', icons: ['fsdsp56k', 'runtime-dsp56300'] } ];
  const RT = Object.fromEntries(RUNTIMES.map(r => [r.id, r]));
  const $ = id => document.getElementById(id);
  const served = /^https?:$/.test(location.protocol);
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const fmt = n => Number(n).toLocaleString('en');
  const hex = w => '$' + ((w >>> 0) & 0xffffff).toString(16).toUpperCase().padStart(6, '0');

  function start() {
    const root = $('playground');
    if (!root) return;
    if (!KERNELS.length) { empty(root); return; }
    const S = { kernel: KERNELS[0], runtime: 'dsp56300', settings: [], live: false, midi: { on: false, ch: 1, track: 1, any: false } };
    const results = {};          // results[kernel][runtime]: a finished check
    const running = {};
    const q = new URLSearchParams(location.search);
    if (q.get('kernel')) S.kernel = KERNELS.find(k => k.id === q.get('kernel')) || S.kernel;
    if (RT[q.get('runtime')]) S.runtime = q.get('runtime');
    if (!served) $('pg-serve').hidden = false;
    // ---- what is available: a kernel on a runtime needs the runtime's artefacts (from the manifest, and on http
    // found where it says) and the kernel built for it; the three live runtimes need http ----
    const files = MANIFEST.runtimes || {};
    const missing = {};       // runtime -> the first artefact that is not there (http only)
    function why(rt, k = S.kernel) {
      if (k.emulatorOnly && rt !== 'dsp56300') return 'no F# kernel: ' + (k.dropped ? 'your file' : 'the original package, from GitHub,') + ' runs on the DSP56300 emulator only';
      if (rt === 'dotnet') return k.reference && k.reference.phrase ? null : 'no reference WAVs in dist';
      const r = files[rt];
      if (!r) return 'not built yet (nothing in dist/playground/' + ({ js: 'fable-js', rust: 'rust', dsp56300: 'dsp56300' })[rt] + ')';
      if (missing[rt]) return 'missing ' + missing[rt];
      if (rt === 'dsp56300' && !k.image) return k.imageUnavailable || 'this kernel is not in the ' + RT[rt].label + ' build';
      if (rt !== 'dsp56300' && !(r.kernels || []).includes(k.id)) return 'this kernel is not in the ' + RT[rt].label + ' build';
      const c = k.checks && k.checks[rt];       // the manifest: graded word for word against .NET when built, or why not
      if (c && !c.available) return c.why;
      if (!served) return 'needs the page served over http';
      return null;
    }
    const available = (rt, k) => why(rt, k) === null;
    if (served) Promise.all(Object.entries(files).flatMap(([rt, r]) => ['module', 'wasm', 'glue', 'processor'].filter(f => r && r[f]).map(f =>
      fetch(DIST + r[f], { method: 'HEAD' }).then(x => { if (!x.ok) missing[rt] = missing[rt] || r[f]; }, () => { missing[rt] = missing[rt] || r[f]; }))))
      .then(() => { picker(); rows(); });
    if (!available(S.runtime) && !q.get('runtime')) S.runtime = RUNTIMES.map(r => r.id).reverse().find(id => available(id)) || 'dotnet';
    for (const k of KERNELS) if (files.rust && files.rust.kernels) k.rustIndex = files.rust.kernels.indexOf(k.id);
    const url = p => new URL(DIST + p, location.href).href;

    // ---- audio ----
    let ctx = null, master = null, analyser = null, node = null, nodeKey = '', modules = {}, refBuffers = {};
    function audio() {
      if (ctx) return ctx;
      ctx = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: 44100 });
      master = ctx.createGain(); master.gain.value = 1;
      analyser = ctx.createAnalyser(); analyser.fftSize = 2048;
      master.connect(analyser); analyser.connect(ctx.destination);
      return ctx;
    }
    const compiled = name => modules[name] || (modules[name] = fetch(DIST + name).then(r => { if (!r.ok) throw new Error(name + ': ' + r.status); return r.arrayBuffer(); }).then(b => WebAssembly.compile(b)));
    let workletsAdded = {};
    async function addWorklet(url) { if (!workletsAdded[url]) workletsAdded[url] = ctx.audioWorklet.addModule(url); return workletsAdded[url]; }
    const words = () => S.settings.map((s, i) => S.kernel.laws[i][s]);

    async function connectLive() {
      const key = S.kernel.id + '/' + S.runtime;
      if (node && nodeKey === key) return node;
      dropLive();
      audio(); await ctx.resume();
      nodeKey = key;
      if (S.runtime === 'dotnet') { node = { dotnet: true }; return node; }
      if (!available(S.runtime)) throw new Error(why(S.runtime));
      if (S.runtime === 'dsp56300') {
        const mod = await compiled(files.dsp56300.wasm);
        await addWorklet(DIST + files.dsp56300.processor);
        const n = new AudioWorkletNode(ctx, 'dsp56300', { numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [2], processorOptions: { module: mod } });
        await new Promise((ok, fail) => {
          n.port.onmessage = ({ data }) => { if (data.type === 'ready') ok(); else if (data.type === 'error') fail(new Error(data.message)); };
          n.port.postMessage({ type: 'load', image: S.kernel.image });
        });
        n.port.onmessage = ({ data }) => onLevel(data);
        node = { port: n.port, audio: n, dsp: true };
        sendAll();
        n.port.postMessage({ type: 'gain', value: 0.3 });
        n.port.postMessage({ type: 'play', value: true });
      } else {
        const mod = S.runtime === 'rust' ? await compiled(files.rust.wasm) : null;
        await addWorklet('assets/js/playground/voice-' + S.runtime + '-worklet.mjs');
        const n = new AudioWorkletNode(ctx, 'fsdsp56k-voice-' + S.runtime, { numberOfInputs: 0, numberOfOutputs: 1, outputChannelCount: [2],
          processorOptions: { runtime: S.runtime, module: mod, rustIndex: S.kernel.rustIndex, kernel: S.kernel.id, words: words() } });
        n.port.onmessage = ({ data }) => onLevel(data);
        node = { port: n.port, audio: n };
      }
      node.audio.connect(master);
      return node;
    }
    function dropLive() {
      if (node && node.audio) { try { node.port.postMessage({ type: 'play', value: false }); node.audio.disconnect(); } catch (e) { } }
      node = null; nodeKey = '';
      $('pg-load').textContent = '';
    }
    function sendParam(i) {
      if (!node || node.dotnet) return;
      const w = S.kernel.laws[i][S.settings[i]];
      if (node.dsp) node.port.postMessage({ type: 'memory', bank: 2, address: S.kernel.image.registers.find(r => r.register === 6).word + 1 + i, word: w & 0xffffff });
      else node.port.postMessage({ type: 'parameter', index: i, word: w });
    }
    const sendAll = () => S.settings.forEach((_, i) => sendParam(i));
    function onLevel(d) {
      if (d.type === 'error') { setNow('error: ' + d.message); return; }
      if (d.type !== 'level') return;
      const load = d.load != null ? d.load : d.timing && d.timing.totalPercent != null ? d.timing.totalPercent / 100 : null;
      if (load != null && isFinite(load)) $('pg-load').textContent = 'audio thread ' + (load * 100).toFixed(1) + '%';
    }
    async function refBuffer(name) {
      if (!refBuffers[name]) refBuffers[name] = fetch(DIST + name).then(r => r.arrayBuffer()).then(b => ctx.decodeAudioData(b));
      return refBuffers[name];
    }
    let elements = {};
    async function playReference(file) {
      if (!served) {    // from disk: an <audio> element (no fetch, no analyser)
        const a = elements[file] || (elements[file] = new Audio(DIST + file));
        a.currentTime = 0; a.volume = 0.8; await a.play().catch(() => { });
        flashScope();
        return;
      }
      audio(); await ctx.resume();
      const src = ctx.createBufferSource(); src.buffer = await refBuffer(file); src.connect(master); src.start();
    }
    function playWords(w) {
      audio(); ctx.resume();
      const buf = ctx.createBuffer(1, w.length, 44100), ch = buf.getChannelData(0);
      for (let i = 0; i < w.length; i++) ch[i] = ((w[i] << 8) >> 8) / 8388608 * 0.6;
      const src = ctx.createBufferSource(); src.buffer = buf; src.connect(master); src.start();
    }

    async function trig() {
      pulsePad();
      if (S.runtime === 'dotnet' && !S.kernel.emulatorOnly) { setNow(S.kernel.title + ' · .NET reference hit (at the defaults)'); return playReference(S.kernel.reference.hit || S.kernel.reference.phrase); }
      if (!available(S.runtime)) { setNow(RT[S.runtime].label + ': not available, ' + why(S.runtime)); return; }
      if (!S.live) { await setLive(true); }
      if (node && node.port) node.port.postMessage({ type: 'trig' });
    }
    async function setLive(on) {
      S.live = on;
      $('pg-audition').setAttribute('aria-pressed', String(on));
      $('pg-audition').querySelector('span:last-child').textContent = on ? 'Stop' : 'Audition';
      if (!on) { dropLive(); setNow('idle'); return; }
      if (S.runtime === 'dotnet' && !S.kernel.emulatorOnly) { setNow(S.kernel.title + ' · .NET reference: the pads play its pre-rendered hit'); return; }
      if (!available(S.runtime)) {
        S.live = false; $('pg-audition').setAttribute('aria-pressed', 'false'); $('pg-audition').querySelector('span:last-child').textContent = 'Audition';
        setNow(RT[S.runtime].label + ': not available, ' + why(S.runtime)); return;
      }
      setNow('loading ' + RT[S.runtime].label + '…');
      try { await connectLive(); setNow(S.kernel.title + ' · ' + RT[S.runtime].label + ' · live'); }
      catch (e) { setNow('could not start: ' + e.message); S.live = false; }
    }

    // ---- the checks (a worker a run) ----
    function check(runtime, kernel = S.kernel) {
      if (runtime === 'dotnet' || !available(runtime, kernel)) return Promise.resolve(null);
      const key = kernel.id + '/' + runtime;
      if (running[key]) return running[key];
      const mod = runtime === 'rust' ? compiled(files.rust.wasm) : runtime === 'dsp56300' ? compiled(files.dsp56300.wasm) : Promise.resolve(null);
      const urls = { js: files.js && url(files.js.module), glue: files.dsp56300 && url(files.dsp56300.glue) };
      running[key] = mod.then(module => new Promise(resolve => {
        const w = new Worker('assets/js/playground/check-worker.mjs', { type: 'module' });
        w.onmessage = ({ data }) => { w.terminate(); resolve(data); };
        w.onerror = e => { w.terminate(); resolve({ ok: false, error: e.message || 'worker failed' }); };
        w.postMessage({ runtime, module, urls, kernel: { id: kernel.id, rustIndex: kernel.rustIndex, steps: kernel.steps, hashes: kernel.hashes, image: kernel.image, blocks: kernel.phrase.blocks } });
      })).catch(e => ({ ok: false, error: e.message })).then(r => {
        delete running[key];
        (results[kernel.id] = results[kernel.id] || {})[runtime] = r;
        if (kernel === S.kernel) rows();
        return r;
      });
      rows();
      return running[key];
    }
    const checkAll = () => ['js', 'rust', 'dsp56300'].reduce((p, rt) => p.then(() => check(rt)), Promise.resolve());

    async function renderPhrase() {
      if (S.runtime === 'dotnet' && !S.kernel.emulatorOnly) {
        setNow(S.kernel.title + ' · .NET reference phrase (' + fmt(S.kernel.phrase.blocks) + ' blocks)');
        return playReference(S.kernel.reference.phrase);
      }
      if (!available(S.runtime)) { setNow(RT[S.runtime].label + ': not available, ' + why(S.runtime)); return; }
      setNow('rendering the phrase on ' + RT[S.runtime].label + '…');
      const r = await check(S.runtime);
      if (!r || !r.ok) { setNow('render failed: ' + (r && r.error)); return; }
      playWords(r.words);
      setNow(S.kernel.title + ' · ' + RT[S.runtime].label + ' · rendered ' + (r.audioMs / 1000).toFixed(2) + ' s of audio in ' + r.renderMs.toFixed(0) + ' ms');
    }

    // ---- the view ----
    function setNow(t) { $('pg-now').textContent = t; }
    // the kernels, grouped by where they come from; the held machines (catalogue) below, not playable, each with why
    const byGroup = list => list.reduce((m, x) => { const g = x.group || x.origin; (m[g] = m[g] || []).push(x); return m; }, {});
    function kernels() {
      const box = $('pg-kernels'); box.innerHTML = '';
      for (const [g, ks] of Object.entries(byGroup(KERNELS))) {
        const grp = document.createElement('div'); grp.className = 'pg-kgroup';
        grp.innerHTML = `<span class="pg-kgroup-name">${esc(g)} <small>${esc(ks[0].author || '')}</small></span><div class="chips"></div>`;
        for (const k of ks) {
          const b = document.createElement('button');
          b.type = 'button'; b.className = 'chip'; b.setAttribute('aria-pressed', String(k === S.kernel));
          b.innerHTML = `${esc(k.title)}${gradeDot(k.grade)}`;
          b.title = gradeLine(k.grade);
          b.onclick = () => selectKernel(k);
          grp.lastChild.appendChild(b);
        }
        box.appendChild(grp);
      }
      // the original packages, referenced on GitHub at a commit: fetched by this browser when picked, never hosted here
      const refs = CATALOGUE.filter(m => m.ref && !m.ref.error);
      if (refs.length) {
        const d = document.createElement('details'); d.className = 'pg-held pg-gh';
        d.open = ghOpen; d.ontoggle = () => { ghOpen = d.open; };
        d.innerHTML = `<summary>From GitHub <small>${refs.length} original packages · DSP56300 emulator only · loaded from GitHub when picked, not hosted here${refs.some(m => m.silent) ? ` · ${refs.filter(m => m.silent).length} dimmed: they process the other tracks' audio, which a one-track playground cannot give them` : ''}</small></summary><div class="pg-held-list"></div>`;
        for (const [g, ms] of Object.entries(byGroup(refs))) {
          const grp = document.createElement('div'); grp.className = 'pg-kgroup';
          grp.innerHTML = `<span class="pg-kgroup-name">${esc(g)} <small>${esc(ms[0].author)} · ${esc(ms[0].ref.repo)}</small></span><div class="chips"></div>`;
          for (const m of ms) {
            const b = document.createElement('button');
            const quiet = !!m.silent || !m.ref.native.peak;
            b.type = 'button'; b.className = 'chip gh' + (refFailed[m.id] ? ' failed' : '') + (quiet ? ' quiet' : ''); b.dataset.ref = m.id;
            b.setAttribute('aria-pressed', String(S.kernel.id === 'github:' + m.id));
            b.innerHTML = esc(m.title) + (loadingRef === m.id ? ' <small>…</small>' : '');
            b.title = `${m.ref.repo}@${m.ref.sha.slice(0, 7)} · ${m.ref.path}` + (quiet ? ' · ' + (m.silent || 'silent at its defaults on this harness') : '') + (m.preset && m.preset.length ? ' · preset ' + presetText(m.preset) : '') + (m.status === 'held' ? ' · ' + m.reason : '') + (refFailed[m.id] ? ' · ' + refFailed[m.id] : '');
            b.onclick = () => openReferenced(m);
            grp.lastChild.appendChild(b);
          }
          d.lastChild.appendChild(grp);
        }
        box.appendChild(d);
      }
      if (droppedKernels.length) {
        const grp = document.createElement('div'); grp.className = 'pg-kgroup';
        grp.innerHTML = `<span class="pg-kgroup-name">Your files <small>read in this browser, not uploaded · DSP56300 emulator only</small></span><div class="chips"></div>`;
        for (const k of droppedKernels) {
          const b = document.createElement('button');
          b.type = 'button'; b.className = 'chip gh'; b.setAttribute('aria-pressed', String(k === S.kernel));
          b.textContent = k.title; b.onclick = () => selectKernel(k);
          grp.lastChild.appendChild(b);
        }
        box.appendChild(grp);
      }
      const held = CATALOGUE.filter(m => m.status === 'held' && !(m.ref && !m.ref.error));
      if (held.length) {
        const d = document.createElement('details'); d.className = 'pg-held';
        d.open = heldOpen; d.ontoggle = () => { heldOpen = d.open; };
        d.innerHTML = `<summary>Held back <small>${held.length} machines, not loaded: ${esc(commonReason(held))}</small></summary><div class="pg-held-list">` +
          Object.entries(byGroup(held)).map(([g, ms]) => `<div class="pg-kgroup"><span class="pg-kgroup-name">${esc(g)} <small>${esc(ms[0].author)} · ${esc(ms[0].licence)}</small></span>` +
            `<div class="chips">${ms.map(m => `<span class="chip held" title="${esc(m.reason + ' · ' + gradeLine(m.grade))}">${esc(m.title)}${gradeDot(m.grade)}</span>`).join('')}</div>` +
            `<p class="pg-held-why">${esc(commonReason(ms))}</p></div>`).join('') + '</div>';
        box.appendChild(d);
      }
      const k = S.kernel;
      const credit = `<span class="pg-credit">${esc(k.author || k.origin)}</span>` + (k.licence ? `<span class="lic">${esc(k.licence)}</span>` : '') +
        (k.source ? `<a class="src" href="${esc(k.source)}" rel="noopener" target="_blank">source</a>` : '');
      const r = k.ref;
      $('pg-kmeta').innerHTML = `<b>${esc(k.title)}</b> · ${fmt(k.programWords)} program words · ${k.params.length} parameters<span class="row">${credit}</span>` +
        (r ? `<span class="row gh">loaded from GitHub, not hosted here · <a href="${esc(r.blob)}" rel="noopener" target="_blank">${esc(r.repo)}@${esc(r.sha.slice(0, 7))}</a> · ${esc(r.path)}` +
             ` · <a href="${esc(r.raw)}" rel="noopener" target="_blank">download from source</a></span>` +
             `<span class="row"><span class="grade none">DSP56300 emulator only</span>${r.graded ? ' <span class="gcyc">the package the F# port is graded against</span>' : ''}</span>` +
             `<span class="row gcyc">render cycles a block, native emulator: ${fmt(Math.round(r.native.cycles.mean))} (max ${fmt(r.native.cycles.max)}) · declared ${fmt(r.native.cycles.declared)}</span>` +
             (k.preset && k.preset.length ? `<span class="row gcyc">preset ${esc(presetText(k.preset))} over its own defaults, found with fsdsp56k stress: with no input it sounds when its resonance self-oscillates</span>` : '') +
             (k.silent ? `<span class="row gcyc pg-silent">quiet on this harness, natively too · ${esc(k.silent)}</span>` :
              r.native.peak ? '' : `<span class="row gcyc pg-silent">silent at its defaults on the fsdsp56k track harness, natively too: ${/hit|smp/i.test(k.id) ? 'no sample banks are loaded here' : 'it needs state or input the harness does not provide'}</span>`) : '') +
        (k.dropped ? `<span class="row gh">your file, read in this browser, not uploaded · DSP56300 emulator only · declared ${fmt(k.maxCycles)} cycles a block</span>` : '') +
        (k.grade ? `<span class="row"><span class="grade ${gradeClass(k.grade)}">${gradeChip(k.grade)}</span></span><span class="row gcyc">${gradeCycles(k.grade)}</span>` : '');
      $('pg-law').textContent = k.lawNote;
    }
    let heldOpen = false, ghOpen = false, loadingRef = null;
    const refKernels = {}, refFailed = {}, droppedKernels = [];
    const hexOf = b => Array.from(new Uint8Array(b), x => x.toString(16).padStart(2, '0')).join('');
    // a referenced machine: the original package, fetched from GitHub at its commit, checked against the size and
    // SHA-256 recorded at build, unpacked (package.js) and placed on the track harness (package.mjs)
    async function openReferenced(m) {
      const id = 'github:' + m.id;
      if (refKernels[id]) { selectKernel(refKernels[id]); return refKernels[id]; }
      loadingRef = m.id; delete refFailed[m.id]; kernels();
      setNow('fetching ' + m.title + ' from GitHub (' + m.ref.repo + '@' + m.ref.sha.slice(0, 7) + ')…');
      try {
        const rt = await packageRuntime();
        if (!rt.load) throw new Error('the DSP56300 runtime is ' + rt.why);
        const res = await fetch(m.ref.raw);
        if (!res.ok) throw new Error('GitHub answered ' + res.status);
        const bytes = new Uint8Array(await res.arrayBuffer());
        if (bytes.length !== m.ref.bytes) throw new Error(`the file is ${fmt(bytes.length)} bytes, ${fmt(m.ref.bytes)} when built`);
        if (window.crypto && crypto.subtle && hexOf(await crypto.subtle.digest('SHA-256', bytes)) !== m.ref.sha256) throw new Error('the file differs from the one checked at build (SHA-256)');
        const p = window.fsdsp56kPackage.inspect(bytes, m.ref.path);
        if (p.error || !p.packages.length) throw new Error(p.error || 'no machine package in the file');
        const k = await rt.load(p.packages[0].bytes, { id, title: m.title, blocks: m.ref.native.blocks, preset: m.preset || [] });
        Object.assign(k, { silent: m.silent || null, title: m.title, author: m.author, group: m.group, licence: m.licence, source: m.source, ref: m.ref, grade: null, reference: {}, checks: null,
          hashes: b64(m.ref.native.hashes, Uint32Array), nativeCycles: m.ref.native.cycles });
        refKernels[id] = k; loadingRef = null;
        if (S.runtime !== 'dsp56300') S.runtime = 'dsp56300';
        selectKernel(k);
        setNow(m.title + ' · loaded from GitHub · DSP56300 (wasm)');
        return k;
      } catch (e) {
        loadingRef = null; refFailed[m.id] = e.message; kernels();
        $('pg-kmeta').insertAdjacentHTML('afterbegin', `<span class="row pg-file-error">${esc(m.title)}: could not load from GitHub, ${esc(e.message)} · ` +
          `<a href="${esc(m.ref.raw)}" rel="noopener" target="_blank">download from source</a> · <a href="${esc(m.ref.blob)}" rel="noopener" target="_blank">${esc(m.ref.repo)}</a></span>`);
        setNow(m.title + ': could not load from GitHub, ' + e.message);
        return null;
      }
    }
    // a dropped or picked file the DSP56300 runtime loaded: a kernel like the others, with no reference to compare with
    api.addKernel = k => {
      Object.assign(k, { dropped: true, author: 'your file', group: 'Your files', licence: '', grade: null, reference: {}, checks: null, hashes: null, nativeCycles: null });
      const i = droppedKernels.findIndex(x => x.id === k.id);
      if (i >= 0) droppedKernels[i] = k; else droppedKernels.push(k);
      delete results[k.id];
      if (S.runtime !== 'dsp56300') S.runtime = 'dsp56300';
      selectKernel(k);
    };
    const presetText = ps => ps.map(([n, v]) => n + ' ' + v).join(', ');
    const commonReason = ms => { const r = [...new Set(ms.map(m => m.reason))]; return r.length === 1 ? r[0] : r.join(' / '); };
    function selectKernel(k) {
      S.kernel = k; S.settings = k.defaults.slice();
      if (window.fsdsp56kTerminal && window.fsdsp56kTerminal.select) window.fsdsp56kTerminal.select(k.id);   // the terminal follows the kernel
      kernels(); picker(); params(); rows(); midimap();
      if (S.live) { dropLive(); setLive(true); }
      if (served && visible) checkAll();
    }
    // the built-in picker: chips with icon slots (assets/icons/*.png); any picker in #pg-runtimes only has to fire
    // `auditionmode` with a runtime id
    const host = $('pg-runtimes');
    function picker() {
      const list = RUNTIMES.map(r => ({ id: r.id, title: r.label, enabled: available(r.id), why: why(r.id) }));
      api.runtimes = () => list;
      host.dispatchEvent(new CustomEvent('fsdsp56k:runtimes', { detail: list }));
      if (host.dataset.picker === 'external') return;
      host.innerHTML = '';
      for (const r of RUNTIMES) {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'rt'; b.dataset.rt = r.id;
        b.setAttribute('aria-pressed', String(r.id === S.runtime));
        const na = !available(r.id);
        b.classList.toggle('na', na);
        b.title = na ? 'not available: ' + why(r.id) : '';
        b.innerHTML = `<span class="rt-icons">${r.icons.map(i => `<img src="assets/icons/${i}.png" width="16" height="16" alt="">`).join('')}</span>` +
          `<span class="rt-text"><b>${r.label}</b><small>${na ? (served ? 'not available' : 'needs http') : r.sub}</small></span>`;
        b.onclick = () => host.dispatchEvent(new CustomEvent('auditionmode', { detail: r.id, bubbles: true }));
        host.appendChild(b);
      }
    }
    host.addEventListener('auditionmode', e => {
      if (!RT[e.detail]) return;
      S.runtime = e.detail; picker(); rows();
      if (S.live) { dropLive(); setLive(true); }
    });
    function params() {
      const box = $('pg-params'); box.innerHTML = '';
      S.kernel.params.forEach((name, i) => {
        const row = document.createElement('label'); row.className = 'pg-param';
        row.innerHTML = `<span class="n">${i + 1}</span><span class="name">${name}</span><input type="range" min="0" max="127" step="1" value="${S.settings[i]}"><output></output>`;
        const input = row.querySelector('input'), out = row.querySelector('output');
        const show = () => { out.innerHTML = `${S.settings[i]}<small>${hex(S.kernel.laws[i][S.settings[i]])}</small>`; input.style.setProperty('--v', S.settings[i] / 127); };
        input.oninput = () => { S.settings[i] = +input.value; show(); sendParam(i); };
        row.setValue = v => { S.settings[i] = v; input.value = v; show(); sendParam(i); };
        show(); box.appendChild(row);
      });
    }
    function rows() {
      const box = $('pg-rows'), k = S.kernel, res = results[k.id] || {};
      const html = [];
      html.push(k.emulatorOnly
        ? `<div class="pg-row ref"><span class="rt-name">.NET reference</span><span class="state na">no F# kernel</span><span class="detail">${k.dropped ? 'your file' : 'the original package'} runs on the DSP56300 emulator only` +
          (k.hashes ? `; its reference is the native emulator's render at build (${fmt(k.phrase.blocks)} blocks, ${k.preset && k.preset.length ? 'its defaults with the preset' : 'its own defaults'})` : '; there is no reference to compare it with') + '</span></div>'
        : `<div class="pg-row ref${S.runtime === 'dotnet' ? ' sel' : ''}"><span class="rt-name">.NET reference</span><span class="state ref">reference</span><span class="detail">${fmt(k.phrase.blocks)} blocks pre-rendered (one bar), with the native emulator in agreement when exported</span></div>`);
      for (const rt of ['js', 'rust', 'dsp56300']) {
        const r = res[rt], busy = running[k.id + '/' + rt];
        let state, detail;
        if (!available(rt)) { state = '<span class="state na">not available</span>'; detail = why(rt); }
        else if (busy) { state = '<span class="state run">checking…</span>'; detail = 'rendering ' + fmt(k.phrase.blocks) + ' blocks'; }
        else if (!r) { state = '<span class="state na">not checked</span>'; detail = '—'; }
        else if (!r.ok) { state = '<span class="state na">not available</span>'; detail = r.error; }
        else if (r.matched == null) { state = r.peak ? '<span class="state ok">rendered</span>' : '<span class="state bad">silent</span>'; detail = `${fmt(r.blocks)} blocks · peak ${level(r.peak)} · ${(r.audioMs / r.renderMs).toFixed(0)}× real time · no reference to compare with`; }
        else if (r.matched === r.blocks) { state = `<span class="state ok">✓ ${k.emulatorOnly ? '= native emulator, ' : ''}word for word</span>`; detail = `${fmt(r.matched)} / ${fmt(r.blocks)} blocks · ${(r.audioMs / r.renderMs).toFixed(0)}× real time` + (k.emulatorOnly ? ` · peak ${level(r.peak)}` : ''); }
        else { state = '<span class="state bad">✗ differs</span>'; detail = `${fmt(r.matched)} / ${fmt(r.blocks)} blocks match, first difference at block ${fmt(r.first)}`; }
        if (rt === 'dsp56300' && available(rt)) {
          const c = r && r.ok && r.cycles;
          const n = k.nativeCycles, declared = n ? n.declared : k.maxCycles;
          detail += `<br><span class="cyc">${c ? `<b>${fmt(Math.round(c.mean))}</b> cycles a block (max ${fmt(c.max)})` : 'cycles: —'}${n ? ' · native emulator at export ' + fmt(Math.round(n.mean)) : ''}${declared ? ' · declared ' + fmt(declared) : ''}</span>`;
        }
        html.push(`<div class="pg-row${S.runtime === rt ? ' sel' : ''}"><span class="rt-name">${RT[rt].label}</span>${state}<span class="detail">${detail}</span></div>`);
      }
      box.innerHTML = html.join('');
    }
    const level = p => !p ? 'silent' : (20 * Math.log10(p / 8388608)).toFixed(1) + ' dBFS';

    // ---- pads, keys and MIDI ----
    const pad = $('pg-pad');
    pad.addEventListener('pointerdown', e => { e.preventDefault(); trig(); });
    pad.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); trig(); } });
    function pulsePad() { pad.classList.remove('hit'); void pad.offsetWidth; pad.classList.add('hit'); }
    let visible = false;
    addEventListener('keydown', e => {
      if (!visible || e.repeat || e.metaKey || e.ctrlKey || e.altKey) return;
      if (e.target.closest && e.target.closest('input, textarea, button, a')) return;
      if (e.key === ' ' || e.key === 't' || e.key === 'T') { e.preventDefault(); trig(); }
    });
    $('pg-audition').onclick = () => setLive(!S.live);
    $('pg-render').onclick = () => renderPhrase();
    $('pg-check').onclick = () => checkAll();

    const NOTES = [36, 38, 40, 41, 43, 45, 47, 48, 50, 52, 53, 55, 57, 59, 60, 62], CC = [16, 40, 72, 96];
    const midiChannel = () => (S.midi.ch - 1 + Math.floor((S.midi.track - 1) / 4)) % 16;
    function midimap() {
      const cc = CC[(S.midi.track - 1) % 4], n = S.kernel.params.length;
      $('pg-ch').textContent = S.midi.ch; $('pg-track').textContent = S.midi.track;
      $('pg-midimap').innerHTML = `Track ${S.midi.track} listens on channel <b>${midiChannel() + 1}</b>: note <b>${NOTES[S.midi.track - 1]}</b>${S.midi.any ? ' (or any note)' : ''} triggers, CC <b>${cc}–${cc + 7}</b> set parameters 1–8${n < 8 ? ` (this kernel has ${n})` : ''}.` + (midiStatus ? ` <span class="dim">${midiStatus}</span>` : '');
    }
    document.querySelectorAll('[data-step]').forEach(b => b.onclick = () => {
      const k = b.dataset.step, d = +b.dataset.d;
      S.midi[k] = ((S.midi[k] - 1 + d + 16) % 16) + 1; midimap();
    });
    $('pg-anynote').onclick = () => { S.midi.any = !S.midi.any; $('pg-anynote').setAttribute('aria-pressed', String(S.midi.any)); midimap(); };
    let midiStatus = '';
    const led = $('pg-midi').querySelector('.led');
    function blink() { led.classList.remove('on'); void led.offsetWidth; led.classList.add('on'); }
    function onMidi(e) {
      const [st, d1, d2] = e.data; if (st >= 0xf0) return;
      if ((st & 0x0f) !== midiChannel()) return;
      const type = st & 0xf0;
      if (type === 0x90 && d2 > 0 && (S.midi.any || d1 === NOTES[S.midi.track - 1])) { blink(); trig(); }
      else if (type === 0xb0) {
        const i = d1 - CC[(S.midi.track - 1) % 4];
        if (i >= 0 && i < 8 && i < S.kernel.params.length) { blink(); $('pg-params').children[i].setValue(d2); }
      }
    }
    $('pg-midi').onclick = async () => {
      if (S.midi.on) return;
      if (!navigator.requestMIDIAccess) { midiStatus = 'Web MIDI is not available in this browser.'; midimap(); return; }
      try {
        const access = await navigator.requestMIDIAccess();
        const bind = () => {
          const ins = [...access.inputs.values()];
          ins.forEach(i => i.onmidimessage = onMidi);
          midiStatus = ins.length ? 'Inputs: ' + ins.map(i => i.name).join(', ') + '.' : 'No MIDI input connected.';
          midimap();
        };
        access.onstatechange = bind; bind();
        S.midi.on = true; $('pg-midi').setAttribute('aria-pressed', 'true');
      } catch (e) { midiStatus = 'MIDI access refused.'; midimap(); }
    };

    // ---- the scope ----
    const cv = $('pg-wave'), g = cv.getContext('2d');
    let flash = 0;
    function flashScope() { flash = performance.now(); }
    const data = new Float32Array(1024);
    function draw(now) {
      const dpr = devicePixelRatio || 1, W = cv.clientWidth, H = cv.clientHeight;
      if (cv.width !== Math.round(W * dpr)) { cv.width = Math.round(W * dpr); cv.height = Math.round(H * dpr); }
      const k = Math.max(2, Math.round(3 * dpr)), cols = Math.floor(cv.width / k), rowsN = Math.floor(cv.height / k);
      g.clearRect(0, 0, cv.width, cv.height);
      g.fillStyle = 'rgba(48,185,219,.08)';
      for (let x = 0; x < cols; x += 8) g.fillRect(x * k, 0, 1, cv.height);
      for (let y = 0; y < rowsN; y += 8) g.fillRect(0, y * k, cv.width, 1);
      let live = false;
      if (analyser) { analyser.getFloatTimeDomainData(data); live = data.some(v => Math.abs(v) > 1e-4); }
      const mid = rowsN / 2;
      let prev = null;
      for (let c = 0; c < cols; c++) {
        let v = 0;
        if (live) v = data[Math.floor(c / cols * data.length)] * 1.6;
        else if (!reduced) {
          const t = now / 1000, f = Math.max(0, 1 - (now - flash) / 1500);
          v = Math.sin(c / cols * Math.PI * 4 + t * 1.2) * (0.04 + 0.5 * f) * Math.exp(-c / cols * (f ? 2 : 0));
        }
        const y = Math.round(mid - v * mid * 0.9);
        const from = prev == null ? y : prev, lo = Math.min(from, y), hi = Math.max(from, y);
        g.fillStyle = live ? (c % 2 ? '#56E0FE' : '#6BE6FF') : 'rgba(86,224,254,.45)';
        for (let r = lo; r <= hi; r++) g.fillRect(c * k, r * k, k - (k > 2 ? 1 : 0), k - (k > 2 ? 1 : 0));
        prev = y;
      }
      if (visible) requestAnimationFrame(draw);
    }
    new IntersectionObserver(es => {
      const was = visible; visible = es[0].isIntersecting;
      if (visible && !was) { requestAnimationFrame(draw); if (served && !results[S.kernel.id]) checkAll(); }
    }, { rootMargin: '200px' }).observe(root);

    S.settings = S.kernel.defaults.slice();
    kernels(); picker(); params(); rows(); midimap();
    if (window.fsdsp56kTerminal && window.fsdsp56kTerminal.select) window.fsdsp56kTerminal.select(S.kernel.id);   // the terminal shows the kernel's session
    window.__playground = { S, results, check, checkAll, trig, setLive, renderPhrase, selectKernel: id => selectKernel(KERNELS.find(k => k.id === id)),
      openReferenced: id => openReferenced(CATALOGUE.find(m => m.id === id)), catalogue: () => CATALOGUE };
  }
  // with nothing in dist: the section says so, and how to fill it (the file drop stays)
  function empty(root) {
    for (const e of root.querySelectorAll('.pg-rack, .pg-screen, .pg-match')) e.hidden = true;
    const pg = root.querySelector('.pg');
    if (pg) pg.classList.add('pg-bare');
    if (pg) pg.insertAdjacentHTML('beforebegin', '<div class="pg-empty"><b>No playground artefacts in this copy.</b> The kernels and runtimes go in <code>dist/playground</code>; <code>dotnet fsi build.fsx playground</code> lists how each is produced, and <code>dotnet fsi build.fsx playground-manifest</code> records what is there.</div>');
  }

  // ---- a dropped or picked machine file: read here, never uploaded. package.js tells its kind (and unpacks a SysEx
  // as `md unsyx` does); the bytes go to the DSP56300 runtime's package entry point (manifest:
  // runtimes.dsp56300.package, dsp56300/package.mjs: loadPackage(bytes, info)), which places the package on the
  // track harness; the result is a kernel like the others, played on the DSP56300 emulator only ----
  const kb = n => n < 1024 ? n + ' B' : (n / 1024).toFixed(n < 10240 ? 1 : 0) + ' KB';
  const gradeClass = g => !g ? 'none' : ({ match: 'ok', mismatch: 'bad', none: 'none' })[g.status] || 'na';
  const gradeDot = g => `<i class="gdot ${gradeClass(g)}" aria-hidden="true"></i>`;
  const gradeChip = g => !g || g.status === 'none' ? 'no F# kernel yet' :
    g.status === 'match' ? `✓ F# = original, word for word (${fmt(g.blocks)} blocks)` :
    g.status === 'mismatch' ? `✗ F# differs from the original from block ${fmt(g.first.block)}` : g.status + ': ' + g.detail;
  const gradeLine = g => 'grade: ' + gradeChip(g) + (g && g.fsharp ? ' · ' + gradeCycles(g) : '');
  const gradeCycles = g => !g || !g.fsharp ? '' : `render cycles a block: F# ${fmt(Math.round(g.fsharp.mean))} (worst ${fmt(g.fsharp.worst)})` +
    (g.original ? ` · original ${fmt(Math.round(g.original.mean))} (worst ${fmt(g.original.worst)})` : '');
  const esc = t => String(t).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
  let packageEntry = null;
  function packageRuntime() {
    const r = MANIFEST && MANIFEST.runtimes && MANIFEST.runtimes.dsp56300;
    if (!r || !r.package) return Promise.resolve({ why: 'runtime not available yet' });
    if (!/^https?:/.test(location.protocol)) return Promise.resolve({ why: 'runtime not available from disk (needs http)' });
    return packageEntry || (packageEntry = import(new URL(DIST + r.package, location.href).href)
      .then(m => typeof m.loadPackage === 'function' ? { load: m.loadPackage } : { why: 'runtime not available yet' }, () => ({ why: 'runtime not available yet' })));
  }
  api.packageRuntime = packageRuntime;
  function files(root) {
    const zone = $('pg-drop'), info = $('pg-file-info'), input = $('pg-file');
    if (!zone || !info || !window.fsdsp56kPackage) return;
    async function take(file) {
      if (!file) return;
      const bytes = new Uint8Array(await file.arrayBuffer());
      const r = window.fsdsp56kPackage.inspect(bytes, file.name);
      const kind = { mds: 'MDS package', sysex: 'SysEx dump', unknown: 'not a machine file' }[r.kind];
      const lines = r.packages.filter(p => !p.error).map(p => `<li>${p.slot ? 'slot ' + p.slot + ' · ' : ''}${kb(p.bytes.length)} package · ${p.words.toLocaleString('en')} program words · ${p.maxCycles.toLocaleString('en')} cycles a block declared</li>`);
      info.hidden = false;
      info.innerHTML = `<div class="pg-file-name"><b>${esc(file.name)}</b><span>${kb(bytes.length)}</span><span class="kind ${r.kind}">${kind}${r.kind === 'sysex' && r.packages.length ? ' · ' + r.packages.length + (r.packages.length > 1 ? ' machines' : ' machine') : ''}</span></div>` +
        (lines.length ? `<ul>${lines.join('')}</ul>` : '') +
        (r.error ? `<p class="pg-file-error">${esc(r.error)}</p>` : '') +
        `<p class="pg-file-state"><span class="state na" id="pg-file-rt">…</span> DSP56300 (wasm)</p>`;
      const slot = $('pg-file-rt');
      if (r.error || !r.packages.length) { slot.textContent = 'nothing to play'; return; }
      const rt = await packageRuntime();
      if (!rt.load) { slot.textContent = rt.why; return; }
      try {
        slot.textContent = 'loading…';
        const k = await rt.load(r.packages[0].bytes, { name: file.name, kind: r.kind, slot: r.packages[0].slot, packages: r.packages });
        slot.className = 'state ok'; slot.textContent = 'loaded · in the kernels as ' + k.title + ' (DSP56300 emulator only)';
        if (api.addKernel) api.addKernel(k);
      } catch (e) { slot.textContent = 'could not load: ' + e.message; }
    }
    input.addEventListener('change', () => { take(input.files[0]); input.value = ''; });
    let depth = 0;
    const has = e => e.dataTransfer && [...e.dataTransfer.types].includes('Files');
    root.addEventListener('dragenter', e => { if (!has(e)) return; e.preventDefault(); depth++; root.classList.add('dragging'); });
    root.addEventListener('dragover', e => { if (has(e)) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } });
    root.addEventListener('dragleave', () => { if (--depth <= 0) { depth = 0; root.classList.remove('dragging'); } });
    root.addEventListener('drop', e => { if (!has(e)) return; e.preventDefault(); depth = 0; root.classList.remove('dragging'); take(e.dataTransfer.files[0]); });
    api.take = take;   // for tests: take(new File([...], name))
  }

  // the loader: the manifest, then each kernel's data script (classic scripts, so this works from disk too)
  async function load() {
    if (!await script(DIST + 'manifest.js') || !MANIFEST) { MANIFEST = { kernels: [], runtimes: {} }; }
    for (const k of MANIFEST.kernels || []) {
      const before = KERNELS.length;
      if (await script(DIST + k.data) && KERNELS.length > before) { const x = KERNELS[KERNELS.length - 1]; x.reference = k.reference || {}; x.checks = k.runtimes || null; }
    }
    if (MANIFEST.catalogue) await script(DIST + MANIFEST.catalogue);
    await script(DIST + 'terminal/transcripts.js');   // the terminal panel's sessions (optional)
    start();
    if ($('playground')) files($('playground'));
  }
  if (document.readyState === 'complete') load(); else addEventListener('load', load);
})();
