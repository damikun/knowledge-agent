import { useCallback, useSyncExternalStore } from 'react';

// Until someone chooses, windows narrower than a 13-inch laptop get icons only. The choice is kept per browser.
type SidebarPreference = 'auto' | 'expanded' | 'collapsed';

const SIDEBAR_STORAGE_KEY = 'knowledge-agent.sidebar';
const ROOMY_QUERY = '(min-width: 1280px)';

const listeners = new Set<() => void>();
// Holds the choice when storage is unavailable (private windows, blocked site data), for this page view.
let unstored: SidebarPreference = 'auto';

function readPreference(): SidebarPreference {
  try {
    const value = localStorage.getItem(SIDEBAR_STORAGE_KEY);
    return value === 'expanded' || value === 'collapsed' ? value : 'auto';
  } catch {
    return unstored;
  }
}

function setPreference(preference: SidebarPreference): void {
  unstored = preference;
  try {
    localStorage.setItem(SIDEBAR_STORAGE_KEY, preference);
  } catch {
    // Kept in `unstored` instead.
  }
  listeners.forEach((listener) => listener());
}

function subscribePreference(listener: () => void): () => void {
  listeners.add(listener);
  const onStorage = (event: StorageEvent) => {
    if (event.key === SIDEBAR_STORAGE_KEY) {
      listener();
    }
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', onStorage);
  };
}

function readRoomy(): boolean {
  return typeof window.matchMedia !== 'function' || window.matchMedia(ROOMY_QUERY).matches;
}

function subscribeRoomy(listener: () => void): () => void {
  if (typeof window.matchMedia !== 'function') {
    return () => undefined;
  }
  const media = window.matchMedia(ROOMY_QUERY);
  media.addEventListener('change', listener);
  return () => media.removeEventListener('change', listener);
}

export function useSidebar(): { collapsed: boolean; toggle: () => void } {
  const preference = useSyncExternalStore(subscribePreference, readPreference, () => 'auto' as const);
  const roomy = useSyncExternalStore(subscribeRoomy, readRoomy, () => true);
  const collapsed = preference === 'collapsed' || (preference === 'auto' && !roomy);
  const toggle = useCallback(() => setPreference(collapsed ? 'expanded' : 'collapsed'), [collapsed]);
  return { collapsed, toggle };
}
