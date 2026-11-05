import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { controlledEventStream, mockFetch } from '../../test/utils';
import { visitorConversation, visitorMessage, visitorSession, WIDGET_API, WIDGET_KEY, widgetConfig } from '../test/fixtures';
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

describe('widget frame accessibility (axe-core, WCAG 2.2 AA)', () => {
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
