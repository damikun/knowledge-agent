import { LoaderCircle } from 'lucide-react';
import { useEffect, useState } from 'react';
import { isAbortError } from '../../lib/sse';
import { DEFAULT_BRAND_COLOR } from '../shared/color';
import { DEFAULT_PRESET } from '../shared/theme';
import { isApiError, WidgetApi } from './api';
import { LiveChat } from './ChatPanel';
import { postToHost, type HostIdentity } from './host';
import { createTexts } from './texts';
import { applyTheme, type ThemeSettings } from './theme';
import { normalizeConfig, type WidgetConfig } from './types';
import { WidgetProvider } from './WidgetProvider';

export interface WidgetAppProps {
  widgetKey: string;
  retryDelaysMs?: readonly number[];
  // Listened for before the frame tells the loader it is ready.
  identity?: HostIdentity;
}

type LoadState = 'loading' | 'ready' | 'missing' | 'failed';

const DEFAULT_THEME: ThemeSettings = { brandColor: DEFAULT_BRAND_COLOR, theme: 'auto', preset: DEFAULT_PRESET, colors: { light: {}, dark: {} } };

export function WidgetApp({ widgetKey, retryDelaysMs, identity }: WidgetAppProps) {
  const [config, setConfig] = useState<WidgetConfig | null>(null);
  const [load, setLoad] = useState<LoadState>(widgetKey ? 'loading' : 'missing');
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    postToHost({ type: 'ready' });
  }, []);

  useEffect(() => {
    if (!widgetKey) {
      return undefined;
    }
    const abort = new AbortController();
    new WidgetApi(widgetKey)
      .getConfig(abort.signal)
      .then((dto) => {
        const loaded = normalizeConfig(dto);
        if (!loaded) {
          setLoad('failed');
          return;
        }
        setConfig(loaded);
        setLoad('ready');
      })
      .catch((error: unknown) => {
        if (!isAbortError(error)) {
          setLoad(isApiError(error, 404) ? 'missing' : 'failed');
        }
      });
    return () => abort.abort();
  }, [widgetKey, attempt]);

  useEffect(() => applyTheme(config ?? DEFAULT_THEME), [config]);

  const title = config?.title;
  useEffect(() => {
    if (title) {
      document.title = title;
    }
  }, [title]);

  if (load === 'ready' && config) {
    return (
      <WidgetProvider config={config}>
        <LiveChat widgetKey={widgetKey} {...(retryDelaysMs ? { retryDelaysMs } : {})} {...(identity ? { identity } : {})} />
      </WidgetProvider>
    );
  }

  const t = createTexts(config?.texts);

  return (
    <main className="grid h-full place-items-center p-6 text-center text-sm text-ink">
      {load === 'loading' ? (
        <p className="text-muted">
          <LoaderCircle className="size-6 motion-safe:animate-spin" aria-hidden="true" />
          <span className="sr-only">{t('loading')}</span>
        </p>
      ) : load === 'missing' ? (
        <p>{t('unavailable')}</p>
      ) : (
        <div className="space-y-3">
          <p>{t('error')}</p>
          <button
            type="button"
            onClick={() => {
              setLoad('loading');
              setAttempt((value) => value + 1);
            }}
            className="rounded-full bg-accent px-4 py-2 text-sm font-semibold text-on-accent hover:bg-accent-hover"
          >
            {t('retry')}
          </button>
        </div>
      )}
    </main>
  );
}
