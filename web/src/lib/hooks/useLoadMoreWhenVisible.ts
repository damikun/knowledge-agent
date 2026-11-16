import { useCallback } from 'react';

export interface PagedQuery {
  hasNextPage: boolean;
  isFetchingNextPage: boolean;
  fetchNextPage: () => Promise<unknown>;
}

export interface LoadMoreOptions {
  rootMargin?: string;
  root?: Element | null;
}

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
