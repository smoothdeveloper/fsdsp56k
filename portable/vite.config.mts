import { defineConfig } from 'vite'
import fable from 'vite-plugin-fable'

export default defineConfig({
  plugins: [fable({ fsproj: './portable.fable.fsproj' })],
  build: {
    outDir: '../out/portable/js',
    emptyOutDir: true,
    lib: {
      entry: { grade: './Runner.fs', kernels: './Adapters.fs', 'rust-wasm': './RustWasm.fs' },
      formats: ['es']
    },
    rollupOptions: {
      external: id => id.startsWith('node:'),
      output: { entryFileNames: '[name].mjs', chunkFileNames: '[name]-[hash].mjs' }
    }
  }
})
