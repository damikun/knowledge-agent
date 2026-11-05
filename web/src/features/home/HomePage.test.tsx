import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { agentDto, agentsRoutes, builtInAgent } from '../../test/agentsApi';
import { alertDto, alertsRoutes, waitingAlert } from '../../test/alertsApi';
import { embeddingProfile, orchestratorProfile } from '../../test/apiFixtures';
import { sourceDto } from '../../test/fixtures';
import { inboxConversation } from '../../test/inboxApi';
import { integrationsRoutes, slackSettings, workingEmail } from '../../test/integrationsApi';
import { sourcesRoutes } from '../../test/sourcesApi';
import { jsonResponse, mockFetch, renderWithProviders, requestUrls } from '../../test/utils';
import { widgetDto, widgetGuid, widgetsRoutes } from '../../test/widgetsApi';
import type { AlertDto } from '../alerts/types';
import type { OverviewDto } from '../overview/types';
import { HomePage } from './HomePage';
import { setupSteps, type SetupFacts } from './setupSteps';
import type { RecentConversation } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

const shop = widgetDto();

function overview(overrides: Partial<OverviewDto> = {}): OverviewDto {
  return {
    days: 7,
    from: '2026-10-28T09:00:00Z',
    conversations: 40,
    websiteConversations: 30,
    previousConversations: 32,
    answeredByAi: 34,
    handedToPerson: 6,
    averageRating: 4.46,
    ratings: 13,
    widgets: [{ widgetId: shop.id, conversations: 30, lastConversationAt: '2026-11-04T08:00:00Z' }],
    ...overrides,
  };
}

const portalChat: RecentConversation = {
  id: 'portal-1',
  title: 'Vacation policy',
  channel: 'portal',
  status: 'ai',
  widgetId: null,
  updatedAt: '2026-11-04T07:00:00Z',
};

interface Setup {
  overview?: OverviewDto;
  waiting?: ReturnType<typeof inboxConversation>[];
  recent?: unknown[];
  agents?: ReturnType<typeof agentDto>[];
  noChatModel?: boolean;
  alerts?: AlertDto[];
}

// GET /api/conversations serves both the waiting list (status=handover_pending) and recent activity.
function serve({
  overview: numbers = overview(),
  waiting = [],
  recent = [],
  agents = [builtInAgent, agentDto()],
  noChatModel = false,
  alerts = [],
}: Setup = {}) {
  return mockFetch({
    ...alertsRoutes(() => ({ alerts, snoozed: 0 })),
    'GET /api/overview': numbers,
    'GET /api/models/profiles': [orchestratorProfile('openai', noChatModel ? { effective: null, error: 'No chat provider.' } : {}), embeddingProfile()],
    ...sourcesRoutes(() => [sourceDto()]),
    ...agentsRoutes(() => agents),
    ...widgetsRoutes(() => [shop]),
    ...integrationsRoutes(() => ({ email: workingEmail, slack: slackSettings() })),
    'GET /api/conversations': (url: URL) => jsonResponse(url.searchParams.get('status') === 'handover_pending' ? waiting : recent),
  });
}

describe('HomePage', () => {
  it('shows the key numbers of the last 7 days', async () => {
    const fetchMock = serve();
    renderWithProviders(<HomePage />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Home' })).toBeInTheDocument();
    const numbers = await screen.findByRole('region', { name: 'Last 7 days' });
    expect(await within(numbers).findByText('40')).toBeInTheDocument();
    expect(numbers).toHaveTextContent('Conversations40+25% on the week before30 on your website');
    expect(numbers).toHaveTextContent('Answered by AI3485% of conversations');
    expect(numbers).toHaveTextContent('Handed to a person615% of conversations');
    expect(numbers).toHaveTextContent('Average rating4.5Out of 5, from 13 ratings');
    expect(Object.fromEntries(requestUrls(fetchMock, 'GET /api/overview')[0]!.searchParams)).toEqual({ days: '7' });
  });

  it('says when there is nothing to count yet', async () => {
    serve({ overview: overview({ conversations: 0, websiteConversations: 0, previousConversations: 0, answeredByAi: 0, handedToPerson: 0, averageRating: null, ratings: 0 }) });
    renderWithProviders(<HomePage />);

    const numbers = await screen.findByRole('region', { name: 'Last 7 days' });
    expect(await within(numbers).findAllByText('No conversations yet')).toHaveLength(2);
    expect(within(numbers).getByText('No ratings yet')).toBeInTheDocument();
    expect(within(numbers).queryByText(/on the week before/)).toBeNull();
  });

  it('lists who waits for a person and the latest conversations, each linking to where it is answered', async () => {
    serve({
      waiting: [inboxConversation({ preview: 'Can someone call me back?' })],
      recent: [inboxConversation({ status: 'human' }), portalChat],
    });
    renderWithProviders(<HomePage />);

    const waiting = await screen.findByRole('list', { name: 'Waiting for a person' });
    const request = within(waiting).getByRole('link', { name: /Return an order/ });
    expect(request).toHaveAttribute('href', `/inbox/${inboxConversation().id}`);
    expect(request).toHaveTextContent('Can someone call me back?');
    expect(await within(request).findByText('Shop help')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Needs attention' })).toHaveTextContent('Waiting for a person: 1');
    expect(screen.queryByText('All good. Nothing needs you right now.')).toBeNull();
    expect(screen.getByRole('link', { name: 'Open inbox' })).toHaveAttribute('href', '/inbox');

    const recent = screen.getByRole('list', { name: 'Recent conversations' });
    const [website, portal] = within(recent).getAllByRole('link');
    expect(website).toHaveTextContent('With a person');
    expect(portal).toHaveAttribute('href', '/chat/portal-1');
    expect(portal).toHaveTextContent('Portal chat');
  });

  it('says all is good when nothing needs attention and nobody is waiting', async () => {
    serve();
    renderWithProviders(<HomePage />);

    expect(await screen.findByText('All good. Nothing needs you right now.')).toBeInTheDocument();
    expect(await screen.findByText('No conversations yet')).toBeInTheDocument();
  });

  it('puts the alerts first, each with where to fix it, above the people waiting', async () => {
    serve({ alerts: [waitingAlert, alertDto()], waiting: [inboxConversation()] });
    renderWithProviders(<HomePage />);

    const section = await screen.findByRole('region', { name: /^Needs attention/ });
    const alerts = await within(section).findByRole('list', { name: 'Alerts' });
    const [waiting, sources] = within(alerts).getAllByRole('listitem');
    expect(waiting).toHaveTextContent('Urgent: A visitor is waiting for a person');
    expect(within(waiting!).getByRole('link', { name: 'Fix “A visitor is waiting for a person”' })).toHaveAttribute('href', '/inbox');
    expect(within(sources!).getByRole('link', { name: /^Fix/ })).toHaveAttribute('href', '/sources');
    expect(within(section).getByText('2', { selector: 'span' })).toBeInTheDocument();
    expect(await within(section).findByRole('list', { name: 'Waiting for a person' })).toBeInTheDocument();
  });

  it('shows the setup checklist until everything is set up', async () => {
    serve({ noChatModel: true, agents: [builtInAgent] });
    const { unmount } = renderWithProviders(<HomePage />);

    const checklist = await screen.findByRole('region', { name: 'Get your agent on your website' });
    await userEvent.setup().click(within(checklist).getByRole('button', { name: '2 finished steps' }));
    const [model, agent, knowledge, widget] = within(checklist).getAllByRole('listitem');
    expect(model).toHaveTextContent('Connect a model, current step');
    expect(within(model!).getByRole('link', { name: 'Add an API key' })).toHaveAttribute('href', '/models#providers');
    expect(knowledge).toHaveTextContent('Add knowledge, done');
    expect(agent).toHaveTextContent('Create an agent, to do');
    expect(within(agent!).getByRole('link', { name: 'Create an agent' })).toHaveAttribute('href', '/agents/new');
    expect(widget).toHaveTextContent('Put a widget on your site, done');
    unmount();

    serve();
    renderWithProviders(<HomePage />);
    await screen.findByText('40');
    expect(screen.queryByRole('region', { name: 'Get your agent on your website' })).toBeNull();
  });

});

describe('setup steps', () => {
  const facts: SetupFacts = { modelConnected: true, hasSources: true, hasAgent: true, firstWidgetId: widgetGuid(1), widgetLive: false };

  it('waits for every fact', () => {
    expect(setupSteps({ ...facts, widgetLive: undefined })).toBeNull();
  });

  it('marks the first open step as current and offers its way to do it', () => {
    const steps = setupSteps({ ...facts, hasSources: false })!;
    expect(steps.map((step) => step.state)).toEqual(['done', 'current', 'done', 'todo']);
    expect(steps[3]!.action).toEqual({ label: 'Get the snippet', to: `/widgets/${widgetGuid(1)}` });
    expect(setupSteps({ ...facts, firstWidgetId: null })![3]!.action).toEqual({ label: 'Create a widget', to: '/widgets/new' });
  });
});
