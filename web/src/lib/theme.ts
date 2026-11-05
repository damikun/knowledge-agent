import { useCallback, useEffect, useSyncExternalStore } from 'react';

export type ThemePreference = 'system' | 'light' | 'dark';

// Keep in sync with the inline script in index.html.
const THEME_STORAGE_KEY = 'knowledge-agent.theme';

const listeners = new Set<() => void>();

function readPreference(): ThemePreference {
  try {
    const value = localStorage.getItem(THEME_STORAGE_KEY);
    return value === 'light' || value === 'dark' ? value : 'system';
  } catch {
    return 'system';
  }
}

function systemPrefersDark(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-color-scheme: dark)').matches
    : false;
}

function resolveTheme(preference: ThemePreference, prefersDark: boolean): 'light' | 'dark' {
  return preference === 'system' ? (prefersDark ? 'dark' : 'light') : preference;
}

export function applyTheme(preference: ThemePreference = readPreference()): void {
  const root = document.documentElement;
  const dark = resolveTheme(preference, systemPrefersDark()) === 'dark';
  if (root.classList.contains('dark') === dark) {
    return;
  }
  // Switch at once: index.css turns transitions off for a frame, or hover fades would trail behind.
  root.setAttribute('data-switching-theme', '');
  root.classList.toggle('dark', dark);
  // Force a reflow so the new colors apply while transitions are still off.
  void root.offsetWidth;
  requestAnimationFrame(() => root.removeAttribute('data-switching-theme'));
}

function setThemePreference(preference: ThemePreference): void {
  try {
    if (preference === 'system') {
      localStorage.removeItem(THEME_STORAGE_KEY);
    } else {
      localStorage.setItem(THEME_STORAGE_KEY, preference);
    }
  } catch {
    // Storage unavailable (private mode): the choice lasts for this page view.
  }
  applyTheme(preference);
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  const onStorage = (event: StorageEvent) => {
    if (event.key === THEME_STORAGE_KEY) {
      applyTheme();
      listener();
    }
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', onStorage);
  };
}

export function useTheme(): { preference: ThemePreference; setPreference: (value: ThemePreference) => void } {
  const preference = useSyncExternalStore(subscribe, readPreference, () => 'system' as const);

  useEffect(() => {
    if (preference !== 'system' || typeof window.matchMedia !== 'function') {
      return undefined;
    }
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => applyTheme('system');
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, [preference]);

  const setPreference = useCallback((value: ThemePreference) => setThemePreference(value), []);
  return { preference, setPreference };
}
