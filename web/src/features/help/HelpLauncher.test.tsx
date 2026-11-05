import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { supportUser } from '../../test/apiFixtures';
import { jsonResponse, mockFetch, renderWithProviders, requestUrls, sseResponse } from '../../test/utils';
import { HelpLauncher } from './HelpLauncher';
import { HelpSettingsSection } from './HelpSettingsSection';
import type { HelpStatusDto } from './types';

const noModel: HelpStatusDto = {
  enabled: true,
  canManage: true,
  modelReady: false,
  setup: { chatModel: false, embeddingModel: false, sources: true, agent: false, widget: false },
};

const ready: HelpStatusDto = {
  enabled: true,
  canManage: true,
  modelReady: true,
  setup: { chatModel: true, embeddingModel: true, sources: true, agent: true, widget: true },
};

const agent = { agent: 'help', parentCallId: null };
const citation = {
  ref: 'S1',
  sourceId: 'c1',
  chunkId: 'c1',
  sourceTitle: 'Connect a model > Add an API key',
  sourceUrl: '/models#providers',
  page: null,
  snippet: 'Paste the key.',
  score: 1,
};

function answer(text: string) {
  return sseResponse([
    { event: 'conversation', data: { ...agent, conversationId: 's1', title: 'Help', isNew: true } },
    { event: 'tool-call', data: { ...agent, callId: 'call-1', toolName: 'read_help_section', arguments: { id: 'guide/connect-a-model#add-an-api-key' } } },
    { event: 'tool-result', data: { ...agent, callId: 'call-1', toolName: 'read_help_section', isError: false, summary: 'Read "Connect a model > Add an API key"', durationMs: 3, citations: [citation] } },
    { event: 'text-delta', data: { ...agent, text } },
    { event: 'done', data: { ...agent, conversationId: 's1', turnId: 't1', finishReason: 'stop' } },
  ]);
}

function renderLauncher(route = '/sources') {
  return renderWithProviders(
    <>
      <label>
        Name
        <input />
      </label>
      <HelpLauncher />
    </>,
    { route },
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  window.sessionStorage.clear();
});

describe('HelpLauncher', () => {
  it('is hidden while the help is off', async () => {
    const fetchMock = mockFetch({ 'GET /api/help': { enabled: false, canManage: true, modelReady: false, setup: null } });
    renderLauncher();

    await waitFor(() => expect(requestUrls(fetchMock, 'GET /api/help')).toHaveLength(1));
    expect(screen.queryByRole('button', { name: 'Help' })).toBeNull();
  });

  it('opens with "?" outside fields and, without a model, says the help is disabled and links the AI models page', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({ 'GET /api/help': noModel });
    renderLauncher('/widgets');
    await screen.findByRole('button', { name: 'Help' });

    await user.type(screen.getByRole('textbox', { name: 'Name' }), '?');
    expect(screen.queryByRole('dialog', { name: 'Help' })).toBeNull();

    await user.click(document.body);
    await user.keyboard('?');
    const panel = await screen.findByRole('dialog', { name: 'Help' });
    expect(within(panel).getByText('No AI model connected')).toBeInTheDocument();
    expect(within(panel).getByRole('link', { name: 'Connect a model' })).toHaveAttribute('href', '/models');
    expect(within(panel).getByRole('textbox', { name: 'Ask the help' })).toBeDisabled();
    expect(within(panel).getByRole('button', { name: 'Ask' })).toBeDisabled();
    expect(within(panel).queryByText('Suggested for this page')).toBeNull();
    expect(within(panel).queryByText(/Help sections only/)).toBeNull();
    expect(requestUrls(fetchMock, 'POST /api/help/chat')).toEqual([]);
  });

  it('tells support users an admin connects the model, without a link to the admin page', async () => {
    mockFetch({ 'GET /api/auth/me': supportUser, 'GET /api/help': { ...noModel, canManage: false } });
    renderLauncher('/inbox');

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Help' }));
    const panel = await screen.findByRole('dialog', { name: 'Help' });
    expect(await within(panel).findByText('The help answers once an admin connects a chat model.')).toBeInTheDocument();
    expect(within(panel).queryByRole('link', { name: 'Connect a model' })).toBeNull();
    expect(within(panel).getByRole('textbox', { name: 'Ask the help' })).toBeDisabled();
  });

  it('streams an answer whose citations link to portal pages and sends the page and earlier turns', async () => {
    const user = userEvent.setup();
    const bodies: unknown[] = [];
    mockFetch({
      'GET /api/help': ready,
      'POST /api/help/chat': (_url: URL, init: RequestInit) => {
        bodies.push(JSON.parse(init.body as string));
        return answer('Open [AI models](/models#providers) and paste the key [S1].');
      },
    });
    renderLauncher('/sources');

    await user.click(await screen.findByRole('button', { name: 'Help' }));
    const panel = await screen.findByRole('dialog', { name: 'Help' });
    expect(within(panel).queryByText('No AI model connected')).toBeNull();
    await user.type(within(panel).getByRole('textbox', { name: 'Ask the help' }), 'How do I add an API key?{Enter}');

    const article = await within(panel).findByRole('article', { name: 'Help answer' });
    await waitFor(() => expect(article).toHaveTextContent('Open AI models and paste the key'));
    expect(within(article).getByRole('link', { name: 'AI models' })).toHaveAttribute('href', '/models#providers');
    expect(within(article).getByRole('link', { name: 'AI models' })).not.toHaveAttribute('target');
    expect(within(article).getByRole('link', { name: 'Help section 1: Connect a model > Add an API key' })).toHaveAttribute('href', '/models#providers');
    expect(within(article).getByRole('link', { name: 'Open page: 1. Connect a model > Add an API key' })).toHaveAttribute('href', '/models#providers');

    await user.type(within(panel).getByRole('textbox', { name: 'Ask the help' }), 'And then?{Enter}');
    await waitFor(() => expect(bodies).toHaveLength(2));
    expect(bodies[0]).toMatchObject({ message: 'How do I add an API key?', page: '/sources', history: [] });
    expect(bodies[1]).toMatchObject({
      message: 'And then?',
      history: [
        { role: 'user', text: 'How do I add an API key?' },
        { role: 'assistant', text: 'Open [AI models](/models#providers) and paste the key [S1].' },
      ],
    });

    await user.click(within(panel).getByRole('button', { name: 'New chat' }));
    expect(within(panel).queryByRole('article', { name: 'Help answer' })).toBeNull();
  });

  it('turns disabled when the API says no model is connected', async () => {
    const user = userEvent.setup();
    let status = ready;
    mockFetch({
      'GET /api/help': () => jsonResponse(status),
      'POST /api/help/chat': () => {
        status = noModel;
        return sseResponse([
          { event: 'error', data: { ...agent, message: 'No chat model is configured.', code: 'model_not_configured' } },
          { event: 'done', data: { ...agent, conversationId: 's1', turnId: 't1', finishReason: 'error' } },
        ]);
      },
    });
    renderLauncher();

    await user.click(await screen.findByRole('button', { name: 'Help' }));
    const panel = await screen.findByRole('dialog', { name: 'Help' });
    await user.type(within(panel).getByRole('textbox', { name: 'Ask the help' }), 'Snippet?{Enter}');

    expect(await within(panel).findByText('No AI model connected')).toBeInTheDocument();
    expect(within(panel).getByRole('textbox', { name: 'Ask the help' })).toBeDisabled();
    expect(within(panel).queryByText('No chat model is configured.')).toBeNull();
  });
});

describe('HelpSettingsSection', () => {
  it('lets an admin turn the help off for everyone', async () => {
    const user = userEvent.setup();
    const bodies: unknown[] = [];
    mockFetch({
      'GET /api/help': ready,
      'PUT /api/help/settings': (_url: URL, init: RequestInit) => {
        bodies.push(JSON.parse(init.body as string));
        return jsonResponse({ ...ready, enabled: false, setup: null });
      },
    });
    renderWithProviders(<HelpSettingsSection />);

    const toggle = await screen.findByRole('switch', { name: 'In-app help assistant' });
    expect(toggle).toBeChecked();
    await user.click(toggle);

    await waitFor(() => expect(toggle).not.toBeChecked());
    expect(bodies).toEqual([{ enabled: false }]);
  });

  it('shows support users the state without letting them change it', async () => {
    mockFetch({ 'GET /api/auth/me': supportUser, 'GET /api/help': { ...ready, canManage: false } });
    renderWithProviders(<HelpSettingsSection />);

    expect(await screen.findByRole('switch', { name: 'In-app help assistant' })).toBeDisabled();
  });
});
