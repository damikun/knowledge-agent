// Rebuilds the embeddable widget (chat frame and loader) into dist-widget on every change.
import { spawn } from 'node:child_process';
import { rmSync } from 'node:fs';
import { join } from 'node:path';

const webDir = join(import.meta.dirname, '..');
const viteCli = join(webDir, 'node_modules', 'vite', 'bin', 'vite.js');
let stopping = false;

// Watch builds never empty dist-widget (see vite.widget.config.ts), so old hashed assets are removed once here.
rmSync(join(webDir, 'dist-widget'), { recursive: true, force: true });

const builds = [
  { name: 'frame', config: 'vite.widget.config.ts' },
  { name: 'loader', config: 'vite.loader.config.ts' },
];

const children = builds.map(({ name, config }) => {
  const prefix = `[widget:${name}]`;
  const child = spawn(process.execPath, [viteCli, 'build', '--watch', '--config', config], {
    cwd: webDir,
    // The output is piped, so Vite only keeps its colors when asked; any FORCE_COLOR value turns them on.
    env: process.stdout.isTTY && process.env.FORCE_COLOR === undefined ? { ...process.env, FORCE_COLOR: '1' } : process.env,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  [child.stdout, child.stderr].forEach((stream, index) => {
    const target = index === 0 ? process.stdout : process.stderr;
    if (!stream) {
      return;
    }
    let pending = '';
    stream.setEncoding('utf8');
    stream.on('data', (chunk) => {
      const lines = (pending + String(chunk)).split(/\r?\n/);
      pending = lines.pop() ?? '';
      for (const line of lines) {
        target.write(`${prefix} ${line}\n`);
      }
    });
    stream.on('end', () => {
      if (pending) {
        target.write(`${prefix} ${pending}\n`);
      }
    });
  });
  child.on('exit', (code, signal) => {
    if (!stopping) {
      process.stderr.write(`${prefix} exited (${signal ?? code}); stopping the other build.\n`);
      process.exitCode = code ?? 1;
      stopAll();
    }
  });
  return child;
});

function stopAll() {
  stopping = true;
  for (const child of children) {
    if (child.exitCode === null && child.signalCode === null) {
      child.kill('SIGTERM');
    }
  }
}

process.on('SIGINT', stopAll);
process.on('SIGTERM', stopAll);
