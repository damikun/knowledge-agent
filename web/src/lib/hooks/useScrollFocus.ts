import { useLayoutEffect, useState } from 'react';

export function useScrollFocus<T extends HTMLElement>(): { ref: (element: T | null) => void; tabIndex: 0 | undefined } {
  const [element, setElement] = useState<T | null>(null);
  const [scrolls, setScrolls] = useState(false);

  useLayoutEffect(() => {
    if (!element || typeof ResizeObserver === 'undefined') {
      return undefined;
    }
    const measure = () => {
      const { overflowX, overflowY } = getComputedStyle(element);
      const sideways = /auto|scroll/.test(overflowX) && element.scrollWidth > element.clientWidth + 1;
      const down = /auto|scroll/.test(overflowY) && element.scrollHeight > element.clientHeight + 1;
      setScrolls(sideways || down);
    };
    measure();
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
