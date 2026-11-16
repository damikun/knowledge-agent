import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

function primaryActionsOutsideDialogs(): string[] {
  return [...document.body.querySelectorAll<HTMLElement>('.action-primary')]
    .filter((element) => !element.closest('[role="dialog"], [role="alertdialog"], [hidden], [aria-hidden="true"], .sr-only'))
    .map((element) => element.textContent?.trim() || element.getAttribute('aria-label') || element.tagName);
}

const KNOWN_PRIMARY_PAIRS = new Set([
  'Take over, Send reply',
]);

afterEach(() => {
  const primaries = primaryActionsOutsideDialogs();
  if (primaries.length > 1 && !KNOWN_PRIMARY_PAIRS.has(primaries.join(', '))) {
    cleanup();
    throw new Error(`More than one primary action in one view: ${primaries.join(', ')}. Keep one primary; make the others secondary or ghost.`);
  }
  cleanup();
  localStorage.clear();
});

if (typeof window.matchMedia !== 'function') {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string): MediaQueryList =>
      ({
        matches: false,
        media: query,
        onchange: null,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        addListener: () => undefined,
        removeListener: () => undefined,
        dispatchEvent: () => false,
      }) as MediaQueryList,
  });
}

if (typeof Element.prototype.scrollIntoView !== 'function') {
  Element.prototype.scrollIntoView = () => undefined;
}

if (typeof Element.prototype.setPointerCapture !== 'function') {
  Element.prototype.setPointerCapture = () => undefined;
  Element.prototype.releasePointerCapture = () => undefined;
  Element.prototype.hasPointerCapture = () => false;
}

Object.defineProperty(window.navigator, 'vendor', { value: 'Google Inc.', configurable: true });
