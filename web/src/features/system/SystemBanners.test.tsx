import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { embeddingProfile, orchestratorProfile, providersFixture, systemInfoFixture } from '../../test/apiFixtures';
import { mockFetch, renderWithProviders } from '../../test/utils';
import { ModelsPage } from '../models/ModelsPage';
import type { ModelProfileDto } from '../models/types';
import { SystemBanners } from './SystemBanners';
import { SystemStatus } from './SystemStatus';

afterEach(() => {
  vi.unstubAllGlobals();
});

const noChatModel = orchestratorProfile('openai', {
  effective: null,
  error: "No configured chat provider is available for the 'orchestrator' profile (Provider = auto).",
});
const noEmbeddingModel = embeddingProfile({
  effective: null,
  error: "No configured embedding provider is available for the 'embedding' profile (Provider = auto).",
});

function serveProfiles(profiles: ModelProfileDto[]) {
  return mockFetch({
    'GET /api/system/info': systemInfoFixture(),
    'GET /api/models/profiles': profiles,
    'GET /api/models/providers': providersFixture,
  });
}

describe('SystemBanners', () => {
  it.each([
    ['both models', [noChatModel, noEmbeddingModel], 'Connect a chat model and an embedding model to use the agent.'],
    ['the chat model', [noChatModel, embeddingProfile()], 'Connect a chat model to chat with the agent.'],
    ['the embedding model', [orchestratorProfile('anthropic'), noEmbeddingModel], 'Connect an embedding model so sources can be ingested and searched.'],
  ])('asks to connect %s', async (_missing, profiles, message) => {
    serveProfiles(profiles);
    renderWithProviders(<SystemBanners />);

    const banner = await screen.findByRole('status');
    expect(banner).toHaveAttribute('data-tone', 'warning');
    expect(banner).toHaveTextContent(message);
    expect(within(banner).getAllByRole('link')).toHaveLength(1);
    expect(within(banner).getByRole('link', { name: 'Add an API key' })).toHaveAttribute('href', '/models#providers');
    expect(banner).not.toHaveTextContent(/No configured/);
  });

  it('opens Providers & API keys from the setup banner', async () => {
    const user = userEvent.setup();
    serveProfiles([noChatModel, noEmbeddingModel]);
    renderWithProviders(<SystemBanners />, { extraRoutes: <Route path="/models" element={<ModelsPage />} /> });

    await user.click(await screen.findByRole('link', { name: 'Add an API key' }));
    const section = await screen.findByRole('region', { name: 'Providers & API keys' });
    expect(section).toHaveAttribute('id', 'providers');
    await waitFor(() => expect(section).toHaveFocus());
    const anthropic = await within(section).findByRole('region', { name: 'anthropic' });
    expect(within(anthropic).getByRole('button', { name: 'Set API key' })).toBeInTheDocument();
  });

  it('shows nothing when both models resolve', async () => {
    const fetchMock = serveProfiles([orchestratorProfile('openai'), embeddingProfile()]);
    const { container } = renderWithProviders(<SystemBanners />);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(container).toBeEmptyDOMElement();
  });

  it('reports an unreachable API', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))));
    renderWithProviders(<SystemBanners />);
    const banner = await screen.findByRole('status');
    expect(banner).toHaveAttribute('data-tone', 'danger');
    expect(banner).toHaveTextContent('Cannot reach the API. Data shown may be out of date.');
    expect(within(banner).getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});

describe('SystemStatus', () => {
  it('summarizes the effective chat model', async () => {
    mockFetch({
      'GET /api/system/info': systemInfoFixture({ langfuseConfigured: true, langfuseUrl: 'http://localhost:3000' }),
      'GET /api/models/profiles': [orchestratorProfile('openai'), embeddingProfile()],
    });
    renderWithProviders(<SystemStatus />);
    const button = await screen.findByRole('button', { name: 'System status: openai · gpt-5-mini' });
    expect(button.firstElementChild).toHaveAttribute('data-tone', 'success');
  });

  it('says so when no chat model resolves and shows why in the popover', async () => {
    const user = userEvent.setup();
    serveProfiles([noChatModel, noEmbeddingModel]);
    renderWithProviders(<SystemStatus />);

    await user.click(await screen.findByRole('button', { name: 'System status: No chat model' }));
    const popover = await screen.findByRole('dialog', { name: 'System status' });
    expect(within(popover).getByText(noChatModel.error!)).toBeInTheDocument();
    expect(within(popover).getByText(noEmbeddingModel.error!)).toBeInTheDocument();
  });

  it('warns while no embedding model resolves, even with a chat model', async () => {
    serveProfiles([orchestratorProfile('anthropic'), noEmbeddingModel]);
    renderWithProviders(<SystemStatus />);
    const button = await screen.findByRole('button', { name: 'System status: anthropic · claude-opus-5-5' });
    expect(button.firstElementChild).toHaveAttribute('data-tone', 'warning');
  });
});
