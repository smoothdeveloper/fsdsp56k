// fsdsp56k audition glyphs: 16x16 pixel icons for the render paths a kernel can be auditioned through, in the
// icon's palette, the command glyphs' and the colours below. 'fable' is a fable (an open book), 'rust' Ferris the
// crab (public domain), 'wasm' and 'javascript' their squares, 'webaudio' the browser's audio thread (a window with a
// wave), 'emulator' the DSP56300 as a virtual chip (The Usual Suspects' emulator). The F# glyph is 'kernel' in
// fsdsp56k-command-icons.js, fsdsp56k itself 'diamond-16' in fsdsp56k-icon-pixels.js.
window.fsdsp56kAuditionIcons = {
  palette: {
    F: '#FF8A4C', G: '#F74C00', H: '#A83200',  // Ferris orange: light, face, shade
    V: '#8A78FF', X: '#654FF0', Z: '#4532B8',  // WebAssembly violet
    U: '#F7DF1E', T: '#C2AC00',  // JavaScript yellow
  },
  icons: {
    'fable': `
................
..oooo....oooo..
.ohhhhoooohhhho.
ohhhhhhppphhhhho
hhqqqhhppphqqqhh
hhhhhhhpphhhhhhh
hhqqqqhpphqqqqhh
hhhhhhhpphhhhhhh
hhqqhhhpphqqqhhh
hhhhhhhpphhhhhhh
hhhhhhippihhhhhh
CBBBBBBiiBBBBBBD
oCBBBBBooBBBBBDo
.oooooo..oooooo.
................
................
`,
    'rust': `
................
.oo..........oo.
oFFo........oHHo
oFFo........oHHo
.oFFo......oHHo.
..oFo......oHo..
..oFooooooooHo..
.oFFFGGGGGGGHHo.
oFFFFkhGGhkGHHHo
oFFFFGGGGGGGHHHo
oFFFFGkGGkGGHHHo
.oFFFHHkkHHHHHo.
.oooooooooooooo.
oHHoHo....oHoHHo
HoooHo....oHoooH
o...o......o...o
`,
    'wasm': `
.ooooo....ooooo.
oVVVVVo..oXXXXXo
oVVVVVVooXXXXXXo
oVVVVVXXXXXXXXXo
oVVVVXXXXXXXXXXo
oVVVXXXXXXXXXXXo
oVVXXXXXXXXXXXXo
oVXXXXXXXXXXXXXo
oXhXXXhXXXhhXXXo
oXhXXXhXXhXXhXXo
oXhXhXhXXhhhhXXo
oXhXhXhXXhXXhXZo
oXXhXhXXXhXXhZZo
oXXXXXXXXXXXZZZo
oXXXXXXXXXXZZZZo
.oooooooooooooo.
`,
    'javascript': `
.oooooooooooooo.
oUUUUUUUUUUUUUTo
oUUUUUUUUUUUUUTo
oUUUUUUUUUUUUUTo
oUUUUUUUUUUUUUTo
oUUUUUUUUUUUUUTo
oUUUUUUUUUUUUUTo
oUUUUUUUUUUUUUTo
oUUUUUUUUkUUkkTo
oUUUUUUUUkUkUUTo
oUUUUUUUUkUUkUTo
oUUUUUUkUkUUUkTo
oUUUUUUUkUUkkUTo
oUUUUUUUUUUUUUTo
oTTTTTTTTTTTTTTo
.oooooooooooooo.
`,
    'webaudio': `
................
oooooooooooooooo
qquqYqKqqqqqqqqq
qqqqqqqqqqqqqqqq
kkkkkkkkkkkkkkkk
kkkkkkkkkkkkkkkk
kkkkWWkkkkkkkkkk
kkkWkkWkkkkkkkkk
kkWkkkkWkkkkkkkk
kWkkkkkkkkkkkkWk
kkkkkkkkWkkkkWkk
kkkkkkkkkWkkWkkk
kkkkkkkkkkWWkkkk
kkkkkkkkkkkkkkkk
kkkkkkkkkkkkkkkk
oooooooooooooooo
`,
    'emulator': `
....o.o..o.o....
...oMoMooMoMo...
...oooooooooo...
.ooKkKkKkKkKkoo.
oMokkkkkkkkkKoMo
.ooKkKkkkkkkkoo.
oMokkkkkkkkkKoMo
.ooKkkkkkkkkkoo.
.ookkkMMMMkkKoo.
oMoKkkkkkkkkkoMo
.ookkkMMMkkkKoo.
oMoKkkkkkkkkkoMo
.ookKkKkKkKkKoo.
...oooooooooo...
...oMoMooMoMo...
....o.o..o.o....
`,
  },
};
