import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse, mockFetch } from '../../test/utils';
import { visitorSession, WIDGET_API, WIDGET_KEY } from '../test/fixtures';
import { WidgetApi } from './api';
import { ChatController } from './controller';
import { listenForHostIdentity } from './host';

const HASH = 'a'.repeat(64);

// In jsdom the frame is its own parent, so the loader's messages come from window.
function fromLoader(data: Record<string, unknown>) {
  window.dispatchEvent(new MessageEvent('message', { data: { source: 'knowledge-agent', ...data }, source: window }));
}

function sessionBodies(fetchMock: ReturnType<typeof mockFetch>): unknown[] {
  return fetchMock.mock.calls
    .filter(([input, init]) => String(input).endsWith(`${WIDGET_API}/sessions`) && (init as RequestInit | undefined)?.method === 'POST')
    .map(([, init]) => JSON.parse(String((init as RequestInit).body)) as unknown);
}

afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

describe('the signed-in user of the host page', () => {
  it('goes with the first session, and a new user or a sign-out starts another one', async () => {
    let sessions = 0;
    const fetchMock = mockFetch({
      [`POST ${WIDGET_API}/sessions`]: () => jsonResponse(visitorSession({ token: `token-${++sessions}`, visitorId: `visitor-${sessions}`, conversationId: null })),
    });
    const identity = listenForHostIdentity(1000);
    const controller = new ChatController(new WidgetApi(WIDGET_KEY), { storageKey: 'ka-identity-test', identity });

    const started = controller.start();
    fromLoader({ type: 'identity', user: { userId: '42', userHash: HASH, name: 'Ada' } });
    await started;
    expect(sessionBodies(fetchMock)).toEqual([{ user: { userId: '42', userHash: HASH, name: 'Ada' } }]);

    fromLoader({ type: 'identity', user: { userId: '42', userHash: HASH } });
    fromLoader({ type: 'identity', user: { userId: '7', userHash: HASH } });
    await vi.waitFor(() => expect(sessionBodies(fetchMock)).toHaveLength(2));
    expect(sessionBodies(fetchMock)[1]).toEqual({ token: 'token-1', user: { userId: '7', userHash: HASH } });

    fromLoader({ type: 'logout' });
    await vi.waitFor(() => expect(sessionBodies(fetchMock)).toHaveLength(3));
    expect(sessionBodies(fetchMock)[2]).toEqual({});
    controller.dispose();
  });

  it('starts anonymous when the loader says nobody is signed in', async () => {
    const fetchMock = mockFetch({ [`POST ${WIDGET_API}/sessions`]: visitorSession({ conversationId: null }) });
    const identity = listenForHostIdentity(1000);
    const controller = new ChatController(new WidgetApi(WIDGET_KEY), { storageKey: 'ka-identity-test', identity });

    const started = controller.start();
    fromLoader({ type: 'identity', user: null });
    await started;

    expect(sessionBodies(fetchMock)).toEqual([{}]);
    controller.dispose();
  });

  it('sends what update() said and the page with the session, changes while open at once, and warns about refused keys', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const fetchMock = mockFetch({
      [`POST ${WIDGET_API}/sessions`]: visitorSession({ conversationId: null, rejectedAttributes: [{ key: 'Bad', reason: 'Keys are lowercase.' }] }),
      [`POST ${WIDGET_API}/attributes`]: jsonResponse({ applied: ['plan'], rejected: [] }),
    });
    const identity = listenForHostIdentity(1000);
    const controller = new ChatController(new WidgetApi(WIDGET_KEY), { storageKey: 'ka-identity-test', identity });

    fromLoader({ type: 'attributes', attributes: { early: 1 } });
    const started = controller.start();
    fromLoader({ type: 'identity', user: null, attributes: { plan: 'free', Bad: 1 }, page: { url: 'https://shop.example.com/', referrer: null, language: 'sk' } });
    await started;

    expect(sessionBodies(fetchMock)).toEqual([
      { attributes: { plan: 'free', Bad: 1 }, page: { url: 'https://shop.example.com/', referrer: null, language: 'sk' } },
    ]);
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('"Bad" was not saved. Keys are lowercase.'));

    fromLoader({ type: 'attributes', attributes: { plan: 'pro', coupon: null } });
    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.some(([input, init]) => String(input).endsWith(`${WIDGET_API}/attributes`) && String((init as RequestInit).body) === JSON.stringify({ attributes: { plan: 'pro', coupon: null } }))).toBe(true),
    );
    expect(identity.attributes()).toEqual({ plan: 'pro', Bad: 1, coupon: null });
    controller.dispose();
  });
});
