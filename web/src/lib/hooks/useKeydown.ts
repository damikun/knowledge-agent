import { useEffect, useEffectEvent } from 'react';

export interface KeydownOptions {
  enabled?: boolean;
  /** The document hears a key before the window does. */
  target?: 'window' | 'document';
}

export function useKeydown(onKeyDown: (event: KeyboardEvent) => void, { enabled = true, target = 'window' }: KeydownOptions = {}): void {
  const handle = useEffectEvent(onKeyDown);
  useEffect(() => {
    if (!enabled) {
      return undefined;
    }
    const node: EventTarget = target === 'document' ? document : window;
    const listener = (event: Event) => handle(event as KeyboardEvent);
    node.addEventListener('keydown', listener);
    return () => node.removeEventListener('keydown', listener);
  }, [enabled, target]);
}

/** Esc leaves a full-page flow, unless it is closing a menu, a list or a dialog of the page's own. */
export function useEscapeToLeave(onLeave: () => void): void {
  useKeydown((event) => {
    if (event.key === 'Escape' && !event.defaultPrevented && !document.querySelector('[role="dialog"], [role="listbox"], [role="menu"]')) {
      onLeave();
    }
  });
}
