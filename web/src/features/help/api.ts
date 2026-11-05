import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { buildUrl, isRecord, request, toApiError } from '../../lib/api';
import { sseRequest } from '../../lib/sse';
import { parseChatEvent } from '../chat/api';
import type { ChatEvent } from '../chat/types';
import type { HelpChatRequest, HelpSetupDto, HelpStatusDto } from './types';

export const helpKeys = {
  status: ['help', 'status'] as const,
};

/** Only portal paths, so a link from the API never leaves the portal. */
export function portalPath(value: unknown): string | null {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') ? value : null;
}

function toSetup(value: unknown): HelpSetupDto | null {
  if (!isRecord(value)) {
    return null;
  }
  return {
    chatModel: value.chatModel === true,
    embeddingModel: value.embeddingModel === true,
    sources: value.sources === true,
    agent: value.agent === true,
    widget: value.widget === true,
  };
}

export function toHelpStatus(value: unknown): HelpStatusDto {
  const dto = isRecord(value) ? value : {};
  return {
    enabled: dto.enabled === true,
    canManage: dto.canManage === true,
    modelReady: dto.modelReady === true,
    setup: toSetup(dto.setup),
  };
}

const helpApi = {
  status: async (signal?: AbortSignal) => toHelpStatus(await request<unknown>('/api/help', { signal })),
  saveSettings: async (enabled: boolean) => toHelpStatus(await request<unknown>('/api/help/settings', { method: 'PUT', body: { enabled } })),
};

export function useHelpStatus() {
  return useQuery({ queryKey: helpKeys.status, queryFn: ({ signal }) => helpApi.status(signal), staleTime: 30_000, retry: false });
}

export function useSaveHelpSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (enabled: boolean) => helpApi.saveSettings(enabled),
    onSuccess: (status) => queryClient.setQueryData(helpKeys.status, status),
  });
}

/** Same event types as the chat stream, so the chat's parser reads it. */
export async function* streamHelp(body: HelpChatRequest, signal?: AbortSignal): AsyncGenerator<ChatEvent, void, undefined> {
  const stream = sseRequest(buildUrl('/api/help/chat'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    ...(signal ? { signal } : {}),
    onErrorResponse: toApiError,
  });
  for await (const message of stream) {
    const parsed = parseChatEvent(message.event, message.data);
    if (parsed) {
      yield parsed;
    }
  }
}
