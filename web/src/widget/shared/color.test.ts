import { describe, expect, it } from 'vitest';
import { brandPalette, contrastRatio, fromHsl, legibleOn, parseHexColor, readableOn, toHexColor, toHsl, WHITE } from './color';

function ratio(a: string, b: string): number {
  return contrastRatio(parseHexColor(a)!, parseHexColor(b)!);
}

describe('widget colors (WCAG 2.2 AA)', () => {
  it.each(['#0f766e', '#7c3aed', '#facc15', '#767676', '#808080', '#e11d48', '#ffffff', '#000000', '#22d3ee', 'not a color'])(
    'derives readable colors from the brand color %s',
    (brand) => {
      const palette = brandPalette(brand);
      expect(ratio(palette.onBrand, palette.brand)).toBeGreaterThanOrEqual(4.5);
      for (const background of ['#ffffff', '#f4f4f5']) {
        expect(ratio(palette.accentLight, background)).toBeGreaterThanOrEqual(4.5);
      }
      for (const background of ['#18181b', '#27272a']) {
        expect(ratio(palette.accentDark, background)).toBeGreaterThanOrEqual(4.5);
      }
    },
  );

  it('keeps a brand color that already reads well, and picks dark text on light brands', () => {
    expect(brandPalette('#7c3aed')).toMatchObject({ brand: '#7c3aed', onBrand: '#ffffff', hoverMix: '#000000' });
    expect(brandPalette('#facc15')).toMatchObject({ brand: '#facc15', onBrand: '#111111', hoverMix: '#ffffff' });
  });

  it('moves a color as little as it must to read on every background, and keeps one that already does', () => {
    const teal = parseHexColor('#14b8a6')!;
    expect(legibleOn(teal, [[0, 0, 0]], 4.5)).toBe(teal);
    const onWhite = legibleOn(teal, [WHITE, [244, 244, 245]], 4.5);
    expect(contrastRatio(onWhite, WHITE)).toBeGreaterThanOrEqual(4.5);
    expect(toHexColor(readableOn(parseHexColor('#facc15')!))).toBe('#111111');
  });

  it('turns colors around the hue circle and back', () => {
    const [hue, saturation, lightness] = toHsl(parseHexColor('#7c3aed')!);
    expect(hue).toBeCloseTo(262, 0);
    expect(toHexColor(fromHsl(hue, saturation, lightness))).toBe('#7c3aed');
    expect(toHexColor(fromHsl(hue + 360, saturation, lightness))).toBe('#7c3aed');
  });
});
