import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, call, isRecord } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
import type { Guid } from '../../lib/types';
import { conversationKeys } from '../chat/api';
import { LABEL_COLORS, type ConversationLabel, type Label, type LabelColor, type LabelInput } from './types';

const labelKeys = {
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

export const labelsApi = {
  list: async (signal?: AbortSignal) => items(await call(endpoints.labels.list, { signal })).map(toLabel),
  create: async (input: LabelInput) => toLabel(await call(endpoints.labels.create, { body: input })),
  update: async (id: Guid, input: Partial<LabelInput>) => toLabel(await call(endpoints.labels.update, { params: { id }, body: input })),
  remove: (id: Guid): Promise<void> => call(endpoints.labels.remove, { params: { id } }),
  setForConversation: async (id: Guid, labelIds: Guid[]) =>
    toConversationLabels(await call(endpoints.conversations.setLabels, { params: { id }, body: { labelIds } })),
  relabel: async (days: number) => {
    const dto = await call(endpoints.labels.relabel, { query: { days } });
    return isRecord(dto) && typeof dto.queued === 'number' ? dto.queued : 0;
  },
};

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

export function labelsOrEmpty(data: unknown): Label[] {
  return Array.isArray(data) ? (data as Label[]) : [];
}

export function labelsAvailable(data: unknown): boolean {
  return Array.isArray(data);
}

export function useSetConversationLabels(conversationId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    scope: { id: `conversation-labels-${conversationId}` },
    mutationFn: (labelIds: Guid[]) => labelsApi.setForConversation(conversationId, labelIds),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: conversationKeys.all });
      void queryClient.invalidateQueries({ queryKey: labelKeys.all });
    },
  });
}

export function useRefreshAfterLabelChange() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: labelKeys.all });
    void queryClient.invalidateQueries({ queryKey: conversationKeys.all });
  };
}
