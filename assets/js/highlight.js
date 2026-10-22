// fsdsp56k: a small syntax colouring for the code blocks (F#, the shell, DSP56300 assembly), no dependencies.
// <pre class="code" data-lang="fsharp|shell|asm"><code>…</code></pre>; plain text in, spans out. Adds a copy button
// to blocks that sit under a .code-head.
(function () {
  var esc = function (s) { return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); };
  var span = function (cls, s) { return '<span class="tok-' + cls + '">' + esc(s) + '</span>'; };
  var FS_KW = /^(let|mutable|type|module|open|if|then|else|elif|for|in|do|done|match|with|function|fun|rec|and|of|when|while|yield|return|true|false|not|member|static|new|inline|private|internal|namespace|begin|end|use|null)$/;
  var RULES = {
    fsharp: [
      [/^\/\/.*/, 'c'], [/^\(\*[\s\S]*?\*\)/, 'c'], [/^\$?"(?:[^"\\]|\\.)*"/, 's'], [/^'.'/, 's'],
      [/^\[<[^\]]*>\]/, 'a'], [/^0x[0-9a-fA-F]+/, 'n'], [/^\d+(\.\d+)?/, 'n'],
      [/^[A-Z][A-Za-z0-9_]*/, 't'], [/^[a-z_][A-Za-z0-9_']*/, function (w) { return FS_KW.test(w) ? 'k' : null; }],
      [/^(<-|->|>>>|<<<|\|>|::|\.\.|[+\-*\/=<>|&@^%])/, 'o']
    ],
    shell: [
      [/^#.*/, 'c'], [/^\$ /, 'p'], [/^"(?:[^"\\]|\\.)*"/, 's'], [/^--?[A-Za-z][\w-]*/, 'flag'],
      [/^(dotnet|git|cargo|cd|fsdsp56k)\b/, 'k'], [/^\d+(\.\d+)?\b/, 'n']
    ],
    asm: [
      [/^;.*/, 'c'], [/^#\$?[0-9A-Fa-f]+/, 'n'], [/^\$[0-9A-Fa-f]+/, 'n'], [/^[A-Z_][A-Z0-9_]*(?=:?\s*$)/, 't'],
      [/^\b(move|movec|mac|macr|mpy|mpyr|clr|add|sub|asl|asr|rts|do|enddo|lua|tfr|rnd|jsr|jmp|bra|and|or)\b/i, 'k'],
      [/^[xyp]:/i, 'f'], [/^\b(r[0-7]|n[0-7]|m[0-7]|x[01]|y[01]|a|b)\b/i, 't']
    ]
  };
  function colour(text, lang) {
    var rules = RULES[lang]; if (!rules) return esc(text);
    var out = '', i = 0, plain = '';
    var flush = function () { if (plain) { out += esc(plain); plain = ''; } };
    while (i < text.length) {
      var rest = text.slice(i), hit = false;
      for (var r = 0; r < rules.length; r++) {
        var m = rules[r][0].exec(rest);
        if (!m || m.index !== 0 || !m[0].length) continue;
        var cls = typeof rules[r][1] === 'function' ? rules[r][1](m[0]) : rules[r][1];
        // identifiers glued to a previous word char are not a token start
        if (/^[A-Za-z_]/.test(m[0]) && i > 0 && /[A-Za-z0-9_.]/.test(text[i - 1]) && cls !== 'k' && cls !== 't') { break; }
        if (cls) { flush(); out += span(cls, m[0]); } else plain += m[0];
        i += m[0].length; hit = true; break;
      }
      if (!hit) { plain += text[i]; i++; }
    }
    flush();
    return out;
  }
  function run() {
    document.querySelectorAll('pre.code[data-lang] > code').forEach(function (c) {
      if (c.dataset.done) return;
      c.innerHTML = colour(c.textContent, c.parentNode.dataset.lang);
      c.dataset.done = '1';
    });
    document.querySelectorAll('.code-head').forEach(function (h) {
      var pre = h.nextElementSibling;
      if (!pre || h.querySelector('.copy')) return;
      var b = document.createElement('button'); b.type = 'button'; b.className = 'copy'; b.textContent = 'copy';
      b.onclick = function () {
        var text = pre.textContent.split('\n').map(function (l) { return l.replace(/^\$ /, ''); }).join('\n');
        (navigator.clipboard ? navigator.clipboard.writeText(text) : Promise.reject()).then(
          function () { b.textContent = 'copied'; setTimeout(function () { b.textContent = 'copy'; }, 1200); },
          function () { b.textContent = 'select + ⌘C'; });
      };
      h.appendChild(b);
    });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', run); else run();
  window.fsdsp56kHighlight = { colour: colour, run: run };
})();
