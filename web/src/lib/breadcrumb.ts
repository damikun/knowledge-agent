import { createContext, useContext, useLayoutEffect } from 'react';

export const BreadcrumbContext = createContext<(item: string | null) => void>(() => undefined);

export function useBreadcrumb(item: string | null | undefined): void {
  const setItem = useContext(BreadcrumbContext);
  useLayoutEffect(() => {
    setItem(item || null);
    return () => setItem(null);
  }, [setItem, item]);
}
