import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PageHeader } from '../components/ui/feedback';
import { AnalyticsPage } from '../features/analytics/AnalyticsPage';
import { InboxPage } from '../features/inbox/InboxPage';
import { PersonPage } from '../features/people/PersonPage';
import { agentsRoutes, builtInAgent } from '../test/agentsApi';
import { systemInfoFixture } from '../test/apiFixtures';
import { countsDto, handoverDto, searchItem, WIDGET_CONVERSATION_ID, widgetConversation } from '../test/inboxApi';
import { controlledEventStream, jsonResponse, mockFetch, renderWithProviders } from '../test/utils';
import { widgetDto, widgetsRoutes } from '../test/widgetsApi';
import { AppLayout } from './AppLayout';

afterEach(() => {
  vi.unstubAllGlobals();
});

const PERSON_ID = '0199c1e2-0000-7000-8000-00000000ada1';
const DETAIL = `GET /api/conversations/${WIDGET_CONVERSATION_ID}`;
const contact = { id: PERSON_ID, name: 'Ada Lovelace', email: 'ada@example.com', identified: true };

const profile = {
  id: PERSON_ID,
  name: 'Ada Lovelace',
  savedName: 'Ada Lovelace',
  email: 'ada@example.com',
  externalId: null,
  identified: true,
  notes: null,
  firstSeenAt: '2026-10-01T08:00:00Z',
  lastSeenAt: '2026-11-04T09:00:00Z',
  sessionCount: 1,
  returning: false,
  attributes: [],
  widgets: [],
  stats: { conversations: 1, handovers: 1, averageRating: null, ratings: 0, lastSeenAt: '2026-11-04T09:00:00Z' },
};

function analytics() {
  return {
    days: 7,
    from: '2026-11-01T00:00:00Z',
    to: '2026-11-07T12:00:00Z',
    totals: { conversations: 1, answeredWithoutPerson: 0, handedToPerson: 1, ratings: 0, averageRating: null, thumbsUp: 0, thumbsDown: 0, noSourceAnswers: 1 },
    daily: [],
    widgets: [],
    unanswered: [
      {
        question: 'Do you ship to Norway?',
        count: 1,
        reasons: ['handover'],
        lastAskedAt: '2026-11-07T10:00:00Z',
        conversationId: WIDGET_CONVERSATION_ID,
        widgetId: null,
        messageId: 'm-9',
        answer: 'I could not find that.',
        comment: null,
      },
    ],
    labels: [],
    segments: [],
  };
}

function Address() {
  const location = useLocation();
  return <output data-testid="address">{`${location.pathname}${location.search}`}</output>;
}

const address = () => screen.getByTestId('address').textContent;

function setup(route: string) {
  const events = controlledEventStream();
  mockFetch({
    'GET /api/system/info': systemInfoFixture(),
    [`GET /api/people/${PERSON_ID}`]: profile,
    'GET /api/conversations/search': { items: [searchItem({ contact })], nextCursor: null },
    'GET /api/conversations/counts': countsDto({ handoverPending: 1, all: 1 }),
    [DETAIL]: () => jsonResponse(widgetConversation({ contact })),
    [`${DETAIL}/events`]: (_url: URL, init: RequestInit) => events.respond(init),
    [`${DETAIL}/handover`]: handoverDto(),
    [`${DETAIL}/history`]: { feedback: [], handovers: [handoverDto()], labels: [] },
    'GET /api/analytics': analytics(),
    ...widgetsRoutes(() => [widgetDto()]),
    ...agentsRoutes(() => [builtInAgent]),
  });
  renderWithProviders(
    <>
      <Routes>
        <Route element={<AppLayout />}>
          <Route path="/contacts/:personId" element={<PersonPage />} />
          <Route path="/inbox/:conversationId?" element={<InboxPage />} />
          <Route path="/analytics" element={<AnalyticsPage />} />
        </Route>
      </Routes>
      <Address />
    </>,
    { route },
  );
}

const header = () => screen.getByRole('banner');

describe('the way back to where a page was opened from', () => {
  it('goes from a contact to their conversation in the Inbox and back to the contact', async () => {
    const user = userEvent.setup();
    setup(`/contacts/${PERSON_ID}`);

    await user.click(await screen.findByRole('link', { name: /Return an order/ }));
    await waitFor(() => expect(address()).toBe(`/inbox?person=${PERSON_ID}&c=${WIDGET_CONVERSATION_ID}`));
    expect(await screen.findByRole('button', { name: 'Remove filter: Contact: Ada Lovelace' })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { selected: true })).toBeNull();

    const back = within(header()).getByRole('link', { name: 'Back to Ada Lovelace' });
    expect(back).toHaveAttribute('href', `/contacts/${PERSON_ID}`);
    expect(back).toHaveAttribute('title', 'Back to Ada Lovelace (Alt+← / ⌘[)');
    const location = within(header()).getByRole('navigation', { name: 'Location' });
    expect(location).toHaveTextContent('Ada Lovelace/Inbox');

    await user.click(within(screen.getByRole('navigation', { name: 'Inbox views' })).getByRole('tab', { name: /Waiting for a person/ }));
    await waitFor(() => expect(address()).toContain('view=waiting'));
    expect(within(header()).getByRole('link', { name: 'Back to Ada Lovelace' })).toBeInTheDocument();

    await user.click(within(header()).getByRole('link', { name: 'Back to Ada Lovelace' }));
    await waitFor(() => expect(address()).toBe(`/contacts/${PERSON_ID}`));
    expect(await screen.findByRole('heading', { level: 1, name: 'Ada Lovelace' })).toBeInTheDocument();
    expect(within(header()).getByRole('link', { name: 'Back to Contacts' })).toHaveAttribute('href', '/contacts');
  });

  it('goes from the Inbox to the contact and back to the same view and conversation', async () => {
    const user = userEvent.setup();
    setup(`/inbox?view=waiting&q=shoes&c=${WIDGET_CONVERSATION_ID}`);

    const people = await screen.findAllByRole('link', { name: 'Ada Lovelace' });
    await user.click(people.find((link) => link.getAttribute('href') === `/contacts/${PERSON_ID}`)!);
    await waitFor(() => expect(address()).toBe(`/contacts/${PERSON_ID}`));
    expect(await screen.findByRole('heading', { level: 1, name: 'Ada Lovelace' })).toBeInTheDocument();

    expect(within(screen.getByRole('main')).queryByRole('link', { name: 'Contacts' })).toBeNull();
    await user.click(within(header()).getByRole('link', { name: 'Back to Inbox' }));
    await waitFor(() => expect(address()).toBe(`/inbox?view=waiting&q=shoes&c=${WIDGET_CONVERSATION_ID}`));
    expect(await screen.findByRole('heading', { level: 2, name: 'Return an order' })).toBeInTheDocument();
  });

  it('goes from an unanswered question in Analytics to the Inbox and back to the same period', async () => {
    const user = userEvent.setup();
    setup('/analytics?days=7');

    await user.click(await screen.findByRole('link', { name: 'Read' }));
    await waitFor(() => expect(address()).toBe(`/inbox?only=${WIDGET_CONVERSATION_ID}`));
    const location = within(header()).getByRole('navigation', { name: 'Location' });
    expect(within(location).getByRole('link', { name: 'Analytics' })).toHaveAttribute('href', '/analytics?days=7');
    expect(await screen.findByRole('button', { name: 'Remove filter: Only this conversation' })).toBeInTheDocument();

    await user.click(within(header()).getByRole('link', { name: 'Back to Analytics' }));
    await waitFor(() => expect(address()).toBe('/analytics?days=7'));
  });

  it('keeps the way back out of the page header, since the app header shows it', () => {
    render(
      <MemoryRouter initialEntries={['/agents/a1']}>
        <PageHeader title="Support agent" />
      </MemoryRouter>,
    );
    expect(screen.getByRole('heading', { name: 'Support agent' })).toBeInTheDocument();
    expect(screen.queryByRole('link')).toBeNull();
  });
});
