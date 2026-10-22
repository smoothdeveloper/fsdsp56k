// fsdsp56k splash: the command cards, drawn from the lettering page's 16x16 command glyphs
// (assets/icons/fsdsp56k-command-icons.js over the icon's palette): inputs, the command, outputs.
// <div class="cmd-flow" data-in="file-asm" data-hub="dsp56300" data-out="file-lst file-hex"></div>
(() => {
  const icons = window.fsdsp56kCommandIcons, base = window.fsdsp56kIcon;
  if (!icons || !base) return;
  const palette = Object.assign({}, base.palette, icons.palette);
  const cache = new Map();
  function glyph(name) {
    if (cache.has(name)) return cache.get(name).cloneNode();
    const src = icons.icons[name];
    if (!src) return document.createElement('span');
    const rows = src.trim().split('\n'), h = rows.length, w = rows[0].length;
    const c = document.createElement('canvas'); c.width = w; c.height = h; c.className = 'px glyph';
    const g = c.getContext('2d');
    rows.forEach((r, y) => [...r].forEach((ch, x) => { if (ch !== '.' && palette[ch]) { g.fillStyle = palette[ch]; g.fillRect(x, y, 1, 1); } }));
    const img = new Image(); img.src = c.toDataURL(); img.width = w; img.height = h; img.className = 'px glyph'; img.alt = '';
    img.dataset.w = w;
    cache.set(name, img);
    return img.cloneNode();
  }
  document.querySelectorAll('.cmd-flow').forEach(el => {
    const part = (names, cls) => {
      const box = document.createElement('span'); box.className = 'flow-' + cls;
      for (const n of (names || '').split(/\s+/).filter(Boolean)) {
        const i = glyph(n); i.title = n.replace('file-', '.').replace('files-', '.'); box.appendChild(i);
      }
      return box;
    };
    const wire = () => { const s = document.createElement('span'); s.className = 'flow-wire'; return s; };
    el.replaceChildren(part(el.dataset.in, 'in'), wire(), part(el.dataset.hub, 'hub'), wire(), part(el.dataset.out, 'out'));
  });
})();
