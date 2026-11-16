import { useCallback, useEffect, useLayoutEffect, useRef, type DependencyList, type RefObject, type UIEvent } from 'react';

const NEAR_END = 80;

export interface StickToBottom {
  scrollRef: RefObject<HTMLDivElement | null>;
  contentRef: RefObject<HTMLDivElement | null>;
  onScroll: (event: UIEvent<HTMLDivElement>) => void;
  stick: () => void;
}

export function useStickToBottom(changes: DependencyList): StickToBottom {
  const scrollRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const sticking = useRef(true);

  useLayoutEffect(() => {
    const scroller = scrollRef.current;
    if (scroller && sticking.current) {
      scroller.scrollTop = scroller.scrollHeight;
    }
  }, changes);

  useEffect(() => {
    const scroller = scrollRef.current;
    if (!scroller || typeof ResizeObserver === 'undefined') {
      return undefined;
    }
    const observer = new ResizeObserver(() => {
      if (sticking.current) {
        scroller.scrollTop = scroller.scrollHeight;
      }
    });
    observer.observe(scroller);
    if (contentRef.current) {
      observer.observe(contentRef.current);
    }
    return () => observer.disconnect();
  }, []);

  const onScroll = (event: UIEvent<HTMLDivElement>) => {
    const scroller = event.currentTarget;
    sticking.current = scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight < NEAR_END;
  };

  const stick = useCallback(() => {
    sticking.current = true;
  }, []);

  return { scrollRef, contentRef, onScroll, stick };
}
