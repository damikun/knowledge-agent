import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useLocation } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatDateTime } from '../../lib/format';
import { systemInfoFixture } from '../../test/apiFixtures';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders, requestUrls } from '../../test/utils';
import { INNGEST_DASHBOARD_URL, inngestOverview, jobsOverview, refreshSchedule } from '../../test/systemApi';
import { SystemPage } from './SystemPage';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function LocationProbe() {
  const { pathname, search } = useLocation();
  return <p data-testid="location">{`${pathname}${search}`}</p>;
}

const RUN_ROUTE = 'POST /api/jobs/schedules/refresh-web-sources/run';

describe('SystemPage', () => {
  it('opens on General with the public address and the help switch, and keeps the tab in the URL', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({ 'GET /api/jobs/overview': jobsOverview(), 'GET /api/system/info': systemInfoFixture() });
    renderWithProviders(
      <>
        <SystemPage />
        <LocationProbe />
      </>,
      { route: '/system' },
    );

    expect(await screen.findByRole('tab', { name: 'General', selected: true })).toBeInTheDocument();
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['General', 'Schedules', 'System overview']);
    expect(await screen.findByRole('region', { name: 'Public address' })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'For your team' })).toBeInTheDocument();
    expect(screen.queryByText('Development mode')).toBeNull();
    expect(screen.queryByRole('region', { name: 'Schedules' })).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
    await waitFor(() => expect(new Set(fetchCalls(fetchMock))).toEqual(new Set(['GET /api/jobs/overview', 'GET /api/system/info', 'GET /api/help'])));

    await user.click(screen.getByRole('tab', { name: 'System overview' }));
    expect(await screen.findByRole('region', { name: 'How your installation is built' })).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('?tab=overview');
    await waitFor(() => expect(fetchCalls(fetchMock)).toContain('GET /api/system/health'));

    await user.click(screen.getByRole('tab', { name: 'General' }));
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/system$/);
  });

  it('says that jobs run inside the API process and shows the schedules read-only', async () => {
    mockFetch({ 'GET /api/jobs/overview': jobsOverview() });
    renderWithProviders(<SystemPage />, { route: '/system?tab=schedules' });

    expect(await screen.findByText('Development mode')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Schedules', selected: true })).toBeInTheDocument();
    expect(screen.getByText(/a restart forgets what was queued/)).toBeInTheDocument();
    expect(screen.getByText(/Anything that fails is tried again automatically/)).toBeInTheDocument();
    expect(screen.queryByText(/Inngest/)).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.getByText(/set in configuration under/)).toHaveTextContent('Jobs:Schedules');

    const refresh = await screen.findByRole('region', { name: 'refresh-web-sources' });
    expect(within(refresh).getByText('Web source refresh')).toBeInTheDocument();
    expect(within(refresh).getByText('Every day at 04:00')).toBeInTheDocument();
    expect(within(refresh).getByText('0 4 * * *')).toBeInTheDocument();
    expect(within(refresh).getByText('UTC')).toBeInTheDocument();
    expect(within(refresh).getByText('Disabled')).toBeInTheDocument();
    expect(within(refresh).getByText('Not scheduled')).toBeInTheDocument();
    expect(
      within(refresh)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Run now']);
  });

  it('links to the Inngest dashboard and the traces on General when they exist', async () => {
    mockFetch({
      'GET /api/jobs/overview': inngestOverview({ schedules: [refreshSchedule({ enabled: true, nextRunAt: '2026-10-31T04:00:00Z' })] }),
      'GET /api/system/info': systemInfoFixture({ langfuseConfigured: true, langfuseUrl: 'https://langfuse.example.com' }),
    });
    const { unmount } = renderWithProviders(<SystemPage />, { route: '/system' });

    const link = await screen.findByRole('link', { name: /Run history/ });
    expect(screen.getByRole('link', { name: /Run history/ })).not.toHaveTextContent(/Inngest/);
    expect(link).toHaveAttribute('href', `${INNGEST_DASHBOARD_URL}/`);
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    expect(screen.getByRole('link', { name: /Answer traces/ })).toHaveAttribute('href', 'https://langfuse.example.com/');
    unmount();

    renderWithProviders(<SystemPage />, { route: '/system?tab=schedules' });
    const refresh = await screen.findByRole('region', { name: 'refresh-web-sources' });
    expect(screen.queryByText('Development mode')).toBeNull();
    expect(within(refresh).getByText('Enabled')).toBeInTheDocument();
    expect(within(refresh).getByText(formatDateTime('2026-10-31T04:00:00Z'))).toHaveAttribute('datetime', '2026-10-31T04:00:00Z');
  });

  it('runs a schedule now', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({ 'GET /api/jobs/overview': inngestOverview(), [RUN_ROUTE]: () => new Response(null, { status: 202 }) });
    renderWithProviders(<SystemPage />, { route: '/system?tab=schedules' });

    const refresh = await screen.findByRole('region', { name: 'refresh-web-sources' });
    await user.click(within(refresh).getByRole('button', { name: 'Run now' }));
    expect(await within(refresh).findByText('Job queued.')).toBeInTheDocument();
    expect(fetchCalls(fetchMock).filter((call) => call === RUN_ROUTE)).toHaveLength(1);
  });

  it('explains why a schedule was not queued', async () => {
    const user = userEvent.setup();
    mockFetch({
      'GET /api/jobs/overview': jobsOverview(),
      [RUN_ROUTE]: () => problemResponse({ title: 'Not Found', detail: "Schedule 'refresh-web-sources' was not found." }, 404),
    });
    renderWithProviders(<SystemPage />, { route: '/system?tab=schedules' });

    const refresh = await screen.findByRole('region', { name: 'refresh-web-sources' });
    await user.click(within(refresh).getByRole('button', { name: 'Run now' }));
    const alert = await within(refresh).findByRole('alert');
    expect(alert).toHaveTextContent('The job was not queued');
    expect(alert).toHaveTextContent("Schedule 'refresh-web-sources' was not found.");
    expect(within(refresh).queryByText('Job queued.')).toBeNull();
  });

  it('says so when no schedule is registered', async () => {
    mockFetch({ 'GET /api/jobs/overview': jobsOverview({ schedules: [] }) });
    renderWithProviders(<SystemPage />, { route: '/system?tab=schedules' });

    expect(await screen.findByText('No schedules')).toBeInTheDocument();
  });

  it('reads the overview once instead of polling it', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const fetchMock = mockFetch({ 'GET /api/jobs/overview': jobsOverview() });
    renderWithProviders(<SystemPage />, { route: '/system?tab=schedules' });

    await screen.findByRole('region', { name: 'refresh-web-sources' });
    await act(() => vi.advanceTimersByTimeAsync(60_000));
    expect(requestUrls(fetchMock, 'GET /api/jobs/overview')).toHaveLength(1);
  });

  describe('Public address', () => {
    const PUT_ROUTE = 'PUT /api/system/public-address';

    function card() {
      return screen.getByRole('region', { name: 'Public address' });
    }

    it('shows where the address comes from and saves a new one', async () => {
      const user = userEvent.setup();
      const bodies: unknown[] = [];
      mockFetch({
        'GET /api/jobs/overview': jobsOverview(),
        'GET /api/system/info': systemInfoFixture({ publicAddress: { url: 'https://config.acme.com', source: 'configuration' } }),
        [PUT_ROUTE]: (_url: URL, init: RequestInit) => {
          bodies.push(JSON.parse(init.body as string));
          return jsonResponse({ url: 'https://support.acme.com/help', source: 'saved' });
        },
      });
      renderWithProviders(<SystemPage />);

      const input = await within(await screen.findByRole('region', { name: 'Public address' })).findByLabelText('Public address of this installation');
      expect(card()).toHaveTextContent('Used in embed code, links in emails and Slack messages, and the MCP and API addresses.');
      expect(input).toHaveValue('https://config.acme.com');
      expect(card()).toHaveTextContent('From the configuration (App:PublicUrl). Saving here takes its place.');
      const save = within(card()).getByRole('button', { name: 'Save' });
      expect(save).toBeDisabled();

      await user.clear(input);
      await user.type(input, 'support.acme.com');
      await user.click(save);
      expect(await within(card()).findByText(/Enter a full address that starts with https:\/\//)).toBeInTheDocument();
      expect(input).toHaveAttribute('aria-invalid', 'true');
      expect(bodies).toEqual([]);

      await user.clear(input);
      await user.type(input, 'https://support.acme.com/help/');
      await user.click(save);
      await waitFor(() => expect(card()).toHaveTextContent('Saved here.'));
      expect(bodies).toEqual([{ url: 'https://support.acme.com/help' }]);
      expect(input).toHaveValue('https://support.acme.com/help');
      expect(save).toBeDisabled();
    });

    it('shows the API\'s reason next to the field', async () => {
      const user = userEvent.setup();
      mockFetch({
        'GET /api/jobs/overview': jobsOverview(),
        'GET /api/system/info': systemInfoFixture(),
        [PUT_ROUTE]: () => problemResponse({ title: 'One or more validation errors occurred.', errors: { url: ['Leave out the user name and password.'] } }, 400),
      });
      renderWithProviders(<SystemPage />);

      const input = await within(await screen.findByRole('region', { name: 'Public address' })).findByLabelText('Public address of this installation');
      expect(input).toHaveValue('');
      expect(card()).toHaveTextContent(/Not set yet\. Until then, the code to copy shows yourdomain\.com/);
      await user.type(input, 'https://support.acme.com');
      await user.click(within(card()).getByRole('button', { name: 'Save' }));

      expect(await within(card()).findByText('Leave out the user name and password.')).toBeInTheDocument();
      expect(input).toHaveAttribute('aria-invalid', 'true');
    });
  });
});
