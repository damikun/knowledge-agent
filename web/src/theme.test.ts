/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// Read from disk: with css: false (vite.config.ts) Vitest stubs CSS imports, ?raw included. Tests run from web/.
const read = (path: string) => readFileSync(resolve(process.cwd(), path), 'utf8');
const tokensCss = read('src/styles/tokens.css');
const indexCss = read('src/index.css');

/** The custom properties declared in each block, keyed by the block's selector or at-rule ("@theme", ":root", ".dark"). */
function declarations(css: string): Map<string, Record<string, string>> {
  const blocks = new Map<string, Record<string, string>>();
  const stack: string[] = [];
  let buffer = '';
  const declare = () => {
    const match = /^(--[\w-]+)\s*:\s*([\s\S]+)$/.exec(buffer.trim());
    const selector = stack.at(-1);
    if (match && selector) {
      blocks.set(selector, { ...blocks.get(selector), [match[1]!]: match[2]!.trim() });
    }
    buffer = '';
  };
  for (const char of css.replace(/\/\*[\s\S]*?\*\//g, '')) {
    if (char === '{') {
      stack.push(buffer.trim());
      buffer = '';
    } else if (char === '}') {
      declare();
      stack.pop();
    } else if (char === ';') {
      declare();
    } else {
      buffer += char;
    }
  }
  return blocks;
}

const blocks = declarations(tokensCss);
const lightBlock = blocks.get(':root') ?? {};
const darkBlock = blocks.get('.dark') ?? {};

type Theme = 'light' | 'dark';

/** A token's color as #rrggbb or #rrggbbaa, following var() references within the theme. */
function color(theme: Theme, name: string): string {
  const source = theme === 'light' ? lightBlock : { ...lightBlock, ...darkBlock };
  let value = source[`--ka-${name}`];
  for (let depth = 0; value?.startsWith('var('); depth += 1) {
    value = depth < 5 ? source[value.slice(4, -1).trim()] : undefined;
  }
  if (!value || !/^#([0-9a-f]{6}|[0-9a-f]{8})$/i.test(value)) {
    throw new Error(`${theme} ${name} is not a hex color: ${value}`);
  }
  return value.toLowerCase();
}

function channels(hex: string): [number, number, number, number] {
  const byte = (offset: number) => parseInt(hex.slice(offset, offset + 2), 16);
  return [byte(1), byte(3), byte(5), hex.length === 9 ? byte(7) / 255 : 1];
}

/** An alpha color composited onto an opaque one, as the browser paints a fill over a surface. */
function over(fill: string, surface: string): string {
  const [r, g, b, a] = channels(fill);
  const [sr, sg, sb] = channels(surface);
  const mix = (top: number, bottom: number) => Math.round(top * a + bottom * (1 - a));
  return `#${[mix(r, sr), mix(g, sg), mix(b, sb)].map((value) => value.toString(16).padStart(2, '0')).join('')}`;
}

function luminance(hex: string): number {
  const linear = (value: number) => {
    const channel = value / 255;
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  };
  const [r, g, b] = channels(hex);
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
}

function contrast(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (light + 0.05) / (dark + 0.05);
}

/** "fill-active/sidebar" is the alpha fill painted over the sidebar. */
function background(theme: Theme, name: string): string {
  const [fill, surface] = name.split('/') as [string, string?];
  return surface ? over(color(theme, fill), color(theme, surface)) : color(theme, fill);
}

type Pair = [foreground: string, background: string, minimum: number];

// The label colors the API accepts (features/labels/types.ts), each a muted category tone.
const TAGS = ['gray', 'blue', 'green', 'amber', 'red', 'purple', 'teal', 'pink'];

// The semantic color table; info reuses the accent.
const SPEC_TOKENS = [
  ...['canvas', 'sidebar', 'surface', 'surface-subtle', 'raised', 'overlay', 'fill-hover', 'fill-active', 'fill-muted'],
  ...['border-subtle', 'border', 'border-strong', 'fg', 'fg-secondary', 'fg-muted', 'fg-disabled', 'fg-on-accent'],
  ...['accent', 'accent-hover', 'accent-active', 'accent-subtle', 'accent-text', 'accent-icon', 'accent-edge', 'selection'],
  ...['control', 'fg-on-control', 'fg-on-solid', 'focus', 'danger-hover'],
  ...['info', 'success', 'warning', 'danger'].flatMap((tone) => [tone, `${tone}-subtle`, `${tone}-text`]),
  ...TAGS.flatMap((tag) => [`tag-${tag}`, `tag-${tag}-subtle`, `tag-${tag}-text`]),
];

// Text needs 4.5:1 (WCAG 1.4.3); input edges, the focus ring, checked controls, status dots and icons need 3:1 (1.4.11,
// 2.4.13). Disabled text is exempt.
const SURFACES = ['canvas', 'sidebar', 'surface', 'surface-subtle', 'raised', 'fill-muted', 'accent-subtle'];
const FILLS = ['fill-hover/canvas', 'fill-hover/sidebar', 'fill-hover/raised', 'fill-active/canvas', 'fill-active/sidebar', 'fill-active/raised'];

const tokenPairs: Pair[] = [
  ...['fg', 'fg-secondary', 'fg-muted', 'accent-text', 'success-text', 'warning-text', 'danger-text'].flatMap((text) =>
    [...SURFACES, ...FILLS].map((surface): Pair => [text, surface, 4.5]),
  ),
  ['fg', 'segment-active', 4.5],
  ...['accent', 'accent-hover', 'accent-active'].map((fill): Pair => ['fg-on-accent', fill, 4.5]),
  ...['danger', 'danger-hover'].map((fill): Pair => ['fg-on-solid', fill, 4.5]),
  ['fg-on-control', 'control', 4.5],
  ...['canvas', 'surface', 'raised'].map((surface): Pair => ['control', surface, 3]),
  ...['canvas', 'sidebar', 'surface', 'surface-subtle'].map((surface): Pair => ['accent-icon', surface, 3]),
  ...['surface', 'surface-subtle', 'canvas'].map((surface): Pair => ['accent-edge', surface, 3]),
  ...TAGS.flatMap((tag): Pair[] => [
    [`tag-${tag}-text`, `tag-${tag}-subtle`, 4.5],
    [`tag-${tag}`, `tag-${tag}-subtle`, 3],
  ]),
  ...['success', 'warning', 'danger'].flatMap((tone): Pair[] => [
    [`${tone}-text`, `${tone}-subtle`, 4.5],
    [`${tone}-text`, 'canvas', 4.5],
    [`${tone}-text`, 'surface', 4.5],
    // Menus put danger text on the raised surface.
    [`${tone}-text`, 'raised', 4.5],
    ['fg', `${tone}-subtle`, 4.5],
    ['fg-secondary', `${tone}-subtle`, 4.5],
    [tone, `${tone}-subtle`, 3],
    ...['canvas', 'surface', 'sidebar'].map((surface): Pair => [tone, surface, 3]),
  ]),
  ['accent', 'accent-subtle', 3],
  // Fields also sit on muted fills, such as a toolbar or a disabled row.
  ...['canvas', 'surface', 'raised', 'surface-subtle', 'fill-muted'].flatMap((surface): Pair[] => [
    ['border-strong', surface, 3],
    ['accent', surface, 3],
  ]),
  ...['canvas', 'sidebar', 'surface', 'raised'].map((surface): Pair => ['focus', surface, 3]),
  ['accent-icon', 'fill-active/sidebar', 3],
];

const proseText = ['body', 'headings', 'lead', 'links', 'bold', 'counters', 'captions', 'quotes', 'code', 'kbd'];

/** The tokens the Markdown answers use, from `.prose-zinc` in index.css. */
function proseTokens(): Record<string, string> {
  return Object.fromEntries(
    Object.entries(declarations(indexCss).get('.prose-zinc') ?? {}).map(([name, value]) => [
      name.replace('--tw-prose-', ''),
      /^var\(--ka-([\w-]+)\)$/.exec(value)?.[1] ?? value,
    ]),
  );
}

function ratioOf([foreground, back, minimum]: Pair, theme: Theme) {
  return { ratio: contrast(color(theme, foreground), background(theme, back)), minimum };
}

describe('design tokens', () => {
  it('defines the same tokens in light and dark', () => {
    const tokens = (block: Record<string, string>) =>
      Object.keys(block)
        .filter((name) => name.startsWith('--ka-'))
        .sort();
    expect(tokens(lightBlock)).toEqual(expect.arrayContaining(SPEC_TOKENS.map((name) => `--ka-${name}`)));
    expect(tokens(darkBlock)).toEqual(tokens(lightBlock));
  });

  it('maps every token to a Tailwind theme value', () => {
    const mapped = Object.values(blocks.get('@theme inline') ?? {}).join(' ');
    for (const name of Object.keys(lightBlock).filter((key) => key.startsWith('--ka-'))) {
      expect(mapped, name).toContain(`var(${name})`);
    }
  });

  it('keeps color values out of index.css', () => {
    expect(indexCss.replace(/\/\*[\s\S]*?\*\//g, '')).not.toMatch(/#[0-9a-f]{3,8}\b|rgba?\(|hsla?\(|oklch\(/i);
  });
});

describe('theme contrast (WCAG 2.2 AA)', () => {
  for (const theme of ['light', 'dark'] as const) {
    it.each(tokenPairs.map(([fg, bg, min]) => ({ fg, bg, min })))(`${theme}: $fg on $bg is at least $min:1`, ({ fg, bg, min }) => {
      const { ratio } = ratioOf([fg, bg, min], theme);
      expect(ratio, `${fg} on ${bg}: ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(min);
    });

    it(`${theme}: Markdown answer text is at least 4.5:1`, () => {
      const prose = proseTokens();
      for (const name of proseText) {
        for (const surface of ['canvas', 'surface', 'raised', prose['pre-bg']!]) {
          const ratio = contrast(color(theme, prose[name]!), color(theme, surface));
          expect(ratio, `${name} (${prose[name]}) on ${surface}: ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(4.5);
        }
      }
      expect(contrast(color(theme, prose['pre-code']!), color(theme, prose['pre-bg']!))).toBeGreaterThanOrEqual(4.5);
    });
  }

  // The lowest body-text ratios the spec reports, so a token change that lowers them fails here first.
  it.each([
    { theme: 'light' as const, pair: ['fg-muted', 'fill-active/sidebar', 4.5] as Pair, spec: 4.89 },
    { theme: 'dark' as const, pair: ['fg-muted', 'fill-active/raised', 4.5] as Pair, spec: 5.22 },
    { theme: 'light' as const, pair: ['fg-on-accent', 'accent', 4.5] as Pair, spec: 6.91 },
  ])('$theme: $pair.0 on $pair.1 stays at $spec:1', ({ theme, pair, spec }) => {
    expect(ratioOf(pair, theme).ratio).toBeCloseTo(spec, 1);
  });
});

describe('copies of the colors outside the tokens', () => {
  it('index.html paints the browser chrome in the canvas color of each scheme', () => {
    const html = read('index.html');
    const meta = (scheme: string) =>
      new RegExp(`<meta name="theme-color" content="(#[0-9a-f]{6})" media="\\(prefers-color-scheme: ${scheme}\\)"`, 'i')
        .exec(html)?.[1]
        ?.toLowerCase();
    expect(meta('light')).toBe(color('light', 'canvas'));
    expect(meta('dark')).toBe(color('dark', 'canvas'));
  });

  it('the favicon is the sidebar mark: the petrol with white text, in each scheme', () => {
    const svg = read('public/favicon.svg');
    const [light, dark] = svg.split('@media (prefers-color-scheme: dark)') as [string, string];
    const fill = (css: string, rule: string) => new RegExp(`\\.${rule} \\{ fill: (#[0-9a-f]{6}); \\}`, 'i').exec(css)?.[1]?.toLowerCase();
    expect([fill(light, 'mark'), fill(light, 'text')]).toEqual([color('light', 'accent'), color('light', 'fg-on-accent')]);
    expect([fill(dark, 'mark'), fill(dark, 'text')]).toEqual([color('dark', 'accent'), color('dark', 'fg-on-accent')]);
  });
});
