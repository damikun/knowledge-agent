import { useEffect, useEffectEvent } from 'react';
import { useLocation } from 'react-router';

export interface ScrollToHashOptions {
  /** Waits until the page can show the target, such as once its data has loaded. */
  enabled?: boolean;
  block?: ScrollLogicalPosition;
  resolve?: (hash: string) => HTMLElement | null;
}

const byId = (hash: string) => document.getElementById(hash);

/** The router doesn't scroll to the hash on client-side navigation, and pages scroll inside their own container. */
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
