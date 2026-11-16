import { createContext, useContext, useEffect, useEffectEvent } from 'react';
import { useStore } from 'zustand';
import type { EditMark, EditTarget } from '../shared/preview';
import type { ChatActions, ChatState } from './chatState';
import type { WidgetHost } from './hostLink';
import type { Texts } from './texts';
import type { WidgetConfig } from './types';
import type { VisitorState } from './visitor';
import type { Widget } from './widgetRuntime';

export interface WidgetContextValue {
  widget: Widget;
  config: WidgetConfig;
  t: Texts;
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
    throw new Error('useWidget must be used inside WidgetProvider.');
  }
  return value;
}

export function useChat(): ChatActions {
  return useWidget().widget.chat.getState();
}

export function useChatState(): ChatState;
export function useChatState<T>(select: (state: ChatState) => T): T;
export function useChatState<T>(select?: (state: ChatState) => T): ChatState | T {
  return useStore(useWidget().widget.chat, select as (state: ChatState) => T);
}

export function useHost(): WidgetHost {
  return useWidget().widget.host;
}

export function useHostOpen(listener: () => void): void {
  const host = useHost();
  const onOpen = useEffectEvent(listener);
  useEffect(() => host.onOpen(() => onOpen()), [host]);
}

export function useVisitorState(): VisitorState {
  return useStore(useWidget().widget.visitor);
}
