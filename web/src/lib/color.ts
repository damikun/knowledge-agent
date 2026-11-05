const HEX_COLOR = /^#([0-9a-f]{6})$/i;

export function isHexColor(value: string): boolean {
  return HEX_COLOR.test(value.trim());
}

/** WCAG 2.2 relative luminance of a #rrggbb color; null for anything else. */
export function relativeLuminance(color: string): number | null {
  const match = HEX_COLOR.exec(color.trim());
  if (!match) {
    return null;
  }
  const hex = match[1]!;
  const channel = (offset: number) => {
    const value = Number.parseInt(hex.slice(offset, offset + 2), 16) / 255;
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(0) + 0.7152 * channel(2) + 0.0722 * channel(4);
}

/** From 1 to 21, as WCAG defines it; null when a color is not #rrggbb. */
export function contrastRatio(foreground: string, background: string): number | null {
  const a = relativeLuminance(foreground);
  const b = relativeLuminance(background);
  if (a === null || b === null) {
    return null;
  }
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

/** 4.5:1 for normal text (WCAG 1.4.3, level AA). */
export const MIN_TEXT_CONTRAST = 4.5;

export const WHITE = '#ffffff';
export const NEAR_BLACK = '#111111';

/** White or near-black, whichever reads better; the widget picks text on its brand color the same way. */
export function readableTextColor(background: string): string | null {
  const white = contrastRatio(WHITE, background);
  const dark = contrastRatio(NEAR_BLACK, background);
  if (white === null || dark === null) {
    return null;
  }
  return white >= dark ? WHITE : NEAR_BLACK;
}
