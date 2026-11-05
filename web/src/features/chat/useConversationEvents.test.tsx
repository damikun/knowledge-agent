import { renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { messageDto } from '../../test/fixtures';
import { controlledEventStream, mockFetch, problemResponse, requestUrls } from '../../test/utils';
import type { ConversationLiveEvent } from './types';
import { useConversationEvents } from './useConversationEvents';

afterEach(() => {
  vi.unstubAllGlobals();
});

const EVENTS = 'GET /api/conversations/c1/events';
const FAST_RETRY = [5];

function follow(enabled = true) {
  const received: ConversationLiveEvent[] = [];
  let after = 4;
  const onOpen = vi.fn();
  const hook = renderHook(
    ({ on }: { on: boolean }) =>
      useConversationEvents(
        'c1',
        on,
        {
          afterSequence: () => after,
          onOpen,
          onEvent: (event) => {
            received.push(event);
            if (event.type === 'message') {
              after = event.message.sequence;
            }
          },
        },
        { retryDelaysMs: FAST_RETRY },
      ),
    { initialProps: { on: enabled } },
  );
  return { ...hook, received, onOpen };
}

describe('useConversationEvents', () => {
  it('resumes after the given sequence and reconnects with Last-Event-ID after a network error', async () => {
    const events = controlledEventStream();
    const fetchMock = mockFetch({ [EVENTS]: (_url: URL, init: RequestInit) => events.respond(init) });
    const { received, onOpen } = follow();

    await waitFor(() => expect(events.isOpen).toBe(true));
    expect(requestUrls(fetchMock, EVENTS)[0]!.searchParams.get('after')).toBe('4');
    expect(onOpen).toHaveBeenCalledTimes(1);

    events.send('message', messageDto({ sequence: 5 }), 5);
    events.send('turn', { type: 'text-delta', data: { text: 'Hi' } });
    events.send('conversation', { conversationId: 'c1', status: 'human' });
    events.send('surprise', { anything: true });
    await waitFor(() => expect(received).toHaveLength(3));
    expect(received.map((event) => event.type)).toEqual(['message', 'turn', 'status']);
    expect(received[1]).toEqual({ type: 'turn', event: { event: 'text-delta', data: { text: 'Hi', agent: 'orchestrator', parentCallId: null } } });

    events.fail();
    await waitFor(() => expect(events.isOpen).toBe(true));
    expect(events.opened).toBe(2);
    expect(onOpen).toHaveBeenCalledTimes(2);
    const [, resumed] = fetchMock.mock.calls.map(([, init]) => init as RequestInit);
    expect(resumed!.headers).toMatchObject({ 'Last-Event-ID': '5', Accept: 'text/event-stream' });
    expect(requestUrls(fetchMock, EVENTS)[1]!.search).toBe('');
  });

  it('reconnects when the server ends the stream, and closes it when disabled or unmounted', async () => {
    const events = controlledEventStream();
    mockFetch({ [EVENTS]: (_url: URL, init: RequestInit) => events.respond(init) });
    const { rerender, unmount } = follow();

    await waitFor(() => expect(events.isOpen).toBe(true));
    events.close();
    await waitFor(() => expect(events.opened).toBe(2));
    await waitFor(() => expect(events.isOpen).toBe(true));

    rerender({ on: false });
    await waitFor(() => expect(events.isOpen).toBe(false));
    rerender({ on: true });
    await waitFor(() => expect(events.opened).toBe(3));
    await waitFor(() => expect(events.isOpen).toBe(true));

    unmount();
    expect(events.isOpen).toBe(false);
  });

  it('gives up when the conversation does not exist', async () => {
    const fetchMock = mockFetch({ [EVENTS]: () => problemResponse({ title: 'Not Found', detail: "Conversation 'c1' was not found." }, 404) });
    follow();
    await waitFor(() => expect(requestUrls(fetchMock, EVENTS)).toHaveLength(1));
    await new Promise((resolve) => setTimeout(resolve, 30));
    expect(requestUrls(fetchMock, EVENTS)).toHaveLength(1);
  });
});
