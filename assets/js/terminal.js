// fsdsp56k splash: the terminal panel, replaying real fsdsp56k sessions (dist/playground/terminal/transcripts.js,
// captured by `dotnet fsi build.fsx terminal-capture`). Deliberately small, and meant to be replaced:
//   - the data registers itself with window.fsdsp56kTerminal.transcripts(data); window.fsdsp56kTerminal.data holds it;
//   - the host is [data-terminal]. With data-terminal="builtin" this file draws it (chips, replay, the screen); with
//     data-terminal="external" it draws nothing and only announces, on the host:
//       `fsdsp56k:transcripts` (detail: the data), once it is loaded,
//       `terminalsession` (detail: a session id), when the page picks one (the playground's kernel follows here);
//     an external component renders the data and may fire `terminalsession` itself to say which one it shows.
// Replay: the command typed out, then each line at its captured time (clamped to a gentle cadence). With reduced
// motion, the whole session at once.
// Looks: Plain (this file's screen, the default), Workbench and Workbench on tube: the art project's shell window
// (assets/console/fsdsp56k-console.mjs, imported when first picked) replaying the same sessions, ANSI codes and all.
(() => {
  const consoleUrl = new URL('../console/fsdsp56k-console.mjs', document.currentScript.src).href;
  const T = window.fsdsp56kTerminal = window.fsdsp56kTerminal || {};
  const hosts = () => [...document.querySelectorAll('[data-terminal]')];
  T.transcripts = data => {
    T.data = data;
    for (const h of hosts()) { h.dispatchEvent(new CustomEvent('fsdsp56k:transcripts', { detail: data })); if (h.dataset.terminal === 'builtin') builtin(h, data); }
  };
  T.select = id => { for (const h of hosts()) h.dispatchEvent(new CustomEvent('terminalsession', { detail: id })); };
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  // SGR codes to Copper classes (the CLI prints none when its output is captured, but the format allows them)
  const SGR = { 1: 'b', 2: 'dim', 31: 'red', 32: 'green', 33: 'amber', 34: 'blue', 35: 'magenta', 36: 'cyan', 90: 'dim', 91: 'red', 92: 'green', 93: 'amber', 96: 'cyan' };
  function line(l) {
    const el = document.createElement('div');
    el.className = 'tl ' + l.s;
    if (!l.spans) { el.textContent = l.text || '\u00a0'; return el; }
    let at = 0;
    for (const [a, b, codes] of l.spans) {
      if (a > at) el.append(l.text.slice(at, a));
      const sp = document.createElement('span');
      sp.className = codes.split(';').map(c => SGR[c] ? 'a-' + SGR[c] : '').join(' ');
      sp.textContent = l.text.slice(a, b);
      el.append(sp); at = b;
    }
    if (at < l.text.length) el.append(l.text.slice(at));
    return el;
  }
  function builtin(host, data) {
    host.innerHTML = '';
    const bar = document.createElement('div'); bar.className = 'term-bar';
    const chips = document.createElement('div'); chips.className = 'chips'; chips.setAttribute('role', 'group'); chips.setAttribute('aria-label', 'Session');
    const replay = document.createElement('button'); replay.type = 'button'; replay.className = 'chip term-replay'; replay.textContent = '↻ replay';
    const looks = document.createElement('div'); looks.className = 'chips term-looks'; looks.setAttribute('role', 'group'); looks.setAttribute('aria-label', 'Look');
    for (const [id, title] of [['plain', 'Plain'], ['workbench', 'Workbench'], ['tube', 'Workbench on tube']]) {
      const b = document.createElement('button'); b.type = 'button'; b.className = 'chip'; b.dataset.look = id; b.textContent = title;
      b.setAttribute('aria-pressed', String(id === 'plain'));
      b.onclick = () => pick(id);
      looks.append(b);
    }
    const tools = document.createElement('div'); tools.className = 'term-tools'; tools.append(looks, replay);
    bar.append(chips, tools);
    const view = document.createElement('div'); view.className = 'term-view';
    const screen = document.createElement('div'); screen.className = 'term-screen'; screen.setAttribute('aria-live', 'off');
    const shell = document.createElement('div'); shell.className = 'term-look'; shell.hidden = true; shell.setAttribute('aria-hidden', 'true');
    view.append(screen, shell);
    const foot = document.createElement('p'); foot.className = 'term-foot';
    foot.textContent = `Real output of fsdsp56k (toolchain ${data.toolchain}, captured ${data.captured}), replayed at the pace it was printed.`;
    host.append(bar, view, foot);
    let current = data.sessions[0], run = 0, visible = false, played = false;
    for (const s of data.sessions) {
      const b = document.createElement('button'); b.type = 'button'; b.className = 'chip'; b.dataset.session = s.id; b.textContent = s.title;
      b.onclick = () => host.dispatchEvent(new CustomEvent('terminalsession', { detail: s.id }));
      chips.append(b);
    }
    const wait = ms => new Promise(r => setTimeout(r, ms));
    async function play(s) {
      const me = ++run;
      screen.innerHTML = '';
      for (const b of chips.children) b.setAttribute('aria-pressed', String(b.dataset.session === s.id));
      const prompt = cmd => { const p = document.createElement('div'); p.className = 'tl cmd'; p.innerHTML = '<span class="ps">$ </span><span class="typed"></span>'; screen.append(p); return p.lastChild; };
      for (const step of s.steps) {
        const typed = prompt(step.command);
        if (reduced) typed.textContent = step.command;
        else for (let i = 1; i <= step.command.length; i++) { if (me !== run) return; typed.textContent = step.command.slice(0, i); await wait(i < 9 ? 45 : 14); }
        if (!reduced) await wait(260);
        let last = 0;
        for (const l of step.lines) {
          if (me !== run) return;
          if (!reduced) await wait(Math.max(35, Math.min(700, l.t - last)));
          last = l.t;
          screen.append(line(l)); screen.scrollTop = screen.scrollHeight;
        }
        if (step.exit) screen.append(line({ s: 'err', text: `(exit ${step.exit})` }));
        if (!reduced) await wait(650);
      }
      if (me !== run) return;
      const end = prompt(''); end.classList.add('cursor');
    }
    // the Workbench looks: a console per session, mounted when first shown, at 2 page pixels a pixel, as many columns
    // as the panel is wide and the rows the plain screen's height holds (so switching looks moves nothing)
    let look = 'plain', consoles = new Map(), cols = 0, loading = null;
    const steps = s => s.steps.flatMap(step => {
      let last = 0;
      const lines = step.lines.flatMap(l => {
        const gap = Math.min(0.7, Math.max(0, (l.t - last) / 1000) - 0.05); last = l.t;
        const text = l.spans ? l.spans.reduceRight((t, [a, b, codes]) => t.slice(0, a) + `\u001b[${codes}m` + t.slice(a, b) + '\u001b[0m' + t.slice(b), l.text) : (l.text || '');
        return gap > 0.02 ? [{ pause: gap }, { output: l.s === 'err' ? `\u001b[33m${text}\u001b[0m` : text }] : [{ output: l.s === 'err' ? `\u001b[33m${text}\u001b[0m` : text }];
      });
      return [{ command: step.command }, ...lines, ...(step.exit ? [{ output: `\u001b[31m(exit ${step.exit})\u001b[0m` }] : []), { pause: 0.65 }];
    });
    const fit = () => {
      const h = screen.offsetHeight || shell.offsetHeight, w = view.clientWidth;
      return { rows: Math.max(4, Math.floor((h / 2 - 20) / 10)), cols: Math.max(24, Math.floor((w / 2 - 10) / 6)) };
    };
    function showConsole(s, again) {
      const f = fit();
      if (f.cols !== cols) { cols = f.cols; consoles = new Map(); shell.innerHTML = ''; }
      let c = consoles.get(s.id), fresh = false;
      if (!c) {
        const box = document.createElement('div'); box.className = 'term-console'; shell.append(box);
        const ctl = window.fsdsp56kConsole.mount(box, s.title, f.cols, f.rows, steps(s));
        const stage = box.querySelector('.fsdsp56k-console'); if (stage) stage.style.width = `${(10 + 6 * f.cols) * 2}px`;
        c = { box, ctl }; consoles.set(s.id, c); fresh = true;
      }
      for (const [, o] of consoles) o.box.hidden = o !== c;
      c.ctl.tube(look === 'tube');
      if (again && !fresh) c.ctl.replay();
    }
    function load() {
      if (window.fsdsp56kConsole) return Promise.resolve(true);
      return loading = loading || import(consoleUrl).then(() => !!window.fsdsp56kConsole, () => false);
    }
    async function pick(id) {
      const was = look; look = id;
      for (const b of looks.children) b.setAttribute('aria-pressed', String(b.dataset.look === id));
      if (id === 'plain') { shell.hidden = true; screen.hidden = false; if (was !== 'plain') play(current); return; }
      if (!(await load())) { foot.textContent = 'The Workbench looks did not load in this copy; the plain look stays.'; return pick('plain'); }
      if (look !== id) return;
      run++;   // the plain replay stops
      if (!screen.hidden) shell.style.height = `${screen.offsetHeight}px`;   // measured on the plain screen only
      screen.hidden = true; shell.hidden = false;
      showConsole(current, was === 'plain');
    }
    host.addEventListener('terminalsession', e => {
      const s = data.sessions.find(x => x.id === e.detail); if (!s) return;
      current = s;
      if (look !== 'plain') { for (const b of chips.children) b.setAttribute('aria-pressed', String(b.dataset.session === s.id)); showConsole(s, true); return; }
      played = visible; if (visible) play(s); else for (const b of chips.children) b.setAttribute('aria-pressed', String(b.dataset.session === s.id));
    });
    new ResizeObserver(() => { if (look !== 'plain' && fit().cols !== cols) showConsole(current, false); }).observe(view);
    replay.onclick = () => look === 'plain' ? play(current) : showConsole(current, true);
    new IntersectionObserver(es => { visible = es[0].isIntersecting; if (visible && !played) { played = true; play(current); } }).observe(screen);
    T.replay = () => replay.onclick();
    T.look = pick;
  }
})();
