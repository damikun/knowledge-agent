import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { currentUser } from '../../test/apiFixtures';
import { mockFetch, problemResponse, renderWithProviders } from '../../test/utils';
import { teamDto } from '../../test/teamApi';
import { TeamPage } from './TeamPage';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('TeamPage', () => {
  it('lists the team with roles and when they were added, links Keycloak for its user admins and explains the roles', async () => {
    mockFetch({ 'GET /api/team': teamDto(), 'GET /api/auth/me': currentUser({ canManageUsers: true }) });
    renderWithProviders(<TeamPage />);

    const table = await screen.findByRole('table', { name: 'Team members' });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]!).getByText('Ada Lovelace')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('ada@example.com')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('Admin')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('2 hours ago')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('Oct 1, 2026')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Support')).toBeInTheDocument();
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Person', 'Role', 'Last seen', 'Added']);
    expect(screen.queryByText(/Assigned|Handled/)).toBeNull();

    expect(screen.queryByText(/create them in Keycloak/)).toBeNull();
    expect(await screen.findByRole('link', { name: /Open Keycloak/ })).toHaveAttribute('href', 'http://localhost:8180/admin/knowledge-agent/console/');
    expect(screen.queryByRole('button', { name: /Open Keycloak/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /role/i })).toBeNull();

    const roles = screen.getByRole('table', { name: 'What each role can do' });
    const inbox = within(roles).getByRole('rowheader', { name: /Answer conversations in the Inbox/ }).closest('tr')!;
    expect(within(inbox).getByText('Support can')).toBeInTheDocument();
    const settings = within(roles).getByRole('rowheader', { name: /see the team/ }).closest('tr')!;
    expect(within(settings).getByText('Support cannot')).toBeInTheDocument();
  });

  it('disables Open Keycloak with the reason for admins who are not user admins in Keycloak', async () => {
    mockFetch({ 'GET /api/team': teamDto(), 'GET /api/auth/me': currentUser({ canManageUsers: false }) });
    renderWithProviders(<TeamPage />);
    await screen.findByRole('table', { name: 'Team members' });

    const button = screen.getByRole('button', { name: /Open Keycloak/ });
    const hint = 'You need user admin rights in Keycloak (manage-users) to open it.';
    expect(button).toBeDisabled();
    expect(button).toHaveAccessibleDescription(hint);
    expect(button.parentElement).toHaveAttribute('title', hint);
    expect(screen.queryByRole('link', { name: /Open Keycloak/ })).toBeNull();
  });

  it('searches by name, email or role', async () => {
    const user = userEvent.setup();
    mockFetch({ 'GET /api/team': teamDto() });
    renderWithProviders(<TeamPage />);
    const table = await screen.findByRole('table', { name: 'Team members' });

    await user.type(screen.getByRole('searchbox', { name: 'Search the team' }), 'support');
    expect(within(table).getAllByRole('row')).toHaveLength(2);
    expect(within(table).getByText('Sam Rivera')).toBeInTheDocument();

    await user.clear(screen.getByRole('searchbox', { name: 'Search the team' }));
    await user.type(screen.getByRole('searchbox', { name: 'Search the team' }), 'nobody');
    expect(screen.getByText('No match')).toBeInTheDocument();
  });

  it('has no console link without sign-in and shows errors', async () => {
    mockFetch({ 'GET /api/team': teamDto({ keycloakConsoleUrl: null, members: [] }) });
    const { unmount } = renderWithProviders(<TeamPage />);
    expect(await screen.findByText('Nobody signed in yet')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Keycloak/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /Open Keycloak/ })).toBeNull();
    unmount();

    mockFetch({ 'GET /api/team': () => problemResponse({ title: 'Forbidden' }, 403) });
    renderWithProviders(<TeamPage />);
    expect(await screen.findByText('Could not load the team')).toBeInTheDocument();
  });
});
