import { keepPreviousData, queryOptions, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { asArray, call } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
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
  renderedPrompt: (name: string, version: number, variables: string) => ['prompt-render', 'prompt', name, version, variables] as const,
  renderedTemplate: (template: string, variables: string) => ['prompt-render', 'template', template, variables] as const,
};

const promptsApi = {
  listPrompts: async (signal?: AbortSignal) => asArray<PromptSummaryDto>(await call(endpoints.prompts.list, { signal })),
  getPrompt: (name: string, signal?: AbortSignal): Promise<PromptDetailDto> => call(endpoints.prompts.get, { params: { name }, signal }),
  renderPrompt: (name: string, body: RenderPromptRequest, signal?: AbortSignal): Promise<RenderedPromptDto> =>
    call(endpoints.prompts.render, { params: { name }, body, signal }),
  renderTemplate: (body: RenderTemplateRequest, signal?: AbortSignal): Promise<RenderTemplateResponse> =>
    call(endpoints.prompts.renderTemplate, { body, signal }),
  listVersions: (name: string, signal?: AbortSignal): Promise<PromptVersionsDto> => call(endpoints.prompts.versions, { params: { name }, signal }),
  saveDraft: (name: string, body: SavePromptDraftRequest): Promise<PromptVersionDto> =>
    call(endpoints.prompts.saveDraft, { params: { name }, body }),
  publish: (name: string, body: PublishPromptRequest): Promise<PublishPromptResponse> =>
    call(endpoints.prompts.publish, { params: { name }, body }),
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

export function usePromptVersions(name: string, enabled = true) {
  return useQuery({
    queryKey: promptKeys.versions(name),
    queryFn: ({ signal }) => promptsApi.listVersions(name, signal),
    enabled,
    staleTime: 10_000,
  });
}

export function useSavePrompt(name: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ draft, publish }: { draft: SavePromptDraftRequest; publish?: { expectedLiveVersion: number | null } }) => {
      const saved = await promptsApi.saveDraft(name, draft);
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

export function usePublishPrompt(name: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: PublishPromptRequest) => promptsApi.publish(name, body),
    onSettled: () => queryClient.invalidateQueries({ queryKey: promptKeys.all }),
  });
}

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
