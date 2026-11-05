import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useEffectEvent, useMemo, useRef, useState } from 'react';
import { dismissToast, toast } from '../../components/ui/Toast';
import { errorMessage } from '../../lib/api';
import { chatApi, conversationKeys, patchListItem, updateListItems } from './api';
import type { ConversationDto, ConversationLabelDto, ConversationListItem, LabelDto } from './types';

/** How long Undo is offered before the conversation is really deleted; a little longer than the toast stays. */
const UNDO_DELETE_MS = 7000;

export interface ConversationActions {
  hidden: ReadonlySet<string>;
  rename: (item: ConversationListItem, title: string) => void;
  togglePin: (item: ConversationListItem) => void;
  /** Sets the labels a person chose; labels a rule added stay as they are unless unticked. */
  setLabels: (item: ConversationListItem, labels: readonly LabelDto[]) => void;
  remove: (item: ConversationListItem) => void;
}

export interface ConversationActionsOptions {
  /** Called when a conversation disappears from the list; returns whether it was the open one. */
  onRemoved?: (id: string) => boolean;
  onRestored?: (id: string, wasOpen: boolean) => void;
}

interface PendingDelete {
  timer: ReturnType<typeof setTimeout>;
  toastId: number;
}

function displayTitle(item: ConversationListItem): string {
  return item.title || 'New conversation';
}

export function useConversationActions({ onRemoved, onRestored }: ConversationActionsOptions = {}): ConversationActions {
  const queryClient = useQueryClient();
  const [hidden, setHidden] = useState<ReadonlySet<string>>(() => new Set());
  const pending = useRef(new Map<string, PendingDelete>());
  // Label changes for one conversation go out one after another, so the last choice is the one stored.
  const labelQueue = useRef(new Map<string, Promise<unknown>>());
  // Called later, from a delete and its Undo, with the page's latest handlers; the actions stay stable for memoized rows.
  const removed = useEffectEvent((id: string) => onRemoved?.(id) ?? false);
  const restored = useEffectEvent((id: string, wasOpen: boolean) => onRestored?.(id, wasOpen));

  const setHiddenId = useCallback((id: string, on: boolean) => {
    setHidden((current) => {
      const next = new Set(current);
      if (on) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }, []);

  const failed = useCallback(
    (title: string, error: unknown) => {
      toast({ title, description: errorMessage(error), tone: 'danger' });
      // The optimistic change may be wrong now; the server has the truth.
      void queryClient.invalidateQueries({ queryKey: conversationKeys.lists });
    },
    [queryClient],
  );

  const commitDelete = useCallback(
    async (id: string) => {
      const entry = pending.current.get(id);
      if (!entry) {
        return;
      }
      pending.current.delete(id);
      clearTimeout(entry.timer);
      dismissToast(entry.toastId);
      try {
        await chatApi.deleteConversation(id);
        updateListItems(queryClient, (item) => (item.id === id ? null : item));
        queryClient.removeQueries({ queryKey: conversationKeys.detail(id), exact: true });
      } catch (error) {
        failed('Could not delete the conversation', error);
      } finally {
        setHiddenId(id, false);
      }
    },
    [queryClient, failed, setHiddenId],
  );

  // Leaving the page carries out pending deletes.
  useEffect(() => {
    const waiting = pending.current;
    return () => {
      for (const id of [...waiting.keys()]) {
        void commitDelete(id);
      }
    };
  }, [commitDelete]);

  const rename = useCallback(
    (item: ConversationListItem, title: string) => {
      const next = title.trim();
      if (!next || next === item.title) {
        return;
      }
      patchListItem(queryClient, item.id, { title: next });
      queryClient.setQueryData<ConversationDto>(conversationKeys.detail(item.id), (old) => (old ? { ...old, title: next } : old));
      chatApi.updateConversation(item.id, { title: next }).catch((error: unknown) => {
        patchListItem(queryClient, item.id, { title: item.title });
        queryClient.setQueryData<ConversationDto>(conversationKeys.detail(item.id), (old) => (old ? { ...old, title: item.title } : old));
        failed('Could not rename the conversation', error);
      });
    },
    [queryClient, failed],
  );

  const togglePin = useCallback(
    (item: ConversationListItem) => {
      const pinned = !item.pinned;
      patchListItem(queryClient, item.id, { pinned });
      chatApi.updateConversation(item.id, { pinned }).catch((error: unknown) => {
        patchListItem(queryClient, item.id, { pinned: item.pinned });
        failed(pinned ? 'Could not pin the conversation' : 'Could not unpin the conversation', error);
      });
    },
    [queryClient, failed],
  );

  const setLabels = useCallback(
    (item: ConversationListItem, labels: readonly LabelDto[]) => {
      const before = new Map(item.labels.map((label) => [label.id, label] as const));
      const next: ConversationLabelDto[] = labels.map((label) => ({
        id: label.id,
        name: label.name,
        color: label.color,
        source: before.get(label.id)?.source ?? 'manual',
      }));
      patchListItem(queryClient, item.id, { labels: next });
      // The API sets the manual labels; an automatic one the person kept is not sent, so it stays automatic.
      const labelIds = next.filter((label) => label.source === 'manual').map((label) => label.id);
      const previous = labelQueue.current.get(item.id) ?? Promise.resolve();
      const request = previous
        .catch(() => undefined)
        .then(() => chatApi.setLabels(item.id, labelIds))
        .catch((error: unknown) => failed('Could not change the labels', error));
      labelQueue.current.set(item.id, request);
    },
    [queryClient, failed],
  );

  const remove = useCallback(
    (item: ConversationListItem) => {
      if (pending.current.has(item.id)) {
        return;
      }
      setHiddenId(item.id, true);
      const wasOpen = removed(item.id);
      const toastId = toast({
        title: 'Conversation deleted',
        description: displayTitle(item),
        action: {
          label: 'Undo',
          onClick: () => {
            const entry = pending.current.get(item.id);
            if (!entry) {
              return;
            }
            clearTimeout(entry.timer);
            pending.current.delete(item.id);
            setHiddenId(item.id, false);
            restored(item.id, wasOpen);
          },
        },
      });
      const timer = setTimeout(() => void commitDelete(item.id), UNDO_DELETE_MS);
      pending.current.set(item.id, { timer, toastId });
    },
    [commitDelete, setHiddenId],
  );

  return useMemo(() => ({ hidden, rename, togglePin, setLabels, remove }), [hidden, rename, togglePin, setLabels, remove]);
}
