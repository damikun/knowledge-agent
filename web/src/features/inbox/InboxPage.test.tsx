import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, useLocation } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { agentDto, agentsRoutes, builtInAgent } from '../../test/agentsApi';
import { currentUser, supportUser } from '../../test/apiFixtures';
import { evalDatasetList } from '../../test/evalsApi';
import { messageDto } from '../../test/fixtures';
import { countsDto, handoverDto, searchItem, WIDGET_CONVERSATION_ID, widgetConversation } from '../../test/inboxApi';
import { labelDto, labelList, PROBLEM_LABEL_ID, QUESTION_LABEL_ID } from '../../test/labelsApi';
import { controlledEventStream, fetchCalls, jsonResponse, mockFetch, renderWithProviders, requestUrls } from '../../test/utils';
import { widgetDto, widgetGuid, widgetsRoutes } from '../../test/widgetsApi';
import { createTurn } from '../chat/chatReducer';
import { ConversationsRedirect } from './ConversationsRedirect';
import { readInboxState, toSearchQuery } from './inboxState';
import { inboxTimeline } from './inboxTimeline';
import { InboxPage } from './InboxPage';
import { tabsThatFit } from './views/ViewTabs';
import type { InboxConversationDetail } from './types';

/** + Filter, the kind, then its value in the popover. */
async function addFilter(user: ReturnType<typeof userEvent.setup>, kind: string, value?: string) {
  await user.click(screen.getByRole('button', { name: 'Add filter' }));
  await user.click(await screen.findByRole('menuitem', { name: kind }));
  if (value) {
    await user.click(within(await screen.findByRole('dialog', { name: kind })).getByRole('button', { name: value }));
  }
}

afterEach(() => {
  vi.unstubAllGlobals();
});

const SEARCH = 'GET /api/conversations/search';
const COUNTS = 'GET /api/conversations/counts';
const DETAIL = `GET /api/conversations/${WIDGET_CONVERSATION_ID}`;
const EVENTS = `${DETAIL}/events`;
const HANDOVER = `${DETAIL}/handover`;
const HISTORY = `${DETAIL}/history`;
const POST = (action: string) => `POST /api/conversations/${WIDGET_CONVERSATION_ID}/${action}`;

const waiting = searchItem();
const withAssistant = searchItem({
  id: '0199c1e2-0000-7000-8000-00000000d002',
  title: 'Opening hours',
  status: 'ai',
  preview: 'We open at nine.',
  widgetId: widgetGuid(2),
  handoverStatus: null,
});

function Location() {
  const location = useLocation();
  return <output data-testid="location">{`${location.pathname}${location.search}`}</output>;
}

function renderInbox(route = '/inbox') {
  return renderWithProviders(
    <>
      <InboxPage />
      <Location />
    </>,
    { route, path: '/inbox/:conversationId?' },
  );
}

const address = () => new URL(screen.getByTestId('location').textContent ?? '', 'http://localhost');

function body(fetchMock: ReturnType<typeof mockFetch>, route: string): unknown {
  const [method, path] = route.split(' ');
  const call = fetchMock.mock.calls
    .filter(([input, init]) => new URL(String(input), 'http://localhost').pathname === path && (init as RequestInit).method === method)
    .at(-1);
  return JSON.parse((call![1] as RequestInit).body as string);
}

const baseRoutes = {
  [SEARCH]: { items: [waiting, withAssistant], nextCursor: null },
  [COUNTS]: countsDto({ handoverPending: 3, human: 1, ai: 7, closed: 4, all: 15, labels: [{ labelId: QUESTION_LABEL_ID, count: 5 }] }),
  ...widgetsRoutes(() => [widgetDto(), widgetDto({ id: widgetGuid(2), name: 'Docs help' })]),
  ...agentsRoutes(() => [builtInAgent, agentDto()]),
};

function setup(
  getConversation: () => InboxConversationDetail,
  extra: Record<string, unknown> = {},
  route = `/inbox?view=all&c=${WIDGET_CONVERSATION_ID}`,
) {
  const events = controlledEventStream();
  const fetchMock = mockFetch({
    ...baseRoutes,
    [DETAIL]: () => jsonResponse(getConversation()),
    [EVENTS]: (_url: URL, init: RequestInit) => events.respond(init),
    [HANDOVER]: handoverDto(),
    [HISTORY]: { feedback: [], handovers: [handoverDto()], labels: [] },
    ...extra,
  });
  renderInbox(route);
  return { events, fetchMock };
}

describe('InboxPage views and list', () => {
  it('shows the views with their counts and lists a view, marking who waits', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({ ...baseRoutes, 'GET /api/labels': labelList() });
    renderInbox();

    const views = await screen.findByRole('tablist', { name: 'Views' });
    const waitingView = within(views).getByRole('tab', { name: /Waiting for a person/ });
    expect(await within(waitingView).findByText('3')).toBeInTheDocument();
    expect(within(views).getByRole('tab', { name: /^All/ })).toHaveAttribute('aria-selected', 'true');
    expect(waitingView).toHaveAttribute('aria-controls', screen.getByRole('complementary', { name: 'Inbox conversations' }).id);

    const list = await screen.findByRole('list', { name: 'Conversations' });
    const links = within(list).getAllByRole('link');
    expect(links[0]).toHaveTextContent('Return an order');
    expect(links[0]).toHaveTextContent('Waiting for a person');
    expect(links[0]).toHaveAttribute('href', `/inbox?view=all&c=${WIDGET_CONVERSATION_ID}`);
    expect(await within(links[0]!).findByText('Shop help')).toBeInTheDocument();
    expect(links[1]).toHaveTextContent('Assistant');
    expect(within(links[1]!).getByText('Docs help')).toBeInTheDocument();
    expect(screen.getByText('Select a conversation')).toBeInTheDocument();
    const first = requestUrls(fetchMock, SEARCH)[0]!.searchParams;
    expect([first.get('status'), first.get('channel'), first.get('includeTests'), first.get('limit')]).toEqual([null, null, null, '50']);

    await user.click(waitingView);
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('status')).toBe('handover_pending'));
    expect(address().searchParams.get('view')).toBe('waiting');
    expect(waitingView).toHaveAttribute('aria-selected', 'true');
    waitingView.focus();
    await user.keyboard('{ArrowRight}');
    expect(within(views).getByRole('tab', { name: /^Unassigned/ })).toHaveFocus();

    await addFilter(user, 'Label', 'Question');
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('labelId')).toBe(QUESTION_LABEL_ID));
    expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('status')).toBe('handover_pending');
    expect(address().searchParams.get('label')).toBe(QUESTION_LABEL_ID);
    await user.click(within(screen.getByRole('list', { name: 'Active filters' })).getByRole('button', { name: 'Remove filter: Label: Question' }));
    await waitFor(() => expect(address().searchParams.get('label')).toBeNull());
  });

  it('searches what was said, with the filters in the address and the words marked', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...baseRoutes,
      [SEARCH]: (url: URL) =>
        jsonResponse({
          items: [searchItem({ match: url.searchParams.get('q') ? 'Can I return shoes I wore once?' : null, rating: 2, thumbsDown: 1 })],
          nextCursor: null,
        }),
    });
    renderInbox('/inbox?view=waiting');

    const list = await screen.findByRole('list', { name: 'Conversations' });
    expect(within(list).getByText('of 5', { exact: false })).toBeInTheDocument();
    expect(within(list).getByText('answers rated not helpful', { exact: false })).toBeInTheDocument();

    await user.type(screen.getByRole('searchbox', { name: 'Search conversations' }), 'shoes{Enter}');
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).some((url) => url.searchParams.get('q') === 'shoes')).toBe(true));
    expect((await screen.findAllByText('shoes', { selector: 'mark' })).length).toBeGreaterThan(0);
    expect(address().searchParams.get('q')).toBe('shoes');

    await addFilter(user, 'Rating', '1 or 2 stars');
    await addFilter(user, 'Thumbs down');
    await addFilter(user, 'Widget', 'Docs help');
    await waitFor(() => {
      const last = requestUrls(fetchMock, SEARCH).at(-1)!.searchParams;
      expect([last.get('maxRating'), last.get('thumbsDown'), last.get('q'), last.get('status'), last.get('widgetId')]).toEqual([
        '2',
        'true',
        'shoes',
        'handover_pending',
        widgetGuid(2),
      ]);
    });
    await waitFor(() => expect(requestUrls(fetchMock, COUNTS).at(-1)!.searchParams.get('widgetId')).toBe(widgetGuid(2)));
    expect([address().searchParams.get('rating'), address().searchParams.get('thumbs')]).toEqual(['poor', 'down']);
    const chips = screen.getByRole('list', { name: 'Active filters' });
    expect(within(chips).getAllByRole('listitem')).toHaveLength(3);
    expect(screen.getByRole('button', { name: 'Clear' })).toBeInTheDocument();

    await user.click(within(chips).getByRole('button', { name: 'Change filter: Rating: 1-2' }));
    await user.click(within(await screen.findByRole('dialog', { name: 'Rating' })).getByRole('button', { name: '4 or 5 stars' }));
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('minRating')).toBe('4'));

    await addFilter(user, 'Channel', 'Playground tests');
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('channel')).toBe('portal'));
    await user.click(screen.getByRole('button', { name: 'Sort: Latest activity' }));
    await user.click(await screen.findByRole('menuitem', { name: /Newest first/ }));
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('sort')).toBe('created'));
  });

  it('loads the next page of a view with the cursor', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...baseRoutes,
      [SEARCH]: (url: URL) =>
        jsonResponse(
          url.searchParams.get('cursor') === 'next-1'
            ? { items: [searchItem({ id: 'c2', title: 'Second page' })], nextCursor: null }
            : { items: [searchItem()], nextCursor: 'next-1' },
        ),
    });
    renderInbox();

    await screen.findByRole('link', { name: /Return an order/ });
    await user.click(screen.getByRole('button', { name: 'Show more' }));
    expect(await screen.findByRole('link', { name: /Second page/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Return an order/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Show more' })).toBeNull();
    expect(requestUrls(fetchMock, SEARCH).map((url) => url.searchParams.get('cursor'))).toContain('next-1');
  });

  it('says when nothing matches and clears the search, and says when nobody waits', async () => {
    const user = userEvent.setup();
    mockFetch({ ...baseRoutes, [SEARCH]: { items: [], nextCursor: null } });
    renderInbox('/inbox?view=waiting&q=invoice');

    expect(await screen.findByText('No conversations match')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Clear search and filters' }));
    expect(await screen.findByText('Nobody is waiting')).toBeInTheDocument();
    expect(address().searchParams.get('q')).toBeNull();
  });

  it('shows support users widget ids, no widget filter and no way to add to datasets', async () => {
    const user = userEvent.setup();
    const { fetchMock } = setup(() => widgetConversation(), { 'GET /api/auth/me': supportUser });

    const list = await screen.findByRole('list', { name: 'Conversations' });
    expect(within(list).getAllByRole('link')[0]).toHaveTextContent(/Widget 0199c1e2…0001/);
    await user.click(screen.getByRole('button', { name: 'Add filter' }));
    expect(await screen.findByRole('menuitem', { name: 'Contact' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Widget' })).toBeNull();
    expect(screen.queryByRole('menuitem', { name: 'Segment' })).toBeNull();
    await user.keyboard('{Escape}');
    const view = screen.getByRole('region', { name: 'Conversation' });
    await within(view).findByRole('article', { name: 'Assistant answer' });
    expect(within(view).queryByRole('button', { name: 'Add to dataset' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Manage labels' })).toBeNull();
    expect(fetchCalls(fetchMock)).not.toContain('GET /api/widgets');
  });

  it('opens old links: /inbox/{id} from emails and the Conversations page', async () => {
    setup(() => widgetConversation(), {}, `/inbox/${WIDGET_CONVERSATION_ID}?status=closed`);
    expect(await screen.findByRole('heading', { level: 2, name: 'Return an order' })).toBeInTheDocument();
    expect(address().pathname).toBe('/inbox');
    expect([address().searchParams.get('c'), address().searchParams.get('view')]).toEqual([WIDGET_CONVERSATION_ID, 'closed']);
  });

  it('turns a Conversations link into the same Inbox search', async () => {
    mockFetch(baseRoutes);
    renderWithProviders(<ConversationsRedirect />, {
      route: `/conversations?q=shoes&rating=poor&open=${WIDGET_CONVERSATION_ID}`,
      path: '/conversations',
      extraRoutes: <Route path="/inbox" element={<Location />} />,
    });
    const target = new URL((await screen.findByTestId('location')).textContent ?? '', 'http://localhost');
    expect(target.pathname).toBe('/inbox');
    expect(Object.fromEntries(target.searchParams)).toEqual({ view: 'all', q: 'shoes', rating: 'poor', c: WIDGET_CONVERSATION_ID });
  });
});

describe('InboxPage conversation', () => {
  it('shows the conversation with the request for a person and the tool calls of the answer', async () => {
    const user = userEvent.setup();
    setup(() => widgetConversation());

    const view = await screen.findByRole('region', { name: 'Conversation' });
    expect(await within(view).findByRole('heading', { level: 2, name: 'Return an order' })).toBeInTheDocument();
    expect(within(view).getByRole('heading', { level: 3, name: 'Jane' })).toBeInTheDocument();
    const messages = within(view).getByRole('list', { name: 'Messages' });
    expect(within(messages).getByText('Can I return shoes I wore once?')).toBeInTheDocument();
    expect(within(messages).getByRole('article', { name: 'Assistant answer' })).toHaveTextContent('Worn shoes cannot be returned');
    expect(within(messages).getByText(/asked a colleague to join/)).toBeInTheDocument();

    const panel = await within(view).findByRole('region', { name: 'Request for a person' });
    expect(panel).toHaveTextContent('Waiting');
    expect(panel).toHaveTextContent('The visitor asked for a person');
    expect(panel).toHaveTextContent('Sent by email');
    expect(panel).toHaveTextContent('Wants to return worn shoes');

    const tool = within(messages).getByRole('button', { name: /Searched the knowledge base/ });
    await user.click(tool);
    expect(tool).toHaveAttribute('aria-expanded', 'true');

    const details = within(view).getByRole('button', { name: 'Details' });
    expect(details).toHaveAttribute('aria-expanded', 'true');
    await user.click(details);
    expect(within(view).queryByRole('complementary', { name: 'Conversation details' })).toBeNull();
    expect(localStorage.getItem('inbox.details')).toBe('hidden');
    await user.click(details);
    expect(within(view).getByRole('complementary', { name: 'Conversation details' })).toBeInTheDocument();
  });

  it('takes the conversation over and replies to the visitor', async () => {
    const user = userEvent.setup();
    let conversation = widgetConversation();
    let handover = handoverDto();
    const { fetchMock } = setup(() => conversation, {
      [HANDOVER]: () => jsonResponse(handover),
      [POST('join')]: () => {
        conversation = { ...conversation, status: 'human' };
        handover = handoverDto({ status: 'active', assignee: 'Ada', acceptedAt: '2026-10-31T09:06:00Z' });
        return jsonResponse(handover);
      },
      [POST('messages')]: (_url: URL, init: RequestInit) => {
        const { text } = JSON.parse(init.body as string) as { text: string };
        const reply = messageDto({ sequence: 5, author: 'staff', authorName: 'Ada', text, turnId: null });
        conversation = { ...conversation, messages: [...conversation.messages, reply] };
        return jsonResponse(reply);
      },
    });

    const view = await screen.findByRole('region', { name: 'Conversation' });
    expect(await within(view).findByText('Sending a reply takes the conversation over from the assistant.')).toBeInTheDocument();
    await user.click(within(view).getByRole('button', { name: 'Take over' }));
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain(POST('join')));
    expect(await within(view).findByRole('button', { name: 'Back to the assistant' })).toBeInTheDocument();
    expect(within(view).queryByRole('button', { name: 'Take over' })).toBeNull();
    expect(within(view).getByRole('region', { name: 'Request for a person' })).toHaveTextContent('Ada');
    await waitFor(() => expect(requestUrls(fetchMock, COUNTS).length).toBeGreaterThan(1));

    const reply = within(view).getByRole('textbox', { name: 'Reply' });
    await user.type(reply, 'Hi Jane, I can help with that.{Enter}');
    expect(await within(view).findByText('Hi Jane, I can help with that.')).toBeInTheDocument();
    expect(body(fetchMock, POST('messages'))).toEqual({ text: 'Hi Jane, I can help with that.' });
    expect(reply).toHaveValue('');
    expect(within(view).getByText('Ada (staff)')).toBeInTheDocument();
  });

  it('shows a visitor message as it arrives and reads it out, but not the messages already there', async () => {
    const { events, fetchMock } = setup(() => widgetConversation({ status: 'human' }));
    const announced = () => screen.getAllByRole('status').map((region) => region.textContent);

    await screen.findByText('Can I return shoes I wore once?');
    expect(announced().some((text) => text?.includes('Can I return shoes'))).toBe(false);
    await waitFor(() => expect(events.isOpen).toBe(true));
    expect(requestUrls(fetchMock, EVENTS)[0]!.searchParams.get('after')).toBe('3');

    events.send('message', messageDto({ sequence: 4, author: 'visitor', authorName: 'Jane', text: 'Are you still there?', turnId: null }), 4);
    expect(await screen.findByText('Are you still there?')).toBeInTheDocument();
    await waitFor(() => expect(announced()).toContain('Jane: Are you still there?'));
  });

  it('marks a request nobody joined in time as missed, so the team can follow up even after the chat closed', async () => {
    const user = userEvent.setup();
    let conversation = widgetConversation({ status: 'closed' });
    const expired = handoverDto({ status: 'expired', channel: 'portal', resolvedAt: '2026-10-31T09:20:00Z' });
    const { fetchMock } = setup(() => conversation, {
      [SEARCH]: { items: [searchItem({ status: 'closed', handoverStatus: 'expired' }), withAssistant], nextCursor: null },
      [HANDOVER]: expired,
      [HISTORY]: { feedback: [], handovers: [expired], labels: [] },
      [POST('messages')]: (_url: URL, init: RequestInit) => {
        const { text } = JSON.parse(init.body as string) as { text: string };
        const reply = messageDto({ sequence: 4, author: 'staff', authorName: 'Ada', text, turnId: null });
        conversation = { ...conversation, status: 'human', messages: [...conversation.messages, reply] };
        return jsonResponse(reply);
      },
    });

    const list = await screen.findByRole('list', { name: 'Conversations' });
    const [missedLink, otherLink] = within(list).getAllByRole('link');
    expect(missedLink).toHaveTextContent('Missed request');
    expect(otherLink).not.toHaveTextContent('Missed request');

    const view = await screen.findByRole('region', { name: 'Conversation' });
    const panel = await within(view).findByRole('region', { name: 'Request for a person' });
    expect(panel).toHaveTextContent('Nobody joined in time.');
    expect(await within(within(view).getByRole('list', { name: 'Messages' })).findByText(/nobody joined in time/)).toBeInTheDocument();
    expect(within(view).queryByText('This conversation is closed. The visitor can start a new one.')).toBeNull();
    expect(within(view).getByText(/Sending a reply opens the conversation again/)).toBeInTheDocument();

    await user.type(within(view).getByRole('textbox', { name: 'Reply' }), 'Sorry for the wait, Jane.{Enter}');
    expect(await within(view).findByText('Sorry for the wait, Jane.')).toBeInTheDocument();
    expect(body(fetchMock, POST('messages'))).toEqual({ text: 'Sorry for the wait, Jane.' });
  });

  it('hands the conversation back to the assistant and closes it after confirmation', async () => {
    const user = userEvent.setup();
    let conversation = widgetConversation({ status: 'human' });
    const { fetchMock } = setup(() => conversation, {
      [POST('return')]: () => {
        conversation = { ...conversation, status: 'ai' };
        return new Response(null, { status: 204 });
      },
      [POST('close')]: () => {
        conversation = { ...conversation, status: 'closed' };
        return new Response(null, { status: 204 });
      },
    });

    const view = await screen.findByRole('region', { name: 'Conversation' });
    await user.click(await within(view).findByRole('button', { name: 'Back to the assistant' }));
    expect(await within(view).findByRole('button', { name: 'Take over' })).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain(POST('return'));

    await user.click(within(view).getByRole('button', { name: 'Close' }));
    const confirm = await screen.findByRole('dialog', { name: 'Close conversation?' });
    await user.click(within(confirm).getByRole('button', { name: 'Close conversation' }));
    expect(await within(view).findByText('This conversation is closed. The visitor can start a new one.')).toBeInTheDocument();
    expect(within(view).queryByRole('textbox', { name: 'Reply' })).toBeNull();
    expect(fetchCalls(fetchMock)).toContain(POST('close'));
  });

  it('shows the ratings in the thread and adds an answer to a dataset with its question', async () => {
    const user = userEvent.setup();
    const answerId = widgetConversation().messages[1]!.id;
    setup(() => widgetConversation({ status: 'closed' }), {
      [HANDOVER]: () => new Response(null, { status: 204 }),
      [HISTORY]: {
        feedback: [
          {
            id: 'f1',
            messageId: null,
            kind: 'conversation',
            rating: 2,
            comment: 'Not what I hoped.',
            reasons: [],
            author: 'visitor',
            createdAt: '2026-10-31T09:06:00Z',
          },
          {
            id: 'f2',
            messageId: answerId,
            kind: 'answer',
            rating: -1,
            comment: null,
            reasons: [],
            author: 'visitor',
            createdAt: '2026-10-31T09:01:00Z',
          },
        ],
        handovers: [],
        labels: [],
      },
      'GET /api/evals/datasets': evalDatasetList(),
    });

    const view = await screen.findByRole('region', { name: 'Conversation' });
    const messages = await within(view).findByRole('list', { name: 'Messages' });
    expect(await within(messages).findByText('Rated not helpful')).toBeInTheDocument();
    expect(within(messages).getByText('The visitor rated the chat 2 of 5')).toBeInTheDocument();
    expect(within(messages).getByText('“Not what I hoped.”')).toBeInTheDocument();
    expect(within(view).getByText('This conversation is closed. The visitor can start a new one.')).toBeInTheDocument();

    await user.click(within(messages).getByRole('button', { name: 'Add to dataset' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add to a dataset' });
    expect(within(dialog).getByRole('textbox', { name: 'Question' })).toHaveValue('Can I return shoes I wore once?');
    expect(within(dialog).getByRole('textbox', { name: /Expected answer/ })).toHaveValue('Worn shoes cannot be returned [S1].');
  });

  it("narrows the list to one person from the address and links the conversation's person", async () => {
    const personId = '0199c1e2-0000-7000-8000-00000000ada1';
    const contact = { id: personId, name: 'Ada Lovelace', email: 'ada@example.com', identified: true };
    const { fetchMock } = setup(
      () => ({ ...widgetConversation(), contact }),
      {
        [SEARCH]: { items: [searchItem({ contact })], nextCursor: null },
        [`GET /api/people/${personId}`]: {
          id: personId,
          name: 'Ada Lovelace',
          identified: true,
          attributes: {},
          widgets: [],
          stats: { conversations: 1, handovers: 1, averageRating: null, ratings: 0 },
        },
      },
      `/inbox?view=all&person=${personId}&c=${WIDGET_CONVERSATION_ID}`,
    );

    const chips = await screen.findByRole('list', { name: 'Active filters' });
    expect(await within(chips).findByRole('button', { name: 'Remove filter: Contact: Ada Lovelace' })).toBeInTheDocument();
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)?.searchParams.get('contactId')).toBe(personId));
    expect(requestUrls(fetchMock, COUNTS).at(-1)?.searchParams.get('contactId')).toBe(personId);

    const person = await screen.findAllByRole('link', { name: 'Ada Lovelace' });
    expect(person[0]).toHaveAttribute('href', `/contacts/${personId}`);
    expect(screen.getByRole('link', { name: 'All conversations with this contact' })).toHaveAttribute(
      'href',
      `/inbox?person=${personId}&c=${WIDGET_CONVERSATION_ID}`,
    );

    await userEvent.click(within(chips).getByRole('button', { name: 'Remove filter: Contact: Ada Lovelace' }));
    await waitFor(() => expect(address().searchParams.get('person')).toBeNull());
  });

  it("opens a contact's deep link as the contact's list, with no view tab or count claiming the list", async () => {
    const user = userEvent.setup();
    const personId = '0199c1e2-0000-7000-8000-00000000ada1';
    const contact = { id: personId, name: 'Ada Lovelace', email: 'ada@example.com', identified: true };
    const { fetchMock } = setup(
      () => ({ ...widgetConversation(), contact }),
      {
        [SEARCH]: { items: [searchItem({ contact })], nextCursor: null },
        [`GET /api/people/${personId}`]: {
          id: personId,
          name: 'Ada Lovelace',
          identified: true,
          attributes: {},
          widgets: [],
          stats: { conversations: 1, handovers: 1, averageRating: null, ratings: 0 },
        },
      },
      `/inbox?person=${personId}&c=${WIDGET_CONVERSATION_ID}`,
    );

    expect(await screen.findByRole('heading', { level: 2, name: 'Return an order' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Remove filter: Contact: Ada Lovelace' })).toBeInTheDocument();
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)?.searchParams.get('contactId')).toBe(personId));
    const tabs = screen.getByRole('navigation', { name: 'Inbox views' });
    expect(within(tabs).queryByRole('tab', { selected: true })).toBeNull();
    expect(within(tabs).getByRole('tab', { name: 'All' })).toHaveTextContent(/^All$/);

    await user.click(within(tabs).getByRole('tab', { name: /Waiting for a person/ }));
    await waitFor(() => expect(address().searchParams.get('view')).toBe('waiting'));
    expect(address().searchParams.get('person')).toBe(personId);
    expect(within(tabs).getByRole('tab', { selected: true })).toHaveTextContent('Waiting for a person');
  });

  it('opens one conversation from a page without a list as the only one, until its chip is removed', async () => {
    const user = userEvent.setup();
    const { fetchMock } = setup(() => widgetConversation(), {}, `/inbox?only=${WIDGET_CONVERSATION_ID}`);

    expect(await screen.findByRole('heading', { level: 2, name: 'Return an order' })).toBeInTheDocument();
    const list = await screen.findByRole('list', { name: 'Conversations' });
    expect(within(list).getAllByRole('link')).toHaveLength(1);
    expect(within(list).getByRole('link')).toHaveAttribute('aria-current', 'page');
    expect(fetchCalls(fetchMock)).not.toContain(SEARCH);
    const tabs = screen.getByRole('navigation', { name: 'Inbox views' });
    expect(within(tabs).queryByRole('tab', { selected: true })).toBeNull();
    expect(within(tabs).getByRole('tab', { name: 'All' })).toHaveTextContent(/^All$/);

    await user.click(screen.getByRole('button', { name: 'Remove filter: Only this conversation' }));
    await waitFor(() => expect(Object.fromEntries(address().searchParams)).toEqual({ view: 'all', c: WIDGET_CONVERSATION_ID }));
    expect(await screen.findByRole('link', { name: /Opening hours/ })).toBeInTheDocument();
    expect(within(tabs).getByRole('tab', { selected: true })).toHaveTextContent(/^All/);
  });

  it('opens /inbox/{id} from emails, Home and Feedback as that conversation only', async () => {
    setup(() => widgetConversation(), {}, `/inbox/${WIDGET_CONVERSATION_ID}`);
    expect(await screen.findByRole('heading', { level: 2, name: 'Return an order' })).toBeInTheDocument();
    expect(address().pathname).toBe('/inbox');
    expect(Object.fromEntries(address().searchParams)).toEqual({ only: WIDGET_CONVERSATION_ID });
  });

  it('says a contact has no conversations when the views count none for them, without a channel filter', async () => {
    const personId = '0199c1e2-0000-7000-8000-000000001645';
    const fetchMock = mockFetch({
      ...baseRoutes,
      [SEARCH]: { items: [], nextCursor: null },
      [COUNTS]: countsDto({ handoverPending: 0, ai: 0, all: 0, unassigned: 0 }),
      [`GET /api/people/${personId}`]: {
        id: personId,
        name: 'Visitor 1645',
        identified: false,
        attributes: {},
        widgets: [],
        stats: { conversations: 0, handovers: 0, averageRating: null, ratings: 0 },
      },
    });
    renderInbox(`/inbox?view=all&person=${personId}`);

    expect(await screen.findByText('No conversations with this contact here')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Remove filter: Contact: Visitor 1645' })).toBeInTheDocument();
    for (const route of [SEARCH, COUNTS]) {
      const params = requestUrls(fetchMock, route).at(-1)!.searchParams;
      expect([params.get('contactId'), params.get('channel')]).toEqual([personId, null]);
    }
    expect(screen.queryByRole('button', { name: /Channel/ })).toBeNull();
  });

  it('filters by a segment once the API has some', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({ ...baseRoutes, 'GET /api/segments': [{ id: 'SEG-1', name: 'Returns' }] });
    renderInbox();

    await screen.findByRole('list', { name: 'Conversations' });
    await addFilter(user, 'Segment', 'Returns');
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('segmentId')).toBe('seg-1'));
    expect(address().searchParams.get('segment')).toBe('seg-1');
    expect(
      within(screen.getByRole('list', { name: 'Active filters' })).getByRole('button', { name: 'Change filter: Segment: Returns' }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Clear' })).toBeNull();
  });

  it('opens a playground test read only, with the way to the playground', async () => {
    setup(() => widgetConversation({ channel: 'portal', status: 'ai' }), { [HANDOVER]: () => new Response(null, { status: 204 }) });

    const view = await screen.findByRole('region', { name: 'Conversation' });
    expect(await within(view).findByRole('link', { name: 'Open it in the playground' })).toHaveAttribute(
      'href',
      `/playground/${WIDGET_CONVERSATION_ID}`,
    );
    expect(within(view).queryByRole('button', { name: 'Take over' })).toBeNull();
    expect(within(view).queryByRole('textbox', { name: 'Reply' })).toBeNull();
  });
});

describe('InboxPage pins', () => {
  it('pins from the header at once, lists pinned ones with the toggle and puts a pin back when the server refuses', async () => {
    const user = userEvent.setup();
    let refuse = false;
    const { fetchMock } = setup(() => widgetConversation(), {
      [COUNTS]: countsDto({ pinned: 2 }),
      [`PATCH /api/conversations/${WIDGET_CONVERSATION_ID}`]: async (_url: URL, init: RequestInit) => {
        await new Promise((resolve) => setTimeout(resolve, 300));
        return refuse
          ? jsonResponse({ title: 'Nope' }, 500)
          : jsonResponse({ ...searchItem(), pinned: (JSON.parse(String(init.body)) as { pinned: boolean }).pinned });
      },
    });

    expect(await screen.findByRole('tablist', { name: 'Views' })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /Pinned/ })).toBeNull();

    const view = await screen.findByRole('region', { name: 'Conversation' });
    await user.click(await within(view).findByRole('button', { name: 'Pin conversation' }));
    expect(await within(view).findByRole('button', { name: 'Unpin conversation' }, { timeout: 200 })).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Conversations' })).getByRole('link', { name: /^Pinned:\s*Return an order/ })).toBeInTheDocument();
    expect(fetchCalls(fetchMock).filter((call) => call.startsWith('PATCH'))).toHaveLength(1);
    await waitFor(() => expect(body(fetchMock, `PATCH /api/conversations/${WIDGET_CONVERSATION_ID}`)).toEqual({ pinned: true }));

    refuse = true;
    await user.click(within(screen.getByRole('list', { name: 'Conversations' })).getByRole('button', { name: 'Actions for Return an order' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Unpin' }));
    expect(await within(view).findByRole('button', { name: 'Pin conversation' }, { timeout: 200 })).toBeInTheDocument();
    expect(await within(view).findByRole('button', { name: 'Unpin conversation' })).toBeInTheDocument();
    expect(await within(view).findByText('That did not work')).toBeInTheDocument();

    const toggle = screen.getByRole('button', { name: 'Pinned only' });
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    await user.click(toggle);
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('pinned')).toBe('true'));
    expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('status')).toBeNull();
    expect(toggle).toHaveAttribute('aria-pressed', 'true');
    expect(address().searchParams.get('pinned')).toBe('1');
  });
});

describe('InboxPage assignment and unread', () => {
  const me = currentUser({ id: 'kc-ada' });
  const ana = { id: 'kc-ana', name: 'Ana Novak', email: 'ana@example.com', role: 'support' };

  it("puts the team's work first: Mine and Unassigned with their counts and what is unread", async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...baseRoutes,
      [COUNTS]: countsDto({ mine: 2, unassigned: 1, handoverPending: 3, unread: { mine: 1, handoverPending: 2 } }),
    });
    renderInbox();

    const views = await screen.findByRole('tablist', { name: 'Views' });
    expect(
      within(views)
        .getAllByRole('tab')
        .map((tab) =>
          tab.textContent
            ?.replace(/[\d,]+ unread|\d/g, '')
            .replace(/,/g, '')
            .trim(),
        ),
    ).toEqual(['Mine', 'Waiting for a person', 'Unassigned', 'Handled by AI', 'Closed', 'All']);
    const mine = within(views).getByRole('tab', { name: /^Mine/ });
    expect(await within(mine).findByText('1 unread')).toBeInTheDocument();
    const waitingTab = within(views).getByRole('tab', { name: /^Waiting for a person\s*3/ });
    expect(within(waitingTab).getByText('3')).toHaveClass('text-warning-text');
    expect(within(views).getByRole('tab', { name: /^Closed/ })).toHaveTextContent('0');
    const work = views;

    await user.click(mine);
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('assignee')).toBe('me'));
    expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('status')).toBe('ai,handover_pending,human');
    expect(address().searchParams.get('view')).toBe('mine');

    await user.click(within(work).getByRole('tab', { name: /^Unassigned/ }));
    await waitFor(() => expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('assignee')).toBe('none'));
    expect(requestUrls(fetchMock, SEARCH).at(-1)!.searchParams.get('status')).toBe('handover_pending,human');
  });

  it('marks unread rows, reads the open conversation, marks it unread again and assigns it', async () => {
    const user = userEvent.setup();
    const { fetchMock } = setup(() => widgetConversation({ assignee: { id: ana.id, name: ana.name } }), {
      'GET /api/auth/me': me,
      'GET /api/staff': [ana],
      [SEARCH]: { items: [searchItem({ unread: true, assignee: { id: ana.id, name: ana.name } })], nextCursor: null },
      [POST('read')]: { lastReadSequence: 3, unread: false },
      [`DELETE /api/conversations/${WIDGET_CONVERSATION_ID}/read`]: { lastReadSequence: 1, unread: true },
      [POST('assign')]: { assignee: { id: me.id, name: me.name } },
    });

    const list = await screen.findByRole('list', { name: 'Conversations' });
    const row = within(list).getByRole('link', { name: /^Unread:\s*Return an order/ });
    expect(row).toHaveTextContent('Assigned to Ana Novak');

    await waitFor(() => expect(fetchCalls(fetchMock)).toContain(POST('read')), { timeout: 3000 });
    expect(body(fetchMock, POST('read'))).toEqual({ sequence: 3 });
    await waitFor(() => expect(within(list).queryByRole('link', { name: /^Unread:/ })).toBeNull());

    await user.click(within(list).getByRole('button', { name: 'Actions for Return an order' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Mark as unread' }));
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain(`DELETE /api/conversations/${WIDGET_CONVERSATION_ID}/read`));

    const view = screen.getByRole('region', { name: 'Conversation' });
    await user.click(await within(view).findByRole('button', { name: 'Assigned to Ana Novak. Change' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Assign to me' }));
    await waitFor(() => expect(body(fetchMock, POST('assign'))).toEqual({ userId: 'kc-ada' }));
  });
});

describe('InboxPage labels', () => {
  it('shows labels on the conversations and sets them in the details, creating one', async () => {
    const user = userEvent.setup();
    const { fetchMock } = setup(() => widgetConversation(), {
      [SEARCH]: {
        items: [searchItem({ pinned: true, labels: [{ id: QUESTION_LABEL_ID, name: 'Question', color: 'blue', source: 'auto' }] }), withAssistant],
        nextCursor: null,
      },
      [HISTORY]: { feedback: [], handovers: [handoverDto()], labels: [{ id: QUESTION_LABEL_ID, name: 'Question', color: 'blue', source: 'auto' }] },
      'GET /api/labels': labelList(),
      [`PUT /api/conversations/${WIDGET_CONVERSATION_ID}/labels`]: (_url: URL, init: RequestInit) => {
        const ids = (JSON.parse(String(init.body)) as { labelIds: string[] }).labelIds;
        return jsonResponse(ids.map((id) => ({ id, name: id === PROBLEM_LABEL_ID ? 'Problem' : 'Billing', color: 'red', source: 'manual' })));
      },
      'POST /api/labels': () => jsonResponse(labelDto({ id: 'new-label', name: 'Billing', color: 'green', autoApply: false }), 201),
    });

    const list = await screen.findByRole('list', { name: 'Conversations' });
    expect(within(list).getByRole('link', { name: /^Pinned:/ })).toBeInTheDocument();
    expect(await within(list).findByText('Question')).toBeInTheDocument();
    const labels = await screen.findByRole('region', { name: 'Labels' });
    expect(await within(labels).findByText('Question')).toBeInTheDocument();

    await user.click(within(labels).getByRole('button', { name: 'Edit labels' }));
    const picker = await screen.findByRole('dialog', { name: 'Labels' });
    await user.click(await within(picker).findByRole('checkbox', { name: 'Problem' }));
    await waitFor(() =>
      expect(body(fetchMock, `PUT /api/conversations/${WIDGET_CONVERSATION_ID}/labels`)).toEqual({ labelIds: [QUESTION_LABEL_ID, PROBLEM_LABEL_ID] }),
    );

    await user.type(within(picker).getByRole('searchbox', { name: 'Find a label' }), 'Billing');
    await user.click(within(picker).getByRole('button', { name: 'Create label “Billing”' }));
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain('POST /api/labels'));
    await waitFor(() =>
      expect((body(fetchMock, `PUT /api/conversations/${WIDGET_CONVERSATION_ID}/labels`) as { labelIds: string[] }).labelIds).toContain('new-label'),
    );
  });

  it('lets admins manage labels from the filters', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...baseRoutes,
      'GET /api/labels': labelList(),
      [`PATCH /api/labels/${PROBLEM_LABEL_ID}`]: (_url: URL, init: RequestInit) =>
        jsonResponse(labelDto({ id: PROBLEM_LABEL_ID, ...(JSON.parse(String(init.body)) as object) })),
    });
    renderInbox();

    await addFilter(user, 'Label');
    await user.click(within(await screen.findByRole('dialog', { name: 'Label' })).getByRole('button', { name: 'Manage labels' }));
    const sheet = await screen.findByRole('dialog', { name: 'Labels' });
    await user.click(await within(sheet).findByRole('button', { name: 'Edit Problem' }));
    const form = within(sheet).getByRole('form', { name: 'Edit Problem' });
    await user.click(within(form).getByRole('radio', { name: 'Amber' }));
    await user.click(within(form).getByRole('button', { name: 'Save label' }));
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain(`PATCH /api/labels/${PROBLEM_LABEL_ID}`));
  });

  it('works without labels or counts when the API has none (404)', async () => {
    const { fetchMock } = setup(
      () => widgetConversation(),
      {
        [COUNTS]: () => jsonResponse({ title: 'Not found' }, 404),
        [HISTORY]: { feedback: [], handovers: [] },
        [SEARCH]: { items: [{ ...searchItem(), pinned: undefined, labels: undefined }], nextCursor: null },
      },
      `/inbox?label=gone&c=${WIDGET_CONVERSATION_ID}`,
    );

    const view = await screen.findByRole('region', { name: 'Conversation' });
    expect(await within(view).findByText('Can I return shoes I wore once?')).toBeInTheDocument();
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain('GET /api/labels'));
    expect(screen.getByRole('list', { name: 'Conversations' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Manage labels' })).toBeNull();
    expect(within(view).queryByRole('button', { name: /labels/ })).toBeNull();
    expect(within(screen.getByRole('tablist', { name: 'Views' })).getByRole('tab', { name: /Waiting for a person/ })).not.toHaveTextContent(/\d/);
  });
});

describe('inbox state', () => {
  it('reads old addresses and leaves playground tests to the portal channel', () => {
    const state = readInboxState(new URLSearchParams('status=closed&open=abc&widget=W1'));
    expect([state.view, state.conversationId, state.widgetId]).toEqual(['closed', 'abc', 'w1']);
    const query = toSearchQuery({ ...state, period: '7', rating: 'good' }, 'cur', 50, new Date('2026-11-05T12:00:00').getTime());
    expect(query).toMatchObject({ status: 'closed', widgetId: 'w1', minRating: 4, cursor: 'cur', sort: 'updated', order: 'desc' });
    expect(new Date(query.from!).getDate()).toBe(30);
    expect(toSearchQuery({ ...state, labelId: 'l1' }, null, 50)).toMatchObject({ status: 'closed', labelId: 'l1' });
    const pinned = readInboxState(new URLSearchParams('view=pinned'));
    expect([pinned.view, pinned.pinned]).toEqual(['all', true]);
    expect(toSearchQuery(pinned, null, 50)).toMatchObject({ status: undefined, pinned: true });
    expect(readInboxState(new URLSearchParams('view=person')).view).toBe('all');
    expect(toSearchQuery({ ...state, segmentId: 's1' }, null, 50)).toMatchObject({ segmentId: 's1' });
  });
});

describe('inbox view tabs', () => {
  it('moves the tabs that do not fit into More, from the end', () => {
    const widths = [80, 170, 120, 130, 80, 50];
    expect(tabsThatFit(widths, null)).toBe(6);
    expect(tabsThatFit([0, 0, 0, 0, 0, 0], 300)).toBe(6);
    expect(tabsThatFit(widths, 2000)).toBe(6);
    expect(tabsThatFit(widths, 80 + 4 + 170 + 4 + 120 + 4 + 96)).toBe(3);
    expect(tabsThatFit(widths, 200)).toBe(1);
  });
});

describe('inbox timeline', () => {
  it('shows an answer being written at the end until its message is stored', () => {
    const question = messageDto({ sequence: 1, author: 'visitor', text: 'Hi', turnId: 'turn-1' });
    const following = { ...createTurn('turn-1', 'Hi', null), text: 'Hello' };
    expect(inboxTimeline([question], [], [following]).map((item) => item.kind)).toEqual(['message', 'turn']);

    const answer = messageDto({ sequence: 2, author: 'assistant', text: 'Hello there', turnId: 'turn-1' });
    const items = inboxTimeline([question, answer], [], [following]);
    expect(items.map((item) => item.kind)).toEqual(['message', 'message']);
    expect(items[1]).toMatchObject({ kind: 'message', turn: { text: 'Hello there', status: 'complete' } });
  });
});
