import { parsePositiveInt } from '../../lib/forms';
import type { ModelProfileDto, ModelSettingsDto, ProviderDto, UpdateModelProfileRequest } from './types';

export const REASONING_EFFORTS = ['low', 'medium', 'high', 'extra_high'] as const;
export const REASONING_OUTPUTS = ['none', 'summary', 'full'] as const;

export interface ProfileRole {
  label: string;
  description: string;
}

/** The others sit under Advanced on the Models page. */
export const MAIN_PROFILES = ['orchestrator', 'embedding', 'rerank', 'contextualizer'] as const;

export const PROFILE_ROLES: Readonly<Record<string, ProfileRole>> = {
  orchestrator: { label: 'Answering model', description: 'Answers questions and decides which tools to use.' },
  embedding: { label: 'Embedding model', description: 'Turns sources and questions into vectors, so search finds passages that match.' },
  rerank: { label: 'Reranking model', description: 'Puts the most relevant search results first.' },
  contextualizer: { label: 'Contextualizer model', description: 'Writes a line of context for each passage, so search knows where it comes from.' },
  researcher: { label: 'Research model', description: 'Investigates hard questions in several steps for the answering model.' },
  utility: { label: 'Background tasks model', description: 'Small jobs such as naming conversations.' },
  judge: { label: 'Evaluation judge model', description: 'Grades answers in evaluations for faithfulness, relevance and correctness.' },
};

export function profileRole(name: string): ProfileRole {
  return PROFILE_ROLES[name.toLowerCase()] ?? { label: name, description: 'A model profile from configuration.' };
}

/** Empty means "use the default". */
export interface ProfileFormValues {
  provider: string;
  model: string;
  maxOutputTokens: string;
  reasoningEffort: string;
  reasoningOutput: string;
  temperature: string;
  dimensions: string;
}

export type ProfileFormErrors = Partial<Record<keyof ProfileFormValues, string>>;

function str(value: string | number | null | undefined): string {
  return value === null || value === undefined ? '' : String(value);
}

export function isEmbeddingProfile(profile: Pick<ModelProfileDto, 'kind'>): boolean {
  return profile.kind?.toLowerCase() === 'embedding';
}

/** Rerank on auto without a reranker is the normal state without a reranker key, not a failure. */
export function isRerankingOff(profile: Pick<ModelProfileDto, 'kind' | 'error' | 'effective' | 'configured' | 'override'>): boolean {
  const provider = (profile.override ?? profile.configured)?.provider ?? 'auto';
  return profile.kind?.toLowerCase() === 'rerank' && !profile.effective && !!profile.error && provider.toLowerCase() === 'auto';
}

export function autoProviderLabel(kind: string): string {
  return kind.toLowerCase() === 'rerank' ? 'auto (first configured reranker, else no reranking)' : 'auto (first configured provider)';
}

export function initialFormValues(profile: ModelProfileDto): ProfileFormValues {
  const source: ModelSettingsDto = profile.override ?? profile.configured;
  return {
    provider: source.provider || 'auto',
    model: str(source.model),
    maxOutputTokens: str(source.maxOutputTokens),
    reasoningEffort: str(source.reasoningEffort),
    reasoningOutput: str(source.reasoningOutput),
    temperature: str(source.temperature),
    dimensions: str(source.dimensions),
  };
}

export function toUpdateRequest(
  values: ProfileFormValues,
  kind: string,
): { request: UpdateModelProfileRequest | null; errors: ProfileFormErrors } {
  const errors: ProfileFormErrors = {};
  const embedding = kind.toLowerCase() === 'embedding';
  const request: UpdateModelProfileRequest = {
    provider: values.provider.trim() || 'auto',
    model: values.model.trim() || null,
  };

  if (embedding) {
    const dimensions = parsePositiveInt(values.dimensions);
    if (dimensions === 'invalid') {
      errors.dimensions = 'Enter a whole number greater than 0.';
    } else {
      request.dimensions = dimensions;
    }
  } else {
    const maxOutputTokens = parsePositiveInt(values.maxOutputTokens);
    if (maxOutputTokens === 'invalid') {
      errors.maxOutputTokens = 'Enter a whole number greater than 0.';
    } else {
      request.maxOutputTokens = maxOutputTokens;
    }
    const temperatureText = values.temperature.trim();
    if (temperatureText) {
      const temperature = Number(temperatureText);
      if (Number.isNaN(temperature) || temperature < 0 || temperature > 2) {
        errors.temperature = 'Use a value between 0 and 2.';
      } else {
        request.temperature = temperature;
      }
    } else {
      request.temperature = null;
    }
    request.reasoningEffort = values.reasoningEffort || null;
    request.reasoningOutput = values.reasoningOutput || null;
  }

  return { request: Object.keys(errors).length > 0 ? null : request, errors };
}

export function providerSetupHint(provider: Pick<ProviderDto, 'key' | 'type'>): string {
  const type = provider.type.toLowerCase();
  const key = provider.key;
  switch (type) {
    case 'anthropic':
      return key.toLowerCase() === 'anthropic' ? 'ANTHROPIC_API_KEY' : `AI__Providers__${key}__ApiKey`;
    case 'openai':
      return key.toLowerCase() === 'openai' ? 'OPENAI_API_KEY' : `AI__Providers__${key}__ApiKey`;
    case 'ollama':
      return key.toLowerCase() === 'ollama' ? 'OLLAMA_ENDPOINT' : `AI__Providers__${key}__Endpoint`;
    case 'voyage':
      return key.toLowerCase() === 'voyage' ? 'VOYAGE_API_KEY' : `AI__Providers__${key}__ApiKey`;
    default:
      return `AI__Providers__${key}__ApiKey`;
  }
}

/** Rerank profiles also take chat models, which rerank through the LLM. */
export function providersForKind(providers: readonly ProviderDto[], kind: string): ProviderDto[] {
  switch (kind.toLowerCase()) {
    case 'embedding':
      return providers.filter((p) => p.supportsEmbeddings);
    case 'rerank':
      return providers.filter((p) => p.supportsRerank || p.supportsChat);
    default:
      return providers.filter((p) => p.supportsChat);
  }
}

export function defaultModelFor(provider: ProviderDto | undefined, kind: string): string | null {
  if (!provider) {
    return null;
  }
  switch (kind.toLowerCase()) {
    case 'embedding':
      return provider.defaultEmbeddingModel;
    case 'rerank':
      return provider.defaultRerankModel ?? (provider.supportsRerank ? null : provider.defaultChatModel);
    default:
      return provider.defaultChatModel;
  }
}
