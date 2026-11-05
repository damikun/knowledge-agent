import { useEffect, useEffectEvent, useState, useSyncExternalStore } from 'react';
import { WidgetApi } from './api';
import { ChatController, type ChatSession, type ChatState } from './controller';
import type { HostIdentity } from './host';

function storageKeyFor(widgetKey: string): string {
  return `ka-widget:${widgetKey}`;
}

// Created in an effect so StrictMode's second mount gets a fresh controller; disposed of when the key changes or on
// unmount. Later changes to what `create` reads don't recreate it.
export function useOwnedController(key: string, create: () => ChatController): ChatController | null {
  const [controller, setController] = useState<ChatController | null>(null);
  const createController = useEffectEvent(create);

  useEffect(() => {
    const created = createController();
    setController(created);
    void created.start();
    return () => created.dispose();
  }, [key]);

  return controller;
}

export function useChatController(widgetKey: string, retryDelaysMs?: readonly number[], identity?: HostIdentity): ChatController | null {
  return useOwnedController(
    widgetKey,
    () =>
      new ChatController(new WidgetApi(widgetKey), {
        storageKey: storageKeyFor(widgetKey),
        ...(retryDelaysMs ? { retryDelaysMs } : {}),
        ...(identity ? { identity } : {}),
      }),
  );
}

export function useChatState(session: ChatSession): ChatState {
  return useSyncExternalStore(session.subscribe, session.getState);
}

export function useOnline(): boolean {
  return useSyncExternalStore(
    (onChange) => {
      window.addEventListener('online', onChange);
      window.addEventListener('offline', onChange);
      return () => {
        window.removeEventListener('online', onChange);
        window.removeEventListener('offline', onChange);
      };
    },
    () => navigator.onLine,
  );
}
