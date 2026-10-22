// fsdsp56k splash: the credits scroller, in the project's own 5x7 pixel font (window.fsdsp56kFont, the font of the pixel
// wordmark and the README lettering), coloured along the logo's cyan-to-yellow ramp. Modes take turns: sine, zoom,
// sine, car, ... The sine scroller is one pass of data-text, its speed and its wave (frequency, phase speed,
// amplitude) drifting smoothly all the time; the credits (data-credits) come one line at a time, either zooming in,
// holding and fleeing past the viewer ("zoom"), or racing in from the right, braking with a little overshoot,
// holding, and accelerating off to the left ("car"). Hovering the band eases the scroll down, makes the wave wobble
// more, and adds rainbow copper bars bouncing up and down, their palettes cycling. Everything is drawn inside the
// band's canvas and clipped to it. Runs only while on screen; still and centred with reduced motion.
// ?scroller=zoom|car|sine starts in that mode (for screenshots); window.__scroller is a hook for tests.
(() => {
  const cv = document.getElementById('scroller');
  const font = window.fsdsp56kFont;
  if (!cv || !font) return;
  const glyphs = new Map();
  let key = null, rows = [];
  for (const line of font.glyphs.split('\n')) {
    if (line.startsWith('= ')) { if (key !== null) glyphs.set(key, rows); key = line.slice(2) === 'space' ? ' ' : line.slice(2); rows = []; }
    else if (line) rows.push(line);
  }
  if (key !== null) glyphs.set(key, rows);
  const GH = font.height || 7;
  // a text's columns: each a list of lit rows
  function columns(text) {
    const cols = [];
    for (const ch of [...text]) {
      const g = glyphs.get(ch) || glyphs.get(ch.toLowerCase()) || glyphs.get('?') || glyphs.get(' ');
      if (!g) continue;
      for (let x = 0; x < g[0].length; x++) cols.push(g.map(r => r[x] === '#'));
      for (let s = 0; s < font.spacing; s++) cols.push([]);
    }
    while (cols.length && !cols[cols.length - 1].length) cols.pop();
    return cols;
  }
  const scroll = columns(cv.dataset.text || 'fs·dsp·56k');
  for (let s = 0; s < 3 + font.spacing; s++) scroll.push([]);
  const credits = (cv.dataset.credits || '').split('|').map(s => s.trim()).filter(Boolean).map(t => ({ text: t, cols: columns(t) }));
  const RAMP = ['#1788A4', '#30B9DB', '#56E0FE', '#6BE6FF', '#fff3a0', '#FFB020', '#fff3a0', '#6BE6FF', '#56E0FE', '#30B9DB'];
  const g = cv.getContext('2d');
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const q = new URLSearchParams(location.search).get('scroller');
  // timings (ms) of a credit line: arrive, hold, flee (zoom), and arrive, hold, leave (car)
  const ARRIVE = 760, HOLD = 1250, FLEE = 260, LINE = ARRIVE + HOLD + FLEE;
  const C_IN = 560, C_HOLD = 1150, C_OUT = 380, C_LINE = C_IN + C_HOLD + C_OUT;
  const SPEED = 0.2;                 // sine scroller, CSS px a ms, on average
  const CYCLE = ['sine', 'zoom', 'sine', 'car'];
  const S = { mode: (q === 'zoom' || q === 'car') && credits.length ? q : 'sine', since: 0, x0: 0, phase: 0, hover: 0, hoverTarget: 0, frozen: null,
              turn: q === 'car' ? 3 : q === 'zoom' ? 1 : 0 };
  // the sine scroller's slow drifts, at time t (s): speed, wave frequency, phase speed and amplitude, each a sum of
  // two slow sines (so they never repeat quickly and never jump); hover eases the speed down and the wobble up
  const drift = t => ({
    speed: 1 + 0.42 * Math.sin(t * 0.43) + 0.22 * Math.sin(t * 1.07 + 1.3),        // 0.36 .. 1.64
    freq: 2.6 * (1 + 0.3 * Math.sin(t * 0.21 + 0.4) + 0.12 * Math.sin(t * 0.67)),
    wob: 2.4 * (1 + 0.35 * Math.sin(t * 0.29 + 2.1) + 0.15 * Math.sin(t * 0.83)),
    amp: 0.72 + 0.16 * Math.sin(t * 0.37 + 0.9) + 0.08 * Math.sin(t * 0.97) });
  let k = 3, frame = 0, on = false, last = 0;

  function resize() {
    const dpr = window.devicePixelRatio || 1;
    cv.width = Math.round(cv.clientWidth * dpr); cv.height = Math.round(cv.clientHeight * dpr);
    k = Math.max(2, Math.round(cv.clientHeight / 26)) * dpr;    // one font pixel, in device pixels
    draw(performance.now());
  }
  function setMode(mode, now) { S.mode = mode; S.since = now; S.x0 = 0; }
  function next(now) { S.turn = (S.turn + 1) % CYCLE.length; setMode(CYCLE[S.turn], now); }

  // rainbow copper bars bouncing up and down inside the band while the pointer is over it: each a vertical gradient
  // (dark edges, a bright core), hues offset bar to bar, the palette rotating as they bounce
  function rasters(t, W, H) {
    if (S.hover <= 0.01) return;
    const n = 5, bar = Math.max(8, Math.round(H / 7));
    g.save();
    g.globalCompositeOperation = 'lighter';
    for (let i = 0; i < n; i++) {
      const y = (H - bar) / 2 + Math.sin(t * (1.25 + i * 0.29) + i * 1.1) * (H - bar) / 2;
      const hue = (t * 70 + i * 60) % 360;
      const grad = g.createLinearGradient(0, y, 0, y + bar);
      const a = 0.7 * S.hover;
      const stops = [0, 0.2, 0.42, 0.5, 0.58, 0.8, 1];
      for (const st of stops) {
        const d = Math.abs(st - 0.5) * 2;                        // 0 at the core, 1 at the edges
        const h = (hue + (st - 0.5) * 50 + t * 40) % 360;        // the palette cycles across the bar too
        const l = 66 - 36 * d, al = a * (1 - d * d * d);
        grad.addColorStop(st, `hsla(${h.toFixed(1)},100%,${l.toFixed(1)}%,${al.toFixed(3)})`);
      }
      g.fillStyle = grad;
      g.fillRect(0, Math.round(y), W, bar);
      g.fillStyle = `rgba(0,0,0,${0.16 * S.hover})`;          // scan lines, as on a CRT
      for (let yy = Math.round(y); yy < y + bar; yy += 2 * Math.max(1, Math.round(k / 3))) g.fillRect(0, yy, W, 1);
    }
    g.restore();
  }

  function glyphCols(cols, x, yAt, px, colourAt, W, alpha, sx = 1) {
    g.globalAlpha = alpha;
    for (let i = 0; i < cols.length; i++) {
      const cx = Math.round(x + i * px * sx);
      if (cx < -px * sx || cx > W) continue;
      const col = cols[i];
      if (!col.length) continue;
      const y = yAt(cx);
      const c = colourAt(cx);
      const s = Math.max(1, Math.ceil(px)), sw = Math.max(1, Math.ceil(px * sx));
      for (let r = 0; r < col.length; r++) {
        if (!col[r]) continue;
        g.fillStyle = 'rgba(0,0,0,.55)'; g.fillRect(cx + Math.max(1, px / 3), Math.round(y + r * px + Math.max(1, px / 3)), sw, s);
        g.fillStyle = c; g.fillRect(cx, Math.round(y + r * px), sw, s);
      }
    }
    g.globalAlpha = 1;
  }

  function draw(now) {
    const W = cv.width, H = cv.height;
    g.clearRect(0, 0, W, H);
    g.save();
    g.beginPath(); g.rect(0, 0, W, H); g.clip();          // nothing drawn outside the band
    const t = now / 1000;
    rasters(t, W, H);
    const margin = k, tall = (GH + 1) * k;                // the glyphs and their shadow
    const ramp = x => RAMP[Math.floor(((x / W) * RAMP.length + t * 3) % RAMP.length + RAMP.length) % RAMP.length];
    if (S.mode === 'zoom' && credits.length && !reduced) {
      const el = now - S.since, i = Math.floor(el / LINE);
      if (i >= credits.length) { next(now); g.restore(); return draw(now); }
      const u = el - i * LINE, line = credits[i];
      // the line's full size: as large as fits the band's height and 92% of its width
      const full = Math.min((H - 2 * margin) / (GH + 1), W * 0.92 / line.cols.length);
      let scale, alpha;
      if (u < ARRIVE) { const p = u / ARRIVE, e = 1 - Math.pow(1 - p, 3); scale = 0.08 + 0.92 * e; alpha = Math.min(1, p * 2.2); }
      else if (u < ARRIVE + HOLD) { scale = 1 + 0.025 * Math.sin((u - ARRIVE) / HOLD * Math.PI); alpha = 1; }
      else { const p = (u - ARRIVE - HOLD) / FLEE; scale = 1 + 3.2 * p * p; alpha = 1 - p; }
      const px = full * scale, w = line.cols.length * px;
      const x = (W - w) / 2, y = (H - GH * px) / 2;
      glyphCols(line.cols, x, () => y, px, ramp, W, Math.max(0, alpha));
    } else if (S.mode === 'car' && credits.length && !reduced) {
      const el = now - S.since, i = Math.floor(el / C_LINE);
      if (i >= credits.length) { next(now); g.restore(); return draw(now); }
      const u = el - i * C_LINE, line = credits[i];
      const px = Math.min((H - 2 * margin) / (GH + 1), W * 0.8 / line.cols.length);
      const w = line.cols.length * px, home = (W - w) / 2, y = (H - GH * px) / 2;
      // position and speed (px a ms): in from the right braking hard with a small overshoot (ease-out-back), then
      // away to the left accelerating (ease-in)
      let x, v;
      const back = p => { const c = 1.15; return 1 + (c + 1) * Math.pow(p - 1, 3) + c * Math.pow(p - 1, 2); };
      if (u < C_IN) {
        const p = u / C_IN, from = W + px * 4, dp = 0.002;
        x = from + (home - from) * back(p);
        v = (home - from) * (back(p + dp) - back(p)) / (dp * C_IN);
      } else if (u < C_IN + C_HOLD) { x = home; v = 0; }
      else {
        const p = (u - C_IN - C_HOLD) / C_OUT, to = -w - px * 8;
        x = home + (to - home) * p * p * p; v = (to - home) * 3 * p * p / C_OUT;
      }
      const sp = Math.min(1, Math.abs(v) / (W / 300));            // 0 still .. 1 flat out
      const sx = 1 + 0.9 * sp;                                     // letters stretched along the motion
      const skew = -Math.sign(v) * 0.35 * sp;                      // and leaning into it
      g.save();
      g.translate(0, y + GH * px); g.transform(1, 0, skew, 1, 0, 0); g.translate(0, -(y + GH * px));
      if (sp > 0.05) for (let gh = 3; gh >= 1; gh--)               // speed trails behind the car
        glyphCols(line.cols, x - Math.sign(v) * gh * px * 2.2 * sp, () => y, px, ramp, W, 0.18 * sp / gh, sx);
      glyphCols(line.cols, x, () => y, px, ramp, W, 1, sx);
      g.restore();
    } else {
      const span = scroll.length * k;
      const room = Math.max(0, (H - tall - 2 * margin) / 2), mid = (H - tall) / 2;
      const d = drift(t);
      const amp = room * Math.min(1, d.amp + 0.3 * S.hover);
      const freq = d.freq * (1 + 0.25 * S.hover);
      const start = reduced ? (W - span) / 2 : -S.x0;
      const yAt = x => mid + (reduced ? 0 : Math.sin(x / W * freq * Math.PI + S.phase) * amp);
      for (let rep = reduced ? 0 : -1; rep <= (reduced ? 0 : Math.ceil(W / span) + 1); rep++) {
        const base = start + rep * span;
        if (base > W || base + span < 0) continue;
        glyphCols(scroll, base, yAt, k, ramp, W, 1);
      }
    }
    g.restore();
  }
  function tick(now) {
    frame = 0;
    const dt = Math.min(50, now - (last || now)); last = now;
    // hover eases in and out (a critically damped follow, no snapping)
    S.hover += (S.hoverTarget - S.hover) * (1 - Math.exp(-dt / 320));
    if (S.mode === 'sine') {
      const d = drift(now / 1000);
      // integrated, so a change of speed or of wobble never makes the text jump
      S.x0 += dt * SPEED * d.speed * (1 - 0.5 * S.hover) * (window.devicePixelRatio || 1);
      S.phase += dt / 1000 * d.wob * (1 + 1.1 * S.hover);
      // one pass of the text, then the next mode
      if (S.x0 >= scroll.length * k + cv.width) { if (credits.length) next(now); else S.x0 = 0; }
    }
    if (S.frozen == null) draw(now); else draw(S.frozen);
    if (on && !reduced) frame = requestAnimationFrame(tick);
  }
  const band = cv.closest('.scroller-band') || cv;
  if (!reduced) {
    band.addEventListener('pointerenter', () => { S.hoverTarget = 1; });
    band.addEventListener('pointerleave', () => { S.hoverTarget = 0; });
  }
  new ResizeObserver(resize).observe(cv);
  new IntersectionObserver(es => {
    on = es[0].isIntersecting;
    if (on && !frame && !reduced) { last = 0; frame = requestAnimationFrame(tick); }
  }).observe(cv);
  S.since = performance.now();
  // for a test: freeze at a time in the current mode, set the hover level, or switch mode
  window.__scroller = { S, setMode: m => { S.turn = Math.max(0, CYCLE.indexOf(m)); setMode(m, performance.now()); }, at: ms => { S.since = performance.now() - ms; },
                        x: px => { S.x0 = px; }, still: ms => { const n = performance.now(); S.since = n - ms; S.frozen = n; }, hover: v => { S.hover = S.hoverTarget = v; }, draw: () => draw(performance.now()) };
})();
