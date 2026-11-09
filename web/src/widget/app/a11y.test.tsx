import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { controlledEventStream, mockFetch, sseResponse } from '../../test/utils';
import { problem, visitorConversation, visitorMessage, visitorSession, WIDGET_API, WIDGET_KEY, widgetConfig } from '../test/fixtures';
import { WidgetApp } from './WidgetApp';

beforeEach(() => {
  vi.spyOn(window, 'postMessage').mockImplementation(() => undefined);
});

const conversation = visitorConversation([
  visitorMessage(1, 'visitor', 'Can I return items?'),
  visitorMessage(2, 'assistant', 'Yes, within **thirty days** [S1]. See the [shipping page](https://shop.example/shipping) [S2].', {
    id: 'answer-1',
    citations: [
      { ref: 'S1', title: 'Returns policy', url: null, snippet: null },
      { ref: 'S2', title: 'Shipping guide', url: 'https://shop.example/shipping', snippet: null },
    ],
  }),
  visitorMessage(3, 'system', 'Dana joined the conversation.'),
  visitorMessage(4, 'staff', 'Hi, Dana here. Anything else?', { authorName: 'Dana' }),
]);

function mockWidget(config: Record<string, unknown> = {}, session: Record<string, unknown> = {}) {
  const live = controlledEventStream();
  mockFetch({
    [`GET ${WIDGET_API}/config`]: widgetConfig(config),
    [`POST ${WIDGET_API}/sessions`]: visitorSession(session),
    [`GET ${WIDGET_API}/conversations/c1`]: conversation,
    [`GET ${WIDGET_API}/conversations/c1/events`]: (_url: URL, init: RequestInit) => live.respond(init),
    [`POST ${WIDGET_API}/conversations/c1/close`]: () => new Response(null, { status: 204 }),
  });
}

function mockSending(reply: () => Response, config: Record<string, unknown> = {}) {
  mockFetch({
    [`GET ${WIDGET_API}/config`]: widgetConfig(config),
    [`POST ${WIDGET_API}/sessions`]: visitorSession(),
    [`POST ${WIDGET_API}/messages`]: reply,
  });
}

describe('widget frame accessibility (axe-core, WCAG 2.2 AA)', () => {
  it('is one main landmark with the title as its heading', async () => {
    mockWidget();
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    const heading = await screen.findByRole('heading', { level: 1, name: 'Shop help' });
    const main = screen.getByRole('main', { name: 'Shop help' });
    expect(main).toContainElement(heading);
    expect(within(main).getByRole('region', { name: 'Messages' })).toHaveAttribute('tabindex', '0');
  });

  it('an answer while it streams, with its status announced', async () => {
    const user = userEvent.setup();
    const turn = controlledEventStream();
    mockSending(() => turn.respond({}));
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    const input = await screen.findByRole('textbox', { name: 'Message' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeEnabled());
    await user.type(input, 'How long is the return window?{Enter}');
    turn.send('conversation', { conversationId: 'c1', title: 'New conversation', isNew: true });
    turn.send('text-delta', { text: 'Returns are accepted for thirty' });
    await screen.findByText(/Returns are accepted for thirty/);
    expect(await axeViolations()).toEqual([]);
  });

  it('a note before a tool call, shown as a status line and announced once', async () => {
    const user = userEvent.setup();
    const turn = controlledEventStream();
    mockSending(() => turn.respond({}));
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    const input = await screen.findByRole('textbox', { name: 'Message' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeEnabled());
    await user.type(input, 'How long is the return window?{Enter}');
    turn.send('conversation', { conversationId: 'c1', title: 'New conversation', isNew: true });
    turn.send('text-delta', { text: "I'll check the returns policy.", step: 1 });
    turn.send('step-finished', { step: 1, kind: 'interim' });
    turn.send('tool-call', { callId: 'k1', toolName: 'search_knowledge_base', arguments: null });
    await screen.findByTestId('answer-status');
    const regions = [...document.querySelectorAll('[aria-live], [role="status"], [role="alert"], [role="log"]')];
    expect(regions.filter((region) => region.textContent?.includes("I'll check the returns policy."))).toHaveLength(1);
    expect(await axeViolations()).toEqual([]);
  });

  it('an error after sending, announced as an alert', async () => {
    const user = userEvent.setup();
    mockSending(() => problem(429, { title: 'Too many requests', code: 'rate_limited' }, { 'Retry-After': '30' }));
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    const input = await screen.findByRole('textbox', { name: 'Message' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeEnabled());
    await user.type(input, 'Hello{Enter}');
    expect(await screen.findByRole('alert')).toHaveTextContent('You are sending messages too quickly.');
    expect(await axeViolations()).toEqual([]);
  });

  it('a widget that could not load', async () => {
    mockFetch({ [`GET ${WIDGET_API}/config`]: () => problem(500, { title: 'Broken' }) });
    render(<WidgetApp widgetKey={WIDGET_KEY} retryDelaysMs={[]} />);

    await screen.findByRole('button', { name: 'Try again' });
    expect(await axeViolations()).toEqual([]);
  });

  it('the handover form, which focuses the field to fix and describes its error', async () => {
    const user = userEvent.setup();
    const live = controlledEventStream();
    mockFetch({
      [`GET ${WIDGET_API}/config`]: widgetConfig({ handoverEnabled: true, contactCapture: 'before_handover' }),
      [`POST ${WIDGET_API}/sessions`]: visitorSession({ conversationId: 'c1' }),
      [`GET ${WIDGET_API}/conversations/c1`]: conversation,
      [`GET ${WIDGET_API}/conversations/c1/events`]: (_url: URL, init: RequestInit) => live.respond(init),
      [`POST ${WIDGET_API}/conversations/c1/handover`]: () => problem(400, { title: 'Invalid', errors: { email: ['Enter a valid email address.'] } }),
    });
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    await user.click(await screen.findByRole('button', { name: 'Talk to a person' }));
    const form = await screen.findByRole('form', { name: 'How can we reach you?' });
    expect(await axeViolations()).toEqual([]);

    await user.type(within(form).getByLabelText('Email'), 'ada@example');
    await user.click(within(form).getByRole('button', { name: 'Continue' }));
    const email = within(form).getByLabelText('Email');
    await waitFor(() => expect(email).toHaveFocus());
    expect(email).toHaveAttribute('aria-invalid', 'true');
    expect(email).toHaveAccessibleDescription('Enter a valid email address.');
    expect(await axeViolations()).toEqual([]);
  });

  it('on a phone, where nothing takes focus by itself', async () => {
    vi.spyOn(window, 'matchMedia').mockImplementation(
      (query) =>
        ({
          matches: query === '(pointer: coarse)',
          media: query,
          addEventListener: () => {},
          removeEventListener: () => {},
        }) as unknown as MediaQueryList,
    );
    window.innerWidth = 320;
    mockWidget({ handoverEnabled: true }, { conversationId: 'c1' });
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    await screen.findByRole('button', { name: 'Talk to a person' });
    window.dispatchEvent(
      new MessageEvent('message', { data: { source: 'knowledge-agent', type: 'open' }, source: window, origin: 'https://shop.example' }),
    );
    expect(screen.getByRole('textbox', { name: 'Message' })).not.toHaveFocus();
    expect(await axeViolations()).toEqual([]);
    window.innerWidth = 1024;
  });

  it('an offered person, an action to confirm with its arguments, and links shown as text', async () => {
    const user = userEvent.setup();
    const live = controlledEventStream();
    mockFetch({
      [`GET ${WIDGET_API}/config`]: widgetConfig({ handoverEnabled: true }),
      [`POST ${WIDGET_API}/sessions`]: visitorSession({ conversationId: 'c1' }),
      [`GET ${WIDGET_API}/conversations/c1`]: conversation,
      [`GET ${WIDGET_API}/conversations/c1/events`]: (_url: URL, init: RequestInit) => live.respond(init),
      [`POST ${WIDGET_API}/messages`]: () =>
        sseResponse([
          { event: 'handover-offered', data: { callId: 'h1' } },
          {
            event: 'confirmation-required',
            data: { callId: 'm1', title: 'Refund order', details: [{ label: 'order_id', value: '1042' }], expiresAt: '2099-01-01T00:00:00Z' },
          },
          { event: 'text-delta', data: { text: 'Shall I refund order 1042?' } },
          { event: 'done', data: { conversationId: 'c1', turnId: 't2', finishReason: 'stop' } },
        ]),
    });
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    const input = await screen.findByRole('textbox', { name: 'Message' });
    await screen.findByText('Can I return items?', { selector: 'p' });
    await user.type(input, 'Refund my order{Enter}');
    await screen.findByRole('group', { name: 'Refund order' });
    await screen.findByRole('group', { name: 'Want someone from the team to take over?' });
    expect(await axeViolations()).toEqual([]);
  });

  it('greeting and suggested questions', async () => {
    mockWidget();
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    await waitFor(() => expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeEnabled());
    expect(await axeViolations()).toEqual([]);
  });

  it('a conversation with sources, feedback, a person and the open menu', async () => {
    const user = userEvent.setup();
    mockWidget({ citationMode: 'links', handoverEnabled: true }, { conversationId: 'c1' });
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    await screen.findByText('Hi, Dana here. Anything else?');
    await screen.findByRole('button', { name: 'Talk to a person' });
    expect(screen.getByRole('list', { name: 'Sources' })).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Not helpful' }));
    await user.click(screen.getByRole('button', { name: 'More options' }));
    await screen.findByRole('menu');
    expect(await axeViolations()).toEqual([]);
  });

  it('contact form before the chat', async () => {
    mockWidget({ contactCapture: 'before_chat' });
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    await screen.findByRole('form', { name: 'How can we reach you?' });
    expect(await axeViolations()).toEqual([]);
  });

  it('ended chat with the rating', async () => {
    const user = userEvent.setup();
    mockWidget({}, { conversationId: 'c1' });
    render(<WidgetApp widgetKey={WIDGET_KEY} />);

    await screen.findByText('Can I return items?', { selector: 'p' });
    await user.click(screen.getByRole('button', { name: 'More options' }));
    await user.click(await screen.findByRole('menuitem', { name: 'End chat' }));
    await screen.findByRole('button', { name: '5 out of 5' });
    expect(await axeViolations()).toEqual([]);
  });
});
