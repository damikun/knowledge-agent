import { useQuery, type QueryClient } from '@tanstack/react-query';
import { asArray, request } from '../../lib/api';
import { systemInfoQueryKey } from '../../lib/queryKeys';
import type { ApiKeySource, ModelProfileDto, ProviderDto, ProviderTestResultDto, UpdateModelProfileRequest, UpdateProviderRequest } from './types';

export const modelKeys = {
  profiles: ['models', 'profiles'] as const,
  providers: ['models', 'providers'] as const,
};

const API_KEY_SOURCES: readonly string[] = ['none', 'configuration', 'portal'] satisfies ApiKeySource[];

/** Older APIs don't send the provider settings fields. */
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

const providerPath = (key: string) => `/api/models/providers/${encodeURIComponent(key)}`;
const profilePath = (name: string) => `/api/models/profiles/${encodeURIComponent(name)}`;

export const modelsApi = {
  listProfiles: async (signal?: AbortSignal) => asArray<ModelProfileDto>(await request<unknown>('/api/models/profiles', { signal })),
  listProviders: async (signal?: AbortSignal) =>
    asArray<ProviderDto>(await request<unknown>('/api/models/providers', { signal })).map(normalizeProvider),
  /** Omit `apiKey` to keep the stored key. */
  updateProvider: async (key: string, body: UpdateProviderRequest) => {
    const provider = await request<ProviderDto | undefined>(providerPath(key), { method: 'PUT', body });
    return provider ? normalizeProvider(provider) : undefined;
  },
  resetProvider: async (key: string) => {
    const provider = await request<ProviderDto | undefined>(`${providerPath(key)}/override`, { method: 'DELETE' });
    return provider ? normalizeProvider(provider) : undefined;
  },
  /** Lists models; generates no tokens. */
  testProvider: async (key: string) =>
    normalizeTestResult(key, await request<Partial<ProviderTestResultDto> | undefined>(`${providerPath(key)}/test`, { method: 'POST' })),
  updateProfile: (name: string, body: UpdateModelProfileRequest) => request<ModelProfileDto | undefined>(profilePath(name), { method: 'PUT', body }),
  resetProfile: (name: string) => request<ModelProfileDto | undefined>(`${profilePath(name)}/override`, { method: 'DELETE' }),
};

export function useProfiles() {
  return useQuery({ queryKey: modelKeys.profiles, queryFn: ({ signal }) => modelsApi.listProfiles(signal), staleTime: 30_000 });
}

export function useProviders() {
  return useQuery({ queryKey: modelKeys.providers, queryFn: ({ signal }) => modelsApi.listProviders(signal), staleTime: 60_000 });
}

/** "auto" profiles may resolve to another provider now, and system info lists providers too. */
export async function invalidateProviderSettings(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: modelKeys.providers }),
    queryClient.invalidateQueries({ queryKey: modelKeys.profiles }),
    queryClient.invalidateQueries({ queryKey: systemInfoQueryKey }),
  ]);
}
