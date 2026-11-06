import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, Route, RouterProvider, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AgentPage } from '../features/agents/AgentPage';
import { AgentsPage } from '../features/agents/AgentsPage';
import { NewAgentPage } from '../features/agents/NewAgentWizard';
import { AnalyticsPage } from '../features/analytics/AnalyticsPage';
import { RequireAdmin } from '../features/auth/RequireAdmin';
import { ChatPage } from '../features/chat/ChatPage';
import { EvalRunPage } from '../features/evals/EvalRunPage';
import { EvalsPage } from '../features/evals/EvalsPage';
import { FeedbackPage } from '../features/feedback/FeedbackPage';
import { HomePage } from '../features/home/HomePage';
import { InboxPage } from '../features/inbox/InboxPage';
import { NotificationsPage } from '../features/notifications/NotificationsPage';
import { inngestOverview, jobsOverview, refreshSchedule } from '../test/systemApi';
import { SystemPage } from '../features/system/SystemPage';
import { ModelsPage } from '../features/models/ModelsPage';
import { PromptsPage } from '../features/prompts/PromptsPage';
import { SourcesPage } from '../features/sources/SourcesPage';
import { teamDto } from '../test/teamApi';
import { TeamPage } from '../features/team/TeamPage';
import { ToolsPage } from '../features/tools/ToolsPage';
import { WidgetsPage } from '../features/widgets/WidgetsPage';
import { axeViolations } from '../test/a11y';
import { agentDto, agentGuid, agentsRoutes, builtInAgent } from '../test/agentsApi';
import { embeddingProfile, orchestratorProfile, providersFixture, supportUser, systemInfoFixture } from '../test/apiFixtures';
import { evalDataset, evalDatasetList, evalRunDto, evalRunsRoutes, evalRunSummary, runGuid } from '../test/evalsApi';
import { feedbackDto, feedbackSummary, portalRating } from '../test/feedbackApi';
import { sourceDto } from '../test/fixtures';
import { countsDto, handoverDto, inboxConversation, searchItem, WIDGET_CONVERSATION_ID, widgetConversation } from '../test/inboxApi';
import { emailSettings, integrationsRoutes, slackSettings, workingEmail } from '../test/integrationsApi';
import { sourceFolder, sourcesRoutes } from '../test/sourcesApi';
import {
  controlledEventStream,
  createTestQueryClient,
  mockFetch,
  problemResponse,
  renderWithProviders,
  signedOutResponse,
  sseResponse,
} from '../test/utils';
import { cancelTool, mcpServer } from '../test/mcpApi';
import { fakePreview } from '../test/widgetPreview';
import { widgetDto, widgetSettings, widgetsRoutes } from '../test/widgetsApi';
import { DatasetPage } from '../features/evals/DatasetPage';
import { HelpLauncher } from '../features/help/HelpLauncher';
import { PeoplePage } from '../features/people/PeoplePage';
import { PersonPage } from '../features/people/PersonPage';
import { NotFoundPage, RouteErrorPage } from './RouteErrorPage';
import { ADA_ID, ada, adaProfile, conversationsPage, proSegment, registry, visitor } from '../test/peopleApi';
import { alertDto, alertsRoutes, waitingAlert } from '../test/alertsApi';
import { QueryClientProvider } from '@tanstack/react-query';
import { AlertsBell } from '../features/alerts';
import { AppLayout } from './AppLayout';
import { AuthGate } from './AuthGate';

afterEach(() => {
  vi.unstubAllGlobals();
});

const profiles = [orchestratorProfile('openai'), embeddingProfile()];

describe('accessibility (axe-core, WCAG 2.2 AA)', () => {
  it('reports violations (self-check of the harness)', async () => {
    renderWithProviders(
      <div>
        <button type="button">
          <svg aria-hidden />
        </button>
        <img src="/logo.png" />
      </div>,
    );

    const violations = await axeViolations();
    expect(violations.some((violation) => violation.startsWith('button-name'))).toBe(true);
    expect(violations.some((violation) => violation.startsWith('image-alt'))).toBe(true);
  });

  it('app shell with the chat landing page', async () => {
    mockFetch({
      'GET /api/system/info': systemInfoFixture(),
      'GET /api/models/profiles': profiles,
      'GET /api/models/providers': providersFixture,
      'GET /api/conversations': [],
      ...sourcesRoutes(() => [sourceDto()]),
      ...agentsRoutes(() => [builtInAgent, agentDto(), agentDto({ id: agentGuid(3), key: 'support', name: 'Support desk' })]),
    });
    renderWithProviders(
      <Routes>
        <Route element={<AppLayout />}>
          <Route path="/playground" element={<ChatPage />} />
        </Route>
      </Routes>,
      { route: '/playground' },
    );

    expect(await screen.findByRole('combobox', { name: 'Test as' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Playground' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Skip to content' })).toHaveAttribute('href', '#main');
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Playground' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await screen.findByRole('button', { name: 'Account: Ada Lovelace' });
    expect(await axeViolations()).toEqual([]);
  });

  it('sign-in screen with a failed sign-in', async () => {
    mockFetch({ 'GET /api/auth/me': () => signedOutResponse() });
    renderWithProviders(
      <AuthGate>
        <p>App</p>
      </AuthGate>,
      { route: '/?authError=The%20sign-in%20was%20cancelled.' },
    );

    await screen.findByRole('button', { name: 'Sign in' });
    expect(screen.getByRole('heading', { level: 1, name: 'Knowledge Agent' })).toBeInTheDocument();
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('The sign-in was cancelled.');
    expect(await axeViolations()).toEqual([]);
  });

  it('admin-role panel and the open user menu of a support user', async () => {
    mockFetch({ 'GET /api/auth/me': supportUser });
    renderWithProviders(
      <Routes>
        <Route element={<AppLayout />}>
          <Route element={<RequireAdmin />}>
            <Route path="/sources" element={<SourcesPage />} />
          </Route>
        </Route>
      </Routes>,
      { route: '/sources' },
    );

    await screen.findByRole('heading', { level: 1, name: 'You need the admin role to open this page' });
    expect(await axeViolations()).toEqual([]);

    await userEvent.click(screen.getByRole('button', { name: 'Account: Sam Rivera' }));
    expect(await screen.findByRole('dialog', { name: 'Account' })).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
  });

  it('sources page', async () => {
    const runUrl = 'http://localhost:8288/run?runID=01M66AE49V11B6EF9BKZ5D4M0S';
    mockFetch({
      ...sourcesRoutes(() => [
        sourceDto(),
        sourceDto({ id: 'failed', title: 'Broken', status: 'failed', error: 'Timeout', jobRunUrl: runUrl }),
        sourceDto({
          id: 'embedding',
          title: 'Leave policy',
          status: 'processing',
          stage: 'embedding',
          chunkCount: 400,
          embeddedChunks: 120,
          jobRunUrl: runUrl,
        }),
        sourceDto({
          id: 'waiting',
          title: 'Travel guide',
          status: 'waiting',
          chunkCount: 0,
          embeddingModel: null,
          error: 'No embedding model is configured.',
        }),
      ]),
      'GET /api/models/profiles': profiles,
    });
    renderWithProviders(<SourcesPage />);

    await screen.findByRole('table');
    await screen.findByText(/1 source waits for an embedding model/);
    expect(await axeViolations()).toEqual([]);
  });

  it('sources page folders, selection and folder dialogs', async () => {
    const user = userEvent.setup();
    const folders = [sourceFolder('legal', 'Legal'), sourceFolder('contracts', 'Contracts', 'legal')];
    const sources = [
      sourceDto({ folderId: 'contracts' }),
      sourceDto({ id: 'loose', title: 'Loose notes' }),
      sourceDto({
        id: 'page',
        type: 'url',
        title: 'Python tutorial',
        url: 'https://docs.python.org/3/tutorial/',
        site: 'docs.python.org',
        fileName: null,
      }),
    ];
    mockFetch({
      ...sourcesRoutes(
        () => sources,
        () => folders,
      ),
      'GET /api/models/profiles': profiles,
    });
    renderWithProviders(<SourcesPage />, { route: '/sources', path: '/sources' });

    await screen.findByRole('link', { name: 'Legal, 1 source' });
    await screen.findByRole('combobox', { name: 'Site' });
    await user.click(await screen.findByRole('checkbox', { name: 'Select “Loose notes”' }));
    expect(await screen.findByText('1 source selected')).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Move to folder' }));
    expect(await screen.findByRole('dialog', { name: 'Move “Loose notes”' })).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    await user.click(screen.getByRole('link', { name: 'Legal, 1 source' }));
    await user.click(await screen.findByRole('button', { name: 'New subfolder' }));
    expect(await screen.findByRole('dialog', { name: 'New folder' })).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
  });

  it('home page with the setup checklist, the numbers and who waits for a person', async () => {
    mockFetch({
      'GET /api/overview': {
        days: 7,
        from: '2026-10-28T09:00:00Z',
        conversations: 12,
        websiteConversations: 9,
        previousConversations: 10,
        answeredByAi: 10,
        handedToPerson: 2,
        averageRating: 4.5,
        ratings: 4,
        widgets: [{ widgetId: widgetDto().id, conversations: 9, lastConversationAt: null }],
      },
      'GET /api/models/profiles': profiles,
      ...sourcesRoutes(() => [sourceDto()]),
      ...agentsRoutes(() => [builtInAgent, agentDto()]),
      ...widgetsRoutes(() => [widgetDto()]),
      'GET /api/conversations': [inboxConversation()],
    });
    renderWithProviders(<HomePage />);

    await screen.findByRole('region', { name: 'Get your agent on your website' });
    await screen.findByText('12');
    await screen.findByRole('list', { name: 'Waiting for a person' });
    await screen.findByRole('list', { name: 'Recent conversations' });
    expect(await axeViolations()).toEqual([]);
  });

  it('agents page, the steps of a new agent with field errors, and the tabs of an agent', async () => {
    const user = userEvent.setup();
    const folders = [sourceFolder('legal', 'Legal'), sourceFolder('contracts', 'Contracts', 'legal')];
    const agent = agentDto({ folderIds: ['contracts'] });
    mockFetch({
      ...agentsRoutes(() => [builtInAgent, agent]),
      ...sourcesRoutes(
        () => [],
        () => folders,
      ),
      ...widgetsRoutes(() => [widgetDto()]),
      'GET /api/analytics': {
        days: 7,
        totals: {},
        daily: [],
        widgets: [{ widgetId: widgetDto().id, conversations: 4, handedToPerson: 1, ratings: 0 }],
        unanswered: [],
        labels: [],
      },
      'GET /api/connections/mcp': [mcpServer()],
      [`GET /api/agents/${agent.id}/access`]: {
        mcpServerEnabled: true,
        httpApiEnabled: false,
        mcpPath: '/mcp/agents/website-helper',
        apiPath: '/api/public/agents/website-helper',
        activeKeys: 1,
      },
      [`GET /api/agents/${agent.id}/api-keys`]: [
        {
          id: 'k1',
          name: 'Cursor',
          prefix: 'ka_abc123',
          requestsPerMinute: 30,
          createdBy: null,
          createdAt: '2026-11-01T08:00:00Z',
          lastUsedAt: null,
          revokedAt: null,
        },
      ],
    });
    const list = renderWithProviders(<AgentsPage />);

    await screen.findByRole('list', { name: 'Agents' });
    await screen.findByText('Legal / Contracts');
    await screen.findByText('On Shop help');
    expect(await axeViolations()).toEqual([]);
    list.unmount();

    const wizard = renderWithProviders(<NewAgentPage />, { route: '/agents/new', path: '/agents/new' });
    await screen.findByRole('radio', { name: /Customer support/ });
    expect(await axeViolations()).toEqual([]);
    await user.click(screen.getByRole('radio', { name: /Start from scratch/ }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(await screen.findByRole('button', { name: 'Continue' }));
    await screen.findByText('Enter a name.');
    expect(await axeViolations()).toEqual([]);
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Helper');
    await user.click(screen.getByRole('combobox', { name: 'Language' }));
    await screen.findByRole('listbox');
    expect(await axeViolations()).toEqual([]);
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await screen.findByRole('heading', { level: 1, name: 'How it talks' });
    expect(await axeViolations()).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(await screen.findByRole('radio', { name: 'Chosen folders' }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await screen.findByText('Choose at least one folder, or All sources.');
    expect(await axeViolations()).toEqual([]);
    await user.click(screen.getByRole('checkbox', { name: 'Legal' }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await screen.findByRole('heading', { level: 1, name: 'Tools and handover' });
    await screen.findByRole('region', { name: 'Shop' });
    expect(await axeViolations()).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await screen.findByRole('heading', { level: 1, name: 'Review and create' });
    expect(await axeViolations()).toEqual([]);
    wizard.unmount();

    renderWithProviders(<AgentPage />, { route: `/agents/${agent.id}`, path: '/agents/:agentId' });
    await screen.findByRole('region', { name: 'Last 7 days' });
    await screen.findByText('Legal / Contracts');
    expect(await axeViolations()).toEqual([]);
    for (const tab of ['General', 'Behavior', 'Knowledge', 'Tools', 'Handover', 'Advanced']) {
      await user.click(screen.getByRole('tab', { name: tab }));
      expect(await axeViolations()).toEqual([]);
    }
    await user.click(screen.getByRole('tab', { name: 'Tools' }));
    const shop = await screen.findByRole('region', { name: 'Shop' });
    await user.click(within(shop).getByRole('switch', { name: 'Use this app: Shop' }));
    await user.click(within(shop).getByText('Choose tools'));
    await user.click(within(shop).getByRole('switch', { name: 'Only the tools I pick: Shop' }));
    await within(shop).findByRole('combobox', { name: 'When Cancel an order may run' });
    expect(await axeViolations()).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await user.click(screen.getByRole('tab', { name: 'Use elsewhere' }));
    await screen.findByRole('list', { name: 'API keys' });
    expect(await axeViolations()).toEqual([]);
    await user.click(screen.getByRole('tab', { name: 'General' }));
    await user.clear(screen.getByRole('textbox', { name: 'Name' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await screen.findByText('Enter a name.');
    expect(await axeViolations()).toEqual([]);
  });

  it('models page and the side sheets of a job and a provider', async () => {
    const user = userEvent.setup();
    mockFetch({ 'GET /api/models/profiles': profiles, 'GET /api/models/providers': providersFixture });
    renderWithProviders(<ModelsPage />);

    await screen.findByRole('heading', { level: 1, name: 'AI models' });
    await screen.findByRole('region', { name: 'Answering model' });
    await screen.findByRole('region', { name: 'anthropic' });
    expect(await axeViolations()).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Change the answering model' }));
    expect(await axeViolations(screen.getByRole('dialog', { name: 'Answering model' }))).toEqual([]);
    await user.keyboard('{Escape}');

    await user.click(within(screen.getByRole('region', { name: 'anthropic' })).getByRole('button', { name: 'Set API key' }));
    expect(await axeViolations(screen.getByRole('dialog', { name: 'anthropic' }))).toEqual([]);
  });

  it('prompts page', async () => {
    mockFetch({
      'GET /api/prompts': [
        {
          name: 'orchestrator-system',
          type: 'text',
          version: 3,
          labels: ['production'],
          tags: ['knowledge-agent'],
          origin: 'langfuse',
          langfuseUrl: null,
          variables: ['appName'],
          lastUpdatedAt: null,
          error: null,
        },
      ],
      'GET /api/prompts/orchestrator-system': {
        name: 'orchestrator-system',
        type: 'text',
        version: 3,
        labels: ['production'],
        tags: ['knowledge-agent'],
        origin: 'langfuse',
        langfuseUrl: null,
        text: 'You are {{appName}}.',
        messages: [],
        config: {},
        variables: ['appName'],
      },
    });
    renderWithProviders(<PromptsPage />);

    await screen.findByRole('heading', { level: 1, name: 'Prompts' });
    expect(await axeViolations()).toEqual([]);
  });

  it('prompt edit mode, the versions with a diff and the publish confirmation', async () => {
    const user = userEvent.setup();
    const version = (number: number, text: string, live: boolean) => ({
      version: number,
      type: 'text',
      text,
      messages: [],
      config: null,
      labels: live ? ['production'] : [],
      commitMessage: null,
      live,
    });
    mockFetch({
      'GET /api/prompts': [
        {
          name: 'conversation-title',
          origin: 'langfuse',
          version: 2,
          type: 'text',
          labels: ['production'],
          variables: ['message'],
          externalUrl: null,
        },
      ],
      'GET /api/prompts/conversation-title': {
        name: 'conversation-title',
        origin: 'langfuse',
        version: 2,
        type: 'text',
        text: 'Title for {{message}}',
        messages: [],
        config: null,
        labels: ['production'],
        variables: ['message'],
        externalUrl: null,
      },
      'GET /api/prompts/conversation-title/versions': {
        name: 'conversation-title',
        editable: true,
        label: 'production',
        liveVersion: 2,
        totalVersions: 3,
        versions: [
          version(3, 'Short title for {{message}}', false),
          version(2, 'Title for {{message}}', true),
          version(1, 'Title: {{message}}', false),
        ],
      },
      'POST /api/prompts/render': { output: 'Title for Hello', variables: ['message'] },
    });
    renderWithProviders(<PromptsPage />, { route: '/prompts/conversation-title', path: '/prompts/:name?' });

    await user.click(await screen.findByRole('tab', { name: 'Versions' }));
    await screen.findByLabelText('Changes against the live version');
    expect(await axeViolations()).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Publish version 3' }));
    expect(await axeViolations(screen.getByRole('dialog', { name: 'Publish version 3?' }))).toEqual([]);
    await user.keyboard('{Escape}');

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await user.type(screen.getByRole('textbox', { name: 'Template' }), '!');
    await screen.findByRole('list', { name: 'Variables in the edited template' });
    expect(await axeViolations()).toEqual([]);
  });

  it('team page', async () => {
    mockFetch({ 'GET /api/team': teamDto() });
    renderWithProviders(<TeamPage />);

    await screen.findByRole('heading', { level: 1, name: 'Team' });
    await screen.findByRole('table', { name: 'Team members' });
    expect(await axeViolations()).toEqual([]);
  });

  it('evaluations page and the run dialog', async () => {
    const runs = [evalRunSummary({ id: runGuid(1) }), evalRunSummary({ id: runGuid(2), status: 'running', completedItems: 3, completedAt: null })];
    mockFetch({ 'GET /api/evals/datasets': evalDatasetList(), 'GET /api/models/profiles': profiles, ...evalRunsRoutes(() => runs) });
    renderWithProviders(<EvalsPage />);

    await screen.findByRole('heading', { level: 1, name: 'Evaluations' });
    await screen.findAllByRole('link', { name: /handbook-golden/ });
    expect(await axeViolations()).toEqual([]);

    await userEvent.click(screen.getAllByRole('button', { name: /^Run/ })[0]!);
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
  });

  it('evaluation run page', async () => {
    const run = evalRunDto({ id: runGuid(7) });
    mockFetch({ [`GET /api/evals/runs/${runGuid(7)}`]: run, ...evalRunsRoutes(() => [run.summary]) });
    renderWithProviders(<EvalRunPage />, { route: `/evals/runs/${runGuid(7)}`, path: '/evals/runs/:id' });

    await screen.findByRole('heading', { level: 1 });
    expect(await axeViolations()).toEqual([]);
  });

  it('jobs page on Inngest and after Run now', async () => {
    mockFetch({
      'GET /api/jobs/overview': inngestOverview({ schedules: [refreshSchedule({ enabled: true, nextRunAt: '2026-10-31T04:00:00Z' })] }),
      'POST /api/jobs/schedules/refresh-web-sources/run': () => new Response(null, { status: 202 }),
      'GET /api/system/info': systemInfoFixture(),
    });
    const general = renderWithProviders(<SystemPage />, { route: '/system' });

    await screen.findByRole('heading', { level: 1, name: 'System' });
    await screen.findByRole('link', { name: /Run history/ });
    await screen.findByLabelText('Public address of this installation');
    expect(await axeViolations()).toEqual([]);
    general.unmount();

    renderWithProviders(<SystemPage />, { route: '/system?tab=schedules' });
    const refresh = await screen.findByRole('region', { name: 'refresh-web-sources' });
    expect(await axeViolations()).toEqual([]);

    await userEvent.click(within(refresh).getByRole('button', { name: 'Run now' }));
    expect(await within(refresh).findByText('Job queued.')).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
  });

  it('widgets page and the new widget wizard with field errors', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...widgetsRoutes(() => [widgetDto(), widgetDto({ id: 'w2', name: 'Docs help', enabled: false })]),
      ...agentsRoutes(() => [builtInAgent, agentDto()]),
      ...integrationsRoutes(() => ({ email: emailSettings(), slack: slackSettings() })),
      'POST /api/widgets': () =>
        problemResponse(
          { title: 'One or more validation errors occurred.', errors: { 'settings.appearance.brandColor': ['Enter a color such as #0f766e.'] } },
          400,
        ),
    });
    const { unmount } = renderWithProviders(<WidgetsPage />, { route: '/widgets', path: '/widgets/:widgetId?' });

    await screen.findByRole('list', { name: 'Widgets' });
    await screen.findAllByText('Website helper');
    expect(await axeViolations()).toEqual([]);
    unmount();

    renderWithProviders(<WidgetsPage />, { route: '/widgets/new', path: '/widgets/:widgetId?' });
    await screen.findByRole('radiogroup', { name: 'Agent' });
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText('Choose an agent.')).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);

    await user.click(screen.getByRole('radio', { name: 'Website helper' }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'Shop help');
    for (const title of ['How it looks', 'Talk to a person', 'Review and create']) {
      await user.click(screen.getByRole('button', { name: 'Continue' }));
      await screen.findByRole('heading', { level: 1, name: title });
    }
    expect(await axeViolations()).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Create widget' }));
    expect(await screen.findByText('Enter a color such as #0f766e.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'How it looks' })).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
  });

  it('editor of a saved widget with its preview and embed code', async () => {
    mockFetch({
      ...widgetsRoutes(() => [widgetDto({ settings: widgetSettings({ appearance: { brandColor: '#8b5cf6' }, handover: { channel: 'slack' } }) })]),
      ...agentsRoutes(() => [builtInAgent, agentDto()]),
      ...integrationsRoutes(() => ({ email: workingEmail, slack: slackSettings() })),
    });
    const user = userEvent.setup();
    renderWithProviders(<WidgetsPage />, { route: `/widgets/${widgetDto().id}`, path: '/widgets/:widgetId?' });

    const frame = await screen.findByTitle('Widget preview');
    await fakePreview();
    const settings = screen.getByRole('region', { name: 'Settings' });
    // axe can't reach into frames under jsdom; the preview has its own test.
    expect(await axeViolations(screen.getByRole('heading', { level: 1 }).closest('header')!)).toEqual([]);
    await user.click(within(settings).getByRole('tab', { name: 'Settings' }));
    await user.click(within(settings).getByRole('button', { name: 'Handover' }));
    await screen.findByText(/Slack is not set up/);
    expect(within(settings).getByRole('button', { name: 'Copy snippet' })).toBeInTheDocument();
    expect(await axeViolations(settings)).toEqual([]);
    await user.click(within(settings).getByRole('tab', { name: 'Theme' }));
    await within(settings).findByText('Text on this color is hard to read');
    expect(await axeViolations(settings)).toEqual([]);
    await user.click(within(settings).getByRole('button', { name: 'Accent: theme' }));
    const picker = await screen.findByRole('dialog', { name: 'Accent color' });
    expect(await axeViolations(picker)).toEqual([]);
    await user.keyboard('{Escape}');
    await user.click(within(settings).getByRole('tab', { name: 'Elements' }));
    await user.click(within(settings).getByRole('button', { name: /^Launcher/ }));
    await within(settings).findByRole('heading', { level: 2, name: 'Launcher' });
    expect(await axeViolations(settings)).toEqual([]);
    await user.click(within(settings).getByRole('button', { name: 'Icon Speech bubble' }));
    expect(await axeViolations(await screen.findByRole('dialog', { name: 'Icon' }))).toEqual([]);
    expect(frame.tagName).toBe('IFRAME');
  });

  it('inbox with a conversation waiting for a person', async () => {
    const events = controlledEventStream();
    mockFetch({
      'GET /api/conversations/search': {
        items: [searchItem(), searchItem({ id: 'c2', title: 'Opening hours', status: 'ai', preview: 'We open at nine.', rating: 2, thumbsDown: 1 })],
        nextCursor: 'next',
      },
      'GET /api/conversations/counts': countsDto(),
      [`GET /api/conversations/${WIDGET_CONVERSATION_ID}`]: widgetConversation(),
      [`GET /api/conversations/${WIDGET_CONVERSATION_ID}/events`]: (_url: URL, init: RequestInit) => events.respond(init),
      [`GET /api/conversations/${WIDGET_CONVERSATION_ID}/handover`]: handoverDto({ lastError: 'SMTP server refused the message.' }),
      [`GET /api/conversations/${WIDGET_CONVERSATION_ID}/history`]: { feedback: [], handovers: [handoverDto()], labels: [] },
      ...widgetsRoutes(() => [widgetDto()]),
      ...agentsRoutes(() => [builtInAgent, agentDto()]),
    });
    renderWithProviders(<InboxPage />, { route: `/inbox?view=all&c=${WIDGET_CONVERSATION_ID}`, path: '/inbox/:conversationId?' });

    await screen.findByRole('list', { name: 'Conversations' });
    await screen.findByRole('navigation', { name: 'Inbox views' });
    await screen.findByRole('region', { name: 'Request for a person' });
    await screen.findByRole('article', { name: 'Assistant answer' });
    expect(await axeViolations()).toEqual([]);
  });

  it('inbox searching with the filters open', async () => {
    const user = userEvent.setup();
    mockFetch({
      'GET /api/conversations/search': { items: [searchItem({ match: 'Can I return shoes I wore once?' })], nextCursor: null },
      'GET /api/conversations/counts': countsDto(),
      ...widgetsRoutes(() => [widgetDto()]),
      ...agentsRoutes(() => [builtInAgent, agentDto()]),
    });
    renderWithProviders(<InboxPage />, { route: '/inbox?view=all&q=shoes', path: '/inbox/:conversationId?' });

    await screen.findAllByText('shoes', { selector: 'mark' });
    await user.click(screen.getByRole('button', { name: 'Add filter' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Started' }));
    await screen.findByRole('dialog', { name: 'Started' });
    expect(await axeViolations()).toEqual([]);
  });

  it('analytics page', async () => {
    mockFetch({
      'GET /api/analytics': {
        days: 30,
        from: '2026-10-07T00:00:00Z',
        to: '2026-11-05T12:00:00Z',
        totals: {
          conversations: 4,
          answeredWithoutPerson: 3,
          handedToPerson: 1,
          ratings: 1,
          averageRating: 4,
          thumbsUp: 2,
          thumbsDown: 1,
          noSourceAnswers: 1,
        },
        daily: Array.from({ length: 30 }, (_, index) => ({
          date: new Date(Date.UTC(2026, 9, 7 + index)).toISOString().slice(0, 10),
          conversations: index % 3,
          handedToPerson: index % 5 === 0 ? 1 : 0,
          thumbsUp: index % 2,
          thumbsDown: index % 7 === 0 ? 1 : 0,
          ratings: 0,
          averageRating: null,
        })),
        widgets: [{ widgetId: widgetDto().id, conversations: 4, handedToPerson: 1, thumbsUp: 2, thumbsDown: 1, ratings: 1, averageRating: 4 }],
        unanswered: [
          {
            question: 'Do you ship to Norway?',
            count: 2,
            reasons: ['thumbs_down'],
            lastAskedAt: '2026-11-04T10:00:00Z',
            conversationId: WIDGET_CONVERSATION_ID,
            widgetId: widgetDto().id,
            messageId: null,
            answer: null,
            comment: 'Wrong',
          },
        ],
      },
      ...widgetsRoutes(() => [widgetDto()]),
      ...agentsRoutes(() => [builtInAgent, agentDto()]),
    });
    renderWithProviders(<AnalyticsPage />, { route: '/analytics', path: '/analytics' });

    await screen.findByRole('list', { name: 'Questions the agent could not answer' });
    expect(await axeViolations()).toEqual([]);
  });

  it('feedback page', async () => {
    mockFetch({
      'GET /api/feedback/summary': [feedbackSummary(), feedbackSummary({ widgetId: null, agentId: null })],
      'GET /api/feedback': [
        feedbackDto(),
        portalRating,
        feedbackDto({ id: 'f3', kind: 'agent', rating: 0, reasons: [], comment: 'Asked for phone support.' }),
      ],
      ...widgetsRoutes(() => [widgetDto()]),
      ...agentsRoutes(() => [builtInAgent, agentDto()]),
    });
    renderWithProviders(<FeedbackPage />);

    await screen.findByRole('list', { name: 'Feedback' });
    await screen.findByRole('region', { name: 'Shop help' });
    expect(await axeViolations()).toEqual([]);
  });

  it('connections page with a saved and a missing setup', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...integrationsRoutes(() => ({ email: workingEmail, slack: slackSettings({ enabled: true, hasBotToken: true, saved: true }) })),
      'POST /api/integrations/slack/test': { ok: false, message: 'channel_not_found' },
      'GET /api/models/profiles': profiles,
    });
    renderWithProviders(<NotificationsPage />);

    const slack = await screen.findByRole('region', { name: 'Slack' });
    await user.click(within(slack).getByRole('button', { name: 'Send test message' }));
    expect(await axeViolations()).toEqual([]);
    await user.click(within(screen.getByRole('dialog', { name: 'Send a test message' })).getByRole('button', { name: 'Send' }));
    await within(slack).findByText('channel_not_found');
    expect(await axeViolations()).toEqual([]);
  });

  it('tools page with a connected and a failing MCP server, and the sheet of a new one', async () => {
    const user = userEvent.setup();
    mockFetch({
      'GET /api/connections/mcp': [
        mcpServer({ usedBy: [{ agentId: 'a1', agentName: 'Shop helper', toolCount: 2, mode: 'all' }] }),
        mcpServer({
          id: 's2',
          key: 'crm',
          name: 'CRM',
          lastCheck: { checkedAt: '2026-11-05T08:00:00Z', ok: false, error: 'The server could not be reached.' },
          tools: [cancelTool],
        }),
      ],
    });
    renderWithProviders(<ToolsPage />);

    await screen.findByRole('region', { name: 'Shop' });
    expect(await axeViolations()).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Add MCP server' }));
    await screen.findByRole('dialog');
    expect(await axeViolations()).toEqual([]);
  });

  it('contacts list and its empty state', async () => {
    mockFetch({
      'GET /api/people': { items: [ada, visitor], nextCursor: null },
      'GET /api/people/count': { count: 2 },
      'GET /api/people/attributes': registry,
      'GET /api/segments': [proSegment],
      ...widgetsRoutes(() => [widgetDto()]),
    });
    const { unmount } = renderWithProviders(<PeoplePage />, { route: '/contacts' });
    await screen.findByRole('table', { name: 'Contacts' });
    expect(await axeViolations()).toEqual([]);
    unmount();

    mockFetch({ 'GET /api/people': { items: [], nextCursor: null }, 'GET /api/auth/me': supportUser });
    renderWithProviders(<PeoplePage />, { route: '/contacts' });
    await screen.findByText('No people yet');
    expect(await axeViolations()).toEqual([]);
  });

  it('a contact, the erase dialog, and a contact that does not exist', async () => {
    const user = userEvent.setup();
    mockFetch({ [`GET /api/people/${ADA_ID}`]: adaProfile, 'GET /api/conversations/search': conversationsPage });
    const { unmount } = renderWithProviders(<PersonPage />, { route: `/contacts/${ADA_ID}`, path: '/contacts/:personId' });
    await screen.findByRole('heading', { level: 1, name: 'Ada Lovelace' });
    await screen.findByRole('link', { name: /Return an order/ });
    expect(await axeViolations()).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Erase' }));
    await screen.findByRole('dialog', { name: 'Erase Ada Lovelace?' });
    expect(await axeViolations()).toEqual([]);
    unmount();

    mockFetch({ [`GET /api/people/${ADA_ID}`]: () => problemResponse({ title: 'Not found' }, 404) });
    renderWithProviders(<PersonPage />, { route: `/contacts/${ADA_ID}`, path: '/contacts/:personId' });
    expect(await screen.findByRole('heading', { level: 1, name: 'This person does not exist' })).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
  });

  it('a dataset with its questions', async () => {
    const id = '0199c1e2-0000-7000-8000-0000000b0001';
    mockFetch({
      'GET /api/evals/datasets': evalDatasetList({
        datasets: [evalDataset({ id, name: 'Shipping questions', source: 'custom', itemCount: 1, corpusDocuments: 0 })],
      }),
      [`GET /api/evals/datasets/${id}/items`]: [
        {
          id: 'i1',
          question: 'Do you ship to Norway?',
          referenceAnswer: 'Yes, in 5 days.',
          expectedFacts: ['5 days'],
          expectedSources: [],
          answerable: true,
          tags: ['shipping'],
          conversationId: null,
          messageId: null,
          createdAt: '2026-11-04T10:00:00Z',
          updatedAt: '2026-11-04T10:00:00Z',
          source: 'manual',
          history: [],
        },
      ],
      ...evalRunsRoutes(() => []),
    });
    renderWithProviders(<DatasetPage />, { route: `/evals/datasets/${id}`, path: '/evals/datasets/:id' });

    await screen.findByRole('table', { name: 'Questions' });
    expect(await axeViolations()).toEqual([]);
  });

  it('an agents page that failed to load, and an agent that does not exist', async () => {
    mockFetch({ 'GET /api/agents': () => problemResponse({ title: 'Server error', detail: 'The database is not reachable.' }, 500) });
    const { unmount } = renderWithProviders(<AgentsPage />, { route: '/agents' });
    await screen.findByText('The database is not reachable.');
    expect(await axeViolations()).toEqual([]);
    unmount();

    mockFetch({ ...agentsRoutes(() => [builtInAgent]), 'GET /api/models/profiles': profiles });
    renderWithProviders(<AgentPage />, { route: `/agents/${agentGuid(9)}`, path: '/agents/:agentId' });
    expect(await screen.findByRole('heading', { level: 1, name: 'This agent does not exist' })).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
  });

  it('an open actions menu and the delete confirmation it leads to', async () => {
    const user = userEvent.setup();
    mockFetch({ ...agentsRoutes(() => [builtInAgent, agentDto()]), ...widgetsRoutes(() => [widgetDto()]) });
    renderWithProviders(<AgentsPage />, { route: '/agents' });

    const trigger = await screen.findByRole('button', { name: /^Actions for / });
    await user.click(trigger);
    await screen.findByRole('menu');
    expect(await axeViolations()).toEqual([]);

    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));
    await screen.findByRole('dialog');
    expect(await axeViolations()).toEqual([]);
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it('the alerts popover and the help panel with an answer', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...alertsRoutes(() => ({ alerts: [waitingAlert, alertDto()], snoozed: 0 })),
      'GET /api/help': {
        enabled: true,
        canManage: true,
        modelReady: true,
        setup: { chatModel: true, embeddingModel: true, sources: true, agent: true, widget: true },
      },
      'POST /api/help/chat': () =>
        sseResponse([
          { event: 'conversation', data: { agent: 'help', parentCallId: null, conversationId: 's1', title: 'Help', isNew: true } },
          { event: 'text-delta', data: { agent: 'help', parentCallId: null, text: 'Open [AI models](/models) and paste the key.' } },
          { event: 'done', data: { agent: 'help', parentCallId: null, conversationId: 's1', turnId: 't1', finishReason: 'stop' } },
        ]),
    });
    renderWithProviders(
      <>
        <AlertsBell />
        <HelpLauncher />
      </>,
      { route: '/sources' },
    );

    await user.click(await screen.findByRole('button', { name: /^Alerts, 2 things need attention/ }));
    await screen.findByRole('dialog', { name: 'Alerts' });
    expect(await axeViolations()).toEqual([]);
    await user.keyboard('{Escape}');

    await user.click(await screen.findByRole('button', { name: 'Help' }));
    const panel = await screen.findByRole('dialog', { name: 'Help' });
    expect(await axeViolations()).toEqual([]);
    await user.type(within(panel).getByRole('textbox', { name: 'Ask the help' }), 'How do I add an API key?{Enter}');
    await within(panel).findByRole('link', { name: 'AI models' });
    await waitFor(() =>
      expect(
        within(panel)
          .getAllByRole('status')
          .some((region) => region.textContent === 'The answer is ready.'),
      ).toBe(true),
    );
    expect(await axeViolations()).toEqual([]);
  });

  it('page not found and a page that failed', async () => {
    const { unmount } = renderWithProviders(<NotFoundPage />);
    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
    unmount();

    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const Broken = () => {
      throw new Error('The page broke.');
    };
    const router = createMemoryRouter([{ path: '/', element: <Broken />, errorElement: <RouteErrorPage /> }]);
    render(
      <QueryClientProvider client={createTestQueryClient()}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    );
    expect(await screen.findByRole('heading', { level: 1, name: 'Something went wrong' })).toBeInTheDocument();
    expect(await axeViolations()).toEqual([]);
  });

  it('jobs page inside the API process', async () => {
    mockFetch({ 'GET /api/jobs/overview': jobsOverview() });
    renderWithProviders(<SystemPage />, { route: '/system?tab=schedules' });

    await screen.findByText('Development mode');
    await screen.findByRole('region', { name: 'refresh-web-sources' });
    expect(await axeViolations()).toEqual([]);
  });
});
