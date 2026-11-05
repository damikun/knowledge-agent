import { afterEach, describe, expect, it, vi } from 'vitest';
import { providerDto } from '../../test/apiFixtures';
import { fetchCalls, jsonResponse, mockFetch } from '../../test/utils';
import { modelsApi } from './api';
import type { ProviderDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('modelsApi', () => {
  it('saves, resets and tests providers', async () => {
    const saved = providerDto({ key: 'my provider', usesApiKey: true, apiKeySource: 'portal', apiKeyHint: '…cdef', hasOverride: true });
    const fetchMock = mockFetch({
      'PUT /api/models/providers/my%20provider': () => jsonResponse(saved),
      'DELETE /api/models/providers/my%20provider/override': () => jsonResponse({ ...saved, apiKeySource: 'none', apiKeyHint: null, hasOverride: false }),
      'POST /api/models/providers/my%20provider/test': () => jsonResponse({ key: 'my provider', ok: true, message: 'Connected.', durationMs: 120 }),
    });

    await expect(modelsApi.updateProvider('my provider', { apiKey: 'sk-new', endpoint: null })).resolves.toEqual(saved);
    await expect(modelsApi.resetProvider('my provider')).resolves.toMatchObject({ apiKeySource: 'none', hasOverride: false });
    await expect(modelsApi.testProvider('my provider')).resolves.toEqual({
      key: 'my provider',
      ok: true,
      message: 'Connected.',
      models: [],
      durationMs: 120,
    });

    expect(fetchCalls(fetchMock)).toEqual([
      'PUT /api/models/providers/my%20provider',
      'DELETE /api/models/providers/my%20provider/override',
      'POST /api/models/providers/my%20provider/test',
    ]);
    const [, put] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(put.body as string)).toEqual({ apiKey: 'sk-new', endpoint: null });
    const [, test] = fetchMock.mock.calls[2] as unknown as [string, RequestInit];
    expect(test.body).toBeNull();
  });

  it('fills provider settings fields that older API versions do not send', async () => {
    mockFetch({
      'GET /api/models/providers': [
        { key: 'openai', type: 'openai', configured: true, supportsChat: true, supportsEmbeddings: true, defaultChatModel: 'gpt-5-mini', defaultEmbeddingModel: null },
        providerDto({ key: 'anthropic', usesApiKey: true, apiKeySource: 'Portal' as ProviderDto['apiKeySource'], apiKeyHint: '…a1b2' }),
      ],
    });
    const [legacy, current] = await modelsApi.listProviders();
    expect(legacy).toEqual(
      providerDto({ key: 'openai', configured: true, supportsEmbeddings: true, defaultChatModel: 'gpt-5-mini' }),
    );
    expect(current).toMatchObject({ apiKeySource: 'portal', apiKeyHint: '…a1b2' });
  });
});
