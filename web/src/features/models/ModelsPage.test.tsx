import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '../../components/ui/Toast';
import { embeddingProfile, orchestratorProfile, providersFixture } from '../../test/apiFixtures';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders } from '../../test/utils';
import { chooseOption } from '../../test/select';
import { ModelsPage } from './ModelsPage';
import { providerSetupHint, toUpdateRequest } from './profileSettings';
import type { ModelProfileDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

const withOverride: ModelProfileDto = orchestratorProfile('openai', {
  override: { provider: 'openai', model: 'gpt-5-mini', temperature: null, maxOutputTokens: 8000, reasoningEffort: 'high', reasoningOutput: 'summary', dimensions: null },
  effective: { providerKey: 'openai', providerType: 'openai', model: 'gpt-5-mini', temperature: null, maxOutputTokens: 8000, reasoningEffort: 'high', reasoningOutput: 'summary', dimensions: null, source: 'Override' },
  overrideUpdatedAt: '2026-10-22T07:00:00Z',
});

const rerankOff: ModelProfileDto = orchestratorProfile('openai', {
  name: 'rerank',
  kind: 'rerank',
  configured: { provider: 'auto', model: null, temperature: null, maxOutputTokens: null, reasoningEffort: null, reasoningOutput: null, dimensions: null },
  effective: null,
  error: 'No rerank provider is configured.',
});

function renderPage(route = '/models') {
  return renderWithProviders(
    <>
      <ModelsPage />
      <Toaster />
    </>,
    { route },
  );
}

describe('ModelsPage', () => {
  it('shows the model of each job in plain words, with its state and technical name', async () => {
    mockFetch({
      'GET /api/models/profiles': [
        withOverride,
        embeddingProfile(),
        rerankOff,
        orchestratorProfile('openai', { name: 'contextualizer', effective: null, error: 'Provider "anthropic" is not configured.' }),
        orchestratorProfile('openai', { name: 'utility' }),
      ],
      'GET /api/models/providers': providersFixture,
    });
    renderPage();

    const answering = await screen.findByRole('region', { name: 'Answering model' });
    expect(within(answering).getByText('Ready')).toBeInTheDocument();
    expect(within(answering).getByText('gpt-5-mini')).toBeInTheDocument();
    expect(answering).toHaveTextContent('orchestrator, changed in the portal');
    expect(within(answering).getByRole('button', { name: 'Change the answering model' })).toBeInTheDocument();

    const embedding = screen.getByRole('region', { name: 'Embedding model' });
    expect(embedding).toHaveTextContent('openai, 1536 dimensions');

    const reranking = screen.getByRole('region', { name: 'Reranking model' });
    expect(within(reranking).getByText('Off')).toBeInTheDocument();
    expect(within(reranking).queryByRole('link', { name: 'Connect' })).toBeNull();

    const contextualizer = screen.getByRole('region', { name: 'Contextualizer model' });
    expect(within(contextualizer).getByText('Not connected')).toBeInTheDocument();
    expect(within(contextualizer).getByText('No provider is ready for this job yet.')).toBeInTheDocument();
    expect(within(contextualizer).getByRole('link', { name: 'Connect' })).toHaveAttribute('href', '/models#providers');
    expect(within(contextualizer).queryByText('Provider "anthropic" is not configured.')).toBeNull();
    await userEvent.click(within(contextualizer).getByRole('button', { name: 'Change the contextualizer model' }));
    expect(within(screen.getByRole('dialog', { name: 'Contextualizer model' })).getByText('Provider "anthropic" is not configured.')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');

    expect(screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)).toEqual(['Models in use', 'Providers & API keys']);
    expect(screen.getByText('Advanced')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Background tasks model' })).toBeInTheDocument();
    expect(within(await screen.findByRole('region', { name: 'anthropic' })).getByText('No API key')).toBeInTheDocument();
  });

  it('scrolls to and focuses Providers & API keys when opened with #providers', async () => {
    const scrollIntoView = vi.spyOn(Element.prototype, 'scrollIntoView');
    mockFetch({
      'GET /api/models/profiles': [orchestratorProfile('openai'), embeddingProfile()],
      'GET /api/models/providers': providersFixture,
    });
    renderPage('/models#providers');

    const section = await screen.findByRole('region', { name: 'Providers & API keys' });
    expect(section).toHaveAttribute('id', 'providers');
    expect(scrollIntoView.mock.contexts).toContain(section);
    expect(section).toHaveFocus();
    expect(await within(section).findByRole('region', { name: 'anthropic' })).toBeInTheDocument();
  });

  it('changes the model of a job in a side sheet and maps server validation errors onto fields', async () => {
    const user = userEvent.setup();
    let calls = 0;
    const fetchMock = mockFetch({
      'GET /api/models/profiles': [orchestratorProfile('openai'), embeddingProfile()],
      'GET /api/models/providers': providersFixture,
      'PUT /api/models/profiles/orchestrator': () => {
        calls += 1;
        return calls === 1
          ? problemResponse({ title: 'One or more validation errors occurred.', errors: { MaxOutputTokens: ['Must be at most 64000.'] } }, 400)
          : jsonResponse(withOverride);
      },
    });
    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Change the answering model' }));
    const sheet = screen.getByRole('dialog', { name: 'Answering model' });
    const form = within(sheet).getByRole('form', { name: 'Change the answering model' });

    const provider = within(form).getByRole('combobox', { name: 'Provider' });
    await user.click(provider);
    const providers = screen.getByRole('listbox', { name: 'Provider' });
    expect(within(providers).getByRole('option', { name: 'anthropic, not configured' })).toBeInTheDocument();
    expect(within(providers).queryByRole('option', { name: /^voyage/ })).toBeNull();
    await user.click(within(providers).getByRole('option', { name: 'anthropic, not configured' }));
    expect(within(form).getByRole('link', { name: 'add an API key' })).toHaveAttribute('href', '/models#providers');
    expect(within(form).getByText(/ANTHROPIC_API_KEY/)).toBeInTheDocument();
    expect(within(form).getByRole('textbox', { name: 'Model' })).toHaveAttribute('placeholder', 'claude-opus-5-5');
    expect(within(form).getByText(/Claude models ignore it/)).toBeInTheDocument();

    const maxTokens = within(form).getByRole('textbox', { name: 'Max output tokens' });
    await user.clear(maxTokens);
    await user.type(maxTokens, '128000');
    await chooseOption(user, within(form).getByRole('combobox', { name: 'Reasoning effort' }), 'Extra high');
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    expect(await within(form).findByText('Must be at most 64000.', { selector: 'p' })).toBeInTheDocument();
    expect(maxTokens).toHaveAttribute('aria-invalid', 'true');

    await user.clear(maxTokens);
    await user.type(maxTokens, '32000');
    await user.click(within(form).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('Answering model saved')).toBeInTheDocument();

    const puts = fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method === 'PUT');
    expect(JSON.parse((puts.at(-1)![1] as RequestInit).body as string)).toEqual({
      provider: 'anthropic',
      model: null,
      maxOutputTokens: 32000,
      temperature: null,
      reasoningEffort: 'extra_high',
      reasoningOutput: 'summary',
    });
  });

  it('shows where each setting comes from and resets a change after confirming it in the sheet', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      'GET /api/models/profiles': [withOverride],
      'GET /api/models/providers': providersFixture,
      'DELETE /api/models/profiles/orchestrator/override': () => new Response(null, { status: 204 }),
    });
    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Change the answering model' }));
    const sheet = screen.getByRole('dialog', { name: 'Answering model' });
    const details = within(sheet).getByRole('table', { name: 'Settings of the answering model' });
    expect(within(details).getByRole('columnheader', { name: 'In use' })).toBeInTheDocument();
    expect(within(details).getByRole('row', { name: /Reasoning effort/ })).toHaveTextContent('Reasoning effortmediumhighhigh');
    expect(within(sheet).getByText(/In use from: Changed in the portal\./)).toBeInTheDocument();

    await user.click(within(sheet).getByRole('button', { name: 'Reset to configuration' }));
    const confirm = within(sheet).getByRole('alert');
    expect(confirm).toHaveTextContent('Undo the change made in the portal?');
    await user.click(within(confirm).getByRole('button', { name: 'Reset' }));

    await waitFor(() => expect(fetchCalls(fetchMock)).toContain('DELETE /api/models/profiles/orchestrator/override'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('Answering model reset')).toBeInTheDocument();
  });
});

describe('profile settings', () => {
  const values = { provider: 'openai', model: ' gpt-5 ', maxOutputTokens: '', reasoningEffort: '', reasoningOutput: 'none', temperature: '0.2', dimensions: '' };

  it('builds chat requests with nulls for defaults', () => {
    expect(toUpdateRequest(values, 'chat')).toEqual({
      request: { provider: 'openai', model: 'gpt-5', maxOutputTokens: null, temperature: 0.2, reasoningEffort: null, reasoningOutput: 'none' },
      errors: {},
    });
  });

  it('validates numbers', () => {
    const result = toUpdateRequest({ ...values, maxOutputTokens: '-5', temperature: '3' }, 'chat');
    expect(result.request).toBeNull();
    expect(result.errors).toEqual({ maxOutputTokens: 'Enter a whole number greater than 0.', temperature: 'Use a value between 0 and 2.' });
    expect(toUpdateRequest({ ...values, dimensions: '1.5' }, 'embedding').errors).toEqual({ dimensions: 'Enter a whole number greater than 0.' });
  });

  it('sends only provider, model and dimensions for embedding profiles', () => {
    expect(toUpdateRequest({ ...values, dimensions: '768' }, 'embedding').request).toEqual({ provider: 'openai', model: 'gpt-5', dimensions: 768 });
  });

  it('names the setting that configures each provider', () => {
    expect(providerSetupHint({ key: 'openai', type: 'openai' })).toBe('OPENAI_API_KEY');
    expect(providerSetupHint({ key: 'my-vllm', type: 'openai' })).toBe('AI__Providers__my-vllm__ApiKey');
    expect(providerSetupHint({ key: 'ollama', type: 'ollama' })).toBe('OLLAMA_ENDPOINT');
    expect(providerSetupHint({ key: 'gpu-box', type: 'ollama' })).toBe('AI__Providers__gpu-box__Endpoint');
  });
});
