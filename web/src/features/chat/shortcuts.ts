import type { KeyboardEvent as ReactKeyboardEvent } from 'react';

function isApple(): boolean {
  return typeof navigator !== 'undefined' && /Mac|iPhone|iPad/i.test(navigator.platform || navigator.userAgent);
}

/** "⌘" on Apple devices, "Ctrl+" elsewhere, for shortcut hints. */
export function modKey(): string {
  return isApple() ? '⌘' : 'Ctrl+';
}

export const SEARCH_SHORTCUT = {
  get label() {
    return `${modKey()}K`;
  },
  aria: 'Meta+K Control+K',
};

// Same keys as ChatGPT.
export const NEW_CHAT_SHORTCUT = {
  get label() {
    return isApple() ? '⌘⇧O' : 'Ctrl+Shift+O';
  },
  aria: 'Meta+Shift+O Control+Shift+O',
};

type AnyKeyboardEvent = KeyboardEvent | ReactKeyboardEvent;

export function isSearchShortcut(event: AnyKeyboardEvent): boolean {
  return (event.metaKey || event.ctrlKey) && !event.shiftKey && !event.altKey && event.key.toLowerCase() === 'k';
}

export function isNewChatShortcut(event: AnyKeyboardEvent): boolean {
  return (event.metaKey || event.ctrlKey) && event.shiftKey && !event.altKey && event.key.toLowerCase() === 'o';
}
