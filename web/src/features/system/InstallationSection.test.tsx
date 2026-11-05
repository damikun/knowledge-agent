import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { mockFetch, renderWithProviders } from '../../test/utils';
import { healthComponent, healthyInstallation, inngestOverview, jobsOverview } from '../../test/systemApi';
import { InstallationSection } from './InstallationSection';

afterEach(() => {
  vi.unstubAllGlobals();
});

// jsdom has no ResizeObserver, which React Flow needs.
class NoopResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

function wideScreen() {
  vi.stubGlobal('ResizeObserver', NoopResizeObserver);
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === '(min-width: 1000px)',
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }));
}

describe('InstallationSection', () => {
  it('lists every part with its state on narrow screens and opens the details of one', async () => {
    const user = userEvent.setup();
    mockFetch({ 'GET /api/jobs/overview': inngestOverview(), 'GET /api/system/health': healthyInstallation() });
    renderWithProviders(<InstallationSection />);

    expect(await screen.findByText('Every part that was checked is working.')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: /as a diagram/ })).toBeNull();
    expect(screen.queryByRole('radiogroup', { name: 'View' })).toBeNull();

    const outside = screen.getByRole('list', { name: 'Outside your server' });
    expect(within(outside).getAllByRole('button').map((b) => b.textContent)).toEqual([
      expect.stringContaining('Website visitors'),
      expect.stringContaining('Your team'),
      expect.stringContaining('Model providers'),
    ]);
    const server = screen.getByRole('list', { name: 'Your server' });
    expect(within(server).getByRole('button', { name: /^PostgreSQL\. Healthy\. Conversations, settings\. postgres:17-alpine/ })).toBeInTheDocument();
    expect(within(server).getByRole('button', { name: /^Slack\. Not set up/ })).toBeInTheDocument();
    const tracing = screen.getByRole('list', { name: 'Tracing' });
    expect(within(tracing).getByRole('button', { name: /^Redis\. Status unknown/ })).toBeInTheDocument();

    await user.click(within(server).getByRole('button', { name: /^Email\./ }));
    const sheet = await screen.findByRole('dialog', { name: 'Email' });
    expect(within(sheet).getByText(/Emails requests for a person/)).toBeInTheDocument();
    expect(within(sheet).getByText('Sends requests and reads replies')).toBeInTheDocument();
    expect(within(sheet).getByText('SMTP + IMAP')).toBeInTheDocument();
    expect(within(sheet).getByRole('link', { name: 'Open Notifications' })).toHaveAttribute('href', '/notifications');
    await user.click(within(sheet).getByRole('button', { name: 'Close' }));

    await user.click(within(server).getByRole('button', { name: /^Keycloak\./ }));
    const docs = within(await screen.findByRole('dialog', { name: 'Keycloak' })).getByRole('link', { name: /Read the configuration docs/ });
    expect(docs).toHaveAttribute('href', expect.stringMatching(/docs\/configuration\.md#sign-in$/));
    expect(docs).toHaveAttribute('target', '_blank');
    expect(docs).toHaveAttribute('rel', 'noopener noreferrer');
  });

  it('names what needs attention, the worst first', async () => {
    mockFetch({
      'GET /api/jobs/overview': inngestOverview(),
      'GET /api/system/health': healthyInstallation([healthComponent('langfuse', 'degraded'), healthComponent('signIn', 'down', 'oidc')]),
    });
    renderWithProviders(<InstallationSection />);

    const attention = await screen.findByRole('list', { name: 'Needs attention' });
    expect(within(attention).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Keycloak: down', 'Langfuse: degraded']);
  });

  it('says the state is unknown against an API without the health endpoint', async () => {
    mockFetch({ 'GET /api/jobs/overview': jobsOverview() });
    renderWithProviders(<InstallationSection />);

    expect(await screen.findByText(/Status unknown: this API does not report/)).toBeInTheDocument();
    const server = screen.getByRole('list', { name: 'Your server' });
    expect(within(server).getByRole('button', { name: /^PostgreSQL\. Status unknown/ })).toBeInTheDocument();
    expect(within(server).getByRole('button', { name: /^Jobs inside the API\./ })).toBeInTheDocument();
  });

  it('draws the diagram on wide screens and switches to the list', async () => {
    wideScreen();
    const user = userEvent.setup();
    mockFetch({ 'GET /api/jobs/overview': inngestOverview(), 'GET /api/system/health': healthyInstallation() });
    renderWithProviders(<InstallationSection />);

    const diagram = await screen.findByRole('region', { name: /as a diagram/ });
    await screen.findByText('Every part that was checked is working.');
    expect(within(diagram).getByText('Knowledge Agent API')).toBeInTheDocument();
    expect(within(diagram).getByText('knowledge-agent-api')).toBeInTheDocument();
    expect(within(diagram).getByText('Your server')).toBeInTheDocument();

    await user.click(within(diagram).getByRole('button', { name: /^Langfuse\. Healthy/ }));
    expect(await screen.findByRole('dialog', { name: 'Langfuse' })).toHaveTextContent('Reachable, version 4.1.0');
    await user.click(screen.getByRole('button', { name: 'Close' }));

    await user.click(screen.getByRole('radio', { name: 'Show as list' }));
    expect(screen.queryByRole('region', { name: /as a diagram/ })).toBeNull();
    expect(screen.getByRole('list', { name: 'Your server' })).toBeInTheDocument();
  });
});
