import { resolve } from 'node:path';
import { defineConfig } from 'vite';

// The loader host pages embed (/widget/v1/loader.js): one small classic script, no React, no CSS file.
export default defineConfig({
  publicDir: false,
  build: {
    outDir: 'dist-widget',
    // The frame is built into the same folder first.
    emptyOutDir: false,
    lib: {
      entry: resolve(import.meta.dirname, 'src/widget/loader/loader.ts'),
      name: 'KnowledgeAgentLoader',
      formats: ['iife'],
      fileName: () => 'loader.js',
    },
  },
});
