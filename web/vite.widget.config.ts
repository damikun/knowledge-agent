import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'node:path';
import { defineConfig } from 'vite';

// The embeddable widget's chat frame (frame.html), served by the API at /widget/v1/frame/{key} from dist-widget.
// Rebuilds in watch mode would empty dist-widget each time and delete the loader built next to the frame, so only full
// builds empty it; scripts/widget-watch.mjs clears it once before watching.
const watching = process.argv.includes('--watch') || process.argv.includes('-w');

export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: '/widget/v1/',
  // The portal's public files (its favicon) do not belong to the widget.
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
