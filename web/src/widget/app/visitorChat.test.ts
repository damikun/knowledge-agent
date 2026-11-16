import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse, mockFetch } from '../../test/utils';
import { problem, testVisitor, visitorConversation, visitorMessage, visitorSession, WIDGET_API, WIDGET_KEY, widgetConfig } from '../test/fixtures';
import { listenForHostIdentity } from './host';
import { normalizeConfig } from './types';
import { WidgetRuntime } from './widgetRuntime';

const HASH = 'a'.repeat(64);
const STORAGE_KEY = 'ka-visitor-chat-test';

function normalizedConfig() {
  const config = normalizeConfig(widgetConfig());
  if (!config) {
    throw new Error('The fixture is not a valid config.');
  }
  return config;
}

function fromLoader(data: Record<string, unknown>) {
  window.dispatchEvent(new MessageEvent('message', { data: { source: 'knowledge-agent', ...data }, source: window }));
}

afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

describe('the chat of a visitor', () => {
  it('opens the conversation of the visitor, and a sign-out on the host page leaves it for a chat of the next visitor', async () => {
    let sessions = 0;
    mockFetch({
      [`GET ${WIDGET_API}/config`]: widgetConfig(),
      [`POST ${WIDGET_API}/sessions`]: () =>
        jsonResponse(
          ++sessions === 1
            ? visitorSession({ token: 'token-1', visitorId: 'visitor-1', conversationId: 'c1', name: 'Ada', email: 'ada@example.com' })
            : visitorSession({ token: 'token-2', visitorId: 'visitor-2', conversationId: null }),
        ),
      [`GET ${WIDGET_API}/conversations/c1`]: visitorConversation([visitorMessage(1, 'visitor', 'Hi'), visitorMessage(2, 'assistant', 'Hello')]),
    });
    const identity = listenForHostIdentity(1000);
    const runtime = new WidgetRuntime(WIDGET_KEY, { storageKey: STORAGE_KEY, identity });
    const { visitor, chat } = runtime;

    const started = runtime.start();
    fromLoader({ type: 'identity', user: { userId: '42', userHash: HASH } });
    await started;
    await vi.waitFor(() => expect(chat.getState().phase).toBe('ready'));
    expect(chat.getState()).toMatchObject({ conversationId: 'c1', messages: [{ text: 'Hi' }, { text: 'Hello' }] });
    expect(visitor.getState()).toMatchObject({ visitorId: 'visitor-1', name: 'Ada', email: 'ada@example.com' });

    fromLoader({ type: 'logout' });
    await vi.waitFor(() => expect(visitor.getState().visitorId).toBe('visitor-2'));
    await vi.waitFor(() => expect(chat.getState().phase).toBe('ready'));
    expect(chat.getState()).toMatchObject({ conversationId: null, messages: [] });
    expect(visitor.getState()).toMatchObject({ name: null, email: null });

    runtime.dispose();
  });

  it('fails with the session of the visitor, and starting the chat again starts the session again', async () => {
    let sessions = 0;
    mockFetch({
      [`GET ${WIDGET_API}/config`]: widgetConfig(),
      [`POST ${WIDGET_API}/sessions`]: () => (++sessions === 1 ? problem(503, { title: 'Unavailable' }) : jsonResponse(visitorSession())),
    });
    const runtime = new WidgetRuntime(WIDGET_KEY, { storageKey: STORAGE_KEY });
    const { visitor, chat } = runtime;

    await runtime.start();
    expect(visitor.getState().phase).toBe('failed');
    expect(chat.getState()).toMatchObject({ phase: 'failed', notice: { kind: 'error' } });

    await chat.getState().retry();
    await vi.waitFor(() => expect(chat.getState()).toMatchObject({ phase: 'ready', notice: null }));
    expect(sessions).toBe(2);

    runtime.dispose();
  });
});

describe('the widget runtime', () => {
  it('loads the settings first and starts no session for a widget that does not exist', async () => {
    const fetchMock = mockFetch({
      [`GET ${WIDGET_API}/config`]: () => problem(404, { title: 'Not found' }),
      [`POST ${WIDGET_API}/sessions`]: visitorSession(),
    });
    const runtime = new WidgetRuntime(WIDGET_KEY, { storageKey: STORAGE_KEY });

    await runtime.start();

    expect(runtime.config.getState()).toEqual({ status: 'missing' });
    expect(fetchMock.mock.calls.map(([input]) => String(input))).toEqual([`${WIDGET_API}/config`]);
    expect(runtime.chat.getState().phase).toBe('starting');
    runtime.dispose();
  });

  it('uses the settings it was given without loading them', async () => {
    const fetchMock = mockFetch({ [`POST ${WIDGET_API}/sessions`]: visitorSession() });
    const config = normalizedConfig();
    const runtime = new WidgetRuntime(WIDGET_KEY, { storageKey: STORAGE_KEY, config });

    await runtime.start();

    expect(runtime.config.getState()).toEqual({ status: 'ready', config });
    await vi.waitFor(() => expect(runtime.chat.getState().phase).toBe('ready'));
    expect(fetchMock.mock.calls.map(([input]) => String(input))).not.toContain(`${WIDGET_API}/config`);
    runtime.dispose();
  });
});

describe('a call for the visitor', () => {
  it('renews the token before it expires, so the server still knows the visitor', async () => {
    const bodies: unknown[] = [];
    let sessions = 0;
    mockFetch({
      [`POST ${WIDGET_API}/sessions`]: (_url: URL, init: RequestInit) => {
        bodies.push(JSON.parse(String(init.body)));
        return jsonResponse(visitorSession({ token: `token-${++sessions}`, expiresAt: new Date(Date.now() + 100_000).toISOString() }));
      },
    });
    const { visitor, dispose } = testVisitor({ storageKey: STORAGE_KEY });
    await visitor.getState().start();
    const now = Date.now();
    const clock = vi.spyOn(Date, 'now');

    clock.mockReturnValue(now + 80_000);
    expect(await visitor.getState().token()).toBe('token-1');
    clock.mockReturnValue(now + 95_000);
    expect(await visitor.getState().token()).toBe('token-2');

    expect(bodies).toEqual([{}, { token: 'token-1' }]);
    clock.mockRestore();
    dispose();
  });

  it('is not repeated with the token of the next visitor when the session started over meanwhile', async () => {
    let sessions = 0;
    const tokens: string[] = [];
    mockFetch({
      [`POST ${WIDGET_API}/sessions`]: () => jsonResponse(visitorSession({ token: `token-${++sessions}`, visitorId: `visitor-${sessions}` })),
      [`POST ${WIDGET_API}/conversations/c1/close`]: (_url: URL, init: RequestInit) => {
        tokens.push(new Headers(init.headers).get('Authorization') ?? '');
        void visitor.getState().start();
        return problem(401, { title: 'Expired' });
      },
    });
    const { api, visitor, dispose } = testVisitor({ storageKey: STORAGE_KEY });
    await visitor.getState().start();

    await expect(api.closeConversation('c1')).rejects.toMatchObject({ name: 'AbortError' });
    expect(tokens).toEqual(['Bearer token-1']);
    dispose();
  });
});
