import { useEffect, useEffectEvent } from 'react';

// Listens to the frame's window while mounted; the listener always sees the latest props and state.
export function useWindowEvent<K extends keyof WindowEventMap>(type: K, listener: (event: WindowEventMap[K]) => void): void {
  const onEvent = useEffectEvent(listener);
  useEffect(() => {
    const handle = (event: WindowEventMap[K]) => onEvent(event);
    window.addEventListener(type, handle);
    return () => window.removeEventListener(type, handle);
  }, [type]);
}
