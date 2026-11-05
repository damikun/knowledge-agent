import { afterEach, describe, expect, it, vi } from 'vitest';
import { conversationDto, conversationSummaryDto, messageDto } from '../../test/fixtures';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, rejection, requestUrls, sseResponse, streamFromText } from '../../test/utils';
import { chatApi, EMPTY_LIST_FILTERS, isConversationBusy, parseChatEvent, parseConversationEvent, streamChat, streamConversationEvents, toConversationPage } from './api';
import type { ChatEvent, ConversationLiveEvent } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('streamChat', () => {
  it('yields typed events, defaults agent/parentCallId and skips unknown or malformed events', async () => {
    const fetchMock = mockFetch({
      'POST /api/chat': () =>
        new Response(
          streamFromText(
            [
              'event: conversation\ndata: {"conversationId":"c1","title":"New conversation","isNew":true}\n\n',
              'event: heartbeat\ndata: {}\n\n',
              'event: text-delta\ndata: not json\n\n',
              'event: text-delta\ndata: {"text":"Hi","agent":"orchestrator","parentCallId":null}\n\n',
              'event: done\ndata: {"conversationId":"c1","turnId":"t1","finishReason":"stop"}',
            ].join(''),
            11,
          ),
          { headers: { 'Content-Type': 'text/event-stream' } },
        ),
    });
    const events: ChatEvent[] = [];
    for await (const event of streamChat({ conversationId: null, message: 'Hi' })) {
      events.push(event);
    }
    expect(events.map((e) => e.event)).toEqual(['conversation', 'text-delta', 'done']);
    expect(events[0]!.data).toMatchObject({ conversationId: 'c1', agent: 'orchestrator', parentCallId: null });
    expect(fetchCalls(fetchMock)).toEqual(['POST /api/chat']);
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({ conversationId: null, message: 'Hi' });
    expect(init.headers).toMatchObject({ 'Content-Type': 'application/json', 'X-KA-Request': '1' });
  });

  it('throws ApiError when the chat request is rejected', async () => {
    mockFetch({
      'POST /api/chat': () => problemResponse({ title: 'One or more validation errors occurred.', errors: { Message: ['The message must not be empty.'] } }, 400),
    });
    const iterate = async () => {
      for await (const event of streamChat({ conversationId: null, message: '' })) {
        void event;
      }
    };
    const error = await rejection(iterate());
    expect(error.status).toBe(400);
    expect(error.message).toBe('The message must not be empty.');
  });

  it('parses events from a well-formed SSE response helper', async () => {
    mockFetch({ 'POST /api/chat': () => sseResponse([{ event: 'text-delta', data: { text: 'x', agent: 'researcher', parentCallId: 'p1' } }]) });
    const events: ChatEvent[] = [];
    for await (const event of streamChat({ conversationId: 'c', message: 'm' })) {
      events.push(event);
    }
    expect(events).toEqual([{ event: 'text-delta', data: { text: 'x', agent: 'researcher', parentCallId: 'p1' } }]);
  });
});

describe('parseChatEvent', () => {
  it('rejects non-object payloads', () => {
    expect(parseChatEvent('text-delta', '"just a string"')).toBeNull();
    expect(parseChatEvent('text-delta', '{"text":"ok"}')).toEqual({ event: 'text-delta', data: { text: 'ok', agent: 'orchestrator', parentCallId: null } });
  });
});

describe('isConversationBusy', () => {
  it('matches the 409 problem of a conversation whose previous answer is still being written', async () => {
    mockFetch({ 'POST /api/chat': () => problemResponse({ title: 'The previous answer is still being written', code: 'conversation_busy' }, 409) });
    const iterate = async () => {
      for await (const event of streamChat({ conversationId: 'c1', message: 'Hi' })) {
        void event;
      }
    };
    const error = await rejection(iterate());
    expect(isConversationBusy(error)).toBe(true);
    expect(isConversationBusy(new Error('busy'))).toBe(false);
  });
});

describe('conversation events', () => {
  it('parses messages, turn events and status changes, and skips everything else', () => {
    const message = messageDto({ sequence: 3 });
    expect(parseConversationEvent({ event: 'message', data: JSON.stringify(message), id: '3' })).toEqual({ type: 'message', message });
    expect(parseConversationEvent({ event: 'turn', data: '{"type":"text-delta","data":{"text":"Hi","agent":"researcher","parentCallId":"p1"}}' })).toEqual({
      type: 'turn',
      event: { event: 'text-delta', data: { text: 'Hi', agent: 'researcher', parentCallId: 'p1' } },
    });
    expect(parseConversationEvent({ event: 'conversation', data: '{"conversationId":"c1","status":"handover_pending"}' })).toEqual({
      type: 'status',
      conversationId: 'c1',
      status: 'handover_pending',
    });
    expect(parseConversationEvent({ event: 'turn', data: '{"type":"heartbeat","data":{}}' })).toBeNull();
    expect(parseConversationEvent({ event: 'message', data: '{"text":"no sequence"}' })).toBeNull();
    expect(parseConversationEvent({ event: 'conversation', data: '{"status":"sleeping"}' })).toBeNull();
    expect(parseConversationEvent({ event: 'other', data: '{}' })).toBeNull();
    expect(parseConversationEvent({ event: 'message', data: 'not json' })).toBeNull();
  });

  it('asks for the messages after a sequence, or sends it as Last-Event-ID when resuming', async () => {
    const fetchMock = mockFetch({
      'GET /api/conversations/c1/events': () =>
        new Response(streamFromText(`id: 4\nevent: message\ndata: ${JSON.stringify(messageDto({ sequence: 4 }))}\n\n: ping\n\n`), {
          headers: { 'Content-Type': 'text/event-stream' },
        }),
    });
    const opened: string[] = [];
    const events: ConversationLiveEvent[] = [];
    for await (const event of streamConversationEvents('c1', { after: 3, onOpen: () => opened.push('open') })) {
      events.push(event);
    }
    expect(opened).toEqual(['open']);
    expect(events.map((event) => event.type)).toEqual(['message']);
    expect(requestUrls(fetchMock, 'GET /api/conversations/c1/events')[0]!.search).toBe('?after=3');

    for await (const event of streamConversationEvents('c1', { after: 4, resume: true })) {
      void event;
    }
    const [, init] = fetchMock.mock.calls[1] as unknown as [string, RequestInit];
    expect(init.method).toBe('GET');
    expect(init.headers).toMatchObject({ 'Last-Event-ID': '4', Accept: 'text/event-stream' });
    expect(requestUrls(fetchMock, 'GET /api/conversations/c1/events')[1]!.search).toBe('');
  });

  it('fills the timeline fields older API versions do not send', async () => {
    const { messages: _messages, agentId: _agentId, channel: _channel, status: _status, ...older } = conversationDto();
    mockFetch({ 'GET /api/conversations/c1': () => jsonResponse(older) });
    expect(await chatApi.getConversation('c1')).toMatchObject({ messages: [], agentId: null, channel: 'portal', status: 'ai' });
  });
});

describe('chatApi.listConversations', () => {
  it('asks for one page of portal conversations with the search and filters', async () => {
    const fetchMock = mockFetch({
      'GET /api/conversations': {
        items: [conversationSummaryDto({ pinned: true, labels: [{ id: 'l1', name: 'Billing', color: 'purple', source: 'auto' }] })],
        nextCursor: 'next-1',
      },
    });

    const page = await chatApi.listConversations({ q: ' refund ', labelId: 'l1', pinned: true }, 'cursor-0');

    expect(Object.fromEntries(requestUrls(fetchMock, 'GET /api/conversations')[0]!.searchParams)).toEqual({
      channel: 'portal',
      q: 'refund',
      labelId: 'l1',
      pinned: 'true',
      cursor: 'cursor-0',
      limit: '50',
    });
    expect(page.nextCursor).toBe('next-1');
    expect(page.items[0]).toMatchObject({ pinned: true, labels: [{ id: 'l1', name: 'Billing', color: 'purple', source: 'auto' }] });
  });

  it('accepts the plain array of older API versions: filters titles here, sorts pinned first and has no next page', async () => {
    mockFetch({
      'GET /api/conversations': [
        conversationSummaryDto({ id: 'a', title: 'Refund policy', updatedAt: '2026-11-01T10:00:00Z' }),
        conversationSummaryDto({ id: 'b', title: 'Opening hours', updatedAt: '2026-11-03T10:00:00Z' }),
        conversationSummaryDto({ id: 'c', title: 'Refunds for shoes', updatedAt: '2026-11-02T10:00:00Z' }),
      ],
    });

    const all = await chatApi.listConversations(EMPTY_LIST_FILTERS, null);
    expect(all.items.map((item) => item.id)).toEqual(['b', 'c', 'a']);
    expect(all.items[0]).toMatchObject({ pinned: false, labels: [] });
    expect(all.nextCursor).toBeNull();

    const searched = await chatApi.listConversations({ ...EMPTY_LIST_FILTERS, q: 'REFUND' }, null);
    expect(searched.items.map((item) => item.id)).toEqual(['c', 'a']);
  });

  it('maps unknown label colors to gray and drops malformed labels', () => {
    const page = toConversationPage(
      { items: [conversationSummaryDto({ labels: [{ id: 'l1', name: 'X', color: 'orange' as never, source: 'manual' }, { bad: true } as never] })], nextCursor: '' },
      EMPTY_LIST_FILTERS,
    );
    expect(page.items[0]!.labels).toEqual([{ id: 'l1', name: 'X', color: 'gray', source: 'manual' }]);
    expect(page.nextCursor).toBeNull();
  });
});

describe('chatApi conversation changes', () => {
  it('renames and pins with PATCH and sets labels with PUT', async () => {
    const fetchMock = mockFetch({
      'PATCH /api/conversations/c1': () => jsonResponse({}),
      'PUT /api/conversations/c1/labels': () => jsonResponse({}),
    });
    await chatApi.updateConversation('c1', { title: 'New title' });
    await chatApi.updateConversation('c1', { pinned: true });
    await chatApi.setLabels('c1', ['l1', 'l2']);
    const bodies = fetchMock.mock.calls.map(([, init]) => JSON.parse((init as RequestInit).body as string));
    expect(bodies).toEqual([{ title: 'New title' }, { pinned: true }, { labelIds: ['l1', 'l2'] }]);
  });
});

describe('chatApi.stopAnswer', () => {
  it('asks the API to stop the answer being written and says whether it did', async () => {
    const fetchMock = mockFetch({ 'POST /api/conversations/c1/stop': () => jsonResponse({ stopped: true }) });
    await expect(chatApi.stopAnswer('c1')).resolves.toBe(true);
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(init.method).toBe('POST');
    expect(init.headers).toMatchObject({ 'X-KA-Request': '1' });

    mockFetch({ 'POST /api/conversations/c1/stop': () => jsonResponse({ stopped: false }) });
    await expect(chatApi.stopAnswer('c1')).resolves.toBe(false);
  });
});
