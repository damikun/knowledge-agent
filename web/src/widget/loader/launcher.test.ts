import { waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { mockFetch } from '../../test/utils';
import { problem, WIDGET_API, WIDGET_KEY, widgetConfig } from '../test/fixtures';
import { createLauncherView, findLoaderScript, startLoader, type LauncherConfig } from './launcher';

const PORTAL = 'https://portal.example.com';

function addScript(): HTMLScriptElement {
  const script = document.createElement('script');
  script.src = `${PORTAL}/widget/v1/loader.js`;
  script.dataset.widget = WIDGET_KEY;
  document.head.append(script);
  return script;
}

async function startWithConfig(config: Record<string, unknown> = {}) {
  const fetchMock = mockFetch({ [`GET ${WIDGET_API}/config`]: widgetConfig(config) });
  const handle = startLoader(addScript());
  expect(handle).not.toBeNull();
  expect(await handle!.ready).toBe(true);
  const host = document.querySelector('knowledge-agent-widget') as HTMLElement;
  const shadow = host.shadowRoot!;
  return {
    fetchMock,
    host,
    shadow,
    launcher: shadow.querySelector<HTMLButtonElement>('button.ka-launcher')!,
    root: shadow.querySelector<HTMLElement>('.ka-root')!,
    frame: () => shadow.querySelector('iframe'),
  };
}

function fromFrame(source: MessageEventSource | null, data: unknown, origin = PORTAL) {
  window.dispatchEvent(new MessageEvent('message', { data, origin, source }));
}

afterEach(() => {
  window.__knowledgeAgentLoader?.destroy();
  document.head.innerHTML = '';
  document.body.innerHTML = '';
});

describe('widget loader', () => {
  it('finds its own script element and its widget key', () => {
    const script = addScript();
    expect(findLoaderScript()).toBe(script);
  });

  it('shows the launcher from the widget config in a shadow root', async () => {
    const { fetchMock, host, shadow, launcher, root, frame } = await startWithConfig({ launcherLabel: 'Ask us', position: 'left', brandColor: '#7c3aed' });

    expect(fetchMock.mock.calls[0]![0]).toBe(`${PORTAL}${WIDGET_API}/config`);
    expect(fetchMock.mock.calls[0]![1]).toMatchObject({ credentials: 'omit' });
    expect(launcher).toHaveAttribute('aria-label', 'Ask us');
    expect(launcher).toHaveAttribute('aria-expanded', 'false');
    expect(launcher).toHaveTextContent('Ask us');
    expect(launcher.querySelector('svg')).not.toBeNull();
    expect(root).toHaveClass('ka-left', 'ka-light', 'ka-glass', 'ka-glow');
    expect(host.style.getPropertyValue('--ka-launcher')).toBe('#7c3aed');
    expect(host.style.getPropertyValue('--ka-launcher-icon')).toBe('#ffffff');
    expect(host.style.getPropertyValue('--ka-panel-fill')).toBe('rgb(255 255 255 / 0.84)');
    expect(shadow.querySelector('style')?.textContent).toContain('.ka-launcher');
    expect(frame()).toBeNull();
  });

  it('uses an uploaded launcher image from the portal', async () => {
    const { launcher } = await startWithConfig({ launcherIconUrl: '/widget/v1/media/123' });

    expect(launcher.querySelector('img')).toHaveAttribute('src', `${PORTAL}/widget/v1/media/123`);
    expect(launcher).toHaveAttribute('aria-label', 'Chat with us');
  });

  it('opens the chat frame on click, tells it when it opens and closes, and closes when the frame asks', async () => {
    const { launcher, root, frame } = await startWithConfig();

    launcher.click();
    const iframe = frame()!;
    expect(iframe).toHaveAttribute('src', `${PORTAL}/widget/v1/frame/${WIDGET_KEY}`);
    expect(iframe).toHaveAttribute('title', 'Shop help');
    // The same color scheme as the frame's, so the browser keeps the frame transparent over the glass.
    expect(iframe.style.colorScheme).toBe('light');
    expect(launcher).toHaveAttribute('aria-expanded', 'true');
    expect(root).toHaveClass('ka-is-open');
    await waitFor(() => expect(iframe.getRootNode()).toHaveProperty('activeElement', iframe));

    const toFrame = vi.spyOn(iframe.contentWindow!, 'postMessage').mockImplementation(() => undefined);
    fromFrame(iframe.contentWindow, { source: 'knowledge-agent', type: 'ready' });
    expect(toFrame).toHaveBeenLastCalledWith({ source: 'knowledge-agent', type: 'open' }, PORTAL);

    fromFrame(iframe.contentWindow, { source: 'knowledge-agent', type: 'busy', busy: true });
    expect(root).toHaveClass('ka-busy');
    fromFrame(iframe.contentWindow, { source: 'knowledge-agent', type: 'busy', busy: false });
    expect(root).not.toHaveClass('ka-busy');

    fromFrame(iframe.contentWindow, { source: 'knowledge-agent', type: 'close' });
    expect(launcher).toHaveAttribute('aria-expanded', 'false');
    expect(root).not.toHaveClass('ka-is-open');
    expect(toFrame).toHaveBeenLastCalledWith({ source: 'knowledge-agent', type: 'close' }, PORTAL);
    expect(document.activeElement).toBe(document.querySelector('knowledge-agent-widget'));
    expect(launcher.getRootNode()).toHaveProperty('activeElement', launcher);

    launcher.click();
    expect(launcher).toHaveAttribute('aria-expanded', 'true');
    expect(frame()).toBe(iframe);
  });

  it('passes the signed-in user from the script tag and identify() to the frame, and logout() forgets them', async () => {
    const hash = 'a'.repeat(64);
    const script = addScript();
    script.dataset.userId = '42';
    script.dataset.userHash = hash;
    script.dataset.userName = 'Ada';
    mockFetch({ [`GET ${WIDGET_API}/config`]: widgetConfig() });
    const handle = startLoader(script);
    expect(await handle!.ready).toBe(true);
    const shadow = document.querySelector('knowledge-agent-widget')!.shadowRoot!;
    shadow.querySelector<HTMLButtonElement>('button.ka-launcher')!.click();
    const iframe = shadow.querySelector('iframe')!;
    const toFrame = vi.spyOn(iframe.contentWindow!, 'postMessage').mockImplementation(() => undefined);

    fromFrame(iframe.contentWindow, { source: 'knowledge-agent', type: 'ready' });
    expect(toFrame).toHaveBeenCalledWith(
      { source: 'knowledge-agent', type: 'identity', user: { userId: '42', userHash: hash, name: 'Ada' }, attributes: {}, page: expect.objectContaining({ url: window.location.href }) },
      PORTAL,
    );

    window.KnowledgeAgent!.identify({ userId: '7', userHash: 'B'.repeat(64), email: 'grace@example.com', attributes: { plan: 'pro', seats: 3, coupon: null } });
    expect(toFrame).toHaveBeenLastCalledWith(
      expect.objectContaining({
        type: 'identity',
        user: { userId: '7', userHash: 'b'.repeat(64), email: 'grace@example.com', attributes: { plan: 'pro', seats: 3, coupon: null } },
      }),
      PORTAL,
    );

    window.KnowledgeAgent!.logout();
    expect(toFrame).toHaveBeenLastCalledWith({ source: 'knowledge-agent', type: 'logout' }, PORTAL);
  });

  it('keeps what update() says, also before the chat opens, sends changes at once and forgets them on logout()', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const script = addScript();
    mockFetch({ [`GET ${WIDGET_API}/config`]: widgetConfig() });
    const handle = startLoader(script);
    window.KnowledgeAgent!.update({ attributes: { plan: 'free', renews: new Date('2026-12-01T00:00:00Z') as unknown as string, nested: { a: 1 } as unknown as string } });
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('"nested" was left out'));
    expect(await handle!.ready).toBe(true);
    const shadow = document.querySelector('knowledge-agent-widget')!.shadowRoot!;
    shadow.querySelector<HTMLButtonElement>('button.ka-launcher')!.click();
    const iframe = shadow.querySelector('iframe')!;
    const toFrame = vi.spyOn(iframe.contentWindow!, 'postMessage').mockImplementation(() => undefined);

    fromFrame(iframe.contentWindow, { source: 'knowledge-agent', type: 'ready' });
    expect(toFrame).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'identity', user: null, attributes: { plan: 'free', renews: '2026-12-01T00:00:00.000Z' } }),
      PORTAL,
    );

    window.KnowledgeAgent!.update({ attributes: { plan: 'pro', coupon: null } });
    expect(toFrame).toHaveBeenLastCalledWith({ source: 'knowledge-agent', type: 'attributes', attributes: { plan: 'pro', coupon: null } }, PORTAL);
    window.KnowledgeAgent!.update('nonsense' as unknown as { attributes: Record<string, string> });
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('update() takes'));

    window.KnowledgeAgent!.logout();
    toFrame.mockClear();
    fromFrame(iframe.contentWindow, { source: 'knowledge-agent', type: 'ready' });
    expect(toFrame).toHaveBeenCalledWith(expect.objectContaining({ type: 'identity', user: null, attributes: {} }), PORTAL);
  });

  it('ignores identify() without a valid user hash', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    await startWithConfig();

    window.KnowledgeAgent!.identify({ userId: '42', userHash: 'not-a-hash' });

    expect(warn).toHaveBeenCalledWith(expect.stringContaining('identify() needs a userId'));
  });

  it('shows the launcher of a widget for signed-in users only after identify(), and hides it on logout()', async () => {
    mockFetch({ [`GET ${WIDGET_API}/config`]: widgetConfig({ requireIdentity: true }) });
    const handle = startLoader(addScript());
    expect(await handle!.ready).toBe(true);
    expect(document.querySelector('knowledge-agent-widget')).toBeNull();

    window.KnowledgeAgent!.identify({ userId: '42', userHash: 'c'.repeat(64) });
    expect(document.querySelector('knowledge-agent-widget')).not.toBeNull();

    window.KnowledgeAgent!.logout();
    expect(document.querySelector('knowledge-agent-widget')).toBeNull();
  });

  it('ignores messages from other origins, other windows and other sources', async () => {
    const { launcher, frame } = await startWithConfig();
    launcher.click();
    const iframe = frame()!;

    fromFrame(iframe.contentWindow, { source: 'knowledge-agent', type: 'close' }, 'https://evil.example');
    fromFrame(window, { source: 'knowledge-agent', type: 'close' });
    fromFrame(iframe.contentWindow, { source: 'something-else', type: 'close' });
    fromFrame(iframe.contentWindow, 'close');

    expect(launcher).toHaveAttribute('aria-expanded', 'true');
  });

  it('shows the unread count the frame reports while the panel is closed', async () => {
    const { launcher, shadow, frame } = await startWithConfig();
    launcher.click();
    const iframe = frame()!;
    launcher.click();
    const badge = shadow.querySelector<HTMLElement>('.ka-badge')!;
    expect(badge.hidden).toBe(true);

    fromFrame(iframe.contentWindow, { source: 'knowledge-agent', type: 'unread', count: 2 });
    expect(badge.hidden).toBe(false);
    expect(badge).toHaveTextContent('2');
    expect(launcher).toHaveAttribute('aria-label', 'Chat with us (2)');

    launcher.click();
    expect(badge.hidden).toBe(true);
    expect(launcher).toHaveAttribute('aria-label', 'Chat with us');
  });

  it('exposes open, close and toggle on window.KnowledgeAgent, also before the launcher shows', async () => {
    mockFetch({ [`GET ${WIDGET_API}/config`]: widgetConfig() });
    const handle = startLoader(addScript());
    window.KnowledgeAgent!.open();
    expect(await handle!.ready).toBe(true);

    const shadow = document.querySelector('knowledge-agent-widget')!.shadowRoot!;
    const launcher = shadow.querySelector('button.ka-launcher')!;
    expect(launcher).toHaveAttribute('aria-expanded', 'true');
    expect(shadow.querySelector('iframe')).not.toBeNull();

    window.KnowledgeAgent!.toggle();
    expect(launcher).toHaveAttribute('aria-expanded', 'false');
    window.KnowledgeAgent!.toggle();
    expect(launcher).toHaveAttribute('aria-expanded', 'true');
    window.KnowledgeAgent!.close();
    expect(launcher).toHaveAttribute('aria-expanded', 'false');
  });

  it('closes on Escape from the launcher', async () => {
    const { launcher } = await startWithConfig();
    launcher.click();

    launcher.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, composed: true }));
    expect(launcher).toHaveAttribute('aria-expanded', 'false');
  });

  it('loads only once when the snippet is on the page twice', async () => {
    await startWithConfig();

    expect(startLoader(addScript())).toBeNull();
    expect(document.querySelectorAll('knowledge-agent-widget')).toHaveLength(1);
  });

  it('renders a changed config in place, as the widget editor does on every edit', () => {
    const view = createLauncherView(PORTAL);
    const config: LauncherConfig = {
      title: 'Shop help',
      launcherIcon: 'message-circle',
      launcherIconUrl: null,
      brandColor: '#7c3aed',
      theme: 'auto',
      preset: 'glow',
      colors: { light: {}, dark: { launcher: '#0f172a' } },
      position: 'right',
      launcherLabel: null,
      texts: { launcherLabel: 'Open the chat' },
    };
    view.render(config, 'light');
    view.setOpen(true);
    expect(view.root).toHaveClass('ka-root', 'ka-right', 'ka-light', 'ka-glass', 'ka-glow', 'ka-round', 'ka-is-open');
    expect(view.button).toHaveAttribute('aria-label', 'Open the chat');
    expect(view.button.querySelector('.ka-label')).toBeNull();
    const firstIcon = view.button.querySelector('.ka-open-icon svg');

    view.render({ ...config, position: 'left', theme: 'dark', preset: 'solid', launcherLabel: 'Ask us', launcherIcon: 'sparkles', brandColor: '#facc15' }, 'dark');
    expect(view.root).toHaveClass('ka-left', 'ka-dark', 'ka-is-open');
    expect(view.root).not.toHaveClass('ka-round', 'ka-right', 'ka-glass', 'ka-glow');
    expect(view.button).toHaveTextContent('Ask us');
    expect(view.button).toHaveAttribute('aria-label', 'Ask us');
    expect(view.button.querySelector('.ka-open-icon svg')).not.toBe(firstIcon);
    expect(view.host.style.getPropertyValue('--ka-launcher')).toBe('#0f172a');
    expect(view.host.style.getPropertyValue('--ka-launcher-icon')).toBe('#ffffff');
    expect(view.host.style.getPropertyValue('--ka-panel-fill')).toBe('#1c1c1e');

    view.setBusy(true);
    expect(view.root).toHaveClass('ka-busy');
    view.render(config, 'light');
    expect(view.root).toHaveClass('ka-busy');
    view.setBusy(false);
    expect(view.root).not.toHaveClass('ka-busy');

    view.render({ ...config, launcherIconUrl: '/widget/v1/media/123' }, 'light');
    expect(view.button.querySelector('.ka-open-icon img')).toHaveAttribute('src', `${PORTAL}/widget/v1/media/123`);
    expect(view.button.querySelector('.ka-label')).toBeNull();

    view.setOpen(false);
    view.setUnread(12);
    expect(view.button.querySelector('.ka-badge')).toHaveTextContent('9+');
    expect(view.button).toHaveAttribute('aria-label', 'Open the chat (12)');
  });

  it('shows nothing and warns once when the widget is not available', async () => {
    mockFetch({ [`GET ${WIDGET_API}/config`]: () => problem(404, { title: 'Not found' }) });
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    const handle = startLoader(addScript());
    expect(await handle!.ready).toBe(false);
    expect(document.querySelector('knowledge-agent-widget')).toBeNull();
    expect(warn).toHaveBeenCalledOnce();
    expect(String(warn.mock.calls[0]![0])).toContain('HTTP 404');
  });
});
