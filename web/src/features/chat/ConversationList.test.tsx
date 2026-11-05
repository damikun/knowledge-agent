import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { dismissAllToasts, Toaster } from '../../components/ui/Toast';
import { billingLabel, labelOn, listItem, refundsLabel, stubWideScreen } from '../../test/chat';
import { conversationDto } from '../../test/fixtures';
import { sourcesRoutes } from '../../test/sourcesApi';
import { fetchCalls, jsonResponse, mockFetch, renderWithProviders, requestUrls } from '../../test/utils';
import { ChatPage } from './ChatPage';
import { formatRowTime, groupConversations } from './conversationGroups';
import { normalizeSummary } from './api';
import type { ConversationSummaryDto } from './types';

const NOW = new Date(2026, 10, 5, 15, 0);

function at(daysAgo: number, hour = 10): string {
  return new Date(2026, 10, 5 - daysAgo, hour, 0).toISOString();
}

beforeEach(() => {
  stubWideScreen();
});

afterEach(() => {
  act(() => dismissAllToasts());
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function renderChat(route = '/chat') {
  return renderWithProviders(
    <>
      <ChatPage />
      <Toaster />
    </>,
    { route, path: '/chat/:conversationId?' },
  );
}

function page(items: ConversationSummaryDto[], nextCursor: string | null = null) {
  return { items, nextCursor };
}

function rowLinks(): HTMLElement[] {
  return within(screen.getByRole('complementary', { name: 'Conversation history' }))
    .getAllByRole('link')
    .filter((link) => link.hasAttribute('data-row-id'));
}

describe('groupConversations', () => {
  it('groups by Pinned, Today, Yesterday, Previous 7 and 30 days, then by month', () => {
    const items = [
      listItem({ id: 'old', updatedAt: new Date(2026, 7, 3).toISOString() }),
      listItem({ id: 'last-year', updatedAt: new Date(2025, 10, 3).toISOString() }),
      listItem({ id: 'today', updatedAt: at(0) }),
      listItem({ id: 'pinned', updatedAt: at(40), pinned: true }),
      listItem({ id: 'yesterday', updatedAt: at(1) }),
      listItem({ id: 'week', updatedAt: at(5) }),
      listItem({ id: 'month', updatedAt: at(20) }),
      listItem({ id: 'september', updatedAt: new Date(2026, 8, 10).toISOString() }),
    ].map(normalizeSummary);
    const groups = groupConversations(items, NOW);
    expect(groups.map((group) => [group.label, group.items.map((item) => item.id)])).toEqual([
      ['Pinned', ['pinned']],
      ['Today', ['today']],
      ['Yesterday', ['yesterday']],
      ['Previous 7 days', ['week']],
      ['Previous 30 days', ['month']],
      ['September', ['september']],
      ['August', ['old']],
      ['November 2025', ['last-year']],
    ]);
  });

  it('shows the time today, the weekday this week and the date before', () => {
    expect(formatRowTime(at(0, 9), NOW)).toMatch(/9:00/);
    expect(formatRowTime(at(2), NOW)).toBe(new Intl.DateTimeFormat(undefined, { weekday: 'short' }).format(new Date(at(2))));
    expect(formatRowTime(at(20), NOW)).toBe(new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(new Date(at(20))));
  });
});

describe('Conversation list', () => {
  it('keeps working with the plain array of older API versions', async () => {
    mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': [listItem({ id: 'a', title: 'Vacation policy', updatedAt: new Date().toISOString() }), { ...listItem({ id: 'b', title: 'Opening hours' }), pinned: undefined, labels: undefined }],
    });
    renderChat();
    await waitFor(() => expect(rowLinks().map((link) => link.textContent)).toEqual(['Vacation policy', 'Opening hours']));
    expect(screen.getByRole('heading', { name: 'Today' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Filter conversations/ })).toBeNull();
  });

  it('searches on the server, highlights the match, and offers to clear an empty result', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': (url: URL) => {
        const q = url.searchParams.get('q') ?? '';
        const all = [listItem({ id: 'a', title: 'Refund policy' }), listItem({ id: 'b', title: 'Opening hours' })];
        return jsonResponse(page(q === 'nothing' ? [] : all.filter((item) => item.title.toLowerCase().includes(q.toLowerCase()))));
      },
    });
    renderChat();
    await waitFor(() => expect(rowLinks()).toHaveLength(2));

    await user.keyboard('{Control>}k{/Control}');
    const search = screen.getByRole('searchbox', { name: 'Search conversations' });
    expect(search).toHaveFocus();
    await user.type(search, 'refund');
    await waitFor(() => expect(rowLinks()).toHaveLength(1));
    expect(requestUrls(fetchMock, 'GET /api/conversations').at(-1)!.searchParams.get('q')).toBe('refund');
    expect(within(rowLinks()[0]!).getByText('Refund').tagName).toBe('MARK');

    await user.clear(search);
    await user.type(search, 'nothing');
    expect(await screen.findByText('No matching conversations')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Clear search' }));
    await waitFor(() => expect(rowLinks()).toHaveLength(2));
    expect(search).toHaveValue('');

    await user.type(search, 'abc');
    await user.keyboard('{Escape}');
    expect(search).toHaveValue('');
  });

  it('loads the next page with the cursor', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': (url: URL) =>
        jsonResponse(url.searchParams.get('cursor') === 'p2' ? page([listItem({ id: 'b', title: 'Second page' })]) : page([listItem({ id: 'a', title: 'First page' })], 'p2')),
    });
    renderChat();
    await waitFor(() => expect(rowLinks()).toHaveLength(1));
    // jsdom has no IntersectionObserver, so the list offers a button instead of loading on scroll.
    await user.click(screen.getByRole('button', { name: 'Load more' }));
    await waitFor(() => expect(rowLinks().map((link) => link.textContent)).toEqual(['First page', 'Second page']));
    expect(requestUrls(fetchMock, 'GET /api/conversations').map((url) => url.searchParams.get('limit'))).toEqual(['50', '50']);
  });

  it('moves through the rows with the arrow keys and renames inline with Enter, or cancels with Esc', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': page([listItem({ id: 'a', title: 'First', updatedAt: at(0) }), listItem({ id: 'b', title: 'Second', updatedAt: at(1) })]),
      'PATCH /api/conversations/b': () => jsonResponse({}),
    });
    renderChat();
    await waitFor(() => expect(rowLinks()).toHaveLength(2));

    await user.click(screen.getByRole('searchbox', { name: 'Search conversations' }));
    await user.keyboard('{ArrowDown}');
    expect(rowLinks()[0]).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    expect(rowLinks()[1]).toHaveFocus();
    expect(rowLinks().map((link) => link.tabIndex)).toEqual([-1, 0]);

    await user.click(screen.getByRole('button', { name: 'Actions for “Second”' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Rename' }));
    const input = screen.getByRole('textbox', { name: 'Conversation title' });
    await user.clear(input);
    await user.type(input, 'Renamed{Escape}');
    expect(rowLinks().map((link) => link.textContent)).toEqual(['First', 'Second']);

    rowLinks()[1]!.focus();
    await user.keyboard('{F2}');
    await user.clear(screen.getByRole('textbox', { name: 'Conversation title' }));
    await user.type(screen.getByRole('textbox', { name: 'Conversation title' }), 'Renamed{Enter}');
    expect(rowLinks().map((link) => link.textContent)).toEqual(['First', 'Renamed']);
    const call = fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === 'PATCH');
    expect(JSON.parse((call![1] as RequestInit).body as string)).toEqual({ title: 'Renamed' });
  });

  it('pins a conversation into the Pinned group and puts it back when the API refuses', async () => {
    const user = userEvent.setup();
    let refuse = false;
    let pinned = false;
    mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': () => jsonResponse(page([listItem({ id: 'b', title: 'Second', updatedAt: at(1), pinned }), listItem({ id: 'a', title: 'First', updatedAt: at(0) })])),
      'PATCH /api/conversations/b': (_url: URL, init: RequestInit) => {
        if (refuse) {
          return jsonResponse({ title: 'Nope' }, 500);
        }
        pinned = (JSON.parse(init.body as string) as { pinned: boolean }).pinned;
        return jsonResponse({});
      },
    });
    renderChat();
    await waitFor(() => expect(rowLinks()).toHaveLength(2));

    await user.click(screen.getByRole('button', { name: 'Actions for “Second”' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Pin' }));
    const pinnedGroup = screen.getByRole('heading', { name: 'Pinned' }).closest('section')!;
    expect(within(pinnedGroup).getByRole('link', { name: /Second/ })).toBeInTheDocument();

    refuse = true;
    await user.click(screen.getByRole('button', { name: 'Actions for “Second”' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Unpin' }));
    expect(await screen.findByText('Could not unpin the conversation')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Pinned' })).toBeInTheDocument());
  });

  it('shows label dots, filters by a label and sets labels with a searchable picker', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/labels': [billingLabel, refundsLabel],
      'GET /api/conversations': (url: URL) =>
        jsonResponse(
          page(
            url.searchParams.get('labelId') === billingLabel.id
              ? [listItem({ id: 'b', title: 'Invoice question', labels: [labelOn(billingLabel)] })]
              : [
                  listItem({ id: 'a', title: 'Refund for shoes', labels: [labelOn(refundsLabel, 'auto')] }),
                  listItem({ id: 'b', title: 'Invoice question', labels: [labelOn(billingLabel)] }),
                ],
          ),
        ),
      'PUT /api/conversations/a/labels': () => jsonResponse({}),
    });
    renderChat();
    await waitFor(() => expect(rowLinks()).toHaveLength(2));
    expect(rowLinks()[0]).toHaveTextContent('labels: Refunds (added automatically)');

    await user.click(screen.getByRole('button', { name: 'Filter conversations' }));
    await user.click(within(await screen.findByRole('dialog', { name: 'Filter conversations' })).getByRole('radio', { name: /Billing/ }));
    await waitFor(() => expect(rowLinks()).toHaveLength(1));
    expect(requestUrls(fetchMock, 'GET /api/conversations').at(-1)!.searchParams.get('labelId')).toBe(billingLabel.id);
    await user.click(screen.getByRole('button', { name: 'Remove filter Billing' }));
    await waitFor(() => expect(rowLinks()).toHaveLength(2));

    await user.click(screen.getByRole('button', { name: 'Actions for “Refund for shoes”' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Labels' }));
    const picker = await screen.findByRole('dialog', { name: 'Labels' });
    expect(within(picker).getByRole('textbox', { name: 'Search labels' })).toHaveFocus();
    expect(within(picker).getByRole('checkbox', { name: /Refunds/ })).toBeChecked();
    expect(within(picker).getByText('Added automatically')).toBeInTheDocument();
    await user.type(within(picker).getByRole('textbox', { name: 'Search labels' }), 'bill');
    expect(within(picker).queryByRole('checkbox', { name: /Refunds/ })).toBeNull();
    await user.click(within(picker).getByRole('checkbox', { name: /Billing/ }));
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain('PUT /api/conversations/a/labels'));
    const call = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/api/conversations/a/labels'));
    expect(JSON.parse((call![1] as RequestInit).body as string)).toEqual({ labelIds: [billingLabel.id] });
    expect(rowLinks()[0]).toHaveTextContent('labels: Billing, Refunds (added automatically)');
  });

  it('deletes with Undo, and deletes for real once Undo is no longer offered', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': page([listItem({ id: 'a', title: 'First', updatedAt: at(0) }), listItem({ id: 'b', title: 'Second', updatedAt: at(1) })]),
      'DELETE /api/conversations/b': () => new Response(null, { status: 204 }),
    });
    const view = renderChat();
    await waitFor(() => expect(rowLinks()).toHaveLength(2));

    await user.click(screen.getByRole('button', { name: 'Actions for “Second”' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    expect(rowLinks().map((link) => link.textContent)).toEqual(['First']);
    await user.click(within(await screen.findByRole('region', { name: 'Notifications' })).getByRole('button', { name: 'Undo' }));
    expect(rowLinks().map((link) => link.textContent)).toEqual(['First', 'Second']);
    expect(fetchCalls(fetchMock)).not.toContain('DELETE /api/conversations/b');

    await user.click(screen.getByRole('button', { name: 'Actions for “Second”' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    view.unmount();
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain('DELETE /api/conversations/b'));
  });

  it('keeps the open conversation visible while a search hides it', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': (url: URL) =>
        jsonResponse(page(url.searchParams.get('q') ? [listItem({ id: 'b', title: 'Opening hours' })] : [listItem({ id: 'c1', title: 'Vacation policy' }), listItem({ id: 'b', title: 'Opening hours' })])),
      'GET /api/conversations/c1': conversationDto({ id: 'c1' }),
    });
    renderChat('/chat/c1');
    await waitFor(() => expect(rowLinks()).toHaveLength(2));
    await user.type(screen.getByRole('searchbox', { name: 'Search conversations' }), 'opening');
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Open conversation' })).toBeInTheDocument());
    expect(rowLinks().map((link) => link.textContent)).toEqual(['Vacation policy', 'Opening hours']);
    expect(rowLinks()[0]).toHaveAttribute('aria-current', 'page');
  });

  it('starts a new chat with Cmd/Ctrl+Shift+O and clears the search', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...sourcesRoutes(() => []),
      'GET /api/conversations': page([listItem({ id: 'c1', title: 'Vacation policy' })]),
      'GET /api/conversations/c1': conversationDto({ id: 'c1' }),
    });
    renderChat('/chat/c1');
    expect(await screen.findByRole('heading', { level: 2, name: 'Vacation policy' })).toBeInTheDocument();
    await user.type(screen.getByRole('searchbox', { name: 'Search conversations' }), 'vac');
    await user.keyboard('{Control>}{Shift>}o{/Shift}{/Control}');
    expect(await screen.findByRole('heading', { level: 2, name: 'New conversation' })).toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: 'Search conversations' })).toHaveValue('');
    expect(screen.getByRole('button', { name: 'New chat' })).toHaveAttribute('title', expect.stringMatching(/Shift\+O|⌘⇧O/));
  });
});
