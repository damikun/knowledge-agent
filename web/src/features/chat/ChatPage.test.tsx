import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { agentDto, agentGuid, agentsRoutes, builtInAgent } from '../../test/agentsApi';
import {
  citationS1,
  citationS2,
  citationS3,
  CONVERSATION_ID,
  conversationDto as storedConversation,
  conversationSummaryDto,
  fullTurnEvents,
  sourceDto,
  TURN_ID,
  turnDto,
} from '../../test/fixtures';
import { expectAnswerDetails, pickOption, stubWideScreen } from '../../test/chat';
import { sourcesRoutes } from '../../test/sourcesApi';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders, requestUrls, sseResponse } from '../../test/utils';
import { ChatPage } from './ChatPage';
import type { ConversationDto, ConversationSummaryDto } from './types';

beforeEach(() => {
  stubWideScreen();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const ANSWER = 'New employees get **25 days** [S1]. Managers approve requests [S2], and days carry over until March [S3].';

function conversationDto(overrides: Partial<ConversationDto> = {}): ConversationDto {
  return storedConversation({ turns: [turnDto({ assistantMessage: ANSWER, citations: [citationS1, citationS2, citationS3] })], ...overrides });
}

function chatBody(fetchMock: ReturnType<typeof mockFetch>, index = 0): unknown {
  const calls = fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/api/chat'));
  return JSON.parse((calls[index]![1] as RequestInit).body as string);
}

const doneEvent = { event: 'done', data: { conversationId: CONVERSATION_ID, turnId: 'next', finishReason: 'stop', agent: 'orchestrator', parentCallId: null } };

const encoder = new TextEncoder();

/** An event stream that stays open until the test closes it or the client aborts. */
function openStream(start: (controller: ReadableStreamDefaultController<Uint8Array>) => void): Response {
  return new Response(new ReadableStream<Uint8Array>({ start }), { headers: { 'Content-Type': 'text/event-stream' } });
}

describe('ChatPage', () => {
  it('streams a new conversation: tool cards, nested researcher, answer with chips, then selects the conversation', async () => {
    const user = userEvent.setup();
    let conversations: ConversationSummaryDto[] = [];
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => [sourceDto(), sourceDto({ id: 'src-policy', title: 'Leave policy', type: 'url', url: 'https://intranet.example.com/leave-policy' })]),
      'GET /api/conversations': () => new Response(JSON.stringify(conversations), { headers: { 'Content-Type': 'application/json' } }),
      'POST /api/chat': () => {
        conversations = [conversationSummaryDto()];
        return sseResponse(fullTurnEvents(), 17);
      },
      [`GET /api/conversations/${CONVERSATION_ID}`]: conversationDto(),
    });

    renderWithProviders(<ChatPage />, { route: '/chat', path: '/chat/:conversationId?' });

    expect(await screen.findByRole('heading', { name: 'Test your agent' })).toBeInTheDocument();
    const textbox = screen.getByRole('textbox', { name: 'Message' });
    await user.type(textbox, 'How many vacation days do I get?');
    await user.keyboard('{Enter}');

    expect(await screen.findByText(/Managers approve requests/)).toBeInTheDocument();
    expect(screen.getByText('How many vacation days do I get?')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /Source 1: Employee handbook/ }).length).toBeGreaterThan(0);

    const activity = screen.getByLabelText('Agent activity');
    await user.click(within(activity).getByRole('button', { expanded: false }));
    const steps = within(activity).getAllByTestId('tool-card');
    expect(within(steps[0]!).getByText('Searched the knowledge base')).toBeInTheDocument();
    expect(within(activity).getByText('Researcher')).toBeInTheDocument();
    expect(within(activity).getByText(/Days carry over until March/)).toBeInTheDocument();

    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'Vacation policy' })).toBeInTheDocument());
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain(`GET /api/conversations/${CONVERSATION_ID}`));
    expect(await screen.findByText('Scoped to 1 source')).toBeInTheDocument();
    expect(screen.queryByText(/orchestrator-system v3/)).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Conversation details' }));
    expect(await screen.findByRole('dialog', { name: 'Conversation details' })).toHaveTextContent('orchestrator-system v3 (langfuse)');
    await user.keyboard('{Escape}');
    expect(screen.getAllByRole('button', { name: /thought process/i }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('article')).toHaveLength(1);

    const chatCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/api/chat'));
    expect(JSON.parse((chatCall![1] as RequestInit).body as string)).toEqual({
      conversationId: null,
      message: 'How many vacation days do I get?',
      sourceIds: null,
    });
    await waitFor(() => expect(screen.getByRole('link', { name: /Vacation policy/ })).toHaveAttribute('aria-current', 'page'));
    expect(fetchCalls(fetchMock)).toContain('GET /api/sources/stats');
    expect(fetchCalls(fetchMock)).not.toContain('GET /api/sources');
  });

  it('scopes a new conversation to the sources picked in the composer', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => [
        sourceDto(),
        sourceDto({ id: 'src-policy', title: 'Leave policy', type: 'url', url: 'https://intranet.example.com/leave-policy', createdAt: '2026-10-22T09:00:00Z' }),
        sourceDto({ id: 'src-draft', title: 'Draft memo', status: 'processing' }),
      ]),
      'GET /api/conversations': [],
      'POST /api/chat': () => sseResponse([{ event: 'done', data: { conversationId: CONVERSATION_ID, turnId: 't', finishReason: 'stop', agent: 'orchestrator', parentCallId: null } }]),
      [`GET /api/conversations/${CONVERSATION_ID}`]: { ...conversationDto(), turns: [] },
    });
    renderWithProviders(<ChatPage />, { route: '/chat', path: '/chat/:conversationId?' });

    await user.click(await screen.findByRole('button', { name: 'Source scope: All sources' }));
    const picker = await screen.findByRole('dialog', { name: 'Choose sources' });
    const options = await within(picker).findAllByRole('checkbox');
    expect(options.map((option) => option.closest('label')!.textContent)).toEqual(['Leave policyurl · 42 chunks', 'Employee handbookpdf · 42 chunks']);
    expect(Object.fromEntries(requestUrls(fetchMock, 'GET /api/sources')[0]!.searchParams)).toEqual({ status: 'ready', limit: '50' });

    await user.click(within(picker).getByRole('checkbox', { name: /Leave policy/ }));
    await user.click(within(picker).getByRole('button', { name: 'Done' }));
    expect(screen.getByRole('button', { name: 'Source scope: Leave policy' })).toBeInTheDocument();

    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'Who approves leave?{Enter}');
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain('POST /api/chat'));
    const chatCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/api/chat'));
    expect(JSON.parse((chatCall![1] as RequestInit).body as string)).toEqual({
      conversationId: null,
      message: 'Who approves leave?',
      sourceIds: ['src-policy'],
    });
  });

  it('sends follow-up turns to the open conversation and renders history', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      'GET /api/conversations': [],
      [`GET /api/conversations/${CONVERSATION_ID}`]: conversationDto(),
      'POST /api/chat': () => sseResponse([{ event: 'done', data: { conversationId: CONVERSATION_ID, turnId: 'next', finishReason: 'stop', agent: 'orchestrator', parentCallId: null } }]),
    });
    renderWithProviders(<ChatPage />, { route: `/chat/${CONVERSATION_ID}`, path: '/chat/:conversationId?' });

    expect(await screen.findByText(/Managers approve requests/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Vacation policy' })).toBeInTheDocument();
    expect(screen.getByText('Scoped to 1 source')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /source scope/i })).toBeNull();

    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'And for part-time staff?{Enter}');
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain('POST /api/chat'));
    const chatCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/api/chat'));
    expect(JSON.parse((chatCall![1] as RequestInit).body as string)).toMatchObject({ conversationId: CONVERSATION_ID, message: 'And for part-time staff?' });
  });

  it('stops the answer on the server and keeps reading until the stream says it stopped', async () => {
    const user = userEvent.setup();
    let answer: ReadableStreamDefaultController<Uint8Array> | null = null;
    let chatSignal: AbortSignal | null | undefined;
    let answerStop: (response: Response) => void = () => undefined;
    const write = (event: string, data: Record<string, unknown>) => answer!.enqueue(encoder.encode(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`));
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      'GET /api/conversations': [],
      'POST /api/chat': (_url: URL, init: RequestInit) => {
        chatSignal = init.signal;
        return openStream((controller) => {
          answer = controller;
        });
      },
      [`POST /api/conversations/${CONVERSATION_ID}/stop`]: () =>
        new Promise<Response>((resolve) => {
          answerStop = resolve;
        }),
      [`GET /api/conversations/${CONVERSATION_ID}`]: conversationDto({ turns: [turnDto({ assistantMessage: 'New employees get', citations: [], finishReason: 'stopped' })] }),
    });
    renderWithProviders(<ChatPage />, { route: '/chat', path: '/chat/:conversationId?' });

    await user.type(await screen.findByRole('textbox', { name: 'Message' }), 'How many vacation days do I get?{Enter}');
    await waitFor(() => expect(answer).not.toBeNull());
    write('conversation', { conversationId: CONVERSATION_ID, title: 'New conversation', isNew: true });
    write('text-delta', { text: 'New employees get' });
    expect(await screen.findByText('New employees get')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Stop generating' }));
    expect(await screen.findByRole('button', { name: 'Stopping' })).toBeDisabled();
    const stopCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith(`/api/conversations/${CONVERSATION_ID}/stop`));
    expect(stopCall![1]).toMatchObject({ method: 'POST', headers: { 'X-KA-Request': '1' } });

    answerStop(jsonResponse({ stopped: true }));
    write('done', { conversationId: CONVERSATION_ID, turnId: TURN_ID, finishReason: 'stopped' });
    answer!.close();

    const turn = await screen.findByRole('article', { name: 'Assistant answer' });
    await waitFor(() => expect(within(turn).getByText('Stopped')).toBeInTheDocument());
    expect(turn).toHaveTextContent('New employees get');
    expect(chatSignal?.aborted).toBe(false);
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.queryByText(/Try sending the message again/)).toBeNull();
    expect(await screen.findByRole('button', { name: 'Send message' })).toBeInTheDocument();
  });

  it('stops reading when Stop comes before the conversation is known', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': [],
      'POST /api/chat': () =>
        openStream((controller) => controller.enqueue(encoder.encode('event: text-delta\ndata: {"text":"Partial answer"}\n\n'))),
    });
    renderWithProviders(<ChatPage />, { route: '/chat', path: '/chat/:conversationId?' });

    await user.type(await screen.findByRole('textbox', { name: 'Message' }), 'Tell me everything{Enter}');
    expect(await screen.findByText('Partial answer')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Stop generating' }));

    const turn = screen.getByRole('article', { name: 'Assistant answer' });
    expect(await within(turn).findByText('Stopped')).toBeInTheDocument();
    expect(turn).toHaveTextContent('Partial answer');
    expect(screen.getByRole('button', { name: 'Send message' })).toBeInTheDocument();
    expect(fetchCalls(fetchMock).filter((call) => call.endsWith('/stop'))).toEqual([]);
  });

  it.each([
    ['has no answer to stop', () => jsonResponse({ stopped: false })],
    ['fails', () => problemResponse({ title: 'Internal Server Error' }, 500)],
  ])('stops reading when the stop request %s', async (_case, stopResponse) => {
    const user = userEvent.setup();
    let chatSignal: AbortSignal | null | undefined;
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      'GET /api/conversations': [],
      [`GET /api/conversations/${CONVERSATION_ID}`]: conversationDto(),
      'POST /api/chat': (_url: URL, init: RequestInit) => {
        chatSignal = init.signal;
        return openStream((controller) => controller.enqueue(encoder.encode('event: text-delta\ndata: {"text":"Part-time staff"}\n\n')));
      },
      [`POST /api/conversations/${CONVERSATION_ID}/stop`]: stopResponse,
    });
    renderWithProviders(<ChatPage />, { route: `/chat/${CONVERSATION_ID}`, path: '/chat/:conversationId?' });
    await screen.findByText(/Managers approve requests/);

    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'And for part-time staff?{Enter}');
    expect(await screen.findByText('Part-time staff')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Stop generating' }));

    await waitFor(() => expect(chatSignal?.aborted).toBe(true));
    expect(fetchCalls(fetchMock)).toContain(`POST /api/conversations/${CONVERSATION_ID}/stop`);
    expect(await screen.findByText('Stopped')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send message' })).toBeInTheDocument();
  });

  it('shows a stored stopped answer as stopped after a reload', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      'GET /api/conversations': [],
      [`GET /api/conversations/${CONVERSATION_ID}`]: conversationDto({
        turns: [turnDto({ assistantMessage: 'New employees get **25 days** [S1]', citations: [citationS1], finishReason: 'stopped' })],
      }),
    });
    renderWithProviders(<ChatPage />, { route: `/chat/${CONVERSATION_ID}`, path: '/chat/:conversationId?' });

    const turn = await screen.findByRole('article', { name: 'Assistant answer' });
    expect(turn).toHaveTextContent('New employees get 25 days');
    expect(within(turn).getByText('Stopped')).toBeInTheDocument();
    expect(within(turn).queryByRole('alert')).toBeNull();
    await expectAnswerDetails(user, turn, 'claude-opus-5-5 · anthropic');
  });

  it('shows API errors inline when the chat request is rejected', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': [],
      'POST /api/chat': () =>
        new Response(JSON.stringify({ title: 'Service Unavailable', status: 503, detail: 'No chat provider is configured.' }), {
          status: 503,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    });
    renderWithProviders(<ChatPage />, { route: '/chat', path: '/chat/:conversationId?' });
    await user.type(await screen.findByRole('textbox', { name: 'Message' }), 'Hello{Enter}');
    expect(await screen.findByRole('alert')).toHaveTextContent('No chat provider is configured.');
  });

  it('shows a hint when there are no sources and no made-up example questions', async () => {
    const fetchMock = mockFetch({ ...sourcesRoutes(() => []), 'GET /api/conversations': [] });
    renderWithProviders(<ChatPage />, { route: '/chat', path: '/chat/:conversationId?' });
    expect(await screen.findByText('No sources yet')).toBeInTheDocument();
    expect(screen.queryByText('Suggested questions of this widget')).toBeNull();
    expect(fetchCalls(fetchMock)).toContain('GET /api/sources/stats');
    expect(fetchCalls(fetchMock)).not.toContain('GET /api/sources');
  });

  it('tells sources that are still being ingested apart from no sources', async () => {
    mockFetch({
      ...sourcesRoutes(() => [sourceDto({ status: 'processing' }), sourceDto({ id: 'queued', status: 'pending' })]),
      'GET /api/conversations': [],
    });
    renderWithProviders(<ChatPage />, { route: '/chat', path: '/chat/:conversationId?' });
    expect(await screen.findByText('Your sources are being processed')).toBeInTheDocument();
    expect(screen.getByText(/2 sources are being ingested/)).toBeInTheDocument();
    expect(screen.queryByText('No sources yet')).toBeNull();
  });

  it('starts a new conversation with the chosen agent and sends no agent with follow-ups', async () => {
    const user = userEvent.setup();
    const helper = agentDto();
    const support = agentDto({ id: agentGuid(3), key: 'support', name: 'Support desk' });
    let posts = 0;
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      ...agentsRoutes(() => [builtInAgent, helper, support]),
      'GET /api/conversations': [],
      [`GET /api/conversations/${CONVERSATION_ID}`]: conversationDto({ agentId: support.id }),
      'POST /api/chat': () => {
        posts += 1;
        return posts === 1
          ? sseResponse([{ event: 'conversation', data: { conversationId: CONVERSATION_ID, title: 'New conversation', isNew: true } }, doneEvent])
          : sseResponse([doneEvent]);
      },
    });
    renderWithProviders(<ChatPage />, { route: '/chat', path: '/chat/:conversationId?' });

    const picker = await screen.findByRole('combobox', { name: 'Test as' });
    expect(picker).toHaveTextContent('Website helper');
    await user.click(picker);
    expect(screen.queryByRole('option', { name: /Knowledge Agent/ })).toBeNull();
    await user.keyboard('{Escape}');
    await pickOption(user, picker, /Support desk/);
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'Where is my order?{Enter}');
    await waitFor(() => expect(posts).toBe(1));
    expect(chatBody(fetchMock)).toEqual({ conversationId: null, message: 'Where is my order?', sourceIds: null, agent: 'support' });

    expect(await screen.findByTitle('Agent')).toHaveTextContent('Support desk');
    expect(screen.queryByRole('combobox', { name: 'Test as' })).toBeNull();

    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'And the delivery date?{Enter}');
    await waitFor(() => expect(posts).toBe(2));
    expect(chatBody(fetchMock, 1)).toEqual({ conversationId: CONVERSATION_ID, message: 'And the delivery date?', sourceIds: null });
  });

  it('names the built-in agent for its old conversations, and leads to New agent while there is no agent of your own', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      ...agentsRoutes(() => [builtInAgent]),
      'GET /api/conversations': [],
      [`GET /api/conversations/${CONVERSATION_ID}`]: conversationDto({ agentId: null }),
    });
    renderWithProviders(<ChatPage />, { route: `/chat/${CONVERSATION_ID}`, path: '/chat/:conversationId?' });
    expect(await screen.findByTitle('Agent')).toHaveTextContent('Knowledge Agent');

    await user.click(screen.getByRole('button', { name: 'New chat' }));
    expect(await screen.findByText('No agent to test yet')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'New agent' })).toHaveAttribute('href', '/agents/new');
    expect(screen.getByRole('textbox', { name: 'Message' })).toBeDisabled();
    expect(screen.queryByRole('combobox', { name: 'Test as' })).toBeNull();
    expect(screen.queryByTitle('Agent')).toBeNull();
  });

  it('keeps the message in the composer when the previous answer is still being written', async () => {
    const user = userEvent.setup();
    let posts = 0;
    mockFetch({
      ...sourcesRoutes(() => [sourceDto()]),
      'GET /api/conversations': [],
      [`GET /api/conversations/${CONVERSATION_ID}`]: conversationDto(),
      'POST /api/chat': () => {
        posts += 1;
        return posts === 1
          ? problemResponse(
              {
                title: 'The previous answer is still being written',
                detail: `The previous answer in conversation ${CONVERSATION_ID} is still being written.`,
                code: 'conversation_busy',
              },
              409,
            )
          : sseResponse([doneEvent]);
      },
    });
    renderWithProviders(<ChatPage />, { route: `/chat/${CONVERSATION_ID}`, path: '/chat/:conversationId?' });
    await screen.findByText(/Managers approve requests/);

    const textbox = screen.getByRole('textbox', { name: 'Message' });
    await user.type(textbox, 'And for part-time staff?{Enter}');
    expect(await screen.findByRole('alert')).toHaveTextContent('The previous answer is still being written. Try again in a moment.');
    expect(textbox).toHaveValue('And for part-time staff?');
    expect(screen.queryByText('And for part-time staff?', { ignore: 'textarea' })).toBeNull();
    expect(screen.getAllByRole('article')).toHaveLength(1);

    await user.click(screen.getByRole('button', { name: 'Send message' }));
    await waitFor(() => expect(posts).toBe(2));
    expect(screen.queryByText('The previous answer is still being written. Try again in a moment.')).toBeNull();
    expect(textbox).toHaveValue('');
  });
});
