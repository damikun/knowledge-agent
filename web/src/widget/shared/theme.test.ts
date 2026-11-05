import { describe, expect, it } from 'vitest';
import { contrastRatio, parseHexColor, toHsl } from './color';
import { COLOR_TOKENS, contrastProblems, panelBackdrops, resolveTheme, THEME_PRESETS, themeColors, TRANSPARENT, type ColorMode } from './theme';

const BRANDS = ['#0f766e', '#7c3aed', '#facc15', '#e11d48', '#22d3ee', '#767676', '#ffffff', '#000000', 'not a color'];
const MODES: ColorMode[] = ['light', 'dark'];

function ratio(a: string, b: readonly [number, number, number]): number {
  return contrastRatio(parseHexColor(a)!, b);
}

describe('widget themes (WCAG 2.2 AA)', () => {
  describe.each(THEME_PRESETS)('the %s preset', (preset) => {
    it.each(MODES.flatMap((mode) => BRANDS.map((brand) => [mode, brand] as const)))('keeps every text readable in %s mode with the brand %s', (mode, brandColor) => {
      const theme = resolveTheme({ preset, brandColor, mode });
      expect(contrastProblems(theme)).toEqual([]);

      // Also over a black or a white page behind the glass.
      const backdrops = panelBackdrops(parseHexColor(theme.colors.panel)!, mode, theme.effects.glass);
      for (const backdrop of backdrops) {
        expect(ratio(theme.variables['--ka-accent-text']!, backdrop)).toBeGreaterThanOrEqual(4.5);
        expect(ratio(theme.variables['--ka-danger']!, backdrop)).toBeGreaterThanOrEqual(4.5);
        expect(ratio(theme.variables['--ka-control']!, backdrop)).toBeGreaterThanOrEqual(3);
      }
    });
  });

  it('gives glass presets a translucent panel and a glass header, and the solid one an opaque panel and a brand header', () => {
    const glow = resolveTheme({ preset: 'glow', brandColor: '#7c3aed', mode: 'light' });
    expect(glow.effects).toEqual({ glass: true, glow: true, radius: 28 });
    expect(glow.variables['--ka-panel-fill']).toBe('rgb(255 255 255 / 0.84)');
    expect(glow.colors.header).toBe(TRANSPARENT);
    expect(glow.variables['--ka-user-bubble-end']).not.toBe(glow.colors.userBubble);

    const glass = resolveTheme({ preset: 'glass', brandColor: '#7c3aed', mode: 'dark' });
    expect(glass.effects.glow).toBe(false);
    expect(glass.variables['--ka-user-bubble-end']).toBe(glass.colors.userBubble);

    const solid = resolveTheme({ preset: 'solid', brandColor: '#7c3aed', mode: 'dark' });
    expect(solid.effects).toEqual({ glass: false, glow: false, radius: 20 });
    expect(solid.variables['--ka-panel-fill']).toBe('#1c1c1e');
    expect(solid.colors).toMatchObject({ header: '#7c3aed', headerText: '#ffffff' });
  });

  it('takes overrides per token, and brings readable text with a changed background unless that text is changed too', () => {
    const theme = resolveTheme({
      preset: 'glow',
      brandColor: '#0f766e',
      mode: 'light',
      overrides: { header: '#facc15', userBubble: '#1e3a8a', launcher: '#ffffff', assistantBubble: '#0b1220', glow: '#f472b6' },
    });
    expect(theme.colors).toMatchObject({
      header: '#facc15',
      headerText: '#111111',
      userBubble: '#1e3a8a',
      userBubbleText: '#ffffff',
      launcher: '#ffffff',
      launcherIcon: '#111111',
      assistantBubbleText: '#ffffff',
      glow: '#f472b6',
    });
    expect(theme.variables['--ka-glow-1']).toBe('#f472b6');
    expect(contrastProblems(theme)).toEqual([]);
  });

  it('keeps a changed text color as given and reports it when it falls below AA', () => {
    const theme = resolveTheme({ preset: 'solid', brandColor: '#0f766e', mode: 'light', overrides: { header: '#ffffff', headerText: '#eeeeee' } });
    expect(theme.colors.headerText).toBe('#eeeeee');
    expect(contrastProblems(theme)).toEqual([{ text: 'headerText', background: 'header', ratio: expect.closeTo(1.16, 2) }]);
  });

  it('follows a changed panel with readable text, muted text and borders', () => {
    const theme = resolveTheme({ preset: 'solid', brandColor: '#0f766e', mode: 'light', overrides: { panel: '#0b1220' } });
    expect(theme.colors.text).toBe('#ffffff');
    expect(contrastProblems(theme)).toEqual([]);
  });

  it('falls back to the default preset and ignores colors that are not #rrggbb', () => {
    const theme = resolveTheme({ preset: 'neon', brandColor: '#0f766e', mode: 'dark', overrides: { accent: 'teal' } });
    expect(theme.preset).toBe('glow');
    expect(theme.colors.accent).toBe(themeColors('glow', '#0f766e', 'dark').accent);
  });

  it.each(['#7c3aed', '#0f766e', '#e11d48', '#facc15', '#2563eb'])('keeps the glow of %s in the hues around the brand', (brand) => {
    const [brandHue] = toHsl(parseHexColor(brand)!);
    for (const mode of ['light', 'dark'] as const) {
      const { variables } = resolveTheme({ preset: 'glow', brandColor: brand, mode });
      for (const stop of ['--ka-glow-1', '--ka-glow-2', '--ka-glow-3', '--ka-glow-4']) {
        const [hue] = toHsl(parseHexColor(variables[stop]!)!);
        const distance = Math.abs(((hue - brandHue + 540) % 360) - 180);
        expect(distance, `${stop} in ${mode}`).toBeLessThanOrEqual(45);
      }
    }
  });

  it('gives a gray brand a gray glow rather than a red one', () => {
    for (const brand of ['#767676', '#000000', '#ffffff']) {
      const { variables } = resolveTheme({ preset: 'glow', brandColor: brand, mode: 'dark' });
      const [, saturation] = toHsl(parseHexColor(variables['--ka-glow-1']!)!);
      expect(saturation, brand).toBeLessThan(0.12);
    }
  });

  it('defines every token in every preset and mode', () => {
    for (const preset of THEME_PRESETS) {
      for (const mode of MODES) {
        expect(Object.keys(themeColors(preset, '#0f766e', mode))).toEqual([...COLOR_TOKENS]);
      }
    }
  });
});
