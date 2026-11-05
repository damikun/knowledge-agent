import {
  BLACK,
  brandPalette,
  contrastRatio,
  fromHsl,
  legibleOn,
  mix,
  parseHexColor,
  readableOn,
  rounded,
  toHexColor,
  toHsl,
  WHITE,
  type Rgb,
} from './color';

// A preset derives every color from the brand color, per mode; any color can be overridden per mode.

export const THEME_PRESETS = ['glow', 'glass', 'solid'] as const;
export type ThemePreset = (typeof THEME_PRESETS)[number];
export const DEFAULT_PRESET: ThemePreset = 'glow';

/** Mirrors WidgetChoices.ColorTokens of the API. */
export const COLOR_TOKENS = [
  'accent',
  'onAccent',
  'panel',
  'text',
  'muted',
  'border',
  'header',
  'headerText',
  'userBubble',
  'userBubbleText',
  'assistantBubble',
  'assistantBubbleText',
  'launcher',
  'launcherIcon',
  'glow',
] as const;
export type ColorToken = (typeof COLOR_TOKENS)[number];

export type ColorMode = 'light' | 'dark';
type ColorOverrides = Partial<Record<ColorToken, string>>;

export interface PresetEffects {
  /** A translucent panel that blurs the page behind it. */
  glass: boolean;
  /** The animated gradient glow around the launcher, and around the chat while it answers. */
  glow: boolean;
  /** Of the panel, in pixels. */
  radius: number;
}

export const PRESET_EFFECTS: Readonly<Record<ThemePreset, PresetEffects>> = {
  glow: { glass: true, glow: true, radius: 28 },
  glass: { glass: true, glow: false, radius: 28 },
  solid: { glass: false, glow: false, radius: 20 },
};

/** Text on a background, which the editor checks against WCAG 2.2 AA. The launcher may carry a label, so it is text. */
const COLOR_PAIRS: ReadonlyArray<{ text: ColorToken; background: ColorToken }> = [
  { text: 'onAccent', background: 'accent' },
  { text: 'text', background: 'panel' },
  { text: 'muted', background: 'panel' },
  { text: 'headerText', background: 'header' },
  { text: 'userBubbleText', background: 'userBubble' },
  { text: 'assistantBubbleText', background: 'assistantBubble' },
  { text: 'launcherIcon', background: 'launcher' },
];

const MIN_CONTRAST = 4.5;

// A glass header has no color of its own: the panel shows through.
export const TRANSPARENT = 'transparent';

// Opaque enough that text keeps 4.5:1 even when the page behind the glass is black or white.
const GLASS_OPACITY: Readonly<Record<ColorMode, number>> = { light: 0.84, dark: 0.82 };

const NEUTRALS: Readonly<Record<ColorMode, { panel: Rgb; text: Rgb; muted: Rgb; danger: Rgb }>> = {
  light: { panel: [255, 255, 255], text: [29, 29, 31], muted: [87, 87, 92], danger: [185, 28, 28] },
  dark: { panel: [28, 28, 30], text: [245, 245, 247], muted: [176, 176, 181], danger: [252, 165, 165] },
};

function hex(value: string | undefined): Rgb | null {
  return value ? parseHexColor(value) : null;
}

/** The colors a widget's panel shows behind its text: the tint itself, or, for glass, the tint over a black and a white page. */
export function panelBackdrops(panel: Rgb, mode: ColorMode, glass: boolean): Rgb[] {
  if (!glass) {
    return [panel];
  }
  const opacity = GLASS_OPACITY[mode];
  return [mix(BLACK, panel, opacity), mix(WHITE, panel, opacity)];
}

// A second gradient stop beside `color`, as far along the hue circle as its text allows.
function gradientEnd(color: Rgb, text: Rgb): Rgb {
  const [hue, saturation, lightness] = toHsl(color);
  const turned = fromHsl(hue + 32, saturation, Math.min(0.9, lightness + 0.04));
  for (let step = 0; step <= 10; step++) {
    const candidate = rounded(mix(turned, color, step / 10));
    if (contrastRatio(text, candidate) >= MIN_CONTRAST) {
      return candidate;
    }
  }
  return color;
}

// The glow color and three neighbors on the hue circle, so the glow stays in the brand's family instead of
// running through unrelated hues (a purple brand glows violet, pink and blue, never yellow or green).
function glowStops(glow: Rgb): Rgb[] {
  const [hue, saturation, lightness] = toHsl(glow);
  return [0, 34, -18, -42].map((turn) => fromHsl(hue + turn, saturation, lightness));
}

/** What a preset makes of the brand color in a mode, before overrides. */
export function themeColors(preset: ThemePreset, brandColor: string, mode: ColorMode): Record<ColorToken, string> {
  const palette = brandPalette(brandColor);
  const neutral = NEUTRALS[mode];
  const accent = parseHexColor(palette.brand)!;
  const onAccent = parseHexColor(palette.onBrand)!;
  const [hue, saturation] = toHsl(parseHexColor(brandColor) ?? accent);
  // A vivid glow for a colorful brand; a gray, black or white brand keeps a gray one rather than turning red (hue 0).
  const glowSaturation = saturation < 0.12 ? saturation : Math.min(0.95, Math.max(0.75, saturation));
  const glow = fromHsl(hue, glowSaturation, mode === 'light' ? 0.58 : 0.64);
  const solid = preset === 'solid';
  return {
    accent: palette.brand,
    onAccent: palette.onBrand,
    panel: toHexColor(neutral.panel),
    text: toHexColor(neutral.text),
    muted: toHexColor(neutral.muted),
    border: toHexColor(mix(neutral.panel, neutral.text, mode === 'light' ? 0.12 : 0.16)),
    header: solid ? palette.brand : TRANSPARENT,
    headerText: solid ? palette.onBrand : toHexColor(neutral.text),
    userBubble: palette.brand,
    userBubbleText: toHexColor(onAccent),
    assistantBubble: toHexColor(mix(neutral.panel, neutral.text, mode === 'light' ? 0.06 : 0.1)),
    assistantBubbleText: toHexColor(neutral.text),
    launcher: palette.brand,
    launcherIcon: palette.onBrand,
    glow: toHexColor(glow),
  };
}

export interface ResolvedTheme {
  preset: ThemePreset;
  mode: ColorMode;
  effects: PresetEffects;
  /** Every token's color after overrides; a glass header is transparent. */
  colors: Record<ColorToken, string>;
  /** CSS custom properties, for the frame's root and the loader's shadow host. */
  variables: Record<string, string>;
}

export interface ThemeInput {
  preset: string;
  brandColor: string;
  mode: ColorMode;
  overrides?: ColorOverrides | undefined;
}

export function isThemePreset(value: unknown): value is ThemePreset {
  return typeof value === 'string' && (THEME_PRESETS as readonly string[]).includes(value);
}

export interface ThemeColors {
  light: ColorOverrides;
  dark: ColorOverrides;
}

function readOverrides(value: unknown): ColorOverrides {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return {};
  }
  return Object.fromEntries(
    Object.entries(value).filter(
      (entry): entry is [ColorToken, string] =>
        (COLOR_TOKENS as readonly string[]).includes(entry[0]) && typeof entry[1] === 'string' && parseHexColor(entry[1]) !== null,
    ),
  );
}

/** The overrides of a widget config: known tokens with #rrggbb colors only. */
export function readThemeColors(value: unknown): ThemeColors {
  const colors = typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};
  return { light: readOverrides(colors.light), dark: readOverrides(colors.dark) };
}

function rgb(color: Rgb, alpha = 1): string {
  const [r, g, b] = color.map((channel) => Math.round(channel));
  return alpha >= 1 ? toHexColor(color) : `rgb(${r} ${g} ${b} / ${alpha})`;
}

export function resolveTheme({ preset: presetName, brandColor, mode, overrides = {} }: ThemeInput): ResolvedTheme {
  const preset = isThemePreset(presetName) ? presetName : DEFAULT_PRESET;
  const effects = PRESET_EFFECTS[preset];
  const base = themeColors(preset, brandColor, mode);
  const own = (token: ColorToken) => hex(overrides[token]);
  const pick = (token: ColorToken) => own(token) ?? parseHexColor(base[token]);

  const panel = pick('panel')!;
  const backdrops = panelBackdrops(panel, mode, effects.glass);
  // A changed background brings readable text with it, unless that text is changed too.
  const textOn = (token: ColorToken, background: Rgb | null, fallback: Rgb) => own(token) ?? (background ? readableOn(background) : fallback);
  const text = own('text') ?? (own('panel') ? readableOn(panel) : pick('text')!);
  const muted = own('muted') ?? legibleOn(own('text') || own('panel') ? mix(text, panel, 0.35) : pick('muted')!, backdrops, MIN_CONTRAST);
  const accent = pick('accent')!;
  const onAccent = textOn('onAccent', own('accent'), pick('onAccent')!);
  const header = own('header') ?? (base.header === TRANSPARENT ? null : parseHexColor(base.header));
  const headerText = own('headerText') ?? (own('header') ? readableOn(own('header')!) : base.header === TRANSPARENT ? text : pick('headerText')!);
  const userBubble = own('userBubble') ?? accent;
  const userBubbleText = textOn('userBubbleText', own('userBubble') ?? own('accent'), pick('userBubbleText')!);
  const assistantBubble = own('assistantBubble') ?? (own('panel') ? mix(panel, text, 0.08) : pick('assistantBubble')!);
  const assistantBubbleText = own('assistantBubbleText') ?? (own('assistantBubble') ? readableOn(assistantBubble) : text);
  const launcher = own('launcher') ?? accent;
  const launcherIcon = textOn('launcherIcon', own('launcher') ?? own('accent'), pick('launcherIcon')!);
  const glow = pick('glow')!;
  const border = own('border') ?? (own('panel') || own('text') ? mix(panel, text, 0.14) : pick('border')!);
  const [glow1, glow2, glow3, glow4] = glowStops(glow) as [Rgb, Rgb, Rgb, Rgb];
  const hoverMix = readableOn(accent) === WHITE ? BLACK : WHITE;

  const colors: Record<ColorToken, string> = {
    accent: toHexColor(accent),
    onAccent: toHexColor(onAccent),
    panel: toHexColor(panel),
    text: toHexColor(text),
    muted: toHexColor(muted),
    border: toHexColor(border),
    header: header ? toHexColor(header) : TRANSPARENT,
    headerText: toHexColor(headerText),
    userBubble: toHexColor(userBubble),
    userBubbleText: toHexColor(userBubbleText),
    assistantBubble: toHexColor(assistantBubble),
    assistantBubbleText: toHexColor(assistantBubbleText),
    launcher: toHexColor(launcher),
    launcherIcon: toHexColor(launcherIcon),
    glow: toHexColor(glow),
  };

  const gradient = effects.glow;
  const variables: Record<string, string> = {
    '--ka-accent': colors.accent,
    '--ka-on-accent': colors.onAccent,
    // Accent-colored text and focus rings, on the panel.
    '--ka-accent-text': toHexColor(legibleOn(accent, backdrops, MIN_CONTRAST)),
    '--ka-accent-hover': toHexColor(mix(accent, hoverMix, 0.12)),
    '--ka-accent-soft': rgb(accent, mode === 'light' ? 0.1 : 0.18),
    '--ka-panel': colors.panel,
    '--ka-panel-fill': rgb(panel, effects.glass ? GLASS_OPACITY[mode] : 1),
    '--ka-text': colors.text,
    '--ka-muted': colors.muted,
    '--ka-border': colors.border,
    '--ka-raised': rgb(text, mode === 'light' ? 0.05 : 0.08),
    // Input borders: 3:1 against the panel (WCAG 1.4.11).
    '--ka-control': toHexColor(legibleOn(mix(panel, text, 0.45), backdrops, 3)),
    '--ka-danger': toHexColor(legibleOn(NEUTRALS[mode].danger, backdrops, MIN_CONTRAST)),
    '--ka-header': colors.header,
    '--ka-header-text': colors.headerText,
    '--ka-user-bubble': colors.userBubble,
    '--ka-user-bubble-end': toHexColor(gradient ? gradientEnd(userBubble, userBubbleText) : userBubble),
    '--ka-user-bubble-text': colors.userBubbleText,
    '--ka-assistant-bubble': rgb(assistantBubble, effects.glass ? 0.86 : 1),
    '--ka-assistant-bubble-text': colors.assistantBubbleText,
    '--ka-launcher': colors.launcher,
    '--ka-launcher-end': toHexColor(gradient ? gradientEnd(launcher, launcherIcon) : launcher),
    '--ka-launcher-icon': colors.launcherIcon,
    '--ka-glow-1': toHexColor(glow1),
    '--ka-glow-2': toHexColor(glow2),
    '--ka-glow-3': toHexColor(glow3),
    '--ka-glow-4': toHexColor(glow4),
    '--ka-radius': `${effects.radius}px`,
  };
  return { preset, mode, effects, colors, variables };
}

export interface ContrastProblem {
  text: ColorToken;
  background: ColorToken;
  /** The lowest contrast over every background the text can sit on. */
  ratio: number;
}

// The colors a token's text really sits on: the glass over a black and a white page, a bubble over the glass, and
// both ends of a gradient.
function surfacesOf(theme: ResolvedTheme, background: ColorToken): Rgb[] {
  const { colors, variables, mode, effects } = theme;
  const backdrops = panelBackdrops(parseHexColor(colors.panel)!, mode, effects.glass);
  const solid = (value: string | undefined) => (value ? parseHexColor(value) : null);
  switch (background) {
    case 'panel':
      return backdrops;
    case 'header':
      return colors.header === TRANSPARENT ? backdrops : [solid(colors.header)!];
    case 'assistantBubble': {
      const bubble = solid(colors.assistantBubble)!;
      return effects.glass ? backdrops.map((backdrop) => mix(backdrop, bubble, 0.86)) : [bubble];
    }
    case 'userBubble':
      return [solid(colors.userBubble)!, solid(variables['--ka-user-bubble-end'])!];
    case 'launcher':
      return [solid(colors.launcher)!, solid(variables['--ka-launcher-end'])!];
    default:
      return [solid(colors[background])!];
  }
}

/** Text that falls below WCAG 2.2 AA (4.5:1) on its background. */
export function contrastProblems(theme: ResolvedTheme): ContrastProblem[] {
  return COLOR_PAIRS.flatMap(({ text, background }) => {
    const color = parseHexColor(theme.colors[text])!;
    const ratio = Math.min(...surfacesOf(theme, background).map((surface) => contrastRatio(color, surface)));
    return ratio < MIN_CONTRAST ? [{ text, background, ratio }] : [];
  });
}
