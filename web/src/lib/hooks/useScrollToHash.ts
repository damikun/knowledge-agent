import { useEffect, useEffectEvent } from 'react';
import { useLocation } from 'react-router';

export interface ScrollToHashOptions {
  enabled?: boolean;
  block?: ScrollLogicalPosition;
  resolve?: (hash: string) => HTMLElement | null;
}

const byId = (hash: string) => document.getElementById(hash);

export function useScrollToHash({ enabled = true, block = 'start', resolve = byId }: ScrollToHashOptions = {}): void {
  const { hash, key } = useLocation();
  const find = useEffectEvent(resolve);
  useEffect(() => {
    const target = enabled && hash.length > 1 ? find(hash.slice(1)) : null;
    if (target) {
      target.scrollIntoView({ block });
      target.focus({ preventScroll: true });
    }
  }, [hash, key, enabled, block]);
}
