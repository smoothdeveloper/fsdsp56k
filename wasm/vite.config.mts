import { defineConfig } from 'vite'
import fable from 'vite-plugin-fable'

export default defineConfig({
  plugins: [fable({ fsproj: './dsp56k.wasm.fsproj' })],
  build: {
    outDir: '../out/wasm',
    emptyOutDir: false,
    lib: { entry: { processor: './Processor.fs', 'portable-processor': './PortableProcessor.fs', check: './Check.fs', 'portable-check': './PortableCheck.fs' }, formats: ['es'] },
    rollupOptions: {
      external: id => (id.endsWith('dsp56300.mjs') || id.endsWith('portable-processor.mjs')) || id.startsWith('node:'),
      output: { entryFileNames: '[name].mjs', chunkFileNames: '[name].mjs' }
    }
  }
})
