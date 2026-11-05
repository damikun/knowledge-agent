import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Link, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { currentUser, embeddingProfile, orchestratorProfile, providersFixture, supportUser, systemInfoFixture } from '../test/apiFixtures';
import { alertSettings } from '../test/alertsApi';
import { inboxConversation } from '../test/inboxApi';
import { emailSettings, integrationsRoutes, slackSettings } from '../test/integrationsApi';
import { useBreadcrumb } from '../lib/breadcrumb';
import { useReturnState } from '../lib/returnTo';
import { fetchCalls, mockFetch, problemResponse, renderRouter, renderWithProviders, requestUrls } from '../test/utils';
import { widgetsRoutes } from '../test/widgetsApi';
import { AppLayout } from './AppLayout';
import { routes } from './routes';

afterEach(() => {
  vi.unstubAllGlobals();
});

const adminApis = {
  'GET /api/system/info': systemInfoFixture(),
  'GET /api/models/profiles': [orchestratorProfile('openai'), embeddingProfile()],
  'GET /api/models/providers': providersFixture,
};

function renderLayout() {
  return renderWithProviders(
    <Routes>
      <Route element={<AppLayout />}>
        <Route path="/playground" element={<p>Playground page</p>} />
      </Route>
    </Routes>,
    { route: '/playground' },
  );
}

async function openUserMenu(name: string): Promise<HTMLElement> {
  await userEvent.click(await screen.findByRole('button', { name: `Account: ${name}` }));
  return screen.getByRole('dialog', { name: 'Account' });
}

const STAFF_CALLS = ['GET /api/auth/me', 'GET /api/conversations', 'GET /api/alerts', 'GET /api/help'];

function stubWindow({ roomy }: { roomy: boolean }) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === '(min-width: 1280px)' && roomy,
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }));
}

function navLabels(): string[] {
  return within(screen.getByRole('navigation', { name: 'Main' }))
    .getAllByRole('link')
    .map((link) => link.textContent ?? '');
}

function navGroups(): string[] {
  return within(screen.getByRole('navigation', { name: 'Main' }))
    .getAllByRole('group')
    .map((group) => group.getAttribute('aria-labelledby'))
    .map((id) => document.getElementById(id!)?.textContent ?? '');
}

function OpenWithReturn() {
  return (
    <Link to="/widgets/w1" state={useReturnState()}>
      Open the widget
    </Link>
  );
}

function DetailPage({ name }: { name: string }) {
  useBreadcrumb(name);
  return <Link to="/playground">Back to the playground</Link>;
}

describe('AppLayout', () => {
  it('shows the name, e-mail and role in the user menu and signs out', async () => {
    const assign = vi.fn();
    vi.stubGlobal('location', { ...window.location, assign });
    mockFetch(adminApis);
    renderLayout();

    const menu = await openUserMenu('Ada Lovelace');
    expect(within(menu).getByText('Ada Lovelace')).toBeInTheDocument();
    expect(within(menu).getByText('ada@example.com')).toBeInTheDocument();
    expect(within(menu).getByText('Admin')).toBeInTheDocument();

    await userEvent.click(within(menu).getByRole('button', { name: 'Sign out' }));
    expect(assign).toHaveBeenCalledWith('/api/auth/logout');
  });

  it('says that sign-in is off instead of offering to sign out when the API runs without sign-in', async () => {
    mockFetch({ ...adminApis, 'GET /api/auth/me': currentUser({ name: 'Local admin', email: null, mode: 'none' }) });
    renderLayout();

    const menu = await openUserMenu('Local admin');
    expect(within(menu).getByText('Sign-in is off')).toBeInTheDocument();
    expect(within(menu).getByText('Admin')).toBeInTheDocument();
    expect(within(menu).queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument();
  });

  it('shows admins every page', async () => {
    mockFetch(adminApis);
    renderLayout();

    await screen.findByRole('button', { name: 'Account: Ada Lovelace' });
    expect(navGroups()).toEqual(['Support', 'Insights', 'Improve', 'Settings']);
    expect(navLabels()).toEqual(['Home', 'Inbox', 'Contacts', 'Analytics', 'Feedback', 'Playground', 'Agents', 'Tools', 'Widgets', 'Sources', 'Prompts', 'Evaluations', 'Notifications', 'AI models', 'Team', 'System']);
    expect(screen.getByRole('button', { name: /^System status/ })).toBeInTheDocument();
  });

  it('leaves no focus ring on a sidebar item after a mouse click, but keeps focus after Enter', async () => {
    mockFetch(adminApis);
    renderLayout();
    const user = userEvent.setup();

    const playground = await screen.findByRole('link', { name: 'Playground' });
    await user.click(playground);
    expect(playground).not.toHaveFocus();

    playground.focus();
    await user.keyboard('{Enter}');
    expect(playground).toHaveFocus();
  });

  it('shows support users the inbox and feedback only and calls no admin API', async () => {
    const fetchMock = mockFetch({ ...adminApis, 'GET /api/auth/me': supportUser });
    renderLayout();

    const menu = await openUserMenu('Sam Rivera');
    expect(within(menu).getByText('Support')).toBeInTheDocument();
    expect(navGroups()).toEqual(['Support', 'Insights']);
    expect(navLabels()).toEqual(['Inbox', 'Contacts', 'Feedback']);
    expect(screen.queryByRole('button', { name: /^System status/ })).not.toBeInTheDocument();
    await waitFor(() => expect(new Set(fetchCalls(fetchMock))).toEqual(new Set(STAFF_CALLS)));
  });

  it('puts the help button next to the bell for support users while the help is on', async () => {
    mockFetch({ 'GET /api/auth/me': supportUser, 'GET /api/help': { enabled: true, canManage: false, modelReady: false, setup: null } });
    renderLayout();

    await userEvent.click(await screen.findByRole('button', { name: 'Help' }));
    expect(await screen.findByRole('dialog', { name: 'Help' })).toHaveTextContent('The help answers once an admin connects a chat model.');
  });

  it('names the section in the header, and the item a detail page shows after it', async () => {
    mockFetch(adminApis);
    renderWithProviders(
      <Routes>
        <Route element={<AppLayout />}>
          <Route path="/playground" element={<p>Playground page</p>} />
          <Route path="/widgets/:widgetId" element={<DetailPage name="Demo shop" />} />
        </Route>
      </Routes>,
      { route: '/widgets/w1' },
    );

    const location = await screen.findByRole('navigation', { name: 'Location' });
    expect(within(location).getByRole('link', { name: 'Widgets' })).toHaveAttribute('href', '/widgets');
    expect(within(location).getByText('Demo shop')).toHaveAttribute('aria-current', 'page');
    expect(document.title).toBe('Demo shop · Widgets · Knowledge Agent');

    await userEvent.click(screen.getByRole('link', { name: 'Back to the playground' }));
    expect(screen.queryByRole('navigation', { name: 'Location' })).toBeNull();
    expect(screen.queryByText('Demo shop')).toBeNull();
    expect(document.title).toBe('Playground · Knowledge Agent');
  });

  it('starts the header with a back arrow and the origin when a page was opened from another one', async () => {
    mockFetch(adminApis);
    renderWithProviders(
      <Routes>
        <Route element={<AppLayout />}>
          <Route path="/playground" element={<OpenWithReturn />} />
          <Route path="/widgets/:widgetId" element={<DetailPage name="Demo shop" />} />
        </Route>
      </Routes>,
      { route: '/playground?agent=a1' },
    );

    await userEvent.click(await screen.findByRole('link', { name: 'Open the widget' }));
    const location = await screen.findByRole('navigation', { name: 'Location' });
    const back = within(location).getByRole('link', { name: 'Back to Playground' });
    expect(back).toHaveAttribute('href', '/playground?agent=a1');
    expect(back).toHaveAttribute('title', 'Back to Playground (Alt+← / ⌘[)');
    expect(within(location).getByRole('link', { name: 'Playground' })).toHaveAttribute('href', '/playground?agent=a1');
    expect(within(location).getByRole('link', { name: 'Widgets' })).toHaveAttribute('href', '/widgets');
    expect(within(location).getByText('Demo shop')).toHaveAttribute('aria-current', 'page');

    await userEvent.click(back);
    expect(await screen.findByRole('link', { name: 'Open the widget' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Back to Playground' })).toBeNull();
  });

  it('counts the conversations waiting for a person on the Inbox item', async () => {
    const fetchMock = mockFetch({
      ...adminApis,
      'GET /api/conversations': [inboxConversation({ id: 'c1' }), inboxConversation({ id: 'c2' }), inboxConversation({ id: 'c3' })],
    });
    renderLayout();

    const inbox = await screen.findByRole('link', { name: 'Inbox, 3 waiting' });
    expect(inbox).toHaveAttribute('href', '/inbox');
    const [count] = requestUrls(fetchMock, 'GET /api/conversations');
    expect(Object.fromEntries(count!.searchParams)).toEqual({ channel: 'widget', status: 'handover_pending', limit: '100' });
  });

  it('shows only the menu icons on a narrower window, with the names as tooltips, and expands on request', async () => {
    stubWindow({ roomy: false });
    mockFetch(adminApis);
    renderLayout();

    expect(await screen.findByRole('link', { name: 'Widgets' })).toHaveAttribute('title', 'Widgets');
    const toggle = screen.getByRole('button', { name: 'Expand menu' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByText(/^v1\.0\.0/)).toBeNull();

    await userEvent.click(toggle);

    expect(screen.getByRole('button', { name: 'Collapse menu' })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('link', { name: 'Widgets' })).not.toHaveAttribute('title');
    expect(await screen.findByText(/^v1\.0\.0/)).toBeInTheDocument();
  });

  it('remembers a collapsed menu on a wide window', async () => {
    stubWindow({ roomy: true });
    mockFetch(adminApis);
    const first = renderLayout();

    await userEvent.click(await screen.findByRole('button', { name: 'Collapse menu' }));
    expect(screen.getByRole('button', { name: 'Expand menu' })).toBeInTheDocument();
    first.unmount();

    renderLayout();
    expect(await screen.findByRole('button', { name: 'Expand menu' })).toBeInTheDocument();
  });

  it.each(['/home', '/sources', '/agents', '/agents/new', '/widgets', '/widgets/new', '/models', '/prompts/orchestrator-system', '/evals', '/evals/runs/r1', '/system', '/jobs', '/notifications', '/connections', '/integrations', '/analytics'])(
    'shows support users the admin-role panel when they open %s directly',
    async (path) => {
      const fetchMock = mockFetch({ 'GET /api/auth/me': supportUser });
      renderRouter(routes, { route: path });

      // The chunk of the page still loads first, so this can take a moment.
      expect(await screen.findByRole('heading', { level: 1, name: 'You need the admin role to open this page' }, { timeout: 5_000 })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Go to the Inbox' })).toHaveAttribute('href', '/inbox');
      expect(navLabels()).toEqual(['Inbox', 'Contacts', 'Feedback']);
      await waitFor(() => expect(new Set(fetchCalls(fetchMock))).toEqual(new Set(STAFF_CALLS)));
    },
  );

  it('opens Notifications from the old Integrations address', async () => {
    mockFetch({
      ...adminApis,
      ...integrationsRoutes(() => ({ email: emailSettings(), slack: slackSettings() })),
      ...widgetsRoutes(() => []),
      'GET /api/alerts/settings': alertSettings(),
      'GET /api/connections/mcp': () => problemResponse({ title: 'Not Found' }, 404),
    });
    renderRouter(routes, { route: '/integrations' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Notifications' }, { timeout: 5_000 })).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Notifications' })).toHaveAttribute('aria-current', 'page');
  });

  it('starts admins on Home', async () => {
    mockFetch({ ...adminApis, 'GET /api/overview': { days: 7, conversations: 0, widgets: [] }, 'GET /api/conversations': [] });
    renderRouter(routes, { route: '/' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Home' }, { timeout: 5_000 })).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Home' })).toHaveAttribute('aria-current', 'page');
  });

  it('starts support users in the Inbox', async () => {
    mockFetch({ 'GET /api/auth/me': supportUser, 'GET /api/conversations': [], 'GET /api/conversations/search': { items: [], nextCursor: null }, 'GET /api/agents': [] });
    renderRouter(routes, { route: '/' });

    expect(await screen.findByRole('heading', { level: 2, name: 'All' }, { timeout: 5_000 })).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Inbox' })).toHaveAttribute('aria-current', 'page');
  });

  it.each([
    ['/inbox', 'All', 'Inbox'],
    ['/feedback', 'Feedback', 'Feedback'],
    ['/conversations', 'All', 'Inbox'],
  ])('lets support users open %s', async (path, heading, navItem) => {
    mockFetch({
      'GET /api/auth/me': supportUser,
      'GET /api/conversations': [],
      'GET /api/conversations/search': { items: [], nextCursor: null },
      'GET /api/conversations/counts': { handoverPending: 0, human: 0, ai: 0, closed: 0, all: 0, labels: [] },
      'GET /api/feedback/summary': [],
      'GET /api/feedback': [],
      'GET /api/agents': [],
    });
    renderRouter(routes, { route: path });

    expect(await screen.findByRole('heading', { name: heading }, { timeout: 5_000 })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'You need the admin role to open this page' })).toBeNull();
    expect(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: navItem })).toHaveAttribute('aria-current', 'page');
  });
});
