import { resolve } from 'node:path';
import { defineConfig } from 'vite';

export default defineConfig({
  publicDir: false,
  build: {
    outDir: 'dist-widget',
    emptyOutDir: false,
    lib: {
      entry: resolve(import.meta.dirname, 'src/widget/loader/loader.ts'),
      name: 'KnowledgeAgentLoader',
      formats: ['iife'],
      fileName: () => 'loader.js',
    },
  },
});
