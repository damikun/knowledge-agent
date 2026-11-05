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
  /** A dedicated reranking API (e.g. Voyage). Chat providers rerank through the LLM instead. */
  supportsRerank?: boolean;
  defaultRerankModel?: string | null;
  /** From configuration or the portal. */
  endpoint: string | null;
  usesApiKey: boolean;
  usesEndpoint: boolean;
  /** e.g. Ollama. */
  endpointRequired: boolean;
  apiKeySource: ApiKeySource;
  /** e.g. "…a1b2". The key itself is never returned. */
  apiKeyHint: string | null;
  /** Portal settings win over configuration. */
  hasOverride: boolean;
  overrideUpdatedAt: IsoDateTime | null;
}

/** Null fields fall back to configuration. */
export interface UpdateProviderRequest {
  /** Null or omitted keeps the stored key. */
  apiKey?: string | null;
  /** Removes the stored key so the configured one, if any, applies again. */
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
  /** Sorted. */
  models: string[];
  durationMs: number;
}
