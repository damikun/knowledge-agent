import { useLayoutEffect, useState } from 'react';

/**
 * Props for a box that may scroll, such as a wide table or a long code block. While its content overflows it takes
 * focus, so keyboard users can scroll it too (WCAG 2.1.1); otherwise it stays out of the tab order.
 */
export function useScrollFocus<T extends HTMLElement>(): { ref: (element: T | null) => void; tabIndex: 0 | undefined } {
  const [element, setElement] = useState<T | null>(null);
  const [scrolls, setScrolls] = useState(false);

  useLayoutEffect(() => {
    if (!element || typeof ResizeObserver === 'undefined') {
      return undefined;
    }
    // Only a box that can scroll counts: a clipped one, such as visually hidden text, must never take focus.
    const measure = () => {
      const { overflowX, overflowY } = getComputedStyle(element);
      const sideways = /auto|scroll/.test(overflowX) && element.scrollWidth > element.clientWidth + 1;
      const down = /auto|scroll/.test(overflowY) && element.scrollHeight > element.clientHeight + 1;
      setScrolls(sideways || down);
    };
    measure();
    // The box keeps its size when only its text grows, so content changes are watched too.
    const resize = new ResizeObserver(measure);
    resize.observe(element);
    for (const child of element.children) {
      resize.observe(child);
    }
    const mutation = new MutationObserver(measure);
    mutation.observe(element, { childList: true, characterData: true, subtree: true });
    return () => {
      resize.disconnect();
      mutation.disconnect();
    };
  }, [element]);

  return { ref: setElement, tabIndex: scrolls ? 0 : undefined };
}
