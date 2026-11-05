/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

/* Tailwind's palette is off in tokens.css, so a raw color produces no CSS and a dark: variant bypasses the tokens. */

const SRC = resolve(process.cwd(), 'src');

// The widget has its own theme, tests may name any class, and the tokens are where the colors live.
const SKIPPED = [/^widget\//, /^test\//, /\.test\.tsx?$/, /^styles\/tokens\.css$/];

const FAMILIES = 'red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|slate|gray|zinc|neutral|stone|mauve|olive|mist|taupe';
const SHADES = '50|[1-9]00|950';
const UTILITIES = 'bg|text|text-shadow|border(?:-[xytrblse])?|divide|ring(?:-offset)?|inset-ring|outline|shadow|inset-shadow|drop-shadow|fill|stroke|from|via|to|accent|caret|decoration|placeholder';
const PALETTE = new RegExp(
  `(?<![\\w-])(?:(?:${UTILITIES})-(?:(?:${FAMILIES})-(?:${SHADES})|white|black)(?:/\\d+)?|--color-(?:(?:${FAMILIES})-(?:${SHADES})|white|black))(?![\\w-])`,
  'g',
);
// A variant runs straight into its class; `dark: Moon` is an object key.
const DARK = /(?<![\w-])dark:[\w[!-][^\s'"`]*/g;

function sourceFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? sourceFiles(path) : /\.(tsx?|css)$/.test(entry.name) ? [relative(SRC, path).split('\\').join('/')] : [];
  });
}

function findings(): string[] {
  return sourceFiles(SRC)
    .filter((file) => !SKIPPED.some((pattern) => pattern.test(file)))
    .flatMap((file) =>
      readFileSync(join(SRC, file), 'utf8')
        .split('\n')
        .flatMap((line, index) => [...line.matchAll(PALETTE), ...line.matchAll(DARK)].map(([found]) => `${file}:${index + 1} ${found}`)),
    );
}

describe('raw palette colors', () => {
  it('finds raw colors and dark: variants, and leaves the tokens alone', () => {
    expect('bg-zinc-100 hover:text-teal-700 ring-amber-500/20 text-white var(--color-zinc-500) shadow-black/5'.match(PALETTE)).toHaveLength(6);
    expect('bg-surface text-fg-muted border-border-subtle whitespace-nowrap bg-accent text-fg-on-accent ring-swatch-edge'.match(PALETTE)).toBeNull();
    expect("dark:bg-zinc-900 dark:hover:text-fg text-dark { dark: 'Theme: dark' }".match(DARK)).toEqual(['dark:bg-zinc-900', 'dark:hover:text-fg']);
  });

  it('appear nowhere in the portal', () => {
    expect(findings(), 'Use the tokens of styles/tokens.css instead of raw palette colors and dark: variants.').toEqual([]);
  });
});
