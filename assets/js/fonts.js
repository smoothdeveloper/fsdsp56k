// fsdsp56k: the font pairings, switched live with chips; the choice is kept in localStorage ('fsdsp56k.fonts') and
// shared by the splash and the README page. ?fonts=<id> in the URL picks one without storing it (for screenshots).
// Load this in <head>, before the CSS paints, so the page never flashes another pairing.
(function () {
  var PAIRINGS = [
    { id: 'datasheet', label: 'Datasheet', note: 'Space Grotesk · Inter · JetBrains Mono · Silkscreen' },
    { id: 'copper',    label: 'Copper',    note: 'Chakra Petch · IBM Plex Sans · IBM Plex Mono · VT323' },
    { id: 'megademo',  label: 'Megademo',  note: 'Unbounded · Manrope · Fira Code · Press Start 2P' },
    { id: 'workbench', label: 'Workbench', note: 'Bricolage Grotesque · Instrument Sans · Martian Mono · Pixelify Sans' }
  ];
  var KEY = 'fsdsp56k.fonts', DEFAULT = 'copper';
  var ids = PAIRINGS.map(function (p) { return p.id; });
  var fromUrl = new URLSearchParams(location.search).get('fonts');
  var stored = null;
  try { stored = localStorage.getItem(KEY); } catch (e) { }
  var current = ids.indexOf(fromUrl) >= 0 ? fromUrl : ids.indexOf(stored) >= 0 ? stored : DEFAULT;
  var root = document.documentElement;
  root.dataset.fonts = current;

  function set(id) {
    current = id;
    root.dataset.fonts = id;
    try { localStorage.setItem(KEY, id); } catch (e) { }
    sync();
    document.dispatchEvent(new CustomEvent('fsdsp56k:fonts', { detail: id }));
  }
  var dock = null;
  function sync() {
    if (!dock) return;
    var t = dock.querySelector('.dock-toggle'), p = PAIRINGS.filter(function (x) { return x.id === current; })[0];
    if (t) t.innerHTML = '<span class="aa">Aa</span><span class="cur">' + p.label + '</span>';
    dock.querySelectorAll('[data-pairing]').forEach(function (b) { b.setAttribute('aria-pressed', String(b.dataset.pairing === current)); });
  }
  // collapsed to a small "Aa" button; it opens the pairings on click and closes on a click elsewhere or Escape.
  // A page with a [data-type-dock] slot (the README's top bar) gets it docked there, other pages bottom-right.
  function mount() {
    var slot = document.querySelector('[data-type-dock]');
    dock = document.createElement('div');
    dock.className = 'type-dock' + (slot ? ' docked' : '');
    dock.setAttribute('role', 'group');
    dock.setAttribute('aria-label', 'Font pairing');
    var toggle = document.createElement('button');
    toggle.type = 'button'; toggle.className = 'chip dock-toggle'; toggle.setAttribute('aria-expanded', 'false');
    toggle.title = 'Font pairing';
    toggle.onclick = function (e) { e.stopPropagation(); open(!dock.classList.contains('open')); };
    var panel = document.createElement('div'); panel.className = 'dock-panel';
    var label = document.createElement('span'); label.className = 'dock-label'; label.textContent = 'Type';
    var row = document.createElement('div'); row.className = 'chips';
    PAIRINGS.forEach(function (p) {
      var b = document.createElement('button');
      b.type = 'button'; b.className = 'chip'; b.dataset.pairing = p.id; b.title = p.note;
      b.innerHTML = p.label + '<small>' + p.note + '</small>';
      b.onclick = function () { set(p.id); };
      row.appendChild(b);
    });
    panel.append(label, row);
    dock.append(toggle, panel);
    function open(v) { dock.classList.toggle('open', v); toggle.setAttribute('aria-expanded', String(v)); }
    document.addEventListener('click', function (e) { if (!dock.contains(e.target)) open(false); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') open(false); });
    (slot || document.body).appendChild(dock);
    sync();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount); else mount();
  window.fsdsp56kFonts = { pairings: PAIRINGS, get: function () { return current; }, set: set };
})();
