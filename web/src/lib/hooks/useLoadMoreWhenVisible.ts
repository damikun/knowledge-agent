import { useCallback } from 'react';

export interface PagedQuery {
  hasNextPage: boolean;
  isFetchingNextPage: boolean;
  fetchNextPage: () => Promise<unknown>;
}

export interface LoadMoreOptions {
  /** How far ahead of the visible area the end counts as visible, such as '400px 0px'. */
  rootMargin?: string;
  /** For a list that scrolls inside a container; keep it in state, not a ref. */
  root?: Element | null;
}

/** Does nothing without IntersectionObserver (tests), so keep a "Show more" button next to it. */
export function useLoadMoreWhenVisible({ hasNextPage, isFetchingNextPage, fetchNextPage }: PagedQuery, { rootMargin, root }: LoadMoreOptions = {}) {
  return useCallback(
    (sentinel: HTMLElement | null) => {
      if (!sentinel || !hasNextPage || typeof IntersectionObserver === 'undefined') {
        return undefined;
      }
      const observer = new IntersectionObserver(
        (entries) => {
          if (entries.some((entry) => entry.isIntersecting) && !isFetchingNextPage) {
            void fetchNextPage();
          }
        },
        { root, rootMargin },
      );
      observer.observe(sentinel);
      return () => observer.disconnect();
    },
    [hasNextPage, isFetchingNextPage, fetchNextPage, rootMargin, root],
  );
}
