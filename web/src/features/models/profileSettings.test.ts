import { describe, expect, it } from 'vitest';
import { providerDto } from '../../test/apiFixtures';
import {
  autoProviderLabel,
  defaultModelFor,
  isRerankingOff,
  PROFILE_ROLES,
  profileRole,
  providerSetupHint,
  providersForKind,
} from './profileSettings';
import type { ModelProfileDto } from './types';

const anthropic = providerDto({ key: 'anthropic', defaultChatModel: 'claude-opus-5-5' });
const ollama = providerDto({ key: 'ollama', supportsEmbeddings: true, defaultChatModel: 'qwen3', defaultEmbeddingModel: 'nomic-embed-text' });
const openai = providerDto({
  key: 'openai',
  supportsEmbeddings: true,
  defaultChatModel: 'gpt-5',
  defaultEmbeddingModel: 'text-embedding-3-small',
  defaultRerankModel: 'gpt-5-mini',
});
const voyage = providerDto({
  key: 'voyage',
  supportsChat: false,
  supportsEmbeddings: true,
  supportsRerank: true,
  defaultEmbeddingModel: 'voyage-4',
  defaultRerankModel: 'rerank-2.5',
});

describe('profile kinds', () => {
  it('offer the providers that can serve the kind', () => {
    const providers = [anthropic, ollama, openai, voyage];
    expect(providersForKind(providers, 'rerank').map((p) => p.key)).toEqual(['anthropic', 'ollama', 'openai', 'voyage']);
    expect(providersForKind(providers, 'embedding').map((p) => p.key)).toEqual(['ollama', 'openai', 'voyage']);
    expect(providersForKind(providers, 'chat').map((p) => p.key)).toEqual(['anthropic', 'ollama', 'openai']);
  });

  it('default rerank profiles to the rerank model, else to the chat model of chat providers', () => {
    expect(defaultModelFor(voyage, 'rerank')).toBe('rerank-2.5');
    expect(defaultModelFor(openai, 'rerank')).toBe('gpt-5-mini');
    expect(defaultModelFor(anthropic, 'rerank')).toBe('claude-opus-5-5');
    expect(defaultModelFor(voyage, 'embedding')).toBe('voyage-4');
    expect(defaultModelFor(undefined, 'rerank')).toBeNull();
  });

  it('name every built-in profile by its job, and others by their profile name', () => {
    const builtIn = ['orchestrator', 'researcher', 'utility', 'embedding', 'rerank', 'judge', 'contextualizer'];
    expect(builtIn.filter((name) => !PROFILE_ROLES[name])).toEqual([]);
    expect(profileRole('Orchestrator').label).toBe('Answering model');
    expect(profileRole('summarizer').label).toBe('summarizer');
  });

  it('say what auto picks and how to configure Voyage', () => {
    expect(autoProviderLabel('rerank')).toBe('auto (first configured reranker, else no reranking)');
    expect(autoProviderLabel('chat')).toBe('auto (first configured provider)');
    expect(providerSetupHint({ key: 'voyage', type: 'voyage' })).toBe('VOYAGE_API_KEY');
    expect(providerSetupHint({ key: 'voyage-eu', type: 'voyage' })).toBe('AI__Providers__voyage-eu__ApiKey');
  });

  it('treat an auto rerank profile without a reranker as reranking off, not as a failure', () => {
    const settings = { provider: 'auto', model: null, temperature: null, maxOutputTokens: null, reasoningEffort: null, reasoningOutput: null, dimensions: null };
    const rerank: ModelProfileDto = {
      name: 'rerank',
      kind: 'rerank',
      configured: settings,
      override: null,
      effective: null,
      error: 'No rerank provider is configured.',
      overrideUpdatedAt: null,
    };
    expect(isRerankingOff(rerank)).toBe(true);
    expect(isRerankingOff({ ...rerank, override: { ...settings, provider: 'voyage' } })).toBe(false);
    expect(isRerankingOff({ ...rerank, kind: 'chat' })).toBe(false);
    expect(isRerankingOff({ ...rerank, error: null })).toBe(false);
  });
});
