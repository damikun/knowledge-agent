import { resolveTheme, type ColorMode, type ResolvedTheme } from '../shared/theme';
import type { WidgetConfig } from './types';

export type ThemeSettings = Pick<WidgetConfig, 'brandColor' | 'theme' | 'preset' | 'colors'>;

export function resolveFor(settings: ThemeSettings, mode: ColorMode): ResolvedTheme {
  return resolveTheme({ preset: settings.preset, brandColor: settings.brandColor, mode, overrides: settings.colors[mode] });
}

export function paintTheme(theme: ResolvedTheme, root: HTMLElement = document.documentElement): void {
  for (const [name, value] of Object.entries(theme.variables)) {
    root.style.setProperty(name, value);
  }
  root.dataset.theme = theme.mode;
  root.dataset.glow = String(theme.effects.glow);
  root.dataset.glass = String(theme.effects.glass);
}

// "auto" follows the visitor's system, also when it changes.
export function applyTheme(settings: ThemeSettings, root: HTMLElement = document.documentElement): () => void {
  if (settings.theme !== 'auto') {
    paintTheme(resolveFor(settings, settings.theme), root);
    return () => undefined;
  }
  const query = typeof window.matchMedia === 'function' ? window.matchMedia('(prefers-color-scheme: dark)') : null;
  paintTheme(resolveFor(settings, query?.matches ? 'dark' : 'light'), root);
  if (!query) {
    return () => undefined;
  }
  const onChange = (event: MediaQueryListEvent) => paintTheme(resolveFor(settings, event.matches ? 'dark' : 'light'), root);
  query.addEventListener('change', onChange);
  return () => query.removeEventListener('change', onChange);
}
