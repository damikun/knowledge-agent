import { useLayoutEffect } from 'react';
import { useStore } from 'zustand';
import { ChatPanel } from './ChatPanel';
import { parentHost, type HostIdentity } from './host';
import { applyTheme, DEFAULT_THEME } from './theme';
import { useOwnedRuntime, WidgetRuntime, type Widget } from './widgetRuntime';
import { WidgetProvider } from './WidgetProvider';

export interface WidgetAppProps {
  widgetKey: string;
  retryDelaysMs?: readonly number[];
  identity?: HostIdentity;
}

export function WidgetApp({ widgetKey, retryDelaysMs, identity }: WidgetAppProps) {
  const runtime = useOwnedRuntime(
    widgetKey,
    () =>
      new WidgetRuntime(widgetKey, {
        storageKey: `ka-widget:${widgetKey}`,
        ...(retryDelaysMs ? { retryDelaysMs } : {}),
        ...(identity ? { identity } : {}),
        host: parentHost,
      }),
  );

  return runtime ? <Frame widget={runtime} /> : null;
}

function Frame({ widget }: { widget: Widget }) {
  const state = useStore(widget.config);
  const config = state.status === 'ready' ? state.config : null;

  useLayoutEffect(() => applyTheme(config ?? DEFAULT_THEME), [config]);

  return (
    <WidgetProvider widget={widget}>
      <ChatPanel />
    </WidgetProvider>
  );
}
