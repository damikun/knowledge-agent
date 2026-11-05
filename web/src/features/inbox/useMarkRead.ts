import { useEffect, useEffectEvent } from 'react';
import type { Guid } from '../../lib/types';
import { useMarkReadUpTo } from './api';

/** How long the conversation must be visible before it counts as read. */
const READ_AFTER_MS = 1500;

/** Marks the open conversation read once it has been visible for a moment; hidden tabs read nothing. */
export function useMarkRead(conversationId: Guid, latestSequence: number | null) {
  const markRead = useMarkReadUpTo(conversationId);
  // After a failure, the next message or open retries.
  const readUpTo = markRead.isError ? 0 : (markRead.variables ?? 0);
  const isRead = useEffectEvent((sequence: number) => sequence <= readUpTo);
  const send = useEffectEvent((sequence: number) => markRead.mutate(sequence));

  useEffect(() => {
    if (latestSequence === null || isRead(latestSequence)) {
      return undefined;
    }
    let timer: number | undefined;
    const schedule = () => {
      window.clearTimeout(timer);
      if (document.visibilityState !== 'visible') {
        return;
      }
      timer = window.setTimeout(() => send(latestSequence), READ_AFTER_MS);
    };
    schedule();
    document.addEventListener('visibilitychange', schedule);
    return () => {
      window.clearTimeout(timer);
      document.removeEventListener('visibilitychange', schedule);
    };
  }, [conversationId, latestSequence]);
}
