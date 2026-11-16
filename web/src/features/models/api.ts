import { useQuery, type QueryClient } from '@tanstack/react-query';
import { asArray, call } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
import { systemInfoQueryKey } from '../../lib/queryKeys';
import type { ApiKeySource, ModelProfileDto, ProviderDto, ProviderTestResultDto, UpdateModelProfileRequest, UpdateProviderRequest } from './types';

export const modelKeys = {
  profiles: ['models', 'profiles'] as const,
  providers: ['models', 'providers'] as const,
};

const API_KEY_SOURCES: readonly string[] = ['none', 'configuration', 'portal'] satisfies ApiKeySource[];

function normalizeProvider(provider: ProviderDto): ProviderDto {
  const source = String(provider.apiKeySource ?? 'none').toLowerCase();
  return {
    ...provider,
    defaultEmbeddingDimensions: provider.defaultEmbeddingDimensions ?? null,
    endpoint: provider.endpoint ?? null,
    usesApiKey: provider.usesApiKey ?? false,
    usesEndpoint: provider.usesEndpoint ?? false,
    endpointRequired: provider.endpointRequired ?? false,
    apiKeySource: (API_KEY_SOURCES.includes(source) ? source : 'none') as ApiKeySource,
    apiKeyHint: provider.apiKeyHint ?? null,
    hasOverride: provider.hasOverride ?? false,
    overrideUpdatedAt: provider.overrideUpdatedAt ?? null,
  };
}

function normalizeTestResult(key: string, result: Partial<ProviderTestResultDto> | undefined): ProviderTestResultDto {
  return {
    key: result?.key ?? key,
    ok: result?.ok === true,
    message: result?.message ?? '',
    models: Array.isArray(result?.models) ? result.models : [],
    durationMs: result?.durationMs ?? 0,
  };
}

export const modelsApi = {
  listProfiles: async (signal?: AbortSignal) => asArray<ModelProfileDto>(await call(endpoints.models.listProfiles, { signal })),
  listProviders: async (signal?: AbortSignal) =>
    asArray<ProviderDto>(await call(endpoints.models.listProviders, { signal })).map(normalizeProvider),
  updateProvider: async (key: string, body: UpdateProviderRequest) => {
    const provider: ProviderDto | undefined = await call(endpoints.models.updateProvider, { params: { key }, body });
    return provider ? normalizeProvider(provider) : undefined;
  },
  resetProvider: async (key: string) => {
    const provider: ProviderDto | undefined = await call(endpoints.models.resetProvider, { params: { key } });
    return provider ? normalizeProvider(provider) : undefined;
  },
  testProvider: async (key: string) => normalizeTestResult(key, await call(endpoints.models.testProvider, { params: { key } })),
  updateProfile: (name: string, body: UpdateModelProfileRequest): Promise<ModelProfileDto | undefined> =>
    call(endpoints.models.updateProfile, { params: { name }, body }),
  resetProfile: (name: string): Promise<ModelProfileDto | undefined> => call(endpoints.models.resetProfile, { params: { name } }),
};

export function useProfiles() {
  return useQuery({ queryKey: modelKeys.profiles, queryFn: ({ signal }) => modelsApi.listProfiles(signal), staleTime: 30_000 });
}

export function useProviders() {
  return useQuery({ queryKey: modelKeys.providers, queryFn: ({ signal }) => modelsApi.listProviders(signal), staleTime: 60_000 });
}

export async function invalidateProviderSettings(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: modelKeys.providers }),
    queryClient.invalidateQueries({ queryKey: modelKeys.profiles }),
    queryClient.invalidateQueries({ queryKey: systemInfoQueryKey }),
  ]);
}
