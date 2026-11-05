import { keepPreviousData, queryOptions, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { asArray, request } from '../../lib/api';
import type { JsonObject } from '../../lib/types';
import type {
  PromptDetailDto,
  PromptSummaryDto,
  PromptVersionDto,
  PromptVersionsDto,
  PublishPromptRequest,
  PublishPromptResponse,
  RenderedPromptDto,
  RenderPromptRequest,
  RenderTemplateRequest,
  RenderTemplateResponse,
  SavePromptDraftRequest,
} from './types';

const promptKeys = {
  all: ['prompts'] as const,
  detail: (name: string) => ['prompts', name] as const,
  versions: (name: string) => ['prompts', name, 'versions'] as const,
  // Outside ['prompts'], so a prompt named like a key part can't collide with these.
  renderedPrompt: (name: string, version: number, variables: string) => ['prompt-render', 'prompt', name, version, variables] as const,
  renderedTemplate: (template: string, variables: string) => ['prompt-render', 'template', template, variables] as const,
};

const promptsApi = {
  listPrompts: async (signal?: AbortSignal) => asArray<PromptSummaryDto>(await request<unknown>('/api/prompts', { signal })),
  getPrompt: (name: string, signal?: AbortSignal) => request<PromptDetailDto>(`/api/prompts/${encodeURIComponent(name)}`, { signal }),
  renderPrompt: (name: string, body: RenderPromptRequest, signal?: AbortSignal) =>
    request<RenderedPromptDto>(`/api/prompts/${encodeURIComponent(name)}/render`, { method: 'POST', body, signal }),
  renderTemplate: (body: RenderTemplateRequest, signal?: AbortSignal) =>
    request<RenderTemplateResponse>('/api/prompts/render', { method: 'POST', body, signal }),
  listVersions: (name: string, signal?: AbortSignal) => request<PromptVersionsDto>(`/api/prompts/${encodeURIComponent(name)}/versions`, { signal }),
  saveDraft: (name: string, body: SavePromptDraftRequest) =>
    request<PromptVersionDto>(`/api/prompts/${encodeURIComponent(name)}/versions`, { method: 'POST', body }),
  publish: (name: string, body: PublishPromptRequest) =>
    request<PublishPromptResponse>(`/api/prompts/${encodeURIComponent(name)}/publish`, { method: 'POST', body }),
};

export function usePrompts() {
  return useQuery({ queryKey: promptKeys.all, queryFn: ({ signal }) => promptsApi.listPrompts(signal), staleTime: 30_000 });
}

export function usePrompt(name: string | null) {
  return useQuery({
    queryKey: promptKeys.detail(name ?? ''),
    queryFn: ({ signal }) => promptsApi.getPrompt(name!, signal),
    enabled: Boolean(name),
    staleTime: 30_000,
  });
}

/** Loaded only when shown. */
export function usePromptVersions(name: string, enabled = true) {
  return useQuery({
    queryKey: promptKeys.versions(name),
    queryFn: ({ signal }) => promptsApi.listVersions(name, signal),
    enabled,
    staleTime: 10_000,
  });
}

/** With `publish`, the draft goes live in the same step. */
export function useSavePrompt(name: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ draft, publish }: { draft: SavePromptDraftRequest; publish?: { expectedLiveVersion: number | null } }) => {
      const saved = await promptsApi.saveDraft(name, draft);
      // A failed publish keeps the saved draft, so the editor can say which version is waiting.
      let published: PublishPromptResponse | null = null;
      let publishError: unknown = null;
      if (publish) {
        try {
          published = await promptsApi.publish(name, { version: saved.version, expectedLiveVersion: publish.expectedLiveVersion });
        } catch (error) {
          publishError = error;
        }
      }
      return { saved, published, publishError };
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: promptKeys.all }),
  });
}

/** Publishes a draft or rolls back to an older version. */
export function usePublishPrompt(name: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: PublishPromptRequest) => promptsApi.publish(name, body),
    onSettled: () => queryClient.invalidateQueries({ queryKey: promptKeys.all }),
  });
}

/** Rendering is pure, so each input is cached and the previous output stays while the next renders. Template errors are 400s, so no retry. */
export function useRenderedPrompt(name: string, version: number, variablesText: string, variables: JsonObject | null) {
  return useQuery({
    queryKey: promptKeys.renderedPrompt(name, version, variablesText),
    queryFn: ({ signal }) => promptsApi.renderPrompt(name, { variables }, signal),
    enabled: variables !== null,
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: Infinity,
  });
}

/** Cached forever: the same input always renders the same. */
export function renderedTemplateQuery(template: string, variablesText: string, variables: JsonObject | null) {
  return queryOptions({
    queryKey: promptKeys.renderedTemplate(template, variablesText),
    queryFn: ({ signal }) => promptsApi.renderTemplate({ template, variables }, signal),
    enabled: variables !== null && template.trim() !== '',
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: Infinity,
  });
}

export function useRenderedTemplate(template: string, variablesText: string, variables: JsonObject | null) {
  return useQuery(renderedTemplateQuery(template, variablesText, variables));
}
