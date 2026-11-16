import type { IsoDateTime } from '../../lib/types';

export interface ModelSettingsDto {
  provider: string;
  model: string | null;
  temperature: number | null;
  maxOutputTokens: number | null;
  reasoningEffort: string | null;
  reasoningOutput: string | null;
  dimensions: number | null;
}

type ModelProfileSource = 'Configuration' | 'Override' | 'Prompt' | 'Request';

export interface EffectiveModelDto {
  providerKey: string;
  providerType: string;
  model: string;
  temperature: number | null;
  maxOutputTokens: number | null;
  reasoningEffort: string | null;
  reasoningOutput: string | null;
  dimensions: number | null;
  source: ModelProfileSource;
}

type ModelKind = 'chat' | 'embedding' | 'rerank';

export interface ModelProfileDto {
  name: string;
  kind: ModelKind;
  configured: ModelSettingsDto;
  override: ModelSettingsDto | null;
  effective: EffectiveModelDto | null;
  error: string | null;
  overrideUpdatedAt: IsoDateTime | null;
}

export interface UpdateModelProfileRequest {
  provider: string;
  model?: string | null;
  temperature?: number | null;
  maxOutputTokens?: number | null;
  reasoningEffort?: string | null;
  reasoningOutput?: string | null;
  dimensions?: number | null;
}

export type ApiKeySource = 'none' | 'configuration' | 'portal';

export interface ProviderDto {
  key: string;
  type: string;
  configured: boolean;
  supportsChat: boolean;
  supportsEmbeddings: boolean;
  defaultChatModel: string | null;
  defaultEmbeddingModel: string | null;
  defaultEmbeddingDimensions: number | null;
  supportsRerank?: boolean;
  defaultRerankModel?: string | null;
  endpoint: string | null;
  usesApiKey: boolean;
  usesEndpoint: boolean;
  endpointRequired: boolean;
  apiKeySource: ApiKeySource;
  apiKeyHint: string | null;
  hasOverride: boolean;
  overrideUpdatedAt: IsoDateTime | null;
}

export interface UpdateProviderRequest {
  apiKey?: string | null;
  clearApiKey?: boolean;
  endpoint?: string | null;
  defaultChatModel?: string | null;
  defaultEmbeddingModel?: string | null;
  defaultEmbeddingDimensions?: number | null;
}

export interface ProviderTestResultDto {
  key: string;
  ok: boolean;
  message: string;
  models: string[];
  durationMs: number;
}
