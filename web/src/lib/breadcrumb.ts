import { createContext, useContext, useLayoutEffect } from 'react';

/** Outside AppLayout (single-page tests, the sign-in screen) this is a no-op. */
export const BreadcrumbContext = createContext<(item: string | null) => void>(() => undefined);

/** Pass null while the item loads. A layout effect, so the name clears before the next page paints. */
export function useBreadcrumb(item: string | null | undefined): void {
  const setItem = useContext(BreadcrumbContext);
  useLayoutEffect(() => {
    setItem(item || null);
    return () => setItem(null);
  }, [setItem, item]);
}
