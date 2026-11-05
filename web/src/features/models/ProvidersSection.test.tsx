import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi, type Mock } from 'vitest';
import { Toaster } from '../../components/ui/Toast';
import { systemInfoQueryKey } from '../../lib/queryKeys';
import { providerDto, providersFixture, systemInfoFixture } from '../../test/apiFixtures';
import { createTestQueryClient, fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders } from '../../test/utils';
import { modelKeys } from './api';
import { describeApiKey, initialProviderFormValues, modelFieldFor, toProviderUpdateRequest, type ProviderFormValues } from './providerSettings';
import { ProvidersSection } from './ProvidersSection';
import type { ProviderDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

const [anthropicWithoutKey, openai, ollama, voyage] = providersFixture as [ProviderDto, ProviderDto, ProviderDto, ProviderDto];

const anthropicSaved = providerDto({
  key: 'anthropic',
  configured: true,
  defaultChatModel: 'claude-opus-5-5',
  endpoint: 'https://llm-proxy.internal/anthropic',
  usesApiKey: true,
  usesEndpoint: true,
  apiKeySource: 'portal',
  apiKeyHint: '…a1b2',
  hasOverride: true,
  overrideUpdatedAt: '2026-10-26T09:00:00Z',
});

type Routes = Record<string, unknown>;

/** `routes` receives `save`, which stores a provider. Profiles and system info are cached, not observed, so their invalidation can be asserted. */
function setup(initial: ProviderDto[], routes: (save: (provider: ProviderDto) => Response) => Routes = () => ({})) {
  let providers = [...initial];
  const save = (next: ProviderDto) => {
    providers = providers.map((p) => (p.key === next.key ? next : p));
    return jsonResponse(next);
  };
  const fetchMock = mockFetch({ 'GET /api/models/providers': () => jsonResponse(providers), ...routes(save) });
  const queryClient = createTestQueryClient();
  queryClient.setQueryData(modelKeys.profiles, []);
  queryClient.setQueryData(systemInfoQueryKey, systemInfoFixture());
  renderWithProviders(
    <>
      <ProvidersSection />
      <Toaster />
    </>,
    { queryClient },
  );
  return { fetchMock, queryClient };
}

function bodies(fetchMock: Mock, method: string): string[] {
  return fetchMock.mock.calls
    .filter(([, init]) => (init as RequestInit | undefined)?.method === method)
    .map(([, init]) => (init as RequestInit).body as string);
}

function lastBody(fetchMock: Mock, method: string): Record<string, unknown> {
  return JSON.parse(bodies(fetchMock, method).at(-1)!) as Record<string, unknown>;
}

const card = (key: string) => screen.findByRole('region', { name: key });

async function openSettings(user: ReturnType<typeof userEvent.setup>, provider: HTMLElement, button: 'Edit' | 'Set API key' = 'Edit') {
  await user.click(within(provider).getByRole('button', { name: button }));
  return screen.getByRole('dialog', { name: within(provider).getByRole('heading', { level: 3 }).textContent! });
}

const sheetClosed = () => expect(screen.queryByRole('dialog')).toBeNull();

describe('ProvidersSection', () => {
  it('shows status, what each offers, where each key comes from, endpoints and default models', async () => {
    setup([
      anthropicSaved,
      providerDto({ key: 'claude-proxy', type: 'anthropic', defaultChatModel: 'claude-opus-5-5', usesApiKey: true, usesEndpoint: true }),
      ...providersFixture.slice(1),
    ]);

    const anthropic = await card('anthropic');
    expect(within(anthropic).getByText('Ready')).toBeInTheDocument();
    const offers = within(anthropic).getByRole('list', { name: 'Offers' });
    expect(within(offers).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Chat']);
    expect(within(anthropic).getByText('Saved in portal …a1b2')).toBeInTheDocument();
    expect(within(anthropic).getByText('https://llm-proxy.internal/anthropic')).toBeInTheDocument();
    expect(within(anthropic).getByText('claude-opus-5-5')).toBeInTheDocument();
    expect(within(anthropic).getByText(/^Settings saved in the portal/)).toBeInTheDocument();
    expect(within(anthropic).getByRole('button', { name: 'Reset to configuration' })).toBeInTheDocument();
    expect(within(anthropic).getByRole('button', { name: 'Edit' })).toBeInTheDocument();
    expect(within(anthropic).getByRole('button', { name: 'Test connection' })).toBeInTheDocument();

    const proxy = screen.getByRole('region', { name: 'claude-proxy' });
    expect(within(proxy).getByText('(anthropic)')).toBeInTheDocument();
    expect(within(proxy).getByText('Not set up')).toBeInTheDocument();
    expect(within(proxy).getByText('No API key')).toBeInTheDocument();
    expect(within(proxy).getByText('Provider default')).toBeInTheDocument();
    expect(within(proxy).getByRole('button', { name: 'Set API key' })).toBeInTheDocument();
    expect(within(proxy).queryByRole('button', { name: 'Reset to configuration' })).toBeNull();

    const openaiCard = screen.getByRole('region', { name: 'openai' });
    expect(within(openaiCard).getByText('From configuration …9xQz')).toBeInTheDocument();
    expect(within(within(openaiCard).getByRole('list', { name: 'Offers' })).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Chat', 'Embeddings']);
    expect(within(openaiCard).getByText('text-embedding-3-small')).toBeInTheDocument();

    const ollamaCard = screen.getByRole('region', { name: 'ollama' });
    expect(within(ollamaCard).getByText('Not set')).toBeInTheDocument();
    expect(within(ollamaCard).queryByText('API key')).toBeNull();

    const voyageCard = screen.getByRole('region', { name: 'voyage' });
    expect(within(within(voyageCard).getByRole('list', { name: 'Offers' })).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      'Embeddings',
      'Reranking',
    ]);
    expect(within(voyageCard).getByText('voyage-4')).toBeInTheDocument();
    expect(within(voyageCard).getByText(/1024 dimensions/)).toBeInTheDocument();
    expect(within(voyageCard).getByText('No API key')).toBeInTheDocument();

    expect(screen.getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(['anthropic', 'claude-proxy', 'ollama', 'openai', 'voyage']);
  });

  it('sends a typed API key (never the hint), leaves untouched fields on configuration and refreshes dependent queries', async () => {
    const user = userEvent.setup();
    const { fetchMock, queryClient } = setup(providersFixture, (save) => ({
      'PUT /api/models/providers/openai': () =>
        save({ ...openai, apiKeySource: 'portal', apiKeyHint: '…cdef', hasOverride: true, overrideUpdatedAt: new Date().toISOString() }),
    }));

    const openaiCard = await card('openai');
    const sheet = await openSettings(user, openaiCard);
    const form = within(sheet).getByRole('form', { name: 'Edit openai provider' });
    const apiKey = within(form).getByLabelText('API key');
    expect(apiKey).toHaveFocus();
    expect(apiKey).toHaveValue('');
    expect(apiKey).toHaveAttribute('type', 'password');
    expect(apiKey).toHaveAttribute('placeholder', 'Paste a new key to replace it');
    expect(within(form).getByText('Leave empty to keep the current key (…9xQz).')).toBeInTheDocument();
    expect(within(form).getByRole('textbox', { name: 'Default chat model' })).toHaveValue('');
    expect(within(form).getByRole('textbox', { name: 'Default chat model' })).toHaveAttribute('placeholder', 'gpt-5-mini');
    expect(within(form).getByRole('textbox', { name: 'Default embedding model' })).toHaveAttribute('placeholder', 'text-embedding-3-small');

    await user.type(apiKey, '  sk-test-1234567890abcdef  ');
    await user.click(within(form).getByRole('button', { name: 'Show API key' }));
    expect(apiKey).toHaveAttribute('type', 'text');
    await user.click(within(form).getByRole('button', { name: 'Hide API key' }));
    expect(apiKey).toHaveAttribute('type', 'password');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));

    await waitFor(sheetClosed);
    const [raw] = bodies(fetchMock, 'PUT');
    expect(JSON.parse(raw!)).toEqual({
      apiKey: 'sk-test-1234567890abcdef',
      endpoint: null,
      defaultChatModel: null,
      defaultEmbeddingModel: null,
      defaultEmbeddingDimensions: null,
    });
    expect(raw).not.toContain('9xQz');
    expect(raw).not.toContain('…');

    expect(within(openaiCard).getByText('Saved in portal …cdef')).toBeInTheDocument();
    expect(await screen.findByText('openai saved')).toBeInTheDocument();
    await waitFor(() => expect(within(openaiCard).getByRole('button', { name: 'Edit' })).toHaveFocus());
    expect(queryClient.getQueryState(modelKeys.profiles)?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(systemInfoQueryKey)?.isInvalidated).toBe(true);
    await waitFor(() => expect(fetchCalls(fetchMock).filter((c) => c === 'GET /api/models/providers')).toHaveLength(2));
  });

  it('keeps saved values, omits the API key when none is typed and sends null for cleared fields', async () => {
    const user = userEvent.setup();
    const { fetchMock } = setup([anthropicSaved], (save) => ({
      'PUT /api/models/providers/anthropic': () => save({ ...anthropicSaved, defaultChatModel: 'claude-sonnet-5' }),
    }));

    const anthropic = await card('anthropic');
    let sheet = await openSettings(user, anthropic);
    expect(within(sheet).getByLabelText('API key')).toHaveValue('');
    const endpoint = within(sheet).getByRole('textbox', { name: 'Endpoint' });
    const chatModel = within(sheet).getByRole('textbox', { name: 'Default chat model' });
    expect(endpoint).toHaveValue('https://llm-proxy.internal/anthropic');
    expect(chatModel).toHaveValue('claude-opus-5-5');

    await user.clear(chatModel);
    await user.type(chatModel, 'claude-sonnet-5');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(bodies(fetchMock, 'PUT')).toHaveLength(1));
    expect(lastBody(fetchMock, 'PUT')).toEqual({
      endpoint: 'https://llm-proxy.internal/anthropic',
      defaultChatModel: 'claude-sonnet-5',
      defaultEmbeddingModel: null,
      defaultEmbeddingDimensions: null,
    });

    await waitFor(sheetClosed);
    sheet = await openSettings(user, anthropic);
    await user.clear(within(sheet).getByRole('textbox', { name: 'Endpoint' }));
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(bodies(fetchMock, 'PUT')).toHaveLength(2));
    expect(lastBody(fetchMock, 'PUT')).toEqual({
      endpoint: null,
      defaultChatModel: 'claude-sonnet-5',
      defaultEmbeddingModel: null,
      defaultEmbeddingDimensions: null,
    });
  });

  it('removes the key saved in the portal with clearApiKey', async () => {
    const user = userEvent.setup();
    const { fetchMock } = setup([anthropicSaved], (save) => ({
      'PUT /api/models/providers/anthropic': () => save({ ...anthropicSaved, configured: false, apiKeySource: 'none', apiKeyHint: null }),
    }));

    const anthropic = await card('anthropic');
    const sheet = await openSettings(user, anthropic);
    const apiKey = within(sheet).getByLabelText('API key');
    await user.type(apiKey, 'sk-typed-by-mistake');

    await user.click(within(sheet).getByRole('button', { name: 'Remove saved key' }));
    expect(apiKey).toBeDisabled();
    expect(apiKey).toHaveValue('');
    expect(within(sheet).getByText(/The saved key …a1b2 is removed when you save/)).toBeInTheDocument();
    await user.click(within(sheet).getByRole('button', { name: 'Keep saved key' }));
    expect(apiKey).toBeEnabled();
    await user.click(within(sheet).getByRole('button', { name: 'Remove saved key' }));
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies(fetchMock, 'PUT')).toHaveLength(1));
    const body = lastBody(fetchMock, 'PUT');
    expect(body).toMatchObject({ clearApiKey: true, endpoint: 'https://llm-proxy.internal/anthropic', defaultChatModel: 'claude-opus-5-5' });
    expect(body).not.toHaveProperty('apiKey');
    expect(await within(anthropic).findByText('No API key')).toBeInTheDocument();
    expect(within(anthropic).getByRole('button', { name: 'Set API key' })).toBeInTheDocument();
  });

  it('maps server validation errors onto fields and shows other failures inline', async () => {
    const user = userEvent.setup();
    let attempts = 0;
    const { fetchMock } = setup(providersFixture, (save) => ({
      'PUT /api/models/providers/openai': () => {
        attempts += 1;
        if (attempts === 1) {
          return problemResponse(
            {
              title: 'One or more validation errors occurred.',
              errors: {
                apiKey: ['That does not look like an API key (no spaces or line breaks, at most 512 characters).'],
                endpoint: ['Private network addresses are not allowed.'],
                providerKey: ['Provider settings are read-only in this environment.'],
              },
            },
            400,
          );
        }
        if (attempts === 2) {
          return problemResponse({ title: 'Internal Server Error', detail: 'The settings store is not available.' }, 500);
        }
        return save({ ...openai, endpoint: 'https://gateway.example.com/v1', hasOverride: true });
      },
    }));

    const openaiCard = await card('openai');
    const sheet = await openSettings(user, openaiCard);
    const form = within(sheet).getByRole('form', { name: 'Edit openai provider' });
    const apiKey = within(form).getByLabelText('API key');
    const endpoint = within(form).getByRole('textbox', { name: 'Endpoint' });
    await user.type(apiKey, 'bad key');
    await user.type(endpoint, 'http://10.0.0.5/v1');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));

    expect(await within(form).findByText(/That does not look like an API key/, { selector: 'p' })).toBeInTheDocument();
    expect(within(form).getByText('Private network addresses are not allowed.', { selector: 'p' })).toBeInTheDocument();
    expect(apiKey).toHaveAttribute('aria-invalid', 'true');
    expect(endpoint).toHaveAttribute('aria-invalid', 'true');
    await waitFor(() => expect(apiKey).toHaveFocus());
    const alert = within(form).getByRole('alert');
    expect(alert).toHaveTextContent('The settings were not saved');
    expect(alert).toHaveTextContent('Provider settings are read-only in this environment.');
    expect(alert).not.toHaveTextContent('Private network addresses');

    await user.clear(apiKey);
    await user.clear(endpoint);
    await user.type(endpoint, 'https://gateway.example.com/v1');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    expect(await within(form).findByText('The settings store is not available.')).toBeInTheDocument();
    expect(endpoint).not.toHaveAttribute('aria-invalid');

    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    await waitFor(sheetClosed);
    expect(lastBody(fetchMock, 'PUT')).toEqual({
      endpoint: 'https://gateway.example.com/v1',
      defaultChatModel: null,
      defaultEmbeddingModel: null,
      defaultEmbeddingDimensions: null,
    });
  });

  it('requires an endpoint where the provider needs one and validates it before sending', async () => {
    const user = userEvent.setup();
    const { fetchMock } = setup([ollama], (save) => ({
      'PUT /api/models/providers/ollama': () => save({ ...ollama, configured: true, endpoint: 'http://localhost:11434', hasOverride: true }),
    }));

    const ollamaCard = await card('ollama');
    const sheet = await openSettings(user, ollamaCard);
    const form = within(sheet).getByRole('form', { name: 'Edit ollama provider' });
    expect(within(form).queryByLabelText('API key')).toBeNull();
    const endpoint = within(form).getByRole('textbox', { name: 'Endpoint' });
    expect(endpoint).toHaveFocus();
    expect(endpoint).toBeRequired();
    expect(endpoint).toHaveAttribute('placeholder', 'http://localhost:11434');

    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    expect(within(form).getByText('ollama needs an endpoint, e.g. http://localhost:11434.')).toBeInTheDocument();
    await user.type(endpoint, 'localhost:11434');
    const dimensions = within(form).getByRole('textbox', { name: 'Embedding dimensions' });
    await user.type(dimensions, '1.5');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    expect(within(form).getByText('Enter an absolute http(s) URL, e.g. http://localhost:11434.')).toBeInTheDocument();
    expect(within(form).getByText('Enter a whole number greater than 0.')).toBeInTheDocument();
    expect(endpoint).toHaveFocus();
    expect(bodies(fetchMock, 'PUT')).toHaveLength(0);

    await user.clear(endpoint);
    await user.type(endpoint, 'http://localhost:11434');
    await user.clear(dimensions);
    await user.type(dimensions, '768');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(bodies(fetchMock, 'PUT')).toHaveLength(1));
    expect(lastBody(fetchMock, 'PUT')).toEqual({
      endpoint: 'http://localhost:11434',
      defaultChatModel: null,
      defaultEmbeddingModel: null,
      defaultEmbeddingDimensions: 768,
    });
    expect(await within(ollamaCard).findByText('http://localhost:11434')).toBeInTheDocument();
  });

  it('tests the connection and fills a default model from the reported models', async () => {
    const user = userEvent.setup();
    const { fetchMock } = setup(providersFixture, (save) => ({
      'POST /api/models/providers/openai/test': {
        key: 'openai',
        ok: true,
        message: 'Connected to https://api.openai.com/v1.',
        models: ['gpt-5', 'gpt-5-mini', 'text-embedding-3-large'],
        durationMs: 412,
      },
      'PUT /api/models/providers/openai': () => save({ ...openai, defaultChatModel: 'gpt-5', hasOverride: true }),
    }));

    const openaiCard = await card('openai');
    await user.click(within(openaiCard).getByRole('button', { name: 'Test connection' }));
    expect(await within(openaiCard).findByText('Connection works')).toBeInTheDocument();
    expect(within(openaiCard).getByText('· 412 ms')).toBeInTheDocument();
    expect(within(openaiCard).getByText('Connected to https://api.openai.com/v1.')).toBeInTheDocument();
    expect(within(openaiCard).getByText(/^openai: connection works \(412 ms\)\./)).toHaveAttribute('role', 'status');
    const testCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/test'))!;
    expect((testCall[1] as RequestInit).body).toBeNull();

    const models = within(openaiCard).getByRole('list', { name: 'Models reported by openai' });
    expect(within(models).getAllByRole('button')).toHaveLength(3);
    expect(within(models).getByRole('button', { name: 'Use text-embedding-3-large as default embedding model' })).toBeInTheDocument();
    await user.click(within(models).getByRole('button', { name: 'Use gpt-5 as default chat model' }));
    const sheet = screen.getByRole('dialog', { name: 'openai' });
    const chatModel = within(sheet).getByRole('textbox', { name: 'Default chat model' });
    expect(chatModel).toHaveValue('gpt-5');
    expect(chatModel).toHaveFocus();

    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    await waitFor(sheetClosed);
    expect(lastBody(fetchMock, 'PUT')).toEqual({
      endpoint: null,
      defaultChatModel: 'gpt-5',
      defaultEmbeddingModel: null,
      defaultEmbeddingDimensions: null,
    });
    expect(within(openaiCard).queryByText('Connection works')).toBeNull();
  });

  it('shows failed connection tests and requests that could not run', async () => {
    const user = userEvent.setup();
    let calls = 0;
    setup([anthropicWithoutKey], () => ({
      'POST /api/models/providers/anthropic/test': () => {
        calls += 1;
        return calls === 1
          ? jsonResponse({ key: 'anthropic', ok: false, message: 'Not configured: add an API key under Models → Providers.', models: [], durationMs: 3 })
          : problemResponse({ title: 'Not Found', detail: "Provider 'anthropic' is not configured (AI:Providers)." }, 404);
      },
    }));

    const anthropic = await card('anthropic');
    const testButton = within(anthropic).getByRole('button', { name: 'Test connection' });
    await user.click(testButton);
    expect(await within(anthropic).findByText('Connection failed')).toBeInTheDocument();
    expect(within(anthropic).getByText('Not configured: add an API key under Models → Providers.')).toBeInTheDocument();
    expect(within(anthropic).queryByRole('list', { name: /Models reported/ })).toBeNull();

    await user.click(testButton);
    const alert = await within(anthropic).findByRole('alert');
    expect(alert).toHaveTextContent('The connection test could not run');
    expect(alert).toHaveTextContent("Provider 'anthropic' is not configured (AI:Providers).");
    expect(within(anthropic).queryByText('Connection failed')).toBeNull();
  });

  it('resets portal settings to configuration after confirmation', async () => {
    const user = userEvent.setup();
    const { fetchMock, queryClient } = setup([anthropicSaved], (save) => ({
      'DELETE /api/models/providers/anthropic/override': () =>
        save({ ...anthropicSaved, endpoint: null, apiKeySource: 'configuration', apiKeyHint: '…zzzz', hasOverride: false, overrideUpdatedAt: null }),
    }));

    const anthropic = await card('anthropic');
    await user.click(within(anthropic).getByRole('button', { name: 'Reset to configuration' }));
    const dialog = screen.getByRole('dialog', { name: 'Reset anthropic?' });
    expect(dialog).toHaveTextContent('The settings saved in the portal, including the API key …a1b2, are removed');
    await user.click(within(dialog).getByRole('button', { name: 'Reset' }));

    await waitFor(sheetClosed);
    expect(fetchCalls(fetchMock)).toContain('DELETE /api/models/providers/anthropic/override');
    expect(within(anthropic).getByText('From configuration …zzzz')).toBeInTheDocument();
    expect(within(anthropic).queryByRole('button', { name: 'Reset to configuration' })).toBeNull();
    expect(await screen.findByText('Portal settings removed')).toBeInTheDocument();
    await waitFor(() => expect(within(anthropic).getByRole('button', { name: 'Edit' })).toHaveFocus());
    expect(queryClient.getQueryState(modelKeys.profiles)?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(systemInfoQueryKey)?.isInvalidated).toBe(true);
  });
});

describe('provider settings', () => {
  const empty: ProviderFormValues = initialProviderFormValues(openai);

  it('starts empty without portal settings and from the current values with them', () => {
    expect(empty).toEqual({
      apiKey: '',
      clearApiKey: false,
      endpoint: '',
      defaultChatModel: '',
      defaultEmbeddingModel: '',
      defaultEmbeddingDimensions: '',
    });
    expect(initialProviderFormValues(anthropicSaved)).toEqual({
      apiKey: '',
      clearApiKey: false,
      endpoint: 'https://llm-proxy.internal/anthropic',
      defaultChatModel: 'claude-opus-5-5',
      defaultEmbeddingModel: '',
      defaultEmbeddingDimensions: '',
    });
  });

  it('includes the API key only when typed, prefers clearApiKey and nulls fields that do not apply', () => {
    expect(toProviderUpdateRequest(empty, openai).request).toEqual({
      endpoint: null,
      defaultChatModel: null,
      defaultEmbeddingModel: null,
      defaultEmbeddingDimensions: null,
    });
    expect(toProviderUpdateRequest({ ...empty, apiKey: ' sk-1 ', defaultEmbeddingDimensions: '256' }, openai).request).toMatchObject({
      apiKey: 'sk-1',
      defaultEmbeddingDimensions: 256,
    });
    const cleared = toProviderUpdateRequest({ ...empty, apiKey: 'sk-1', clearApiKey: true }, anthropicSaved).request;
    expect(cleared).toMatchObject({ clearApiKey: true });
    expect(cleared).not.toHaveProperty('apiKey');
    expect(toProviderUpdateRequest({ ...empty, defaultEmbeddingModel: 'x', defaultEmbeddingDimensions: '9' }, anthropicSaved).request).toMatchObject({
      defaultEmbeddingModel: null,
      defaultEmbeddingDimensions: null,
    });
    expect(toProviderUpdateRequest({ ...empty, apiKey: 'sk-1', endpoint: 'http://ollama:11434' }, ollama).request).toEqual({
      endpoint: 'http://ollama:11434',
      defaultChatModel: null,
      defaultEmbeddingModel: null,
      defaultEmbeddingDimensions: null,
    });
    expect(toProviderUpdateRequest({ ...empty, defaultChatModel: 'gpt-5', defaultEmbeddingModel: 'voyage-4-lite' }, voyage).request).toEqual({
      endpoint: null,
      defaultChatModel: null,
      defaultEmbeddingModel: 'voyage-4-lite',
      defaultEmbeddingDimensions: null,
    });
  });

  it('validates endpoints and dimensions', () => {
    expect(toProviderUpdateRequest(empty, ollama).errors).toEqual({ endpoint: 'ollama needs an endpoint, e.g. http://localhost:11434.' });
    expect(toProviderUpdateRequest(empty, { ...ollama, endpoint: 'http://ollama:11434' }).errors).toEqual({});
    expect(toProviderUpdateRequest({ ...empty, endpoint: 'ftp://host' }, openai).errors).toEqual({
      endpoint: 'Enter an absolute http(s) URL, e.g. http://localhost:11434.',
    });
    expect(toProviderUpdateRequest({ ...empty, defaultEmbeddingDimensions: '0' }, openai)).toEqual({
      request: null,
      errors: { defaultEmbeddingDimensions: 'Enter a whole number greater than 0.' },
    });
  });

  it('describes where the API key comes from', () => {
    expect(describeApiKey(anthropicSaved)).toBe('Saved in portal …a1b2');
    expect(describeApiKey(openai)).toBe('From configuration …9xQz');
    expect(describeApiKey({ ...openai, apiKeyHint: null })).toBe('From configuration');
    expect(describeApiKey(anthropicWithoutKey)).toBe('No API key');
    expect(describeApiKey(ollama)).toBeNull();
  });

  it('fills the embedding field with embedding models and the chat field otherwise', () => {
    expect(modelFieldFor(openai, 'gpt-5')).toBe('defaultChatModel');
    expect(modelFieldFor(openai, 'text-embedding-3-large')).toBe('defaultEmbeddingModel');
    expect(modelFieldFor(anthropicSaved, 'claude-opus-5-5')).toBe('defaultChatModel');
    expect(modelFieldFor(voyage, 'voyage-4-large')).toBe('defaultEmbeddingModel');
  });
});
