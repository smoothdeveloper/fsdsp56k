// fsdsp56k playground: one live voice in the audio thread for the JS (Fable) runtime; it imports the Fable bundle from the
// dist layout's fixed place (dist/playground/fable-js/kernels.mjs). voice-rust-worklet.mjs is the Rust one.
// MDS kernels render 32 mono frames a call; each word plays on both channels. (The DSP56300 runtime uses the toolchain's
// own processor, dist/playground/dsp56300/processor.mjs.) Messages take effect between blocks.
import { create } from '../../../playground/fable-js/kernels.mjs';

class Voice extends AudioWorkletProcessor {
  constructor(options) {
    super();
    const o = options.processorOptions;
    this.runtime = o.runtime;
    if (o.runtime === 'rust') {
      const x = new WebAssembly.Instance(o.module, {}).exports;
      x.dsp_select(o.rustIndex);
      this.k = {
        parameters: w => { for (let i = 0; i < 8; i++) x.dsp_parameter(i, i < w.length ? w[i] : 0); },
        parameter: (i, w) => x.dsp_parameter(i, w),
        init: () => x.dsp_init(), trig: () => x.dsp_trig(),
        render: out => { const n = x.dsp_render(); for (let i = 0; i < n; i++) out[i] = x.dsp_output(i); } };
    } else {
      const k = create(o.kernel);
      const words = new Array(8).fill(0);
      this.k = {
        parameters: w => { w.forEach((v, i) => words[i] = v); k.parameters(words.slice()); },
        parameter: (i, v) => { words[i] = v; k.parameters(words.slice()); },
        init: () => k.init(), trig: () => k.trig(),
        render: out => { const w = k.render(); for (let i = 0; i < 32; i++) out[i] = w[i]; } };
    }
    this.k.parameters(o.words); this.k.init();
    this.block = new Int32Array(32); this.cursor = 32;
    this.gain = 0.3; this.playing = true; this.peak = 0; this.quanta = 0; this.busy = 0; this.budget = 0;
    this.port.onmessage = ({ data: m }) => {
      try {
        if (m.type === 'parameter') this.k.parameter(m.index, m.word);
        else if (m.type === 'parameters') this.k.parameters(m.words);
        else if (m.type === 'trig') this.k.trig();
        else if (m.type === 'init') { this.k.init(); this.cursor = 32; }
        else if (m.type === 'gain') this.gain = Math.max(0, Math.min(0.5, m.value));
        else if (m.type === 'play') this.playing = !!m.value;
      } catch (e) { this.port.postMessage({ type: 'error', message: String(e.message || e) }); }
    };
  }
  process(_, outputs) {
    const out = outputs[0]; if (!out || !this.playing) return true;
    const L = out[0], R = out[1] || out[0];
    const t0 = (typeof performance !== 'undefined' ? performance.now() : Date.now());
    try {
      for (let i = 0; i < L.length; i++) {
        if (this.cursor === 32) { this.k.render(this.block); this.cursor = 0; }
        const v = ((this.block[this.cursor++] << 8) >> 8) / 8388608 * this.gain;
        L[i] = v; R[i] = v;
        const a = v < 0 ? -v : v; if (a > this.peak) this.peak = a;
      }
    } catch (e) { this.playing = false; this.port.postMessage({ type: 'error', message: String(e.message || e) }); }
    this.busy += (typeof performance !== 'undefined' ? performance.now() : Date.now()) - t0;
    this.budget += L.length / sampleRate * 1000;
    if (++this.quanta === 64) {
      this.port.postMessage({ type: 'level', peak: this.peak, load: this.busy / this.budget });
      this.quanta = 0; this.peak = 0; this.busy = 0; this.budget = 0;
    }
    return true;
  }
}
registerProcessor('fsdsp56k-voice-js', Voice);
