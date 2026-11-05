import type { CurrentUserDto } from '../features/auth/types';
import type { ModelProfileDto, ProviderDto } from '../features/models/types';
import type { SystemInfoDto } from '../features/system/types';

/** GET /api/auth/me for a signed-in admin, which mockFetch answers unless a test mocks the route itself. */
export function currentUser(overrides: Partial<CurrentUserDto> = {}): CurrentUserDto {
  return { name: 'Ada Lovelace', email: 'ada@example.com', roles: ['admin', 'support'], mode: 'oidc', canManageUsers: false, ...overrides };
}

export const adminUser = currentUser();

export const supportUser = currentUser({ name: 'Sam Rivera', email: 'sam@example.com', roles: ['support'] });

/** A provider without portal settings; `type` defaults to the key. */
export function providerDto(overrides: Partial<ProviderDto> & Pick<ProviderDto, 'key'>): ProviderDto {
  return {
    type: overrides.key,
    configured: false,
    supportsChat: true,
    supportsEmbeddings: false,
    defaultChatModel: null,
    defaultEmbeddingModel: null,
    defaultEmbeddingDimensions: null,
    endpoint: null,
    usesApiKey: false,
    usesEndpoint: false,
    endpointRequired: false,
    apiKeySource: 'none',
    apiKeyHint: null,
    hasOverride: false,
    overrideUpdatedAt: null,
    ...overrides,
  };
}

export const providersFixture: ProviderDto[] = [
  providerDto({ key: 'anthropic', defaultChatModel: 'claude-opus-5-5', usesApiKey: true, usesEndpoint: true }),
  providerDto({
    key: 'openai',
    configured: true,
    supportsEmbeddings: true,
    defaultChatModel: 'gpt-5-mini',
    defaultEmbeddingModel: 'text-embedding-3-small',
    usesApiKey: true,
    usesEndpoint: true,
    apiKeySource: 'configuration',
    apiKeyHint: '…9xQz',
  }),
  providerDto({
    key: 'ollama',
    supportsEmbeddings: true,
    defaultChatModel: 'qwen3',
    defaultEmbeddingModel: 'nomic-embed-text',
    usesEndpoint: true,
    endpointRequired: true,
  }),
  providerDto({
    key: 'voyage',
    supportsChat: false,
    supportsEmbeddings: true,
    supportsRerank: true,
    defaultEmbeddingModel: 'voyage-4',
    defaultEmbeddingDimensions: 1024,
    defaultRerankModel: 'rerank-2.5',
    usesApiKey: true,
    usesEndpoint: true,
  }),
];

export function systemInfoFixture(overrides: Partial<SystemInfoDto> = {}): SystemInfoDto {
  return {
    version: '1.0.0',
    environment: 'Development',
    persistenceProvider: 'InMemory',
    vectorStoreProvider: 'InMemory',
    promptStore: 'Local',
    langfuseConfigured: false,
    langfuseUrl: null,
    researcherEnabled: true,
    providers: providersFixture,
    publicAddress: { url: null, source: null },
    ...overrides,
  };
}

const chatSettings = { provider: 'auto', model: null, temperature: null, maxOutputTokens: 16000, reasoningEffort: 'medium', reasoningOutput: 'summary', dimensions: null };

/** Resolved to the default chat model of `providerType` in `providersFixture`. */
export function orchestratorProfile(providerType: string, overrides: Partial<ModelProfileDto> = {}): ModelProfileDto {
  return {
    name: 'orchestrator',
    kind: 'chat',
    configured: chatSettings,
    override: null,
    effective: {
      providerKey: providerType,
      providerType,
      model: providersFixture.find((p) => p.key === providerType)?.defaultChatModel ?? 'gpt-5-mini',
      temperature: null,
      maxOutputTokens: 16000,
      reasoningEffort: 'medium',
      reasoningOutput: 'summary',
      dimensions: null,
      source: 'Configuration',
    },
    error: null,
    overrideUpdatedAt: null,
    ...overrides,
  };
}

/** Resolved to openai, the embedding model of `sourceDto()`. */
export function embeddingProfile(overrides: Partial<ModelProfileDto> = {}): ModelProfileDto {
  return {
    name: 'embedding',
    kind: 'embedding',
    configured: { provider: 'auto', model: null, temperature: null, maxOutputTokens: null, reasoningEffort: null, reasoningOutput: null, dimensions: null },
    override: null,
    effective: {
      providerKey: 'openai',
      providerType: 'openai',
      model: 'text-embedding-3-small',
      temperature: null,
      maxOutputTokens: null,
      reasoningEffort: null,
      reasoningOutput: null,
      dimensions: 1536,
      source: 'Configuration',
    },
    error: null,
    overrideUpdatedAt: null,
    ...overrides,
  };
}
