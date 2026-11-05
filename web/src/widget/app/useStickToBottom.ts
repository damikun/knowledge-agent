import { useCallback, useEffect, useLayoutEffect, useRef, type DependencyList, type RefObject, type UIEvent } from 'react';

// px from the end within which the list stays pinned to it.
const NEAR_END = 80;

export interface StickToBottom {
  scrollRef: RefObject<HTMLDivElement | null>;
  contentRef: RefObject<HTMLDivElement | null>;
  onScroll: (event: UIEvent<HTMLDivElement>) => void;
  // Back to the end with the next change, such as after the visitor sends a message.
  stick: () => void;
}

// Also follows size changes without new state: a resized panel, the on-screen keyboard, an image that loads.
export function useStickToBottom(changes: DependencyList): StickToBottom {
  const scrollRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  // A ref: only scrolling reads it, and as state every scroll would re-render the chat.
  const sticking = useRef(true);

  useLayoutEffect(() => {
    const scroller = scrollRef.current;
    if (scroller && sticking.current) {
      scroller.scrollTop = scroller.scrollHeight;
    }
    // The caller names what changes the content.
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
