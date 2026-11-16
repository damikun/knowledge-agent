import { createContext, useContext, useMemo } from 'react';
import { useLocation } from 'react-router';

export interface ReturnTo {
  label: string;
  to: string;
  idx?: number;
}

export interface ReturnState {
  returnTo: ReturnTo;
}

const MAX_LABEL = 120;
const MAX_PATH = 2048;

const BACK_SHORTCUTS = 'Alt+← / ⌘[';

export function isSafeReturnPath(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > MAX_PATH) {
    return false;
  }
  if (!value.startsWith('/') || value.startsWith('//') || value.includes('\\') || [...value].some((char) => char.charCodeAt(0) < 32)) {
    return false;
  }
  try {
    const base = 'http://portal.invalid';
    return new URL(value, base).origin === base;
  } catch {
    return false;
  }
}

export function readReturnTo(state: unknown): ReturnTo | null {
  if (typeof state !== 'object' || state === null || !('returnTo' in state)) {
    return null;
  }
  const value = (state as { returnTo: unknown }).returnTo;
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const { label, to, idx } = value as Record<string, unknown>;
  if (typeof label !== 'string' || label.trim() === '' || !isSafeReturnPath(to)) {
    return null;
  }
  const result: ReturnTo = { label: label.trim().slice(0, MAX_LABEL), to };
  if (typeof idx === 'number' && Number.isInteger(idx) && idx >= 0) {
    result.idx = idx;
  }
  return result;
}

export function historyIndex(): number | null {
  if (typeof window === 'undefined') {
    return null;
  }
  const idx = (window.history.state as { idx?: unknown } | null)?.idx;
  return typeof idx === 'number' && Number.isInteger(idx) ? idx : null;
}

export const CurrentPlaceContext = createContext<string>('');

export function useReturnState(label?: string): ReturnState {
  const { pathname, search } = useLocation();
  const place = useContext(CurrentPlaceContext);
  const idx = historyIndex();
  const name = label?.trim() || place || 'previous page';
  const to = `${pathname}${search}`;
  return useMemo(() => ({ returnTo: { label: name, to, ...(idx === null ? {} : { idx }) } }), [name, to, idx]);
}

function useReturnTo(): ReturnTo | null {
  return readReturnTo(useLocation().state);
}

export function useKeptReturnState(): ReturnState | undefined {
  const returnTo = useReturnTo();
  return returnTo ? { returnTo } : undefined;
}

export function returnTitle(label: string): string {
  return `Back to ${label} (${BACK_SHORTCUTS})`;
}
