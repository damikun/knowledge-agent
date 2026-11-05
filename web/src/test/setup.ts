import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// At most one primary action per view. A dialog or sheet is its own view; hidden content doesn't count.
function primaryActionsOutsideDialogs(): string[] {
  return [...document.body.querySelectorAll<HTMLElement>('.action-primary')]
    .filter((element) => !element.closest('[role="dialog"], [role="alertdialog"], [hidden], [aria-hidden="true"], .sr-only'))
    .map((element) => element.textContent?.trim() || element.getAttribute('aria-label') || element.tagName);
}

// Known violations; this list may only shrink.
const KNOWN_PRIMARY_PAIRS = new Set([
  // Inbox: Take over and the reply box's Send both show while a handover waits (features/inbox).
  'Take over, Send reply',
]);

// Vitest runs without globals, so Testing Library's automatic cleanup must be registered here.
afterEach(() => {
  const primaries = primaryActionsOutsideDialogs();
  if (primaries.length > 1 && !KNOWN_PRIMARY_PAIRS.has(primaries.join(', '))) {
    cleanup();
    throw new Error(`More than one primary action in one view: ${primaries.join(', ')}. Keep one primary; make the others secondary or ghost.`);
  }
  cleanup();
  localStorage.clear();
});

// jsdom lacks a few browser APIs the portal uses.
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

// jsdom names Apple as navigator.vendor, which Floating UI reads as Safari: its focus guards then get a role only
// VoiceOver needs. The tests check the markup that Chrome and Firefox get.
Object.defineProperty(window.navigator, 'vendor', { value: 'Google Inc.', configurable: true });
