import { isAbortError } from '../../lib/sse';
import { isApiError, type WidgetApi } from './api';
import type { LiveEvent } from './types';

export interface LiveFeed {
  after(): number;
  onOpen(): void;
  onEvent(event: LiveEvent): void;
  onOffline(): void;
}

export async function followConversation(
  api: WidgetApi,
  conversationId: string,
  feed: LiveFeed,
  retryDelaysMs: readonly number[],
  signal: AbortSignal,
): Promise<void> {
  let failures = 0;
  let resume = false;
  while (!signal.aborted) {
    try {
      const events = api.followConversation(conversationId, {
        after: feed.after(),
        resume,
        signal,
        onOpen: () => {
          failures = 0;
          feed.onOpen();
        },
      });
      for await (const event of events) {
        if (signal.aborted) {
          return;
        }
        feed.onEvent(event);
      }
    } catch (error) {
      if (signal.aborted || isAbortError(error) || isFinal(error)) {
        return;
      }
      failures += 1;
      if (failures >= 2) {
        feed.onOffline();
      }
    }
    resume = true;
    await wait(retryDelaysMs[Math.min(Math.max(failures - 1, 0), retryDelaysMs.length - 1)] ?? 1_000, signal);
  }
}

function isFinal(error: unknown): boolean {
  return isApiError(error) && error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429;
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
