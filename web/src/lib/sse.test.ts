import { afterEach, describe, expect, it, onTestFinished, vi } from 'vitest';
import { streamFromText } from '../test/utils';
import { onUnauthorized } from './session';
import { isAbortError, readSseStream, SseParser, sseRequest, type SseMessage } from './sse';

function parseAll(chunks: string[]): SseMessage[] {
  const parser = new SseParser();
  const events: SseMessage[] = [];
  for (const chunk of chunks) {
    events.push(...parser.push(chunk));
  }
  events.push(...parser.flush());
  return events;
}

async function collect(stream: AsyncIterable<SseMessage>): Promise<SseMessage[]> {
  const events: SseMessage[] = [];
  for await (const event of stream) {
    events.push(event);
  }
  return events;
}

const STREAM = [
  ': connected',
  'event: conversation',
  'data: {"conversationId":"c1","title":"New conversation","isNew":true}',
  '',
  'event: text-delta',
  'data: {"text":"Hello"}',
  '',
  'event: done',
  'data: {"conversationId":"c1","turnId":"t1","finishReason":"stop"}',
  '',
  '',
].join('\n');

describe('SseParser', () => {
  it('parses named events with JSON data', () => {
    expect(parseAll([STREAM])).toEqual([
      { event: 'conversation', data: '{"conversationId":"c1","title":"New conversation","isNew":true}' },
      { event: 'text-delta', data: '{"text":"Hello"}' },
      { event: 'done', data: '{"conversationId":"c1","turnId":"t1","finishReason":"stop"}' },
    ]);
  });

  it('produces identical events however the stream is split into chunks', () => {
    const expected = parseAll([STREAM]);
    // One character at a time (splits inside field names, values and line breaks).
    expect(parseAll(STREAM.split(''))).toEqual(expected);
    for (let i = 1; i < STREAM.length; i++) {
      expect(parseAll([STREAM.slice(0, i), STREAM.slice(i)])).toEqual(expected);
    }
  });

  it('joins multi-line data with newlines and strips one leading space', () => {
    const events = parseAll(['event: text-delta\ndata: first line\ndata:  second (two spaces)\ndata:third\n\n']);
    expect(events).toEqual([{ event: 'text-delta', data: 'first line\n second (two spaces)\nthird' }]);
  });

  it('ignores comments and keep-alive lines', () => {
    const events = parseAll([': keep-alive\n\n', ':another comment\nevent: usage\n: inline comment\ndata: {}\n\n', ':\n\n']);
    expect(events).toEqual([{ event: 'usage', data: '{}' }]);
  });

  it('dispatches a final event that has no trailing blank line', () => {
    expect(parseAll(['event: done\ndata: {"turnId":"t9"}'])).toEqual([{ event: 'done', data: '{"turnId":"t9"}' }]);
    expect(parseAll(['event: done\ndata: {"turnId":"t9"}\n'])).toEqual([{ event: 'done', data: '{"turnId":"t9"}' }]);
  });

  it('handles CRLF and CR line endings, including CRLF split across chunks', () => {
    const crlf = 'event: a\r\ndata: 1\r\n\r\nevent: b\rdata: 2\r\r';
    const expected = [
      { event: 'a', data: '1' },
      { event: 'b', data: '2' },
    ];
    expect(parseAll([crlf])).toEqual(expected);
    expect(parseAll(['event: a\r', '\ndata: 1\r', '\n\r', '\nevent: b\rdata: 2\r\r'])).toEqual(expected);
  });

  it('uses "message" when no event name is given and skips events without data', () => {
    expect(parseAll(['data: plain\n\nevent: empty\n\nid: 7\ndata: x\n\n'])).toEqual([
      { event: 'message', data: 'plain' },
      { event: 'message', data: 'x', id: '7' },
    ]);
  });

  it('strips a byte order mark at the start of the stream', () => {
    expect(parseAll(['﻿event: a\ndata: 1\n\n'])).toEqual([{ event: 'a', data: '1' }]);
  });
});

describe('readSseStream', () => {
  it('decodes UTF-8 sequences split across chunks', async () => {
    const text = 'event: text-delta\ndata: {"text":"Žluťoučký kůň — 東京 ✓"}\n\n';
    const events = await collect(readSseStream(streamFromText(text, 3)));
    expect(events).toEqual([{ event: 'text-delta', data: '{"text":"Žluťoučký kůň — 東京 ✓"}' }]);
  });

  it('stops with an AbortError when the signal is aborted mid-stream', async () => {
    const controller = new AbortController();
    const encoder = new TextEncoder();
    const body = new ReadableStream<Uint8Array>({
      start(stream) {
        stream.enqueue(encoder.encode('event: text-delta\ndata: {"text":"a"}\n\n'));
        // Never closes: only the abort can end it.
      },
    });
    const received: SseMessage[] = [];
    const run = (async () => {
      for await (const event of readSseStream(body, controller.signal)) {
        received.push(event);
        controller.abort();
      }
    })();
    await expect(run).rejects.toSatisfy(isAbortError);
    expect(received).toHaveLength(1);
  });

  it('rejects immediately when the signal is already aborted', async () => {
    const controller = new AbortController();
    controller.abort();
    await expect(collect(readSseStream(streamFromText('data: x\n\n'), controller.signal))).rejects.toSatisfy(isAbortError);
  });
});

describe('sseRequest', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('POSTs the body with an event-stream Accept header and the X-KA-Request header and yields events', async () => {
    const fetchMock = vi.fn(
      async () => new Response(streamFromText('event: a\ndata: 1\n\nevent: b\ndata: 2'), { headers: { 'Content-Type': 'text/event-stream' } }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const events = await collect(sseRequest('/api/chat', { body: '{"message":"hi"}', headers: { 'Content-Type': 'application/json' } }));

    expect(events).toEqual([
      { event: 'a', data: '1' },
      { event: 'b', data: '2' },
    ]);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/chat');
    expect(init.method).toBe('POST');
    expect(init.body).toBe('{"message":"hi"}');
    expect(init.headers).toMatchObject({ Accept: 'text/event-stream', 'Content-Type': 'application/json', 'X-KA-Request': '1' });
  });

  it('GETs without a body and says when the stream is open, before its first event', async () => {
    const fetchMock = vi.fn(async () => new Response(streamFromText(': ping\n\nevent: a\ndata: 1\n\n'), { headers: { 'Content-Type': 'text/event-stream' } }));
    vi.stubGlobal('fetch', fetchMock);
    const order: string[] = [];

    for await (const event of sseRequest('/api/conversations/c1/events', { onOpen: (response) => order.push(`open ${response.status}`) })) {
      order.push(event.event);
    }

    expect(order).toEqual(['open 200', 'a']);
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(init.method).toBe('GET');
    expect(init.body).toBeNull();
  });

  it('throws the error built by onErrorResponse for non-2xx responses', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"title":"Nope"}', { status: 503 })));
    const onErrorResponse = vi.fn(async (response: Response) => new Error(`failed ${response.status}`));
    await expect(collect(sseRequest('/api/chat', { body: '{}', onErrorResponse }))).rejects.toThrow('failed 503');
    expect(onErrorResponse).toHaveBeenCalledOnce();
  });

  it('tells the session listeners about a 401 before it throws', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"title":"Unauthorized","status":401}', { status: 401 })));
    const listener = vi.fn();
    onTestFinished(onUnauthorized(listener));

    await expect(collect(sseRequest('/api/chat', { body: '{}' }))).rejects.toThrow('Request failed with status 401');
    expect(listener).toHaveBeenCalledOnce();
  });
});
