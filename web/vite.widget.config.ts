import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'node:path';
import { defineConfig } from 'vite';

const watching = process.argv.includes('--watch') || process.argv.includes('-w');

export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: '/widget/v1/',
  publicDir: false,
  build: {
    outDir: 'dist-widget',
    emptyOutDir: !watching,
    chunkSizeWarningLimit: 900,
    rolldownOptions: {
      input: { frame: resolve(import.meta.dirname, 'frame.html') },
    },
  },
});
