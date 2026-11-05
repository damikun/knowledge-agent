import { useMemo, type ReactNode } from 'react';
import { noMark, WidgetContext, type WidgetContextValue } from './context';
import { createTexts } from './texts';
import type { WidgetConfig } from './types';

export function WidgetProvider({
  config,
  mark = noMark,
  children,
}: {
  config: WidgetConfig;
  mark?: WidgetContextValue['mark'];
  children: ReactNode;
}) {
  const t = useMemo(() => createTexts(config.texts), [config.texts]);
  const value = useMemo<WidgetContextValue>(() => ({ config, t, mark }), [config, t, mark]);
  return <WidgetContext value={value}>{children}</WidgetContext>;
}
