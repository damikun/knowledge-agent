import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { controlledEventStream, jsonResponse, mockFetch, sseResponse, type FetchHandler } from '../../test/utils';
import { problem, streamCitation, visitorConversation, visitorMessage, visitorSession, WIDGET_API, WIDGET_KEY, widgetConfig } from '../test/fixtures';
import { envelope } from '../shared/preview';
import { WidgetApp } from './WidgetApp';

let posted: MockInstance<typeof window.postMessage>;

beforeEach(() => {
  // In jsdom the frame is its own parent, so what it posts to the host page lands here.
  posted = vi.spyOn(window, 'postMessage').mockImplementation(() => undefined);
});

type Routes = Record<string, FetchHandler | unknown>;

function routes(extra: Routes = {}, config: Record<string, unknown> = {}, session: Record<string, unknown> = {}): Routes {
  return {
    [`GET ${WIDGET_API}/config`]: widgetConfig(config),
    [`POST ${WIDGET_API}/sessions`]: visitorSession(session),
    ...extra,
  };
}

function renderWidget() {
  return render(<WidgetApp widgetKey={WIDGET_KEY} retryDelaysMs={[20]} />);
}

function requestBodies(fetchMock: ReturnType<typeof mockFetch>, route: string): unknown[] {
  return fetchMock.mock.calls.flatMap(([input, init]) => {
    const url = new URL(String(input), 'http://localhost');
    const request = (init ?? {}) as RequestInit;
    return `${request.method ?? 'GET'} ${url.pathname}` === route ? [request.body ? JSON.parse(String(request.body)) : undefined] : [];
  });
}

function requestHeaders(fetchMock: ReturnType<typeof mockFetch>, route: string): Array<Record<string, string>> {
  return fetchMock.mock.calls.flatMap(([input, init]) => {
    const url = new URL(String(input), 'http://localhost');
    const request = (init ?? {}) as RequestInit;
    return `${request.method ?? 'GET'} ${url.pathname}` === route ? [request.headers as Record<string, string>] : [];
  });
}

function hostMessages(): unknown[] {
  return posted.mock.calls.map(([data]) => data);
}

function sendFromHost(type: 'open' | 'close') {
  window.dispatchEvent(new MessageEvent('message', { data: { source: 'knowledge-agent', type }, source: window, origin: 'https://shop.example' }));
}

const answerWithSources = visitorMessage(2, 'assistant', 'Returns are accepted for thirty days [S1]. Shipping is free [S2].', {
  id: 'answer-1',
  citations: [
    { ref: 'S1', title: 'Returns policy', url: null, snippet: 'Thirty days.' },
    { ref: 'S2', title: 'Shipping guide', url: 'https://shop.example/shipping', snippet: 'Free shipping.' },
  ],
});

function resumedConversation(messages: Array<Record<string, unknown>>, overrides: Record<string, unknown> = {}) {
  const live = controlledEventStream();
  return {
    live,
    routes: {
      [`GET ${WIDGET_API}/conversations/c1`]: visitorConversation(messages, overrides),
      [`GET ${WIDGET_API}/conversations/c1/events`]: (_url: URL, init: RequestInit) => live.respond(init),
    } satisfies Routes,
  };
}

describe('widget frame', () => {
  it('shows the greeting, the suggested questions and the texts of the config, and tells the host it is ready', async () => {
    mockFetch(routes({}, { texts: { poweredBy: 'Run by Acme' } }));
    renderWidget();

    expect(await screen.findByRole('heading', { level: 1, name: 'Shop help' })).toBeInTheDocument();
    expect(screen.getByText('Answers in seconds')).toBeInTheDocument();
    expect(screen.getByText('Hi! How can I help you today?')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeEnabled());
    expect(screen.getByRole('button', { name: 'Where is my order?' })).toBeInTheDocument();
    expect(screen.getByText('Run by Acme')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Message' })).toHaveAttribute('placeholder', 'Ask about your order');
    expect(document.title).toBe('Shop help');
    expect(document.documentElement.dataset.theme).toBe('light');
    expect(hostMessages()).toContainEqual({ source: 'knowledge-agent', type: 'ready' });
    expect(localStorage.getItem(`ka-widget:${WIDGET_KEY}`)).toBe('visitor-token');
  });

  it('resumes the stored visitor token and the open conversation', async () => {
    localStorage.setItem(`ka-widget:${WIDGET_KEY}`, 'stored-token');
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Can I return items?'), answerWithSources]);
    const fetchMock = mockFetch(routes(resumed.routes, {}, { token: 'renewed-token', conversationId: 'c1' }));
    renderWidget();

    expect(await screen.findByText('Can I return items?', { selector: 'p' })).toBeInTheDocument();
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/sessions`)).toEqual([{ token: 'stored-token' }]);
    expect(requestHeaders(fetchMock, `GET ${WIDGET_API}/conversations/c1`)[0]).toMatchObject({ Authorization: 'Bearer renewed-token' });
    expect(localStorage.getItem(`ka-widget:${WIDGET_KEY}`)).toBe('renewed-token');
    expect(screen.queryByRole('button', { name: 'Where is my order?' })).not.toBeInTheDocument();
    await waitFor(() => expect(resumed.live.isOpen).toBe(true));
  });

  it('sends a message, streams the answer with a quiet status, and then shows the stored answer once', async () => {
    const user = userEvent.setup();
    const turn = controlledEventStream();
    const live = controlledEventStream();
    let stored = visitorConversation([]);
    const fetchMock = mockFetch(
      routes({
        [`POST ${WIDGET_API}/messages`]: (_url: URL, init: RequestInit) => turn.respond(init),
        [`GET ${WIDGET_API}/conversations/c1/events`]: (_url: URL, init: RequestInit) => live.respond(init),
        [`GET ${WIDGET_API}/conversations/c1`]: () => jsonResponse(stored),
      }),
    );
    renderWidget();

    const input = await screen.findByRole('textbox', { name: 'Message' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeEnabled());
    await user.type(input, 'How long is the return window?{Enter}');

    expect(input).toHaveValue('');
    expect(screen.getByText('How long is the return window?')).toBeInTheDocument();
    expect(await screen.findByRole('img', { name: 'Writing an answer' })).toBeInTheDocument();
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/messages`)).toEqual([{ message: 'How long is the return window?' }]);
    expect(requestHeaders(fetchMock, `POST ${WIDGET_API}/messages`)[0]).toMatchObject({ Authorization: 'Bearer visitor-token', 'X-KA-Request': '1' });

    turn.send('conversation', { conversationId: 'c1', title: 'New conversation', isNew: true });
    turn.send('tool-call', { callId: 'k1', toolName: 'search_knowledge_base', arguments: null });
    expect(await screen.findByText('Searching the knowledge base')).toBeInTheDocument();
    expect(screen.queryByText(/search_knowledge_base/)).not.toBeInTheDocument();

    turn.send('tool-result', {
      callId: 'k1',
      toolName: 'search_knowledge_base',
      isError: false,
      summary: '',
      durationMs: 12,
      citations: [streamCitation('S1', 'Returns policy')],
    });
    turn.send('text-delta', { text: 'Returns are accepted for **thirty days** [S' });
    expect(await screen.findByText('thirty days')).toBeInTheDocument();
    expect(screen.queryByText('Searching the knowledge base')).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain('[S');
    turn.send('text-delta', { text: '1].' });
    expect(await screen.findByText('Source 1')).toBeInTheDocument();
    expect(screen.getByText('Returns policy')).toBeInTheDocument();

    await waitFor(() => expect(live.isOpen).toBe(true));
    live.send('message', visitorMessage(1, 'visitor', 'How long is the return window?'), 1);
    live.send('turn', { type: 'text-delta', data: { text: 'Returns are accepted' } });
    await waitFor(() => expect(screen.getAllByText('How long is the return window?')).toHaveLength(1));

    const answer = visitorMessage(2, 'assistant', 'Returns are accepted for **thirty days** [S1].', {
      id: 'answer-1',
      citations: [{ ref: 'S1', title: 'Returns policy', url: null, snippet: 'Thirty days.' }],
    });
    stored = visitorConversation([visitorMessage(1, 'visitor', 'How long is the return window?'), answer]);
    turn.send('done', { conversationId: 'c1', turnId: 't1', finishReason: 'stop' });
    turn.close();

    expect(await screen.findByRole('button', { name: 'Helpful' })).toBeInTheDocument();
    expect(screen.getAllByText('thirty days')).toHaveLength(1);
    expect(screen.getAllByText('How long is the return window?')).toHaveLength(1);
    expect(screen.queryByText('Returns are accepted')).not.toBeInTheDocument();
    await waitFor(() => expect(document.querySelector('[aria-live="polite"]')).toHaveTextContent('Shop help: Returns are accepted for thirty days.'));
  });

  it('tells the host while an answer is written, so the loader can glow around the panel', async () => {
    const user = userEvent.setup();
    const turn = controlledEventStream();
    mockFetch(
      routes({
        [`POST ${WIDGET_API}/messages`]: (_url: URL, init: RequestInit) => turn.respond(init),
        [`GET ${WIDGET_API}/conversations/c1/events`]: (_url: URL, init: RequestInit) => controlledEventStream().respond(init),
        [`GET ${WIDGET_API}/conversations/c1`]: () =>
          jsonResponse(visitorConversation([visitorMessage(1, 'visitor', 'Hi'), visitorMessage(2, 'assistant', 'Hello!')])),
      }),
    );
    renderWidget();
    const busy = () => hostMessages().filter((message) => (message as { type?: string }).type === 'busy');

    await user.type(await screen.findByRole('textbox', { name: 'Message' }), 'Hi{Enter}');
    await waitFor(() => expect(busy().at(-1)).toEqual({ source: 'knowledge-agent', type: 'busy', busy: true }));
    expect(screen.getByRole('textbox', { name: 'Message' }).closest('.ka-glow-ring')).not.toBeNull();

    turn.send('conversation', { conversationId: 'c1', title: 'Hi', isNew: true });
    turn.send('text-delta', { text: 'Hello!' });
    turn.send('done', { conversationId: 'c1', turnId: 't1', finishReason: 'stop' });
    turn.close();
    await waitFor(() => expect(busy().at(-1)).toEqual({ source: 'knowledge-agent', type: 'busy', busy: false }));
    expect(screen.getByRole('textbox', { name: 'Message' }).closest('.ka-glow-ring')).toBeNull();
  });

  it('turns a note written before a tool call into a status line that the answer replaces', async () => {
    const user = userEvent.setup();
    const turn = controlledEventStream();
    mockFetch(
      routes({
        [`POST ${WIDGET_API}/messages`]: (_url: URL, init: RequestInit) => turn.respond(init),
        [`GET ${WIDGET_API}/conversations/c1/events`]: (_url: URL, init: RequestInit) => controlledEventStream().respond(init),
      }),
    );
    renderWidget();

    await user.type(await screen.findByRole('textbox', { name: 'Message' }), 'How long is the return window?{Enter}');
    turn.send('conversation', { conversationId: 'c1', title: 'Returns', isNew: true });
    turn.send('text-delta', { text: "Sure. I'll check the **returns policy** [S1].", step: 1 });
    expect(await screen.findByText('returns policy')).toBeInTheDocument();

    turn.send('step-finished', { step: 1, kind: 'interim' });
    const status = await screen.findByTestId('answer-status');
    expect(status).toHaveTextContent("I'll check the returns policy.");
    expect(status.closest('[aria-live], [role="status"], [role="log"], [role="alert"]')).toBeNull();
    await waitFor(() => expect(document.querySelector('[aria-live="polite"]')).toHaveTextContent("I'll check the returns policy."));
    expect(screen.queryByText('returns policy')).not.toBeInTheDocument();

    turn.send('tool-call', { callId: 'k1', toolName: 'search_knowledge_base', arguments: null });
    await waitFor(() => expect(screen.getByTestId('answer-status')).toBeInTheDocument());
    expect(screen.queryByText('Searching the knowledge base')).not.toBeInTheDocument();
    turn.send('tool-result', { callId: 'k1', toolName: 'search_knowledge_base', isError: false, summary: '', durationMs: 5, citations: [] });

    turn.send('text-delta', { text: 'Returns are accepted for thirty days.', step: 2 });
    expect(await screen.findByText('Returns are accepted for thirty days.')).toBeInTheDocument();
    expect(screen.queryByTestId('answer-status')).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain('Sure.');
  });

  it('starts a new paragraph for the text of a new step instead of gluing the steps together', async () => {
    const user = userEvent.setup();
    const turn = controlledEventStream();
    mockFetch(
      routes({
        [`POST ${WIDGET_API}/messages`]: (_url: URL, init: RequestInit) => turn.respond(init),
        [`GET ${WIDGET_API}/conversations/c1/events`]: (_url: URL, init: RequestInit) => controlledEventStream().respond(init),
      }),
    );
    renderWidget();

    await user.type(await screen.findByRole('textbox', { name: 'Message' }), 'Hi{Enter}');
    turn.send('conversation', { conversationId: 'c1', title: 'Hi', isNew: true });
    turn.send('text-delta', { text: 'Let me look.', step: 1 });
    turn.send('text-delta', { text: 'Hello there.', step: 2 });

    expect(await screen.findByText('Hello there.')).toBeInTheDocument();
    expect(screen.getByText('Let me look.')).toBeInTheDocument();
    expect(document.body.textContent).not.toContain('look.Hello');
  });

  it('paints the theme with its overrides on the page, and leaves the footer out when the widget hides it', async () => {
    mockFetch(routes({}, { preset: 'solid', colors: { light: { header: '#1E3A8A', glow: 'pink' }, dark: {} }, showFooter: false }));
    renderWidget();

    expect(await screen.findByRole('heading', { level: 1, name: 'Shop help' })).toBeInTheDocument();
    const root = document.documentElement;
    expect(root.dataset).toMatchObject({ theme: 'light', glass: 'false', glow: 'false' });
    expect(root.style.getPropertyValue('--ka-header')).toBe('#1e3a8a');
    expect(root.style.getPropertyValue('--ka-header-text')).toBe('#ffffff');
    expect(root.style.getPropertyValue('--ka-radius')).toBe('20px');
    expect(screen.queryByText('Powered by Knowledge Agent')).not.toBeInTheDocument();
  });

  describe('citation modes', () => {
    async function renderAnswer(citationMode: string) {
      const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answerWithSources]);
      mockFetch(routes(resumed.routes, { citationMode }, { conversationId: 'c1' }));
      renderWidget();
      await screen.findByText('Returns?', { selector: 'p' });
    }

    it('hidden removes the tags and lists no sources', async () => {
      await renderAnswer('hidden');

      expect(screen.getByText('Returns are accepted for thirty days. Shipping is free.')).toBeInTheDocument();
      expect(screen.queryByText('Sources')).not.toBeInTheDocument();
      expect(screen.queryByText('Returns policy')).not.toBeInTheDocument();
      expect(document.body.textContent).not.toContain('[S');
    });

    it('titles numbers the tags and lists the source titles', async () => {
      await renderAnswer('titles');

      expect(screen.getByText('Source 1')).toBeInTheDocument();
      expect(screen.getByText('Source 2')).toBeInTheDocument();
      const sources = screen.getByRole('list', { name: 'Sources' });
      expect(within(sources).getByText('Returns policy')).toBeInTheDocument();
      expect(within(sources).getByText('Shipping guide')).toBeInTheDocument();
      expect(within(sources).queryByRole('link')).not.toBeInTheDocument();
      expect(document.body.textContent).not.toContain('[S');
    });

    it('links links the titles that have a web address', async () => {
      await renderAnswer('links');

      const sources = screen.getByRole('list', { name: 'Sources' });
      const link = within(sources).getByRole('link', { name: /Shipping guide/ });
      expect(link).toHaveAttribute('href', 'https://shop.example/shipping');
      expect(link).toHaveAttribute('target', '_blank');
      expect(link).toHaveAttribute('rel', 'noopener noreferrer');
      expect(within(sources).getByText('Returns policy').closest('a')).toBeNull();
    });
  });

  it('rates answers with thumbs, with an optional comment after thumbs down, and shows saved ratings', async () => {
    const user = userEvent.setup();
    const older = visitorMessage(2, 'assistant', 'We ship worldwide.', { id: 'answer-0', rating: 1 });
    const resumed = resumedConversation([
      visitorMessage(1, 'visitor', 'Shipping?'),
      older,
      visitorMessage(3, 'visitor', 'Returns?'),
      { ...answerWithSources, sequence: 4 },
    ]);
    const fetchMock = mockFetch(
      routes(
        { ...resumed.routes, [`POST ${WIDGET_API}/conversations/c1/feedback`]: { id: 'f1', messageId: 'answer-1', rating: -1 } },
        {},
        { conversationId: 'c1' },
      ),
    );
    renderWidget();

    const helpful = await screen.findAllByRole('button', { name: 'Helpful' });
    expect(helpful[0]).toHaveAttribute('aria-pressed', 'true');
    expect(helpful[1]).toHaveAttribute('aria-pressed', 'false');

    await user.click(screen.getAllByRole('button', { name: 'Not helpful' })[1]!);
    const comment = await screen.findByLabelText('What could be better?');
    expect(screen.getAllByRole('button', { name: 'Not helpful' })[1]).toHaveAttribute('aria-pressed', 'true');
    await user.type(comment, 'The fee is missing');
    await user.click(within(comment.closest('form')!).getByRole('button', { name: 'Send' }));

    expect(await screen.findByText('Thanks for your feedback.')).toBeInTheDocument();
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/conversations/c1/feedback`)).toEqual([
      { rating: -1, messageId: 'answer-1' },
      { rating: -1, messageId: 'answer-1', comment: 'The fee is missing' },
    ]);
  });

  it('asks for name and email before a person joins, then waits and shows who answers', async () => {
    const user = userEvent.setup();
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answerWithSources]);
    const fetchMock = mockFetch(
      routes(
        { ...resumed.routes, [`POST ${WIDGET_API}/conversations/c1/handover`]: { status: 'handover_pending', started: true } },
        { handoverEnabled: true, contactCapture: 'before_handover' },
        { conversationId: 'c1' },
      ),
    );
    renderWidget();

    await user.click(await screen.findByRole('button', { name: 'Talk to a person' }));
    const form = await screen.findByRole('form', { name: 'How can we reach you?' });
    expect(screen.queryByRole('textbox', { name: 'Message' })).not.toBeInTheDocument();
    await user.type(within(form).getByLabelText('Name'), 'Ada');
    await user.type(within(form).getByLabelText('Email'), 'ada@example.com');
    await user.click(within(form).getByRole('button', { name: 'Continue' }));

    expect(await screen.findByText('Waiting for someone from the team to join.')).toBeInTheDocument();
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/conversations/c1/handover`)).toEqual([{ name: 'Ada', email: 'ada@example.com' }]);
    expect(screen.queryByRole('button', { name: 'Talk to a person' })).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Message' })).toBeInTheDocument();

    await waitFor(() => expect(resumed.live.isOpen).toBe(true));
    // The live stream has sent the status without the underscore.
    resumed.live.send('conversation', { conversationId: 'c1', status: 'handoverpending' });
    resumed.live.send('message', visitorMessage(3, 'system', 'A colleague will reply here soon.'), 3);
    resumed.live.send('conversation', { conversationId: 'c1', status: 'human' });
    resumed.live.send('message', visitorMessage(4, 'staff', 'Hi Ada, Dana here.', { authorName: 'Dana' }), 4);

    expect(await screen.findByText('You are chatting with Dana.')).toBeInTheDocument();
    expect(screen.getByText('A colleague will reply here soon.')).toBeInTheDocument();
    expect(screen.getByText('Hi Ada, Dana here.')).toBeInTheDocument();
    expect(screen.queryByText('Waiting for someone from the team to join.')).not.toBeInTheDocument();
  });

  it('asks for the email when the API needs one to hand over, and retries with it', async () => {
    const user = userEvent.setup();
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answerWithSources]);
    let calls = 0;
    mockFetch(
      routes(
        {
          ...resumed.routes,
          [`POST ${WIDGET_API}/conversations/c1/handover`]: () =>
            ++calls === 1
              ? problem(400, { title: 'Validation failed', errors: { email: ['Leave your email address so the team can reply.'] } })
              : jsonResponse({ status: 'handover_pending', started: true }),
        },
        { handoverEnabled: true, contactCapture: 'never' },
        { conversationId: 'c1', name: 'Ada' },
      ),
    );
    renderWidget();

    await user.click(await screen.findByRole('button', { name: 'Talk to a person' }));
    const form = await screen.findByRole('form', { name: 'How can we reach you?' });
    expect(within(form).getByText('Leave your email address so the team can reply.')).toBeInTheDocument();
    expect(within(form).getByLabelText('Name')).toHaveValue('Ada');
    expect(within(form).getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true');
    await user.type(within(form).getByLabelText('Email'), 'ada@example.com');
    await user.click(within(form).getByRole('button', { name: 'Continue' }));

    expect(await screen.findByText('Waiting for someone from the team to join.')).toBeInTheDocument();
    expect(calls).toBe(2);
  });

  it('stops waiting for a person and goes on with the assistant', async () => {
    const user = userEvent.setup();
    const resumed = resumedConversation(
      [visitorMessage(1, 'visitor', 'Returns?'), answerWithSources, visitorMessage(3, 'system', "I've asked a colleague to join.")],
      { status: 'handover_pending' },
    );
    const fetchMock = mockFetch(
      routes(
        { ...resumed.routes, [`POST ${WIDGET_API}/conversations/c1/handover/cancel`]: () => new Response(null, { status: 204 }) },
        { handoverEnabled: true },
        { conversationId: 'c1' },
      ),
    );
    renderWidget();

    expect(await screen.findByText('Waiting for someone from the team to join.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Talk to a person' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Keep chatting with the assistant' }));

    await waitFor(() => expect(screen.queryByText('Waiting for someone from the team to join.')).not.toBeInTheDocument());
    expect(requestHeaders(fetchMock, `POST ${WIDGET_API}/conversations/c1/handover/cancel`)[0]).toMatchObject({
      Authorization: 'Bearer visitor-token',
    });
    expect(screen.getByRole('button', { name: 'Talk to a person' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Message' })).toBeInTheDocument();

    await waitFor(() => expect(resumed.live.isOpen).toBe(true));
    resumed.live.send('conversation', { conversationId: 'c1', status: 'ai' });
    resumed.live.send('message', visitorMessage(4, 'system', "You're chatting with the assistant again."), 4);
    expect(
      await within(screen.getByRole('region', { name: 'Messages' })).findByText("You're chatting with the assistant again."),
    ).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('stays with the person who joined when the visitor stops waiting too late (409 person_joined)', async () => {
    const user = userEvent.setup();
    const live = controlledEventStream();
    let loads = 0;
    mockFetch(
      routes(
        {
          [`GET ${WIDGET_API}/conversations/c1`]: () =>
            jsonResponse(
              ++loads === 1
                ? visitorConversation([visitorMessage(1, 'visitor', 'Returns?')], { status: 'handover_pending' })
                : visitorConversation(
                    [
                      visitorMessage(1, 'visitor', 'Returns?'),
                      visitorMessage(2, 'system', 'Dana joined the conversation.'),
                      visitorMessage(3, 'staff', 'Hi, Dana here.', { authorName: 'Dana' }),
                    ],
                    { status: 'human' },
                  ),
            ),
          [`GET ${WIDGET_API}/conversations/c1/events`]: (_url: URL, init: RequestInit) => live.respond(init),
          [`POST ${WIDGET_API}/conversations/c1/handover/cancel`]: () => problem(409, { title: 'Someone already joined', code: 'person_joined' }),
        },
        { handoverEnabled: true },
        { conversationId: 'c1' },
      ),
    );
    renderWidget();

    await user.click(await screen.findByRole('button', { name: 'Keep chatting with the assistant' }));

    expect(await screen.findByText('You are chatting with Dana.')).toBeInTheDocument();
    expect(screen.getByText('Hi, Dana here.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Keep chatting with the assistant' })).not.toBeInTheDocument();
    expect(screen.queryByText('Waiting for someone from the team to join.')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('ends the wait when nobody joined in time, and shows a chat that closed after a quiet while', async () => {
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answerWithSources], { status: 'handover_pending' });
    mockFetch(routes(resumed.routes, { handoverEnabled: true }, { conversationId: 'c1' }));
    renderWidget();

    expect(await screen.findByText('Waiting for someone from the team to join.')).toBeInTheDocument();
    await waitFor(() => expect(resumed.live.isOpen).toBe(true));
    resumed.live.send('conversation', { conversationId: 'c1', status: 'ai' });
    resumed.live.send('message', visitorMessage(3, 'system', 'Nobody from the team is free right now. Please try again later.'), 3);

    const messages = screen.getByRole('region', { name: 'Messages' });
    expect(await within(messages).findByText('Nobody from the team is free right now. Please try again later.')).toBeInTheDocument();
    expect(screen.queryByText('Waiting for someone from the team to join.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Keep chatting with the assistant' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Talk to a person' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Message' })).toBeInTheDocument();

    resumed.live.send('conversation', { conversationId: 'c1', status: 'closed' });
    resumed.live.send('message', visitorMessage(4, 'system', 'This conversation was closed after a quiet while.'), 4);
    expect(await screen.findByText('This chat has ended.')).toBeInTheDocument();
    expect(within(messages).getByText('This conversation was closed after a quiet while.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'New chat' })).toBeInTheDocument();
  });

  it('shows a message a person handles (202) without an answer, and staff replies from the live stream', async () => {
    const user = userEvent.setup();
    const resumed = resumedConversation(
      [visitorMessage(1, 'visitor', 'Returns?'), visitorMessage(2, 'staff', 'Hello, Dana here.', { authorName: 'Dana' })],
      { status: 'human' },
    );
    const fetchMock = mockFetch(
      routes(
        {
          ...resumed.routes,
          [`POST ${WIDGET_API}/messages`]: () => jsonResponse(visitorMessage(3, 'visitor', 'My order is late'), 202),
        },
        {},
        { conversationId: 'c1' },
      ),
    );
    renderWidget();

    expect(await screen.findByText('You are chatting with Dana.')).toBeInTheDocument();
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'My order is late{Enter}');

    expect(await screen.findByText('My order is late')).toBeInTheDocument();
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/messages`)).toEqual([{ message: 'My order is late', conversationId: 'c1' }]);
    expect(screen.queryByRole('img', { name: 'Writing an answer' })).not.toBeInTheDocument();

    await waitFor(() => expect(resumed.live.isOpen).toBe(true));
    sendFromHost('close');
    resumed.live.send('message', visitorMessage(3, 'visitor', 'My order is late'), 3);
    resumed.live.send('message', visitorMessage(4, 'staff', 'Let me check that for you.', { authorName: 'Dana' }), 4);

    expect(await screen.findByText('Let me check that for you.')).toBeInTheDocument();
    expect(screen.getAllByText('My order is late')).toHaveLength(1);
    await waitFor(() => expect(hostMessages()).toContainEqual({ source: 'knowledge-agent', type: 'unread', count: 1 }));
    expect(document.querySelector('[aria-live="polite"]')).toHaveTextContent('Dana: Let me check that for you.');

    sendFromHost('open');
    await waitFor(() => expect(hostMessages()).toContainEqual({ source: 'knowledge-agent', type: 'unread', count: 0 }));
  });

  it("ignores the widget editor's messages and marks nothing for it: only /widget/v1/preview is the editor's", async () => {
    mockFetch(routes());
    renderWidget();
    await screen.findByRole('heading', { level: 1, name: 'Shop help' });

    const render = envelope({ type: 'render', config: widgetConfig({ title: 'Draft title' }), scene: 'start', colorScheme: 'dark' });
    window.dispatchEvent(new MessageEvent('message', { data: render, origin: window.location.origin, source: window }));
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(screen.getByRole('heading', { level: 1, name: 'Shop help' })).toBeInTheDocument();
    expect(document.querySelector('[data-ka-edit]')).toBeNull();
  });

  it('shows the rate limit texts on 429 and keeps the message to send again', async () => {
    const user = userEvent.setup();
    let reply = problem(429, { title: 'Too many requests', code: 'rate_limited' }, { 'Retry-After': '30' });
    mockFetch(routes({ [`POST ${WIDGET_API}/messages`]: () => reply }, { texts: { rateLimited: 'Slow down a little.' } }));
    renderWidget();

    const input = await screen.findByRole('textbox', { name: 'Message' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeEnabled());
    await user.type(input, 'Hello{Enter}');

    expect(await screen.findByRole('alert')).toHaveTextContent('Slow down a little.');
    await waitFor(() => expect(input).toHaveValue('Hello'));
    expect(screen.queryByText('Hello', { selector: 'p' })).not.toBeInTheDocument();

    reply = problem(429, { title: 'Too many requests', code: 'daily_cap_reached' }, { 'Retry-After': '3600' });
    await user.type(input, '{Enter}');
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('This assistant has answered all it can for today. Please come back tomorrow.'),
    );
  });

  it('says when the previous answer is still being written (409 conversation_busy)', async () => {
    const user = userEvent.setup();
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answerWithSources]);
    mockFetch(
      routes(
        { ...resumed.routes, [`POST ${WIDGET_API}/messages`]: () => problem(409, { title: 'Busy', code: 'conversation_busy' }) },
        {},
        { conversationId: 'c1' },
      ),
    );
    renderWidget();

    const input = await screen.findByRole('textbox', { name: 'Message' });
    await screen.findByText('Returns?', { selector: 'p' });
    await user.type(input, 'And exchanges?{Enter}');

    expect(await screen.findByRole('alert')).toHaveTextContent('The previous answer is still being written. Try again in a moment.');
    await waitFor(() => expect(input).toHaveValue('And exchanges?'));
  });

  it('starts a new conversation when the open one was closed meanwhile (409 conversation_closed)', async () => {
    const user = userEvent.setup();
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answerWithSources]);
    let posts = 0;
    const fetchMock = mockFetch(
      routes(
        {
          ...resumed.routes,
          [`POST ${WIDGET_API}/messages`]: () =>
            ++posts === 1
              ? problem(409, { title: 'Closed', code: 'conversation_closed' })
              : sseResponse([
                  { event: 'conversation', data: { conversationId: 'c2', title: 'New conversation', isNew: true } },
                  { event: 'text-delta', data: { text: 'Exchanges take a week.' } },
                  { event: 'done', data: { conversationId: 'c2', turnId: 't2', finishReason: 'stop' } },
                ]),
          [`GET ${WIDGET_API}/conversations/c2`]: visitorConversation(
            [visitorMessage(1, 'visitor', 'And exchanges?'), visitorMessage(2, 'assistant', 'Exchanges take a week.', { id: 'answer-2' })],
            { id: 'c2' },
          ),
          [`GET ${WIDGET_API}/conversations/c2/events`]: (_url: URL, init: RequestInit) => controlledEventStream().respond(init),
        },
        {},
        { conversationId: 'c1' },
      ),
    );
    renderWidget();

    const input = await screen.findByRole('textbox', { name: 'Message' });
    await screen.findByText('Returns?', { selector: 'p' });
    await user.type(input, 'And exchanges?{Enter}');

    expect(await screen.findByText('Exchanges take a week.')).toBeInTheDocument();
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/messages`)).toEqual([
      { message: 'And exchanges?', conversationId: 'c1' },
      { message: 'And exchanges?' },
    ]);
    expect(screen.queryByText('Returns?', { selector: 'p' })).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('starts a new visitor session when the token is refused (401) and sends again once', async () => {
    const user = userEvent.setup();
    let posts = 0;
    let sessions = 0;
    const fetchMock = mockFetch(
      routes({
        [`POST ${WIDGET_API}/sessions`]: () => jsonResponse(visitorSession({ token: ++sessions === 1 ? 'old-token' : 'new-token' })),
        [`POST ${WIDGET_API}/messages`]: () =>
          ++posts === 1
            ? problem(401, { title: 'Session expired' })
            : sseResponse([
                { event: 'text-delta', data: { text: 'Hello there.' } },
                { event: 'done', data: { conversationId: '', turnId: 't1', finishReason: 'stop' } },
              ]),
      }),
    );
    renderWidget();

    const input = await screen.findByRole('textbox', { name: 'Message' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeEnabled());
    await user.type(input, 'Hi{Enter}');

    expect(await screen.findByText('Hello there.')).toBeInTheDocument();
    expect(requestHeaders(fetchMock, `POST ${WIDGET_API}/messages`).map((headers) => headers.Authorization)).toEqual([
      'Bearer old-token',
      'Bearer new-token',
    ]);
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/sessions`)).toEqual([{}, { token: 'old-token' }]);
  });

  it('asks for name and email before the first message when the widget wants them first', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch(
      routes({ [`POST ${WIDGET_API}/contact`]: { name: 'Ada', email: 'ada@example.com' } }, { contactCapture: 'before_chat' }),
    );
    renderWidget();

    const form = await screen.findByRole('form', { name: 'How can we reach you?' });
    expect(screen.queryByRole('textbox', { name: 'Message' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Can I return items?' })).not.toBeInTheDocument();
    await user.type(within(form).getByLabelText('Name'), 'Ada');
    await user.type(within(form).getByLabelText('Email'), 'ada@example.com');
    await user.click(within(form).getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('textbox', { name: 'Message' })).toBeInTheDocument();
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/contact`)).toEqual([{ name: 'Ada', email: 'ada@example.com' }]);
    expect(requestHeaders(fetchMock, `POST ${WIDGET_API}/contact`)[0]).toMatchObject({ 'X-KA-Request': '1', Authorization: 'Bearer visitor-token' });
  });

  it('ends the chat from the menu, offers a 1 to 5 rating and starts a new chat', async () => {
    const user = userEvent.setup();
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answerWithSources]);
    const fetchMock = mockFetch(
      routes(
        {
          ...resumed.routes,
          [`POST ${WIDGET_API}/conversations/c1/close`]: () => new Response(null, { status: 204 }),
          [`POST ${WIDGET_API}/conversations/c1/feedback`]: { id: 'f2', messageId: null, rating: 4 },
        },
        {},
        { conversationId: 'c1' },
      ),
    );
    renderWidget();

    await screen.findByText('Returns?', { selector: 'p' });
    await user.click(screen.getByRole('button', { name: 'More options' }));
    await user.click(await screen.findByRole('menuitem', { name: 'End chat' }));

    expect(await screen.findByText('This chat has ended.')).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Message' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '4 out of 5' }));
    expect(await screen.findByText('Thanks for your feedback.')).toBeInTheDocument();
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/conversations/c1/feedback`)).toEqual([{ rating: 4 }]);
    expect(screen.getByRole('button', { name: '4 out of 5' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'New chat' }));
    expect(await screen.findByRole('textbox', { name: 'Message' })).toBeInTheDocument();
    expect(screen.queryByText('Returns?', { selector: 'p' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Can I return items?' })).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/close'))).toHaveLength(1);
  });

  it('tells the host to close on the close button and on Escape', async () => {
    const user = userEvent.setup();
    mockFetch(routes());
    renderWidget();

    await user.click(await screen.findByRole('button', { name: 'Close chat' }));
    expect(hostMessages()).toContainEqual({ source: 'knowledge-agent', type: 'close' });
    posted.mockClear();

    await user.keyboard('{Escape}');
    expect(hostMessages()).toEqual([{ source: 'knowledge-agent', type: 'close' }]);
  });

  it('closes only the menu on Escape while the menu is open', async () => {
    const user = userEvent.setup();
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answerWithSources]);
    mockFetch(routes(resumed.routes, {}, { conversationId: 'c1' }));
    renderWidget();

    await screen.findByText('Returns?', { selector: 'p' });
    await user.click(screen.getByRole('button', { name: 'More options' }));
    expect(await screen.findByRole('menuitem', { name: 'New chat' })).toHaveFocus();
    posted.mockClear();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'More options' })).toHaveFocus();
    expect(hostMessages()).toEqual([]);
  });

  it('shows an offered person and the arguments of an action to confirm, and asks for the person on its button', async () => {
    const user = userEvent.setup();
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answerWithSources]);
    const fetchMock = mockFetch(
      routes(
        {
          ...resumed.routes,
          [`POST ${WIDGET_API}/messages`]: () =>
            sseResponse([
              { event: 'handover-offered', data: { callId: 'h1' } },
              {
                event: 'confirmation-required',
                data: {
                  callId: 'm1',
                  toolName: '',
                  title: 'Refund order',
                  connection: 'Shop',
                  expiresAt: '2099-01-01T00:00:00Z',
                  details: [
                    { label: 'order_id', value: '1042' },
                    { label: 'amount', value: '20 EUR' },
                  ],
                },
              },
              { event: 'text-delta', data: { text: 'Press the button if you want a colleague.' } },
              { event: 'done', data: { conversationId: 'c1', turnId: 't2', finishReason: 'stop' } },
            ]),
          [`POST ${WIDGET_API}/conversations/c1/handover`]: { status: 'handover_pending', started: true },
        },
        { handoverEnabled: true },
        { conversationId: 'c1' },
      ),
    );
    renderWidget();

    await screen.findByText('Returns?', { selector: 'p' });
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'I want my money back{Enter}');

    const action = await screen.findByRole('group', { name: 'Refund order' });
    expect(within(action).getByText('order_id:')).toBeInTheDocument();
    expect(within(action).getByText('1042')).toBeInTheDocument();
    expect(within(action).getByText('20 EUR')).toBeInTheDocument();
    const offer = screen.getByRole('group', { name: 'Want someone from the team to take over?' });
    expect(screen.getAllByRole('button', { name: 'Talk to a person' })).toHaveLength(1);
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/conversations/c1/handover`)).toEqual([]);

    await user.click(within(offer).getByRole('button', { name: 'Talk to a person' }));

    expect(await screen.findByText('Waiting for someone from the team to join.')).toBeInTheDocument();
    expect(requestBodies(fetchMock, `POST ${WIDGET_API}/conversations/c1/handover`)).toEqual([{}]);
    expect(screen.queryByRole('group', { name: 'Want someone from the team to take over?' })).not.toBeInTheDocument();
  });

  it('links only to the hosts the widget allows and shows other addresses as text', async () => {
    const answer = visitorMessage(
      2,
      'assistant',
      'Fill in [the form](https://evil.test/f?d=1) or read [our returns page](https://www.shop.example/returns).',
      {
        id: 'answer-1',
      },
    );
    const resumed = resumedConversation([visitorMessage(1, 'visitor', 'Returns?'), answer]);
    mockFetch(routes(resumed.routes, { linkHosts: ['shop.example'] }, { conversationId: 'c1' }));
    renderWidget();

    const allowed = await screen.findByRole('link', { name: /our returns page/ });
    expect(allowed).toHaveAttribute('href', 'https://www.shop.example/returns');
    expect(screen.queryByRole('link', { name: /the form/ })).not.toBeInTheDocument();
    expect(screen.getByText('the form (https://evil.test/f?d=1)')).toBeInTheDocument();
  });

  it('says when the widget does not exist', async () => {
    mockFetch({ [`GET ${WIDGET_API}/config`]: () => problem(404, { title: 'Not found', detail: 'This chat is not available.' }) });
    renderWidget();

    expect(await screen.findByText('This chat is not available.')).toBeInTheDocument();
  });
});
