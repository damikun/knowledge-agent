import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { citationS1, CONVERSATION_ID, conversationDto, conversationSummaryDto, messageDto, sourceDto, TURN_ID, turnDto } from '../../test/fixtures';
import { expectAnswerDetails } from '../../test/chat';
import { sourcesRoutes } from '../../test/sourcesApi';
import { controlledEventStream, fetchCalls, jsonResponse, mockFetch, renderWithProviders, requestUrls } from '../../test/utils';
import { ChatPage } from './ChatPage';
import type { ConversationDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

const DETAIL = `GET /api/conversations/${CONVERSATION_ID}`;
const EVENTS = `GET /api/conversations/${CONVERSATION_ID}/events`;
const NEXT_TURN_ID = '0199c1e2-0000-7000-8000-00000000a002';
const encoder = new TextEncoder();

function turnEvent(type: string, data: Record<string, unknown>) {
  return { type, data: { agent: 'orchestrator', parentCallId: null, ...data } };
}

function setup(getConversation: () => ConversationDto, extra: Record<string, unknown> = {}) {
  const events = controlledEventStream();
  const fetchMock = mockFetch({
    ...sourcesRoutes(() => [sourceDto()]),
    'GET /api/conversations': [conversationSummaryDto()],
    [DETAIL]: () => jsonResponse(getConversation()),
    [EVENTS]: (_url: URL, init: RequestInit) => events.respond(init),
    ...extra,
  });
  renderWithProviders(<ChatPage />, { route: `/chat/${CONVERSATION_ID}`, path: '/chat/:conversationId?' });
  const loads = () => fetchCalls(fetchMock).filter((call) => call === DETAIL).length;
  return { events, fetchMock, loads };
}

describe('live conversation view', () => {
  it('shows the answer being written after a reload, then the stored turn once its answer is saved', async () => {
    const question = 'How many vacation days do I get?';
    let conversation = conversationDto({ turns: [], messages: [messageDto({ sequence: 1, text: question })] });
    const { events, fetchMock, loads } = setup(() => conversation);

    expect(await screen.findByText(question)).toBeInTheDocument();
    expect(screen.queryByRole('article')).toBeNull();
    await waitFor(() => expect(events.isOpen).toBe(true));
    expect(requestUrls(fetchMock, EVENTS)[0]!.searchParams.get('after')).toBe('1');

    events.send('turn', turnEvent('conversation', { conversationId: CONVERSATION_ID, title: 'Vacation policy', isNew: false }));
    events.send('turn', turnEvent('agent-started', { provider: 'anthropic', model: 'claude-opus-5-5' }));
    events.send('turn', turnEvent('text-delta', { text: 'New employees get ' }));
    events.send('turn', turnEvent('text-delta', { text: '25 days.' }));
    const answer = await screen.findByRole('article', { name: 'Assistant answer' });
    await waitFor(() => expect(answer).toHaveTextContent('New employees get 25 days.'));
    expect(answer).toHaveAttribute('aria-busy', 'true');
    expect(screen.getAllByText(question)).toHaveLength(1);

    const stored = turnDto({ assistantMessage: 'New employees get 25 days [S1].', citations: [citationS1] });
    conversation = conversationDto({ turns: [stored] });
    expect(loads()).toBe(1);
    events.send('message', messageDto({ sequence: 2, author: 'assistant', text: stored.assistantMessage, citations: [citationS1] }), 2);
    await waitFor(() => expect(loads()).toBe(2));
    await expectAnswerDetails(userEvent.setup(), await screen.findByRole('article', { name: 'Assistant answer' }), 'claude-opus-5-5 · anthropic');
    expect(screen.getAllByRole('button', { name: /Source 1: Employee handbook/ }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('article')).toHaveLength(1);

    events.send('turn', turnEvent('done', { conversationId: CONVERSATION_ID, turnId: TURN_ID, finishReason: 'stop' }));
    await waitFor(() => expect(loads()).toBe(3));
    expect(screen.getAllByRole('article')).toHaveLength(1);
  });

  it('stops an answer it only follows, such as one still being written after a reload', async () => {
    const question = 'Summarize every policy';
    const conversation = conversationDto({ turns: [], messages: [messageDto({ sequence: 1, text: question })] });
    const stop = vi.fn(() => jsonResponse({ stopped: true }));
    const { events, fetchMock } = setup(() => conversation, { [`POST /api/conversations/${CONVERSATION_ID}/stop`]: stop });

    await waitFor(() => expect(events.isOpen).toBe(true));
    events.send('turn', turnEvent('agent-started', { provider: 'anthropic', model: 'claude-opus-5-5' }));
    events.send('turn', turnEvent('text-delta', { text: 'First, the leave policy' }));
    await screen.findByRole('article', { name: 'Assistant answer' });

    await userEvent.click(await screen.findByRole('button', { name: 'Stop generating' }));

    await waitFor(() => expect(stop).toHaveBeenCalledTimes(1));
    const [, init] = fetchMock.mock.calls.find(([input]) => String(input).endsWith('/stop')) as unknown as [string, RequestInit];
    expect(init.headers).toMatchObject({ 'X-KA-Request': '1' });
    events.send('turn', turnEvent('done', { conversationId: CONVERSATION_ID, turnId: TURN_ID, finishReason: 'stopped' }));
    expect(await screen.findByText('Stopped')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Stop generating' })).toBeNull());
  });

  it('shows questions and answers of another tab as they arrive, once per message', async () => {
    const { events, fetchMock, loads } = setup(() => conversationDto());
    expect(await screen.findByText(/You get 25 days/)).toBeInTheDocument();
    await waitFor(() => expect(events.isOpen).toBe(true));
    expect(requestUrls(fetchMock, EVENTS)[0]!.searchParams.get('after')).toBe('2');

    events.send('turn', turnEvent('conversation', { conversationId: CONVERSATION_ID, title: 'Vacation policy', isNew: false }));
    events.send('message', messageDto({ sequence: 3, text: 'And for part-time staff?', turnId: NEXT_TURN_ID }), 3);
    expect(await screen.findByText('And for part-time staff?')).toBeInTheDocument();
    await waitFor(() => expect(loads()).toBe(2));

    events.send('message', messageDto({ sequence: 3, text: 'And for part-time staff?', turnId: NEXT_TURN_ID }), 3);
    events.send('turn', turnEvent('agent-started', { provider: 'anthropic', model: 'claude-opus-5-5' }));
    events.send('turn', turnEvent('text-delta', { text: 'Part-time staff get days pro rata.' }));
    expect(await screen.findByText('Part-time staff get days pro rata.')).toBeInTheDocument();
    expect(screen.getAllByRole('article')).toHaveLength(2);
    expect(screen.getAllByText('And for part-time staff?')).toHaveLength(1);
    expect(loads()).toBe(2);
  });

  it('does not follow the conversation while this tab streams its own answer', async () => {
    const user = userEvent.setup();
    let answer: ReadableStreamDefaultController<Uint8Array> | null = null;
    const write = (event: string, data: Record<string, unknown>) =>
      answer!.enqueue(encoder.encode(`event: ${event}\ndata: ${JSON.stringify({ agent: 'orchestrator', parentCallId: null, ...data })}\n\n`));
    const { events, fetchMock } = setup(() => conversationDto(), {
      'POST /api/chat': () =>
        new Response(
          new ReadableStream<Uint8Array>({
            start(controller) {
              answer = controller;
            },
          }),
          { headers: { 'Content-Type': 'text/event-stream' } },
        ),
    });
    await screen.findByText(/You get 25 days/);
    await waitFor(() => expect(events.isOpen).toBe(true));

    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'And for part-time staff?{Enter}');
    await waitFor(() => expect(answer).not.toBeNull());
    await waitFor(() => expect(events.isOpen).toBe(false));

    write('conversation', { conversationId: CONVERSATION_ID, title: 'Vacation policy', isNew: false });
    write('text-delta', { text: 'Part-time staff get days pro rata.' });
    expect(await screen.findByText('Part-time staff get days pro rata.')).toBeInTheDocument();
    expect(events.opened).toBe(1);
    expect(events.isOpen).toBe(false);

    write('done', { conversationId: CONVERSATION_ID, turnId: NEXT_TURN_ID, finishReason: 'stop' });
    answer!.close();
    await waitFor(() => expect(events.opened).toBe(2));
    await waitFor(() => expect(events.isOpen).toBe(true));
    expect(requestUrls(fetchMock, EVENTS)[1]!.searchParams.get('after')).toBe('2');
    expect(screen.getAllByText('Part-time staff get days pro rata.')).toHaveLength(1);
  });

  it('shows what the server keeps when it could not stop the answer, without following it twice', async () => {
    const user = userEvent.setup();
    const question = 'And for part-time staff?';
    let conversation = conversationDto();
    let answer: ReadableStreamDefaultController<Uint8Array> | null = null;
    const { events, loads } = setup(() => conversation, {
      // The answer runs on another API instance, which this stop request does not reach.
      [`POST /api/conversations/${CONVERSATION_ID}/stop`]: () => jsonResponse({ stopped: false }),
      'POST /api/chat': () =>
        new Response(
          new ReadableStream<Uint8Array>({
            start(controller) {
              answer = controller;
              controller.enqueue(encoder.encode('event: text-delta\ndata: {"text":"Part-time staff"}\n\n'));
            },
          }),
          { headers: { 'Content-Type': 'text/event-stream' } },
        ),
    });
    await screen.findByText(/You get 25 days/);
    await user.type(screen.getByRole('textbox', { name: 'Message' }), `${question}{Enter}`);
    expect(await screen.findByText('Part-time staff')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Stop generating' }));
    expect(await screen.findByText('Stopped')).toBeInTheDocument();
    expect(answer).not.toBeNull();

    // The server goes on with the turn; the reopened stream replays its question and the answer so far.
    await waitFor(() => expect(events.opened).toBe(2));
    await waitFor(() => expect(events.isOpen).toBe(true));
    events.send('message', messageDto({ sequence: 3, text: question, turnId: NEXT_TURN_ID }), 3);
    events.send('turn', turnEvent('conversation', { conversationId: CONVERSATION_ID, title: 'Vacation policy', isNew: false }));
    events.send('turn', turnEvent('agent-started', { provider: 'anthropic', model: 'claude-opus-5-5' }));
    events.send('turn', turnEvent('text-delta', { text: 'Part-time staff get days pro rata.' }));
    await waitFor(() => expect(loads()).toBeGreaterThanOrEqual(3));
    expect(screen.getAllByText(question)).toHaveLength(1);
    expect(screen.queryByText('Part-time staff get days pro rata.')).toBeNull();

    const stored = turnDto({ id: NEXT_TURN_ID, sequence: 2, userMessage: question, assistantMessage: 'Part-time staff get days pro rata.', citations: [] });
    conversation = conversationDto({ turns: [conversationDto().turns[0]!, stored] });
    events.send('message', messageDto({ sequence: 4, author: 'assistant', text: stored.assistantMessage, turnId: NEXT_TURN_ID }), 4);
    expect(await screen.findByText('Part-time staff get days pro rata.')).toBeInTheDocument();
    expect(screen.queryByText('Stopped')).toBeNull();
    expect(screen.getAllByText(question)).toHaveLength(1);
    expect(screen.getAllByRole('article')).toHaveLength(2);
  });
});
