import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, isRecord, request } from '../../lib/api';
import type { Guid } from '../../lib/types';
import { conversationKeys } from '../chat/api';
import { LABEL_COLORS, type ConversationLabel, type Label, type LabelColor, type LabelInput } from './types';

const labelKeys = {
  // The chat keeps its own label query under this prefix, so the list needs its own key.
  all: ['labels'] as const,
  list: ['labels', 'list'] as const,
};

export function toLabelColor(value: unknown): LabelColor {
  return LABEL_COLORS.find((color) => color === value) ?? 'gray';
}

function toLabel(value: unknown): Label {
  const dto = isRecord(value) ? value : {};
  return {
    id: String(dto.id ?? ''),
    name: typeof dto.name === 'string' ? dto.name : '',
    color: toLabelColor(dto.color),
    description: typeof dto.description === 'string' ? dto.description : '',
    autoApply: dto.autoApply === true,
    conversationCount: typeof dto.conversationCount === 'number' ? dto.conversationCount : 0,
    createdAt: String(dto.createdAt ?? ''),
    updatedAt: String(dto.updatedAt ?? ''),
  };
}

/** A bare array or `{ items }`; anything else is no labels. */
function items(value: unknown): unknown[] {
  if (Array.isArray(value)) {
    return value;
  }
  return isRecord(value) && Array.isArray(value.items) ? value.items : [];
}

export function toConversationLabels(value: unknown): ConversationLabel[] {
  return items(value).map((item) => {
    const dto = isRecord(item) ? item : {};
    return {
      id: String(dto.id ?? ''),
      name: typeof dto.name === 'string' ? dto.name : '',
      color: toLabelColor(dto.color),
      source: dto.source === 'auto' ? 'auto' : 'manual',
    };
  });
}

const conversationPath = (id: Guid) => `/api/conversations/${encodeURIComponent(id)}`;

export const labelsApi = {
  list: async (signal?: AbortSignal) => items(await request<unknown>('/api/labels', { signal })).map(toLabel),
  create: async (input: LabelInput) => toLabel(await request<unknown>('/api/labels', { method: 'POST', body: input })),
  update: async (id: Guid, input: Partial<LabelInput>) =>
    toLabel(
      await request<unknown>(`/api/labels/${encodeURIComponent(id)}`, {
        method: 'PATCH',
        body: input,
      }),
    ),
  remove: (id: Guid) =>
    request<void>(`/api/labels/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    }),
  /** An auto label left out is never applied to the conversation again. */
  setForConversation: async (id: Guid, labelIds: Guid[]) =>
    toConversationLabels(
      await request<unknown>(`${conversationPath(id)}/labels`, {
        method: 'PUT',
        body: { labelIds },
      }),
    ),
  /** Re-queues auto-labeling for the last `days`; returns how many were queued. */
  relabel: async (days: number) => {
    const dto = await request<unknown>('/api/labels/relabel', {
      method: 'POST',
      query: { days },
    });
    return isRecord(dto) && typeof dto.queued === 'number' ? dto.queued : 0;
  },
};

/** Null on an API without labels (404), so label features hide instead of failing. */
export function useLabels(enabled = true) {
  return useQuery<Label[] | null>({
    queryKey: labelKeys.list,
    queryFn: async ({ signal }) => {
      try {
        return await labelsApi.list(signal);
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) {
          return null;
        }
        throw error;
      }
    },
    staleTime: 30_000,
    enabled,
    retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
  });
}

/** Empty while loading, on errors and on unexpected responses. */
export function labelsOrEmpty(data: unknown): Label[] {
  return Array.isArray(data) ? (data as Label[]) : [];
}

/** Whether the API supports labels at all. */
export function labelsAvailable(data: unknown): boolean {
  return Array.isArray(data);
}

export function useSetConversationLabels(conversationId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    // Serialized, so quick toggles reach the server in order.
    scope: { id: `conversation-labels-${conversationId}` },
    mutationFn: (labelIds: Guid[]) => labelsApi.setForConversation(conversationId, labelIds),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: conversationKeys.all });
      void queryClient.invalidateQueries({ queryKey: labelKeys.all });
    },
  });
}

/** Label name and color show in many places, analytics included. */
export function useRefreshAfterLabelChange() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: labelKeys.all });
    void queryClient.invalidateQueries({ queryKey: conversationKeys.all });
  };
}
