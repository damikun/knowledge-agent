import { createContext, useContext } from 'react';
import type { EditMark, EditTarget } from '../shared/preview';
import type { Texts } from './texts';
import type { WidgetConfig } from './types';

export interface WidgetContextValue {
  config: WidgetConfig;
  t: Texts;
  // Marks an element the widget editor can edit; only the preview marks anything.
  mark: (target: EditTarget) => EditMark;
}

const NO_MARK: EditMark = {};

export function noMark(): EditMark {
  return NO_MARK;
}

export const WidgetContext = createContext<WidgetContextValue | null>(null);

export function useWidget(): WidgetContextValue {
  const value = useContext(WidgetContext);
  if (!value) {
    throw new Error('useWidget must be used inside WidgetContext.');
  }
  return value;
}
