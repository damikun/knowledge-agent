import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '../../components/ui/Toast';
import { alertDto, alertsRoutes, waitingAlert } from '../../test/alertsApi';
import { supportUser } from '../../test/apiFixtures';
import { fetchCalls, mockFetch, renderWithProviders } from '../../test/utils';
import { ALERTS_REFRESH_MS, toAlert, useAlerts } from './api';
import { AlertsBell } from './AlertsBell';
import { NeedsAttention } from './NeedsAttention';
import type { AlertListDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

function lastBody(fetchMock: ReturnType<typeof mockFetch>, path: string): unknown {
  const call = fetchMock.mock.calls.filter(([input]) => new URL(String(input), 'http://localhost').pathname === path).at(-1);
  const body = (call?.[1] as RequestInit | undefined)?.body;
  return typeof body === 'string' ? JSON.parse(body) : undefined;
}

describe('AlertsBell', () => {
  it('counts what needs attention and lists it with where to fix it', async () => {
    const user = userEvent.setup();
    mockFetch(alertsRoutes(() => ({ alerts: [waitingAlert, alertDto()], snoozed: 1 })));
    renderWithProviders(<AlertsBell />);

    const bell = await screen.findByRole('button', { name: 'Alerts, 2 things need attention' });
    expect(bell.parentElement).toHaveTextContent('2');
    await user.click(bell);

    const popover = screen.getByRole('dialog', { name: 'Alerts' });
    expect(within(popover).getByText('1 snoozed alert')).toBeInTheDocument();
    const items = within(within(popover).getByRole('list', { name: 'Needs attention' })).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('Urgent: A visitor is waiting for a person');
    expect(items[0]).toHaveTextContent('They have waited 16 minutes.');
    expect(items[1]).toHaveTextContent('Needs attention: 2 sources could not be indexed');

    await user.click(within(items[1]!).getByRole('link', { name: 'Fix “2 sources could not be indexed”' }));
    expect(screen.queryByRole('dialog', { name: 'Alerts' })).toBeNull();
  });

  it('lets an admin snooze an alert from the popover', async () => {
    const user = userEvent.setup();
    let list: AlertListDto = { alerts: [alertDto()], snoozed: 0 };
    const fetchMock = mockFetch({
      ...alertsRoutes(() => list),
      'POST /api/alerts/sources_failed/snooze': () => {
        list = { alerts: [], snoozed: 1 };
        return new Response(null, { status: 204 });
      },
    });
    renderWithProviders(
      <>
        <AlertsBell />
        <Toaster />
      </>,
    );

    await user.click(await screen.findByRole('button', { name: /^Alerts, 1 thing needs attention/ }));
    await user.click(screen.getByRole('button', { name: 'Snooze “2 sources could not be indexed”' }));
    await user.click(within(screen.getByRole('group', { name: 'Snooze “2 sources could not be indexed”' })).getByRole('button', { name: 'For 1 day' }));

    expect(await screen.findByText('Snoozed for 1 day')).toBeInTheDocument();
    expect(lastBody(fetchMock, '/api/alerts/sources_failed/snooze')).toEqual({ hours: 24 });
    expect(await screen.findByText('All good. Nothing needs you right now.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Alerts, nothing needs attention' })).toBeInTheDocument();
  });

  it('shows support the alerts without a way to snooze them', async () => {
    const user = userEvent.setup();
    mockFetch({ 'GET /api/auth/me': supportUser, ...alertsRoutes(() => ({ alerts: [waitingAlert], snoozed: 0 })) });
    renderWithProviders(<AlertsBell />);

    await user.click(await screen.findByRole('button', { name: /^Alerts, 1 thing/ }));
    const popover = screen.getByRole('dialog', { name: 'Alerts' });
    expect(within(popover).getByRole('link', { name: /^Fix/ })).toHaveAttribute('href', '/inbox');
    await waitFor(() => expect(within(popover).queryByRole('button', { name: /^Snooze/ })).toBeNull());
  });
});

describe('NeedsAttention', () => {
  it('says all is good when nothing needs attention', async () => {
    mockFetch(alertsRoutes());
    renderWithProviders(<NeedsAttention />);

    expect(await screen.findByText('All good. Nothing needs you right now.')).toBeInTheDocument();
  });

  it('hides an alert until it changes from the snooze menu', async () => {
    const user = userEvent.setup();
    let list: AlertListDto = { alerts: [alertDto()], snoozed: 0 };
    const fetchMock = mockFetch({
      ...alertsRoutes(() => list),
      'POST /api/alerts/sources_failed/dismiss': () => {
        list = { alerts: [], snoozed: 1 };
        return new Response(null, { status: 204 });
      },
    });
    renderWithProviders(<NeedsAttention />);

    await user.click(await screen.findByRole('button', { name: 'Snooze “2 sources could not be indexed”' }));
    await user.click(screen.getByRole('menuitem', { name: 'Until it changes' }));

    expect(await screen.findByText('1 snoozed alert hidden until it changes or its snooze ends.')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain('POST /api/alerts/sources_failed/dismiss');
    expect(screen.getByText('All good. Nothing needs you right now.')).toBeInTheDocument();
  });
});

describe('alerts api', () => {
  it('keeps links inside the portal', () => {
    expect(toAlert({ key: 'a', link: '/sources' }).link).toBe('/sources');
    expect(toAlert({ key: 'a', link: 'https://evil.example' }).link).toBe('/home');
    expect(toAlert({ key: 'a', link: '//evil.example' }).link).toBe('/home');
    expect(toAlert({ key: 'a', severity: 'loud' }).severity).toBe('warning');
  });

  it('refetches every minute while the tab is visible', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const fetchMock = mockFetch(alertsRoutes());
    function Probe() {
      useAlerts();
      return null;
    }
    renderWithProviders(<Probe />);
    await waitFor(() => expect(fetchCalls(fetchMock).filter((call) => call === 'GET /api/alerts')).toHaveLength(1));

    await vi.advanceTimersByTimeAsync(ALERTS_REFRESH_MS);
    await waitFor(() => expect(fetchCalls(fetchMock).filter((call) => call === 'GET /api/alerts')).toHaveLength(2));
  });
});
