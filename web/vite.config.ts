import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { loadEnv, type ProxyOptions } from 'vite';
import { defineConfig } from 'vitest/config';

const API_PATHS = ['/api', '/health', '/openapi', '/scalar', '/widget/', '/mcp/'];

function resolveApiTarget(env: Record<string, string | undefined>): string {
  const candidates = [
    env.services__api__https__0,
    env.services__api__http__0,
    env.API_HTTPS,
    env.API_HTTP,
    env.VITE_API_PROXY_TARGET,
  ];
  return candidates.find((value) => value !== undefined && value.trim() !== '')?.trim() ?? 'http://localhost:5080';
}

function createProxy(target: string): Record<string, ProxyOptions> {
  const options: ProxyOptions = {
    target,
    changeOrigin: true,
    secure: false,
    configure: (proxy) => {
      proxy.on('proxyRes', (proxyRes) => {
        const contentType = String(proxyRes.headers['content-type'] ?? '');
        if (contentType.includes('text/event-stream')) {
          proxyRes.headers['cache-control'] = 'no-cache, no-transform';
          proxyRes.headers['x-accel-buffering'] = 'no';
        }
      });
    },
  };
  return Object.fromEntries(API_PATHS.map((path) => [path, options]));
}

export default defineConfig(({ mode }) => {
  const env: Record<string, string | undefined> = { ...loadEnv(mode, process.cwd(), ''), ...process.env };
  const apiTarget = resolveApiTarget(env);
  const port = Number.parseInt(env.PORT ?? '', 10);
  const hasPort = Number.isFinite(port) && port > 0;

  return {
    plugins: [react(), tailwindcss()],
    server: {
      port: hasPort ? port : 5173,
      strictPort: hasPort,
      proxy: createProxy(apiTarget),
    },
    preview: {
      port: hasPort ? port : 4173,
      strictPort: hasPort,
      proxy: createProxy(apiTarget),
    },
    build: {
      chunkSizeWarningLimit: 900,
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      include: ['src/**/*.test.{ts,tsx}'],
      css: false,
      restoreMocks: true,
      unstubGlobals: true,
    },
  };
});
