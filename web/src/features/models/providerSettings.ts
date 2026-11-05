import { parsePositiveInt } from '../../lib/forms';
import type { ProviderDto, UpdateProviderRequest } from './types';

export const PROVIDERS_ANCHOR = 'providers';
export const PROVIDERS_PATH = `/models#${PROVIDERS_ANCHOR}`;

/** Empty means "use the configured value", except apiKey, where it means "keep the current key". */
export interface ProviderFormValues {
  /** Never prefilled: the API doesn't return keys. */
  apiKey: string;
  clearApiKey: boolean;
  endpoint: string;
  defaultChatModel: string;
  defaultEmbeddingModel: string;
  defaultEmbeddingDimensions: string;
}

export type ProviderTextField = Exclude<keyof ProviderFormValues, 'clearApiKey'>;
export type ProviderFormErrors = Partial<Record<ProviderTextField, string>>;
export type ModelField = 'defaultChatModel' | 'defaultEmbeddingModel';

export const ENDPOINT_EXAMPLE = 'http://localhost:11434';

export const PROVIDER_DESCRIPTIONS: Record<string, string> = {
  anthropic: 'Claude models through the Anthropic API.',
  openai: 'OpenAI models, or any OpenAI-compatible server through the endpoint.',
  ollama: 'Local or self-hosted models served by Ollama.',
  voyage: 'Embeddings and reranking from Voyage AI.',
};

function str(value: string | number | null | undefined): string {
  return value === null || value === undefined ? '' : String(value);
}

/** Keys come from configuration and may contain characters invalid in ids. */
export function providerDomId(key: string): string {
  return `provider-${key.replace(/[^\w-]/g, '_')}`;
}

export function visibleFields(provider: ProviderDto): ProviderTextField[] {
  const fields: ProviderTextField[] = [];
  if (provider.usesApiKey) {
    fields.push('apiKey');
  }
  if (provider.usesEndpoint) {
    fields.push('endpoint');
  }
  if (provider.supportsChat) {
    fields.push('defaultChatModel');
  }
  if (provider.supportsEmbeddings) {
    fields.push('defaultEmbeddingModel', 'defaultEmbeddingDimensions');
  }
  return fields;
}

/** Without portal settings the form starts empty so untouched fields stay null and fall back to configuration. */
export function initialProviderFormValues(provider: ProviderDto): ProviderFormValues {
  const keep = provider.hasOverride;
  return {
    apiKey: '',
    clearApiKey: false,
    endpoint: keep && provider.usesEndpoint ? str(provider.endpoint) : '',
    defaultChatModel: keep && provider.supportsChat ? str(provider.defaultChatModel) : '',
    defaultEmbeddingModel: keep && provider.supportsEmbeddings ? str(provider.defaultEmbeddingModel) : '',
    defaultEmbeddingDimensions: keep && provider.supportsEmbeddings ? str(provider.defaultEmbeddingDimensions) : '',
  };
}

export function hasApiKey(provider: Pick<ProviderDto, 'apiKeySource'>): boolean {
  return provider.apiKeySource === 'portal' || provider.apiKeySource === 'configuration';
}

/** Null for providers without API keys. */
export function describeApiKey(provider: Pick<ProviderDto, 'usesApiKey' | 'apiKeySource' | 'apiKeyHint'>): string | null {
  if (!provider.usesApiKey) {
    return null;
  }
  const hint = provider.apiKeyHint ? ` ${provider.apiKeyHint}` : '';
  switch (provider.apiKeySource) {
    case 'portal':
      return `Saved in portal${hint}`;
    case 'configuration':
      return `From configuration${hint}`;
    default:
      return 'No API key';
  }
}

/** Only known while nothing is saved in the portal. */
export function hasConfiguredEndpoint(provider: Pick<ProviderDto, 'hasOverride' | 'endpoint'>): boolean {
  return !provider.hasOverride && Boolean(provider.endpoint);
}

/** The provider needs an endpoint and no configured one is known to apply. */
export function isEndpointMandatory(provider: Pick<ProviderDto, 'endpointRequired' | 'hasOverride' | 'endpoint'>): boolean {
  return provider.endpointRequired && !hasConfiguredEndpoint(provider);
}

function isHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

/** The key is sent only when typed or cleared; fields the provider lacks are sent as null. */
export function toProviderUpdateRequest(
  values: ProviderFormValues,
  provider: ProviderDto,
): { request: UpdateProviderRequest | null; errors: ProviderFormErrors } {
  const errors: ProviderFormErrors = {};
  const request: UpdateProviderRequest = {};

  if (provider.usesApiKey) {
    const apiKey = values.apiKey.trim();
    if (values.clearApiKey) {
      request.clearApiKey = true;
    } else if (apiKey) {
      request.apiKey = apiKey;
    }
  }

  const endpoint = provider.usesEndpoint ? values.endpoint.trim() : '';
  if (endpoint && !isHttpUrl(endpoint)) {
    errors.endpoint = `Enter an absolute http(s) URL, e.g. ${ENDPOINT_EXAMPLE}.`;
  } else if (!endpoint && provider.usesEndpoint && isEndpointMandatory(provider)) {
    errors.endpoint = `${provider.key} needs an endpoint, e.g. ${ENDPOINT_EXAMPLE}.`;
  }
  request.endpoint = endpoint || null;

  request.defaultChatModel = provider.supportsChat ? values.defaultChatModel.trim() || null : null;
  request.defaultEmbeddingModel = provider.supportsEmbeddings ? values.defaultEmbeddingModel.trim() || null : null;
  const dimensions = provider.supportsEmbeddings ? parsePositiveInt(values.defaultEmbeddingDimensions) : null;
  if (dimensions === 'invalid') {
    errors.defaultEmbeddingDimensions = 'Enter a whole number greater than 0.';
  } else {
    request.defaultEmbeddingDimensions = dimensions;
  }

  return { request: Object.keys(errors).length > 0 ? null : request, errors };
}

export function modelFieldFor(provider: Pick<ProviderDto, 'supportsChat' | 'supportsEmbeddings'>, model: string): ModelField | null {
  if (provider.supportsEmbeddings && (!provider.supportsChat || /embed/i.test(model))) {
    return 'defaultEmbeddingModel';
  }
  return provider.supportsChat ? 'defaultChatModel' : null;
}
