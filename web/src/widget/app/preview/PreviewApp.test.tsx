import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { axeViolations } from '../../../test/a11y';
import { jsonResponse, mockFetch, problemResponse } from '../../../test/utils';
import { envelope, parsePreviewMessage, PREVIEW_VERSION, type EditorMessage, type PreviewMessage, type PreviewScene } from '../../shared/preview';
import { visitorSession, widgetConfig } from '../../test/fixtures';
import { PreviewApp } from './PreviewApp';

let posted: MockInstance<typeof window.postMessage>;

beforeEach(() => {
  // In jsdom the preview is its own parent, so what it posts to the editor lands here.
  posted = vi.spyOn(window, 'postMessage').mockImplementation(() => undefined);
  // jsdom has no layout: every element on the page gets a box whose height follows its text, so edits move things.
  vi.spyOn(Element.prototype, 'getClientRects').mockImplementation(function (this: Element) {
    return (this.isConnected ? [new DOMRect(0, 0, 10, 10)] : []) as unknown as DOMRectList;
  });
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    return new DOMRect(12, 34, 200, this.textContent?.length ?? 0);
  });
});

function messages(): PreviewMessage[] {
  return posted.mock.calls.flatMap(([data]) => parsePreviewMessage(data) ?? []);
}

function last<T extends PreviewMessage['type']>(type: T): Extract<PreviewMessage, { type: T }> | undefined {
  return messages()
    .filter((message): message is Extract<PreviewMessage, { type: T }> => message.type === type)
    .at(-1);
}

function fromEditor(message: EditorMessage, init: MessageEventInit = {}) {
  act(() => {
    window.dispatchEvent(new MessageEvent('message', { data: envelope(message), origin: window.location.origin, source: window, ...init }));
  });
}

function show(scene: PreviewScene, config: Record<string, unknown> = {}, colorScheme: 'light' | 'dark' = 'light') {
  fromEditor({ type: 'render', config: widgetConfig({ handoverEnabled: true, ...config }), scene, colorScheme });
}

function launcher() {
  const shadow = document.querySelector('knowledge-agent-widget')!.shadowRoot!;
  return { root: shadow.querySelector<HTMLElement>('.ka-root')!, button: shadow.querySelector<HTMLButtonElement>('button.ka-launcher')! };
}

function renderPreview() {
  const fetchMock = mockFetch({});
  render(<PreviewApp />);
  expect(messages()).toContainEqual({ type: 'ready' });
  return fetchMock;
}

describe('widget preview', () => {
  it('shows the scene the editor sends with the loader launcher, reports what shows, and calls no API', () => {
    const fetchMock = renderPreview();
    show('start');

    expect(screen.getByRole('heading', { level: 1, name: 'Shop help' })).toHaveAttribute('data-ka-edit', 'appearance.title');
    expect(screen.getByText('Hi! How can I help you today?').closest('[data-ka-edit]')).toHaveAttribute('data-ka-edit', 'behavior.greeting');
    expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeInTheDocument();
    const { root, button } = launcher();
    expect(button).toHaveAttribute('data-ka-edit', 'appearance.launcher');
    expect(root).toHaveClass('ka-is-open', 'ka-right');
    expect(document.documentElement.dataset.theme).toBe('light');

    const rendered = last('rendered')!;
    expect(rendered.scene).toBe('start');
    expect(rendered.targets).toEqual(
      expect.arrayContaining(['appearance.launcher', 'appearance.header', 'appearance.title', 'appearance.subtitle', 'behavior.greeting', 'behavior.suggestedQuestions']),
    );
    expect(rendered.targets).not.toContain('behavior.feedback');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('plays every scene with sample data and the texts of the config', () => {
    renderPreview();
    const texts = { waitingForPerson: 'Hold on, please.', keepChatting: 'Ask the bot instead', ended: 'All done.' };

    show('conversation', { texts });
    expect(screen.getByText('Can I return something I bought online?')).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Sources' })).getByText('Returns policy')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Helpful' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Talk to a person' })).toHaveAttribute('data-ka-edit', 'behavior.handover');
    expect(screen.getByRole('button', { name: 'More options' })).toHaveAttribute('data-ka-edit', 'header.menu');

    show('contact', { contactCapture: 'never', texts });
    expect(screen.getByRole('form', { name: 'How can we reach you?' })).toHaveAttribute('data-ka-edit', 'behavior.contact');

    show('waiting', { texts });
    const waiting = screen.getByText('Hold on, please.').closest<HTMLElement>('[data-ka-edit]');
    expect(waiting).toHaveAttribute('data-ka-edit', 'status.waiting');
    expect(within(waiting!).getByRole('button', { name: 'Ask the bot instead' })).toBeInTheDocument();

    show('joined', { texts });
    expect(screen.getByText('You are chatting with Dana.')).toHaveAttribute('data-ka-edit', 'status.joined');
    expect(screen.getByText('Hi, I am Dana from the team. I can help with your return.')).toBeInTheDocument();

    show('ended', { texts });
    expect(screen.getByText('All done.').closest('[data-ka-edit]')).toHaveAttribute('data-ka-edit', 'status.ended');
    expect(screen.getByRole('button', { name: '5 out of 5' })).toBeInTheDocument();
    expect(last('rendered')?.scene).toBe('ended');

    show('launcher', { launcherLabel: 'Ask us' });
    expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
    const { root, button } = launcher();
    expect(root).not.toHaveClass('ka-is-open');
    expect(button).toHaveTextContent('Ask us');
    expect(last('rendered')?.targets).toEqual(['appearance.launcher']);
  });

  it('selects a clicked element instead of running it, and deselects on a click elsewhere or Esc', async () => {
    const user = userEvent.setup();
    renderPreview();
    show('start');

    await user.click(screen.getByRole('button', { name: 'Can I return items?' }));
    expect(last('select')).toEqual({ type: 'select', target: 'behavior.suggestedQuestions', rect: { x: 12, y: 34, width: 200, height: 37 } });
    expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeInTheDocument();

    await user.click(launcher().button);
    expect(last('select')?.target).toBe('appearance.launcher');
    expect(launcher().root).toHaveClass('ka-is-open');

    await user.click(document.querySelector('[aria-hidden="true"]')!);
    expect(last('select')).toEqual({ type: 'select', target: null, rect: null });

    await user.click(screen.getByRole('heading', { level: 1 }));
    await user.keyboard('{Escape}');
    expect(last('select')).toEqual({ type: 'select', target: null, rect: null });
  });

  it('reports what the pointer is over', async () => {
    const user = userEvent.setup();
    renderPreview();
    show('start');

    await user.hover(screen.getByRole('heading', { level: 1 }));
    expect(last('hover')).toEqual({ type: 'hover', target: 'appearance.title', rect: { x: 12, y: 34, width: 200, height: 9 } });
    await user.hover(screen.getByText('Powered by Knowledge Agent'));
    expect(last('hover')?.target).toBe('texts.poweredBy');
  });

  it('tracks the element the editor asks for and reports where it is after each render', () => {
    renderPreview();
    show('start');

    fromEditor({ type: 'track', target: 'appearance.title', fallbacks: [] });
    expect(last('rect')).toEqual({ type: 'rect', target: 'appearance.title', anchor: 'appearance.title', rect: { x: 12, y: 34, width: 200, height: 9 } });

    show('start', { title: 'Shop help and returns' });
    expect(last('rect')?.rect?.height).toBe(21);

    fromEditor({ type: 'track', target: 'behavior.feedback', fallbacks: [] });
    expect(last('rect')).toEqual({ type: 'rect', target: 'behavior.feedback', anchor: null, rect: null });
    show('conversation');
    expect(last('rect')).toEqual({ type: 'rect', target: 'behavior.feedback', anchor: 'behavior.feedback', rect: expect.objectContaining({ x: 12 }) });
  });

  it('points at the first fallback that shows while the element does not, and at the element as soon as it shows', () => {
    renderPreview();
    show('start', { subtitle: null });

    fromEditor({ type: 'track', target: 'appearance.subtitle', fallbacks: ['appearance.title', 'appearance.header'] });
    expect(last('rect')).toEqual({ type: 'rect', target: 'appearance.subtitle', anchor: 'appearance.title', rect: expect.objectContaining({ height: 9 }) });

    show('start', { subtitle: 'We answer in a minute' });
    expect(last('rect')).toEqual({ type: 'rect', target: 'appearance.subtitle', anchor: 'appearance.subtitle', rect: expect.objectContaining({ height: 21 }) });

    show('launcher', { subtitle: 'We answer in a minute' });
    expect(last('rect')).toEqual({ type: 'rect', target: 'appearance.subtitle', anchor: null, rect: null });
  });

  it('paints the theme on the page and the launcher, and glows while the sample answer is written', () => {
    renderPreview();
    show('start', { preset: 'solid', colors: { light: { launcher: '#111111' }, dark: {} } });
    expect(document.documentElement.dataset.glass).toBe('false');
    expect(launcher().root).not.toHaveClass('ka-glass');
    expect(launcher().root).not.toHaveClass('ka-glow');
    const host = document.querySelector<HTMLElement>('knowledge-agent-widget')!;
    expect(host.style.getPropertyValue('--ka-launcher')).toBe('#111111');

    show('answering');
    expect(screen.getByText('Can I return something I bought online?')).toHaveAttribute('data-ka-edit', 'messages.visitor');
    expect(screen.getByText(/Start the return from your/).closest('[data-ka-edit]')).toHaveAttribute('data-ka-edit', 'messages.assistant');
    expect(launcher().root).toHaveClass('ka-glass', 'ka-glow', 'ka-busy');
    expect(last('rendered')?.targets).toEqual(expect.arrayContaining(['messages.visitor', 'messages.assistant', 'behavior.placeholder']));

    show('conversation');
    expect(launcher().root).not.toHaveClass('ka-busy');
  });

  it('leaves the footer out when the widget hides it, so the editor knows it does not show', () => {
    renderPreview();
    show('start');
    expect(screen.getByText('Powered by Knowledge Agent')).toHaveAttribute('data-ka-edit', 'texts.poweredBy');
    expect(last('rendered')?.targets).toContain('texts.poweredBy');

    show('start', { showFooter: false });
    expect(screen.queryByText('Powered by Knowledge Agent')).toBeNull();
    expect(last('rendered')?.targets).not.toContain('texts.poweredBy');
  });

  it("follows the editor's light and dark switch only while the widget follows the visitor's system", () => {
    renderPreview();
    show('start', { theme: 'auto' }, 'dark');
    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(screen.getByRole('heading', { level: 1 }).closest('[data-scheme]')).toHaveAttribute('data-scheme', 'dark');

    show('start', { theme: 'light' }, 'dark');
    expect(document.documentElement.dataset.theme).toBe('light');
  });

  it('ignores messages from other origins, other windows and other protocol versions', () => {
    renderPreview();
    show('start');
    const render = envelope({ type: 'render', config: widgetConfig({ title: 'Draft title' }), scene: 'start', colorScheme: 'light' });

    fromEditor({ type: 'render', config: widgetConfig({ title: 'Draft title' }), scene: 'start', colorScheme: 'light' }, { origin: 'https://evil.example' });
    fromEditor({ type: 'render', config: widgetConfig({ title: 'Draft title' }), scene: 'start', colorScheme: 'light' }, { source: null });
    act(() => {
      window.dispatchEvent(new MessageEvent('message', { data: { ...render, version: PREVIEW_VERSION + 1 }, origin: window.location.origin, source: window }));
    });

    expect(screen.getByRole('heading', { level: 1, name: 'Shop help' })).toBeInTheDocument();
  });

  it('has no accessibility violations in a scene with sources, feedback and a person (axe-core, WCAG 2.2 AA)', async () => {
    renderPreview();
    show('joined');
    expect(await axeViolations()).toEqual([]);
  });
  describe('live, as "Test this widget"', () => {
    const widgetId = '0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b';
    const route = `POST /api/widgets/${widgetId}/preview-session`;

    it('runs the real widget with a preview session from the portal, and the launcher opens and closes it', async () => {
      const user = userEvent.setup();
      const fetchMock = mockFetch({
        [route]: () => jsonResponse({ session: visitorSession({ token: 'preview-token' }), config: widgetConfig({ title: 'Demo shop', theme: 'auto' }) }),
      });
      render(<PreviewApp />);
      fromEditor({ type: 'live', widgetId, colorScheme: 'dark' });

      expect(await screen.findByRole('heading', { level: 1, name: 'Demo shop' })).toBeInTheDocument();
      const [, init] = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/preview-session'))!;
      expect((init as RequestInit).headers).toMatchObject({ 'X-KA-Request': '1' });
      expect((init as RequestInit).credentials).toBe('same-origin');
      expect(window.localStorage.getItem(`ka-widget-preview:${widgetId}`)).toBeNull();

      const { root, button } = launcher();
      expect(button).not.toHaveAttribute('data-ka-edit');
      expect(root).toHaveClass('ka-is-open');
      await user.click(button);
      expect(root).not.toHaveClass('ka-is-open');
      await user.click(button);
      expect(root).toHaveClass('ka-is-open');
      expect(document.documentElement.dataset.theme).toBe('dark');
    });

    it('says why when the preview cannot start', async () => {
      mockFetch({ [route]: () => problemResponse({ title: 'Forbidden', detail: 'Only admins can try widgets.' }, 403) });
      render(<PreviewApp />);
      fromEditor({ type: 'live', widgetId, colorScheme: 'light' });
      await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('The preview could not start. Only admins can try widgets.'));
    });
  });
});
