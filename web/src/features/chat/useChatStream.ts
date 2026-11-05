import { useCallback, useEffect, useEffectEvent, useReducer, useRef, useState } from 'react';
import { errorMessage } from '../../lib/api';
import { createClientId } from '../../lib/ids';
import { isAbortError } from '../../lib/sse';
import { chatApi, isConversationBusy, streamChat } from './api';
import { chatReducer, createChatState, type ChatState } from './chatReducer';
import type { ChatRequestDto, ConversationEvent } from './types';

export interface ChatStreamCallbacks {
  onConversation?: (event: ConversationEvent) => void;
  /** The previous answer in the conversation is still being written, so the message was not taken. */
  onBusy?: (request: ChatRequestDto) => void;
  onSettled?: (state: ChatSettled) => void;
}

export interface ChatSettled {
  conversationId: string | null;
  completed: boolean;
  /** The stream created its conversation, and was neither reset nor replaced by another send before it ended. */
  created: boolean;
}

interface ChatSendOptions {
  /** The last timeline sequence this tab knows; the question will be stored after it. */
  sentAfterSequence?: number | null;
}

export interface ChatStream {
  state: ChatState;
  /** Stop was pressed and the API is stopping the answer; the stream then ends with `done`. */
  stopping: boolean;
  send: (request: ChatRequestDto, options?: ChatSendOptions) => Promise<void>;
  stop: () => void;
  reset: (conversationId: string | null) => void;
}

interface RunningStream {
  controller: AbortController;
  /** Known from the start for an existing conversation, otherwise from the `conversation` event. */
  conversationId: string | null;
  stopRequested: boolean;
  /** A `conversation` event said the conversation is new; a later one with a new title does not. */
  created: boolean;
}

export function useChatStream(callbacks: ChatStreamCallbacks = {}): ChatStream {
  const [state, dispatch] = useReducer(chatReducer, null, () => createChatState());
  const [stopping, setStopping] = useState(false);
  const runningRef = useRef<RunningStream | null>(null);
  // The running stream calls the latest callbacks, so the page's handlers can read its current route and state.
  const onConversation = useEffectEvent((event: ConversationEvent) => callbacks.onConversation?.(event));
  const onBusy = useEffectEvent((request: ChatRequestDto) => callbacks.onBusy?.(request));
  const onSettled = useEffectEvent((settled: ChatSettled) => callbacks.onSettled?.(settled));

  const send = useCallback(async (request: ChatRequestDto, options: ChatSendOptions = {}) => {
    runningRef.current?.controller.abort();
    const controller = new AbortController();
    const running: RunningStream = { controller, conversationId: request.conversationId ?? null, stopRequested: false, created: false };
    runningRef.current = running;
    setStopping(false);
    const isCurrent = () => runningRef.current === running && !controller.signal.aborted;

    dispatch({ type: 'send', id: createClientId(), message: request.message, at: Date.now(), sentAfterSequence: options.sentAfterSequence ?? null });
    let conversationId = request.conversationId ?? null;
    let completed = false;
    try {
      for await (const event of streamChat(request, controller.signal)) {
        if (!isCurrent()) {
          break;
        }
        dispatch({ type: 'event', event, at: Date.now() });
        if (event.event === 'conversation') {
          conversationId = event.data.conversationId;
          running.conversationId = conversationId;
          running.created ||= event.data.isNew;
          onConversation(event.data);
        } else if (event.event === 'done') {
          completed = true;
          conversationId = event.data.conversationId || conversationId;
        }
      }
      if (controller.signal.aborted) {
        dispatch({ type: 'aborted', at: Date.now() });
      } else if (!completed && isCurrent()) {
        // After Stop was pressed, a stream that ends without `done` shows as stopped rather than interrupted.
        dispatch(running.stopRequested ? { type: 'aborted', at: Date.now() } : { type: 'stream-ended', at: Date.now() });
      }
    } catch (error) {
      if (controller.signal.aborted || isAbortError(error)) {
        dispatch({ type: 'aborted', at: Date.now() });
      } else if (isCurrent() && isConversationBusy(error)) {
        dispatch({ type: 'refused' });
        onBusy(request);
      } else if (isCurrent()) {
        dispatch(running.stopRequested ? { type: 'aborted', at: Date.now() } : { type: 'failed', message: errorMessage(error), at: Date.now() });
      }
    } finally {
      const current = runningRef.current === running;
      if (current) {
        runningRef.current = null;
        setStopping(false);
      }
      onSettled({ conversationId, completed, created: current && running.created });
    }
  }, []);

  // The API stops the answer, keeps the text so far and ends the stream with `done`. Without a conversation id yet, or
  // when the API has no answer to stop, this tab only stops reading, and the live view shows what the server keeps.
  const stop = useCallback(() => {
    const running = runningRef.current;
    if (!running || running.stopRequested || running.controller.signal.aborted) {
      return;
    }
    const abort = () => {
      if (runningRef.current === running) {
        running.controller.abort();
      }
    };
    if (!running.conversationId) {
      abort();
      return;
    }
    running.stopRequested = true;
    setStopping(true);
    chatApi.stopAnswer(running.conversationId).then((stopped) => {
      if (!stopped) {
        abort();
      }
    }, abort);
  }, []);

  const reset = useCallback((conversationId: string | null) => {
    const running = runningRef.current;
    runningRef.current = null;
    running?.controller.abort();
    setStopping(false);
    dispatch({ type: 'reset', conversationId });
  }, []);

  useEffect(
    () => () => {
      runningRef.current?.controller.abort();
    },
    [],
  );

  return { state, stopping, send, stop, reset };
}
