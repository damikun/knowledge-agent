import { describe, expect, it } from 'vitest';
import { contrastRatio, isHexColor, NEAR_BLACK, readableTextColor, relativeLuminance, WHITE } from './color';

describe('color', () => {
  it('accepts #rrggbb colors only', () => {
    expect(isHexColor('#0f766e')).toBe(true);
    expect(isHexColor(' #0F766E ')).toBe(true);
    expect(isHexColor('#fff')).toBe(false);
    expect(isHexColor('teal')).toBe(false);
  });

  it('computes the relative luminance', () => {
    expect(relativeLuminance('#000000')).toBe(0);
    expect(relativeLuminance('#ffffff')).toBeCloseTo(1, 5);
    expect(relativeLuminance('red')).toBeNull();
  });

  it('computes the contrast ratio in either order', () => {
    expect(contrastRatio('#ffffff', '#000000')).toBeCloseTo(21, 5);
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrastRatio('#ffffff', '#0f766e')).toBeCloseTo(5.47, 2);
    expect(contrastRatio('#ffffff', '#facc15')).toBeLessThan(2);
    expect(contrastRatio('#ffffff', 'nope')).toBeNull();
  });

  it('picks white or near-black text, whichever reads better', () => {
    expect(readableTextColor('#0f766e')).toBe(WHITE);
    expect(readableTextColor('#facc15')).toBe(NEAR_BLACK);
    expect(readableTextColor('#8b5cf6')).toBe(NEAR_BLACK);
    expect(readableTextColor('teal')).toBeNull();
  });
});
