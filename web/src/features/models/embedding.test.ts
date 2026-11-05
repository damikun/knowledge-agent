import { describe, expect, it } from 'vitest';
import { sourceDto } from '../../test/fixtures';
import { embeddingKey, isCurrentEmbedding, staleEmbeddings, staleEmbeddingSources } from './embedding';
import type { ModelProfileDto } from './types';

describe('embedding model changes', () => {
  const profile = (dimensions: number | null): ModelProfileDto => ({
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
      dimensions,
      source: 'Configuration',
    },
    error: null,
    overrideUpdatedAt: null,
  });

  it('builds the vector space key like the API', () => {
    expect(embeddingKey({ providerType: 'openai', model: 'text-embedding-3-small', dimensions: 1536 })).toBe('openai:text-embedding-3-small:1536');
  });

  it('flags ready sources embedded with another model', () => {
    const sources = [
      sourceDto({ id: 'current', embeddingModel: 'openai:text-embedding-3-small:1536' }),
      sourceDto({ id: 'stale', embeddingModel: 'voyage:voyage-4:1024' }),
      sourceDto({ id: 'pending', status: 'pending', embeddingModel: 'voyage:voyage-4:1024' }),
      sourceDto({ id: 'never', embeddingModel: null }),
    ];
    expect(staleEmbeddingSources(sources, profile(1536)).map((s) => s.id)).toEqual(['stale']);
    expect(staleEmbeddingSources(sources, undefined)).toEqual([]);
  });

  it('counts stale ready sources from the stats per embedding model', () => {
    const byModel = { 'openai:text-embedding-3-small:1536': 1200, 'voyage:voyage-4:1024': 38, 'ollama:nomic-embed-text:768': 2, '': 5 };
    expect(staleEmbeddings(byModel, profile(1536))).toEqual({
      count: 40,
      models: ['voyage:voyage-4:1024', 'ollama:nomic-embed-text:768'],
    });
    expect(staleEmbeddings({ 'openai:text-embedding-3-small:1536': 7 }, profile(1536))).toEqual({ count: 0, models: [] });
    expect(staleEmbeddings({ 'openai:text-embedding-3-small:512': 3 }, profile(null)).count).toBe(0);
    expect(staleEmbeddings(byModel, undefined)).toEqual({ count: 0, models: [] });
    expect(staleEmbeddings(undefined, profile(1536))).toEqual({ count: 0, models: [] });
  });

  it('compares provider and model only when the profile does not report dimensions', () => {
    expect(isCurrentEmbedding('openai:text-embedding-3-small:512', profile(null).effective!)).toBe(true);
    expect(isCurrentEmbedding('openai:text-embedding-3-small:512', profile(1536).effective!)).toBe(false);
    expect(isCurrentEmbedding('ollama:nomic-embed-text:768', profile(null).effective!)).toBe(false);
  });
});
