import { useEffect, useEffectEvent } from 'react';
import { ApiError } from '../../lib/api';
import { isAbortError } from '../../lib/sse';
import { streamConversationEvents } from './api';
import type { ConversationLiveEvent } from './types';

const RETRY_DELAYS_MS: readonly number[] = [1_000, 2_000, 5_000, 10_000];

export interface ConversationEventHandlers {
  /** Read before each connection. */
  afterSequence: () => number;
  /** Each connection replays the turn in progress from its start. */
  onOpen?: () => void;
  onEvent: (event: ConversationLiveEvent) => void;
}

export interface ConversationEventOptions {
  /** One delay per consecutive failure; the last one repeats. */
  retryDelaysMs?: readonly number[];
}

function isRetryable(error: unknown): boolean {
  if (!(error instanceof ApiError)) {
    return true;
  }
  return error.status === 0 || error.status === 408 || error.status === 429 || error.status >= 500;
}

function wait(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const done = () => {
      window.clearTimeout(timer);
      signal.removeEventListener('abort', done);
      resolve();
    };
    const timer = window.setTimeout(done, ms);
    signal.addEventListener('abort', done, { once: true });
  });
}

async function follow(
  conversationId: string,
  signal: AbortSignal,
  handlers: ConversationEventHandlers,
  retryDelaysMs: readonly number[],
): Promise<void> {
  let failures = 0;
  let resume = false;
  while (!signal.aborted) {
    try {
      const events = streamConversationEvents(conversationId, {
        after: handlers.afterSequence(),
        resume,
        signal,
        onOpen: () => {
          failures = 0;
          handlers.onOpen?.();
        },
      });
      for await (const event of events) {
        handlers.onEvent(event);
      }
    } catch (error) {
      if (signal.aborted || isAbortError(error) || !isRetryable(error)) {
        return;
      }
      failures += 1;
    }
    // The server can also close the stream, e.g. on restart.
    resume = true;
    const delay = retryDelaysMs[Math.min(Math.max(failures - 1, 0), retryDelaysMs.length - 1)] ?? 1_000;
    await wait(delay, signal);
  }
}

/** Follows the conversation's event stream, reconnecting with Last-Event-ID after network errors. */
export function useConversationEvents(
  conversationId: string | null,
  enabled: boolean,
  handlers: ConversationEventHandlers,
  options: ConversationEventOptions = {},
): void {
  // Effect events, so new handlers don't reconnect.
  const afterSequence = useEffectEvent(() => handlers.afterSequence());
  const onOpen = useEffectEvent(() => handlers.onOpen?.());
  const onEvent = useEffectEvent((event: ConversationLiveEvent) => handlers.onEvent(event));
  const retryDelays = useEffectEvent(() => options.retryDelaysMs ?? RETRY_DELAYS_MS);

  useEffect(() => {
    if (!conversationId || !enabled) {
      return undefined;
    }
    const controller = new AbortController();
    void follow(conversationId, controller.signal, { afterSequence, onOpen, onEvent }, retryDelays());
    return () => controller.abort();
  }, [conversationId, enabled]);
}
