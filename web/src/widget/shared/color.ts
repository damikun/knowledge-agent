// Colors derived from a widget's brand color, shared by the chat frame and the loader. All contrast math follows WCAG 2.2.

export type Rgb = readonly [number, number, number];

export const DEFAULT_BRAND_COLOR = '#0f766e';

export const WHITE: Rgb = [255, 255, 255];
export const BLACK: Rgb = [0, 0, 0];
const NEAR_BLACK: Rgb = [17, 17, 17];
// The frame's light and dark surfaces, where accent-colored text and focus rings appear.
const LIGHT_SURFACES: readonly Rgb[] = [WHITE, [244, 244, 245]];
const DARK_SURFACES: readonly Rgb[] = [[24, 24, 27], [39, 39, 42]];

export function parseHexColor(value: string | null | undefined): Rgb | null {
  const match = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(value?.trim() ?? '');
  if (!match) {
    return null;
  }
  const hex = match[1]!.length === 3 ? [...match[1]!].map((c) => c + c).join('') : match[1]!;
  return [Number.parseInt(hex.slice(0, 2), 16), Number.parseInt(hex.slice(2, 4), 16), Number.parseInt(hex.slice(4, 6), 16)];
}

export function toHexColor(rgb: Rgb): string {
  return `#${rgb.map((c) => Math.round(c).toString(16).padStart(2, '0')).join('')}`;
}

function linear(channel: number): number {
  const c = channel / 255;
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function relativeLuminance([r, g, b]: Rgb): number {
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
}

export function contrastRatio(a: Rgb, b: Rgb): number {
  const [light, dark] = [relativeLuminance(a), relativeLuminance(b)].sort((x, y) => y - x) as [number, number];
  return (light + 0.05) / (dark + 0.05);
}

export function mix(from: Rgb, to: Rgb, amount: number): Rgb {
  return [from[0] + (to[0] - from[0]) * amount, from[1] + (to[1] - from[1]) * amount, from[2] + (to[2] - from[2]) * amount];
}

/** To whole channels, as the color is written as #rrggbb; contrast is checked on that. */
export function rounded(color: Rgb): Rgb {
  return [Math.round(color[0]), Math.round(color[1]), Math.round(color[2])];
}

// Moves `color` toward `target` in small steps until it reaches `minimum` contrast against every background.
function towards(color: Rgb, target: Rgb, backgrounds: readonly Rgb[], minimum: number): Rgb {
  for (let step = 0; step <= 40; step++) {
    const candidate = rounded(mix(color, target, step / 40));
    if (backgrounds.every((background) => contrastRatio(candidate, background) >= minimum)) {
      return candidate;
    }
  }
  return target;
}

/** White or near-black, whichever reads better on `background`. */
export function readableOn(background: Rgb): Rgb {
  return contrastRatio(WHITE, background) >= contrastRatio(NEAR_BLACK, background) ? WHITE : NEAR_BLACK;
}

/** `color` itself when it reaches `minimum` against every background, else moved toward black or white until it does. */
export function legibleOn(color: Rgb, backgrounds: readonly Rgb[], minimum: number): Rgb {
  if (backgrounds.every((background) => contrastRatio(color, background) >= minimum)) {
    return color;
  }
  const darker = towards(color, BLACK, backgrounds, minimum);
  const lighter = towards(color, WHITE, backgrounds, minimum);
  const reaches = (candidate: Rgb) => backgrounds.every((background) => contrastRatio(candidate, background) >= minimum);
  // The smaller change wins, as long as it reaches the contrast.
  const distance = (candidate: Rgb) => Math.abs(candidate[0] - color[0]) + Math.abs(candidate[1] - color[1]) + Math.abs(candidate[2] - color[2]);
  if (reaches(darker) && (!reaches(lighter) || distance(darker) <= distance(lighter))) {
    return darker;
  }
  return reaches(lighter) ? lighter : readableOn(backgrounds[0] ?? WHITE);
}

/** Hue in degrees, saturation and lightness from 0 to 1. */
export function toHsl([r, g, b]: Rgb): [number, number, number] {
  const [red, green, blue] = [r / 255, g / 255, b / 255];
  const max = Math.max(red, green, blue);
  const min = Math.min(red, green, blue);
  const lightness = (max + min) / 2;
  const delta = max - min;
  if (delta === 0) {
    return [0, 0, lightness];
  }
  const saturation = delta / (1 - Math.abs(2 * lightness - 1));
  const hue = max === red ? ((green - blue) / delta) % 6 : max === green ? (blue - red) / delta + 2 : (red - green) / delta + 4;
  return [(hue * 60 + 360) % 360, saturation, lightness];
}

export function fromHsl(hue: number, saturation: number, lightness: number): Rgb {
  const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
  const sector = (((hue % 360) + 360) % 360) / 60;
  const x = chroma * (1 - Math.abs((sector % 2) - 1));
  const [r, g, b] = sector < 1 ? [chroma, x, 0] : sector < 2 ? [x, chroma, 0] : sector < 3 ? [0, chroma, x] : sector < 4 ? [0, x, chroma] : sector < 5 ? [x, 0, chroma] : [chroma, 0, x];
  const m = lightness - chroma / 2;
  return [(r + m) * 255, (g + m) * 255, (b + m) * 255];
}

export interface BrandPalette {
  // Background of brand surfaces that carry text: header, visitor bubbles, buttons.
  brand: string;
  // White or near-black, whichever reads better on `brand`.
  onBrand: string;
  // Hover shades mix this in: darker under white text, lighter under dark text, so contrast only grows.
  hoverMix: string;
  // Brand-colored text, links and focus rings on the light surfaces, and on the dark ones.
  accentLight: string;
  accentDark: string;
}

export function brandPalette(value: string | null | undefined): BrandPalette {
  const base = parseHexColor(value) ?? parseHexColor(DEFAULT_BRAND_COLOR)!;
  const whiteText = contrastRatio(base, WHITE) >= contrastRatio(base, NEAR_BLACK);
  const text = whiteText ? WHITE : NEAR_BLACK;
  // Mid tones reach 4.5:1 with neither text color, so the surface moves away from the text until it does.
  const surface = contrastRatio(base, text) >= 4.5 ? base : towards(base, whiteText ? BLACK : WHITE, [text], 4.5);
  const soft = (background: Rgb) => mix(background, base, 0.1);
  return {
    brand: toHexColor(surface),
    onBrand: toHexColor(text),
    hoverMix: whiteText ? '#000000' : '#ffffff',
    accentLight: toHexColor(towards(base, BLACK, [...LIGHT_SURFACES, soft(WHITE)], 4.5)),
    accentDark: toHexColor(towards(base, WHITE, [...DARK_SURFACES, soft(DARK_SURFACES[0]!)], 4.5)),
  };
}
