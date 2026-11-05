import { useEffect, useState } from 'react';
import { isAbortError } from '../../../lib/sse';
import { fetchPreviewSession, WidgetApi, type PreviewSession } from '../api';
import { ChatPanel } from '../ChatPanel';
import { ChatController } from '../controller';
import { normalizeConfig, type WidgetConfig } from '../types';
import { useOwnedController } from '../useChatController';
import { WidgetProvider } from '../WidgetProvider';

export type LiveSession =
  | { state: 'loading' }
  | { state: 'ready'; config: WidgetConfig; first: PreviewSession }
  | { state: 'failed'; message: string };

const LOADING: LiveSession = { state: 'loading' };

export function useLiveSession(widgetId: string | null): LiveSession {
  const [loaded, setLoaded] = useState<{ widgetId: string; session: LiveSession } | null>(null);

  useEffect(() => {
    if (!widgetId) {
      return undefined;
    }
    const abort = new AbortController();
    fetchPreviewSession(widgetId, abort.signal)
      .then((first) => {
        const config = normalizeConfig(first.config);
        setLoaded({
          widgetId,
          session: config ? { state: 'ready', config, first } : { state: 'failed', message: 'The widget settings could not be read.' },
        });
      })
      .catch((error: unknown) => {
        if (!isAbortError(error)) {
          setLoaded({ widgetId, session: { state: 'failed', message: error instanceof Error ? error.message : 'The preview could not start.' } });
        }
      });
    return () => abort.abort();
  }, [widgetId]);

  return loaded && loaded.widgetId === widgetId ? loaded.session : LOADING;
}

// The public API with a preview session, which the server treats as a test; a new one is fetched when it runs out.
export function LiveChatPreview({
  widgetId,
  config,
  first,
  onClose,
  onBusy,
}: {
  widgetId: string;
  config: WidgetConfig;
  first: PreviewSession;
  onClose: () => void;
  onBusy: (busy: boolean) => void;
}) {
  const controller = useOwnedController(`${widgetId}:${config.key}`, () => {
    let initial: PreviewSession | null = first;
    return new ChatController(new WidgetApi(config.key), {
      storageKey: `ka-widget-preview:${widgetId}`,
      session: async () => {
        const session = initial?.session;
        initial = null;
        return session ?? (await fetchPreviewSession(widgetId)).session;
      },
    });
  });
  return controller ? (
    <WidgetProvider config={config}>
      <ChatPanel session={controller} onClose={onClose} onBusy={onBusy} />
    </WidgetProvider>
  ) : null;
}
