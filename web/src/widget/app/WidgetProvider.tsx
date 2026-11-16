import { useMemo, type ReactNode } from 'react';
import { useStore } from 'zustand';
import { noMark, WidgetContext, type WidgetContextValue } from './context';
import { createTexts } from './texts';
import type { WidgetConfig } from './types';
import type { Widget } from './widgetRuntime';

interface WidgetProviderProps {
  widget: Widget;
  mark?: WidgetContextValue['mark'];
  children: ReactNode;
}

export function WidgetProvider({ widget, mark = noMark, children }: WidgetProviderProps) {
  const state = useStore(widget.config);
  return state.status === 'ready' ? (
    <ReadyWidget widget={widget} config={state.config} mark={mark}>
      {children}
    </ReadyWidget>
  ) : null;
}

function ReadyWidget({ widget, config, mark, children }: Required<WidgetProviderProps> & { config: WidgetConfig }) {
  const t = useMemo(() => createTexts(config.texts), [config.texts]);
  const value = useMemo<WidgetContextValue>(() => ({ widget, config, t, mark }), [widget, config, t, mark]);
  return <WidgetContext value={value}>{children}</WidgetContext>;
}
