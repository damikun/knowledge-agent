import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { agentDto, agentGuid, agentsRoutes, builtInAgent } from '../../test/agentsApi';
import { stubWideScreen } from '../../test/chat';
import { CONVERSATION_ID, conversationDto, messageDto, sourceDto, turnDto, turnMessages } from '../../test/fixtures';
import { sourcesRoutes } from '../../test/sourcesApi';
import { mockFetch, renderWithProviders, sseResponse } from '../../test/utils';
import { widgetDto, widgetGuid, widgetSettings, widgetsRoutes } from '../../test/widgetsApi';
import { ChatPage } from './ChatPage';

const helper = agentDto();
const shop = widgetDto({
  settings: widgetSettings({
    behavior: { handoverEnabled: true, suggestedQuestions: ['Can I return worn shoes?'] },
    handover: { channel: 'slack' },
  }),
});
const offWidget = widgetDto({ id: widgetGuid(2), name: 'Old site', enabled: false });

const doneEvent = { event: 'done', data: { conversationId: CONVERSATION_ID, turnId: 'next', finishReason: 'stop', agent: 'orchestrator', parentCallId: null } };

function chatBody(fetchMock: ReturnType<typeof mockFetch>): unknown {
  const call = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/api/chat'));
  return JSON.parse((call![1] as RequestInit).body as string);
}

beforeEach(() => {
  stubWideScreen();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Playground', () => {
  it('tests as the first enabled widget by default, says what applies and sends the widget with the question', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      ...agentsRoutes(() => [builtInAgent, helper]),
      ...widgetsRoutes(() => [offWidget, shop]),
      'GET /api/conversations': [],
      'POST /api/chat': () => sseResponse([doneEvent]),
    });
    renderWithProviders(<ChatPage />, { route: '/playground', path: '/playground/:conversationId?' });

    expect(await screen.findByRole('heading', { name: 'Test your agent' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Test as' })).toHaveTextContent('Shop help'));
    await waitFor(() => expect(screen.getByTestId('test-target')).toHaveTextContent('Rules ofShop help'));
    const summary = screen.getByTestId('test-target');
    expect(within(summary).getByRole('link', { name: 'Open the real widget preview' })).toHaveAttribute('href', `/widgets/${shop.id}?preview`);
    expect(summary).toHaveTextContent('AgentWebsite helper');
    expect(summary).toHaveTextContent('KnowledgeAll sources');
    expect(summary).toHaveTextContent('Offers a person when the visitor asks, through Slack');
    expect(screen.getByText('Test')).toHaveAttribute('title', expect.stringContaining('not counted in Analytics'));
    expect(screen.getByRole('button', { name: /Can I return worn shoes/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Source scope/ })).toBeNull();

    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'Hello{Enter}');
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/api/chat'))).toBe(true));
    expect(chatBody(fetchMock)).toEqual({ conversationId: null, message: 'Hello', sourceIds: null, widgetId: shop.id, agent: helper.key });
  });

  it('tests an agent directly when a link asks for it', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      ...agentsRoutes(() => [builtInAgent, helper]),
      ...widgetsRoutes(() => [shop]),
      'GET /api/conversations': [],
      'POST /api/chat': () => sseResponse([doneEvent]),
    });
    renderWithProviders(<ChatPage />, { route: `/playground?agentId=${agentGuid(2)}`, path: '/playground/:conversationId?' });

    const picker = await screen.findByRole('combobox', { name: 'Test as' });
    await waitFor(() => expect(picker).toHaveTextContent('Website helper'));
    expect(screen.getByRole('button', { name: /Source scope/ })).toBeInTheDocument();

    await user.click(picker);
    expect(screen.queryByRole('option', { name: /Knowledge Agent/ })).toBeNull();
    await user.keyboard('{Escape}');
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'Hi{Enter}');
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/api/chat'))).toBe(true));
    expect(chatBody(fetchMock)).toEqual({ conversationId: null, message: 'Hi', sourceIds: null, agent: helper.key });
  });

  it('shows system messages, such as a person being asked to join, where they happened', async () => {
    const first = turnDto({ id: 't1', sequence: 1, assistantMessage: 'Let me get a colleague.' });
    const second = turnDto({ id: 't2', sequence: 2, userMessage: 'Thanks', assistantMessage: 'You are welcome.' });
    mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      'GET /api/conversations': [],
      [`GET /api/conversations/${CONVERSATION_ID}`]: conversationDto({
        turns: [first, second],
        messages: [
          ...turnMessages(first, 1),
          messageDto({ sequence: 3, author: 'system', text: 'In a live chat, this would ask your team to join (Inbox, Slack).', turnId: null }),
          ...turnMessages(second, 4),
        ],
      }),
    });
    renderWithProviders(<ChatPage />, { route: `/chat/${CONVERSATION_ID}`, path: '/chat/:conversationId?' });

    const thread = await screen.findByRole('list', { name: 'Conversation' });
    await waitFor(() => expect(within(thread).getAllByRole('listitem', { name: undefined })).toHaveLength(3));
    const items = within(thread).getAllByRole('listitem');
    expect(items[1]).toHaveTextContent('In a live chat, this would ask your team to join (Inbox, Slack).');
    expect(within(items[1]!).getByRole('note')).toBeInTheDocument();
    expect(items[2]).toHaveTextContent('Thanks');
  });
});
