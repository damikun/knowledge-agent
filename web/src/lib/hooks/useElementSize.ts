import { useLayoutEffect, useState } from 'react';

export interface ElementSize {
  width: number;
  height: number;
}

/** Null where there is no layout (tests). Read before the first paint, so a size-dependent layout doesn't jump. */
export function useElementSize(element: HTMLElement | null): ElementSize | null {
  const [size, setSize] = useState<ElementSize | null>(null);

  useLayoutEffect(() => {
    if (!element || typeof ResizeObserver === 'undefined') {
      return undefined;
    }
    const measure = () => {
      const { width, height } = element.getBoundingClientRect();
      setSize((current) => (current?.width === width && current.height === height ? current : { width, height }));
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return size;
}
