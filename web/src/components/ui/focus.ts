import { useEffect, useEffectEvent, type RefObject } from 'react';

const FOCUSABLE = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
  'summary',
].join(',');

function focusableElements(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
    (el) => !el.hasAttribute('inert') && el.getAttribute('aria-hidden') !== 'true',
  );
}

export interface FocusTrapOptions {
  active: boolean;
  onEscape?: () => void;
  initialFocus?: RefObject<HTMLElement | null>;
  restoreFocus?: boolean;
  /** Keeps Tab inside the container, as modal dialogs do; a non-modal panel lets focus move on to the page. */
  contain?: boolean;
}

export function useFocusTrap(container: RefObject<HTMLElement | null>, options: FocusTrapOptions): void {
  const { active, initialFocus, restoreFocus = true, contain = true, onEscape } = options;
  // Whether Esc was handled here, read when the key is pressed, so a new onEscape never moves the focus again.
  const escape = useEffectEvent((): boolean => {
    onEscape?.();
    return onEscape !== undefined;
  });

  useEffect(() => {
    if (!active) {
      return undefined;
    }
    const node = container.current;
    if (!node) {
      return undefined;
    }
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const target = initialFocus?.current ?? focusableElements(node)[0] ?? node;
    target.focus({ preventScroll: true });

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        if (escape()) {
          event.stopPropagation();
        }
        return;
      }
      if (event.key !== 'Tab' || !contain) {
        return;
      }
      const items = focusableElements(node);
      if (items.length === 0) {
        event.preventDefault();
        node.focus();
        return;
      }
      const first = items[0]!;
      const last = items[items.length - 1]!;
      const current = document.activeElement;
      if (event.shiftKey && (current === first || current === node)) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && current === last) {
        event.preventDefault();
        first.focus();
      }
    };
    node.addEventListener('keydown', onKeyDown);
    return () => {
      node.removeEventListener('keydown', onKeyDown);
      if (restoreFocus && previouslyFocused && document.contains(previouslyFocused)) {
        previouslyFocused.focus({ preventScroll: true });
      }
    };
  }, [active, container, initialFocus, restoreFocus, contain]);
}

export function useScrollLock(active: boolean): void {
  useEffect(() => {
    if (!active) {
      return undefined;
    }
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = previous;
    };
  }, [active]);
}
