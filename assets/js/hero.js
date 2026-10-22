// fsdsp56k splash: the hero logo switching between its smooth rendering and its pixel rendering, with a few
// transitions. Both come from the art project's logo (one F# model, LogoView/LogoVector/LogoPixel, compiled by Fable
// into assets/logo/fsdsp56k-logo.mjs, which announces window.fsdsp56kLogo): mounted in #stage it draws the vector
// logo, and its 256x256 pixel art into a canvas this file copies from. Until the module has run (or without modules,
// e.g. from disk) #stage shows the exported house-rest.svg, and the pixel side is the exported house-rest.png.
// The smooth logo is never redrawn: an overlay canvas shows the pixel logo through a mask that grows or shrinks, so
// hover, clicks and the glow keep running underneath. The loop runs only while a transition plays or the logo
// animates under the pointer; at rest everything stops.
(() => {
  const stage = document.getElementById('stage');
  if (!stage) return;
  const root = document.documentElement;
  const TAU = Math.PI * 2;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const smoothstep = u => u * u * (3 - 2 * u);
  const canvas = (w, h = w) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ---- the pixel source: 256x256 ----
  const low = canvas(256), lg = low.getContext('2d');
  let source = 'none', live = null;
  // the live logo's pixel art (its canvas is drawn only while the logo is switched to pixel)
  function copyLive() {
    if (!live || !live.pixels || !live.pixels.width) return;
    lg.imageSmoothingEnabled = false; lg.clearRect(0, 0, 256, 256); lg.drawImage(live.pixels, 0, 0, 256, 256);
  }
  // the exported pixel art, 512x512 (2x): halved exactly, no smoothing
  const still = new Image();
  still.onload = () => {
    if (live) return;
    lg.imageSmoothingEnabled = false; lg.clearRect(0, 0, 256, 256); lg.drawImage(still, 0, 0, 256, 256);
    source = 'pixel art, exported'; redrawIfShown(); sync();
  };
  still.src = 'assets/logo/house-rest.png';
  function attach() {
    if (live || !window.fsdsp56kLogo) return;
    try {
      live = window.fsdsp56kLogo.mount(stage);
      stage.appendChild(out);                         // the overlay stays above the mounted logo
      const img = stage.querySelector('.logo-still');
      requestAnimationFrame(() => { if (img) img.remove(); });
      stage.dataset.logo = 'live';
      source = 'F# pixel art, live';
      if (S.shown === 'pixel' || tr) { live.switchTo('pixel'); copyLive(); redrawIfShown(); }
      sync();
    } catch (e) { console.warn('logo module failed', e); live = null; }
  }

  // ---- the overlay ----
  const out = canvas(1); out.id = 'pix'; out.setAttribute('aria-hidden', 'true'); stage.appendChild(out);
  const og = out.getContext('2d');
  const tmp = canvas(1), tg = tmp.getContext('2d');
  let size = 0;
  function resize() {
    const s = Math.min(1024, Math.round(stage.clientWidth * (window.devicePixelRatio || 1)));
    if (!s || s === size) return;
    size = s; out.width = out.height = tmp.width = tmp.height = s;
    redrawIfShown();
  }
  new ResizeObserver(resize).observe(stage);
  const drawPixel = g => { g.imageSmoothingEnabled = false; g.drawImage(low, 0, 0, size, size); };

  // ---- the transitions (a subset of logo-pixel.js's 18): an order field over a grid, the pixel logo shown where
  // the progress has passed it (towards pixel) or not yet (towards smooth); the others draw themselves ----
  const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
  const bayer8 = (() => {
    let m = [[0]];
    while (m.length < 8) {
      const s = m.length, q = [];
      for (let y = 0; y < 2 * s; y++) { q.push([]); for (let x = 0; x < 2 * s; x++) q[y].push(4 * m[y % s][x % s] + [[0, 2], [3, 1]][Math.floor(y / s)][Math.floor(x / s)]); }
      m = q;
    }
    return m.map(r => r.map(v => (v + 0.5) / 64));
  })();
  const FX = {
    'blinds':    { label: 'blinds',      grid: 128, f: (x, y) => 0.75 * (y % 8) / 8 + 0.25 * Math.floor(y / 8) / 16 },
    'iris':      { label: 'iris',        grid: 256, f: (x, y) => Math.hypot(x - 127.5, y - 127.5) / 181 },
    'dither':    { label: 'dither',      grid: 128, f: (x, y) => bayer8[y % 8][x % 8] },
    'plasma':    { label: 'plasma',      grid: 128, f: (x, y) => 0.5 + 0.125 * (Math.sin(x / 9) + Math.sin(y / 13) + Math.sin((x + y) / 17) + Math.sin(Math.hypot(x - 64, y - 64) / 8)) },
    'interlace': { label: 'interlace',   grid: 128, f: (x, y) => { const p = y % 8 === 0 ? 0 : y % 8 === 4 ? 1 : y % 4 === 2 ? 2 : 3; return (p + y / 128) / 4; } },
    'diamond':   { label: 'diamond',     grid: 48,  f: (x, y) => (Math.abs(x - 23.5) + Math.abs(y - 23.5)) / 47 },
    'parallax':  { label: 'parallax',    draw: parallax },
    'raster':    { label: 'raster bars', draw: raster },
    'melt':      { label: 'melt',        draw: melt }
  };
  const PLAYLIST = ['blinds', 'parallax', 'iris', 'raster', 'dither', 'melt', 'plasma', 'interlace', 'diamond'];
  const fields = new Map();
  function fieldOf(name) {
    if (fields.has(name)) return fields.get(name);
    const { grid, f } = FX[name], v = new Float32Array(grid * grid);
    let lo = Infinity, hi = -Infinity;
    for (let y = 0; y < grid; y++) for (let x = 0; x < grid; x++) { const a = f(x, y); v[y * grid + x] = a; lo = Math.min(lo, a); hi = Math.max(hi, a); }
    for (let i = 0; i < v.length; i++) v[i] = (v[i] - lo) / (hi - lo || 1);
    const c = canvas(grid), g = c.getContext('2d');
    const F = { grid, v, c, g, img: g.createImageData(grid, grid) };
    fields.set(name, F);
    return F;
  }
  // the pixel logo where the mask is opaque
  function maskedPixel(mask) {
    og.clearRect(0, 0, size, size);
    drawPixel(og);
    og.globalCompositeOperation = 'destination-in';
    og.imageSmoothingEnabled = false;
    og.drawImage(mask, 0, 0, size, size);
    og.globalCompositeOperation = 'source-over';
  }
  function field(name, toPixel, t) {
    const F = fieldOf(name), d = F.img.data;
    for (let i = 0; i < F.v.length; i++) d[4 * i + 3] = (F.v[i] < t) === toPixel ? 255 : 0;
    F.g.putImageData(F.img, 0, 0);
    maskedPixel(F.c);
  }
  // bands of the pixel logo sliding in from both sides (towards pixel) or off them (towards smooth), staggered,
  // snapped to the pixel grid
  function parallax(toPixel, t) {
    const u0 = toPixel ? 1 - t : t, n = 12, cell = size / 256, snap = v => Math.round(v / cell) * cell;
    tg.clearRect(0, 0, size, size); drawPixel(tg);
    og.clearRect(0, 0, size, size);
    for (let i = 0; i < n; i++) {
      const y0 = snap(i * size / n), y1 = snap((i + 1) * size / n);
      const speed = 1 + 1.5 * ((i * 7 + 3) % n) / n, lag = 0.12 * ((i * 3) % 4) / 3;
      const u = clamp((u0 - lag) / (1 - lag) * speed, 0, 1);
      const dx = snap((i % 2 ? 1 : -1) * size * 1.05 * u * u);
      if (Math.abs(dx) < size) og.drawImage(tmp, 0, y0, size, y1 - y0, dx, y0, size, y1 - y0);
    }
  }
  // a copper of raster bars falling down the logo, the new rendering above it
  function raster(toPixel, t) {
    const bar = size / 14, front = -2 * bar + t * (size + 4 * bar), h = clamp(Math.round(front), 0, size);
    og.clearRect(0, 0, size, size);
    tg.clearRect(0, 0, size, size); drawPixel(tg);
    if (toPixel) { if (h > 0) og.drawImage(tmp, 0, 0, size, h, 0, 0, size, h); }
    else if (h < size) og.drawImage(tmp, 0, h, size, size - h, 0, h, size, size - h);
    const pal = ['#FFB020', '#fff3a0', '#56E0FE', '#30B9DB', '#1E6189'];
    for (let b = 0; b < 5; b++) {
      const y = front + Math.sin(t * TAU * 2 + b * 0.9) * bar * 1.2 - bar / 2;
      const gr = og.createLinearGradient(0, y, 0, y + bar);
      gr.addColorStop(0, 'rgba(0,0,0,0)'); gr.addColorStop(0.5, pal[b]); gr.addColorStop(1, 'rgba(0,0,0,0)');
      og.fillStyle = gr;
      og.fillRect(0, Math.round(y / 4) * 4, size, bar);
    }
  }
  // Doom's wipe: the pixel logo's columns fall in from the top (towards pixel) or slide down and away (towards smooth)
  const COLUMNS = 64;
  let delays = [];
  function melt(toPixel, t) {
    if (delays.length !== COLUMNS) {
      let d = Math.random() * 0.15; delays = [];
      for (let i = 0; i < COLUMNS; i++) { d = clamp(d + (Math.random() - 0.5) * 0.06, 0, 0.3); delays.push(d); }
    }
    tg.clearRect(0, 0, size, size); drawPixel(tg);
    og.clearRect(0, 0, size, size);
    const w = size / COLUMNS;
    for (let i = 0; i < COLUMNS; i++) {
      const u = clamp((t - delays[i]) / 0.7, 0, 1);
      const x = Math.floor(i * w), cw = Math.floor((i + 1) * w) - x;
      if (toPixel) { const dy = Math.round(size * (1 - u) * (1 - u)); if (dy < size) og.drawImage(tmp, x, dy, cw, size - dy, x, 0, cw, size - dy); }
      else { const dy = Math.round(size * u * u); if (dy < size) og.drawImage(tmp, x, 0, cw, size - dy, x, dy, cw, size - dy); }
    }
  }

  // ---- the state and the loop ----
  const S = { shown: 'smooth', mode: reduced ? 'smooth' : 'auto', duration: 1500, next: 0 };
  let tr = null, frame = 0, held = null;
  const label = document.getElementById('fx-label');
  const setPage = pixel => {
    root.classList.toggle('px-mode', pixel);
    stage.dataset.shown = pixel ? 'pixel' : 'smooth';
  };
  function redrawIfShown() {
    if (S.shown === 'pixel' && !tr && size) { og.clearRect(0, 0, size, size); drawPixel(og); }
  }
  function frameAt(t) {
    const T = FX[tr.name], toPixel = tr.to === 'pixel';
    if (T.draw) T.draw(toPixel, t); else field(tr.name, toPixel, t);
    // the page's lettering flips at the middle of the transition
    setPage(toPixel ? t >= 0.5 : t < 0.5);
  }
  function tick(now) {
    frame = 0;
    if (tr) {
      const u = clamp((now - tr.start) / S.duration, 0, 1);
      frameAt(held === null ? smoothstep(u) : held);
      if (u >= 1 && held === null) {
        S.shown = tr.to; tr = null; delays = [];
        if (S.shown === 'smooth') { og.clearRect(0, 0, size, size); if (live) live.switchTo('vector'); } else redrawIfShown();
        out.style.visibility = S.shown === 'pixel' ? 'visible' : 'hidden';
        sync();
      }
    }
    const moving = live && (pointer.over || now < pointer.until);
    if (moving && (S.shown === 'pixel' || tr)) { copyLive(); redrawIfShown(); }
    if (tr || moving) frame = requestAnimationFrame(tick);
  }
  const wake = () => { if (!frame) frame = requestAnimationFrame(tick); };
  let fxIndex = 0;
  function play(to, name) {
    if (tr || !size) return false;
    to = to || (S.shown === 'pixel' ? 'smooth' : 'pixel');
    if (to === S.shown) return false;
    name = name || PLAYLIST[fxIndex++ % PLAYLIST.length];
    tr = { name, to, start: performance.now() };
    if (live && to === 'pixel') { live.switchTo('pixel'); copyLive(); }
    out.style.visibility = 'visible';
    if (label) label.textContent = FX[name].label;
    if (reduced) { tr.start = -1e9; }
    sync(); wake();
    return true;
  }

  // ---- auto: smooth for a while, a transition, pixel for a while, a transition back ----
  let autoTimer = 0;
  function schedule() {
    clearTimeout(autoTimer);
    if (S.mode !== 'auto') return;
    const wait = S.shown === 'smooth' ? 5200 : 3800;
    autoTimer = setTimeout(() => { if (!document.hidden && visible) play(); schedule(); }, wait + S.duration);
  }
  let visible = true;
  new IntersectionObserver(es => { visible = es[0].isIntersecting; }).observe(stage);
  function setMode(mode) {
    S.mode = mode;
    if (mode === 'smooth' || mode === 'pixel') play(mode);
    schedule(); sync();
  }

  // ---- the pointer: the live logo animates itself (hover, clicks, comets); while it moves and the pixel side shows,
  // the loop copies its pixel art, for as long as the pointer is over it and a few seconds after ----
  const pointer = { over: false, until: 0 };
  if (!reduced) {
    stage.addEventListener('pointerenter', () => { pointer.over = true; wake(); });
    stage.addEventListener('pointerleave', () => { pointer.over = false; pointer.until = performance.now() + 4000; wake(); });
    stage.addEventListener('click', () => { pointer.until = performance.now() + 4000; wake(); });
  }

  // ---- the chips under the logo ----
  const chips = document.querySelectorAll('[data-hero-mode]');
  chips.forEach(b => b.addEventListener('click', () => setMode(b.dataset.heroMode)));
  const fxChip = document.getElementById('fx-play');
  if (fxChip) fxChip.addEventListener('click', () => { if (S.mode !== 'auto') S.mode = S.shown === 'pixel' ? 'smooth' : 'pixel'; play(); sync(); });
  function sync() {
    chips.forEach(b => b.setAttribute('aria-pressed', String(b.dataset.heroMode === S.mode)));
    if (label && !tr) label.textContent = S.shown === 'pixel' ? source : 'vector';
  }

  // both letterings of every .pxable (the headline, the kicker) are laid out at once, stacked in one
  // grid cell (splash.css, .px-stack): switching only changes which is visible, so nothing on the page moves
  for (const el of document.querySelectorAll('.pxable')) {
    if (el.querySelector('.px-stack')) continue;
    const html = el.innerHTML;
    el.innerHTML = `<span class="px-stack"><span class="px-s">${html}</span><span class="px-p" aria-hidden="true">${html}</span></span>`;
  }

  // start: smooth, the first switch soon after load (or at once with ?hero=pixel, for screenshots)
  out.style.visibility = 'hidden';
  setPage(false);
  const q = new URLSearchParams(location.search);
  requestAnimationFrame(() => {
    resize();
    if (q.get('hero') === 'pixel') { S.mode = 'pixel'; tr = { name: 'blinds', to: 'pixel', start: -1e9 }; if (live) { live.switchTo('pixel'); copyLive(); } out.style.visibility = 'visible'; wake(); }
    else if (S.mode === 'auto') { clearTimeout(autoTimer); autoTimer = setTimeout(() => { play(); schedule(); }, 2600); }
    sync();
  });

  // for a test: hold(t) freezes a playing transition at the eased progress t, hold(null) lets it go
  addEventListener('fsdsp56k-logo', attach);
  attach();

  window.__hero = { S, play, live: () => !!live, setMode, fx: Object.keys(FX), source: () => source,
                    hold: t => { held = t; wake(); }, state: () => ({ shown: S.shown, mode: S.mode, transition: tr && tr.name, source }) };
})();
