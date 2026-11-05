import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, useLocation, type RouteObject } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { dismissAllToasts, Toaster } from '../../components/ui/Toast';
import { agentDto, agentGuid, agentOptions, agentsRoutes, builtInAgent } from '../../test/agentsApi';
import { sourceDto } from '../../test/fixtures';
import { sourceFolder, sourcesRoutes } from '../../test/sourcesApi';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderRouter, renderWithProviders } from '../../test/utils';
import { chooseOption } from '../../test/select';
import { widgetDto, widgetSettings, widgetsRoutes } from '../../test/widgetsApi';
import { buildFolderTree } from '../sources/folders';
import { AgentPage } from './AgentPage';
import {
  copyValues,
  initialAgentValues,
  keyFromName,
  sameFields,
  scopeSummary,
  SECTION_FIELDS,
  templateValues,
  toSaveRequest,
  withSection,
} from './agentForm';
import { agentSentence, namesList } from './agentSummary';
import { findTemplate, groupTools, instructionsGuidance, TONE_SENTENCES, toneOf, withSnippet, withTone, INSTRUCTION_SNIPPETS } from './agentTemplates';
import { firstOpenStepBefore, stepErrors, stepOfField } from './agentWizard';
import { AgentsPage } from './AgentsPage';
import { NewAgentPage } from './NewAgentWizard';
import type { AgentDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
  sessionStorage.clear();
  // Toasts live outside React, so one test's toast would show in the next.
  dismissAllToasts();
});

const folders = [sourceFolder('legal', 'Legal'), sourceFolder('contracts', 'Contracts', 'legal'), sourceFolder('hr', 'HR')];
const sources = [sourceDto({ id: 's1', folderId: 'contracts' }), sourceDto({ id: 's2', folderId: 'hr' })];
const helper = agentDto({ folderIds: ['contracts'], tools: ['search_knowledge_base', 'read_source'], profileName: 'utility' });
const overview = { days: 7, widgets: [{ widgetId: widgetDto().id, conversations: 12, lastConversationAt: null }] };
const analytics = {
  days: 7,
  from: '2026-10-29T00:00:00Z',
  to: '2026-11-05T00:00:00Z',
  totals: {},
  daily: [],
  widgets: [{ widgetId: widgetDto().id, conversations: 12, handedToPerson: 3, thumbsUp: 0, thumbsDown: 0, ratings: 2, averageRating: 4.5 }],
  unanswered: [],
  labels: [],
};

function routes(getAgents: () => readonly AgentDto[], extra: Record<string, unknown> = {}, widgets = [widgetDto()]) {
  return {
    ...agentsRoutes(getAgents),
    ...sourcesRoutes(() => sources, () => folders),
    ...widgetsRoutes(() => widgets),
    'GET /api/overview': overview,
    'GET /api/analytics': analytics,
    ...extra,
  };
}

function Landed() {
  const location = useLocation();
  return (
    <p data-testid="landed">
      {location.pathname}
      {location.search} {JSON.stringify(location.state)}
    </p>
  );
}

function Address() {
  const location = useLocation();
  return <p data-testid="address">{location.pathname + location.search}</p>;
}

const withToaster = (page: React.ReactNode) => (
  <>
    {page}
    <Address />
    <Toaster />
  </>
);

function renderAt(path: string) {
  return renderWithProviders(withToaster(<AgentsPage />), {
    route: path,
    path: '/agents',
    extraRoutes: (
      <>
        <Route path="/agents/new" element={withToaster(<NewAgentPage />)} />
        <Route path="/agents/:agentId" element={withToaster(<AgentPage />)} />
        <Route path="/widgets/:widgetId" element={<Landed />} />
        <Route path="/playground" element={<Landed />} />
      </>
    ),
  });
}

// In a data router, as the app has it, so the leave guard can block.
function renderInRouter(path: string) {
  const routeObjects: RouteObject[] = [
    { path: '/agents', element: withToaster(<AgentsPage />) },
    { path: '/agents/new', element: withToaster(<NewAgentPage />) },
    { path: '/agents/:agentId', element: withToaster(<AgentPage />) },
  ];
  return renderRouter(routeObjects, { route: path });
}

function card(name: string): HTMLElement {
  return screen.getByRole('article', { name });
}

function body(fetchMock: ReturnType<typeof mockFetch>, method: string): unknown {
  const call = fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method === method).at(-1);
  return JSON.parse((call![1] as RequestInit).body as string);
}

function createRoute(getAgents: () => AgentDto[], setAgents: (next: AgentDto[]) => void) {
  return {
    'POST /api/agents': (_url: URL, init: RequestInit) => {
      const created = agentDto({ ...(JSON.parse(init.body as string) as Partial<AgentDto>), id: agentGuid(3) });
      setAgents([...getAgents(), created]);
      return jsonResponse(created, 201);
    },
  };
}

const step = () => screen.getByRole('navigation', { name: 'Progress' });
const address = () => screen.getByTestId('address').textContent;

async function continueTo(user: ReturnType<typeof userEvent.setup>, title: string) {
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  expect(await screen.findByRole('heading', { level: 1, name: title })).toBeInTheDocument();
}

describe('AgentsPage', () => {
  it('shows each agent as a card with its purpose, knowledge, widgets and recent conversations', async () => {
    mockFetch(routes(() => [helper, builtInAgent]));
    renderAt('/agents');

    expect(await screen.findByRole('heading', { level: 1, name: 'Agents' })).toBeInTheDocument();
    expect(screen.getByText(/An agent is the assistant your visitors talk to/)).toBeInTheDocument();
    const list = await screen.findByRole('list', { name: 'Agents' });
    expect(
      within(list)
        .getAllByRole('heading', { level: 2 })
        .map((h) => h.textContent),
    ).toEqual(['Website helper']);
    expect(screen.queryByText('Knowledge Agent')).toBeNull();

    const website = card('Website helper');
    expect(within(website).getByText('Answers visitors of the public website.')).toBeInTheDocument();
    expect(await within(website).findByText('Legal / Contracts')).toBeInTheDocument();
    expect(await within(website).findByText('On Shop help')).toBeInTheDocument();
    expect(await within(website).findByText('12 conversations in 7 days')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Create an agent for your website/ })).toBeNull();
  });

  it('keeps duplicate and delete in the card menu, and a copy starts the wizard with its settings', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [builtInAgent, helper]));
    renderAt('/agents');

    await user.click(await screen.findByRole('button', { name: 'Actions for Website helper' }));
    expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Duplicate', 'Delete']);
    await user.click(screen.getByRole('menuitem', { name: 'Duplicate' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Name and language' })).toBeInTheDocument();
    expect(screen.getByText(/It starts as a copy of Website helper/)).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('Website helper copy');
    await continueTo(user, 'How it talks');
    await continueTo(user, 'What it knows');
    expect(await screen.findByRole('checkbox', { name: 'Contracts' })).toBeChecked();
    await user.click(within(step()).getByRole('button', { name: 'Start, done' }));
    expect(await screen.findByRole('radio', { name: /Copy of Website helper/ })).toBeChecked();
  });

  it('shows the empty state while the team has no agent of its own', async () => {
    mockFetch(routes(() => [builtInAgent]));
    renderAt('/agents');

    expect(await screen.findByRole('link', { name: /Create an agent for your website/ })).toHaveAttribute('href', '/agents/new');
    expect(within(screen.getByRole('list', { name: 'Agents' })).getAllByRole('listitem')).toHaveLength(1);
    expect(screen.getByRole('link', { name: 'New agent' })).toHaveAttribute('href', '/agents/new');
  });
});

describe('NewAgentWizard', () => {
  it('walks through the steps from a template, reviews, creates and offers the next steps', async () => {
    const user = userEvent.setup();
    let agents: AgentDto[] = [builtInAgent];
    const fetchMock = mockFetch(routes(() => agents, createRoute(() => agents, (next) => (agents = next))));
    renderAt('/agents');

    await user.click(await screen.findByRole('link', { name: 'New agent' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'How should it start?' })).toBeInTheDocument();
    expect(step()).toHaveTextContent('Step 1 of 5');
    const starts = within(screen.getByRole('radiogroup', { name: 'Starting point' })).getAllByRole('radio');
    expect(starts).toHaveLength(4);
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByText('Choose a starting point.')).toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: /Customer support/ }));
    expect(address()).toBe('/agents/new?template=support&step=start');
    await continueTo(user, 'Name and language');

    expect(step()).toHaveTextContent('Step 2 of 5');
    expect(address()).toBe('/agents/new?template=support&step=general');
    expect(screen.getByRole('heading', { level: 1, name: 'Name and language' })).toHaveFocus();
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('Customer support');
    expect(screen.getByText('customer-support')).toBeInTheDocument();
    const language = screen.getByRole('combobox', { name: 'Language' });
    expect(language).toHaveTextContent('Same as the visitor');
    await user.click(language);
    expect(await screen.findByRole('option', { name: /^Deutsch\s*German$/ })).toBeInTheDocument();
    await user.click(screen.getByRole('option', { name: 'English' }));
    expect(language).toHaveTextContent('English');
    await continueTo(user, 'How it talks');

    expect(screen.getByRole('radio', { name: 'Friendly' })).toBeChecked();
    expect(screen.getAllByText(/Happy to help!/).length).toBeGreaterThan(0);
    await continueTo(user, 'What it knows');

    expect(screen.getByRole('radio', { name: 'All sources' })).toBeChecked();
    expect(await screen.findByText('2 sources')).toBeInTheDocument();
    await continueTo(user, 'Tools and handover');

    expect(screen.getByRole('radio', { name: 'When asked or unsure' })).toBeChecked();
    const answers = screen.getByRole('group', { name: /^Find answers/ });
    expect(within(answers).getAllByRole('checkbox').every((box) => (box as HTMLInputElement).checked)).toBe(true);
    expect(screen.getByRole('checkbox', { name: 'Research hard questions' })).not.toBeChecked();
    await continueTo(user, 'Review and create');

    expect(step()).toHaveTextContent('Last check');
    expect(screen.getByTestId('agent-sentence')).toHaveTextContent('This agent answers from all your sources in English, friendly tone, offers a person when unsure.');
    await user.click(screen.getByRole('button', { name: 'Create agent' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Customer support' })).toBeInTheDocument();
    expect(await screen.findByText('Agent created')).toBeInTheDocument();
    expect(address()).toBe(`/agents/${agentGuid(3)}`);
    const next = screen.getByRole('region', { name: 'Customer support is ready' });
    const test = within(next).getByRole('link', { name: 'Test it in the Playground' });
    expect(test).toHaveAttribute('href', `/playground?agentId=${agentGuid(3)}`);
    expect(test).toHaveClass('action-primary');
    expect(body(fetchMock, 'POST')).toEqual({
      name: 'Customer support',
      key: 'customer-support',
      description: 'Answers customer questions from our help content and brings in the team when needed.',
      instructions: findTemplate('support')!.instructions,
      language: 'English',
      profileName: 'orchestrator',
      folderIds: [],
      tools: ['search_knowledge_base', 'grep_sources', 'find_sources', 'read_source'],
      allowGeneralKnowledge: false,
      handoverPolicy: 'whenUnsure',
    });

    await user.click(within(next).getByRole('button', { name: /Put it on your website/ }));
    await user.click(screen.getByRole('menuitem', { name: 'Create a widget' }));
    expect(await screen.findByTestId('landed')).toHaveTextContent(
      `/widgets/new {"agentId":"${agentGuid(3)}","returnTo":{"label":"Customer support","to":"/agents/${agentGuid(3)}"}}`,
    );
  });

  it('checks each step, and changes from the review return to their step', async () => {
    const user = userEvent.setup();
    let agents: AgentDto[] = [builtInAgent];
    const fetchMock = mockFetch(routes(() => agents, createRoute(() => agents, (next) => (agents = next))));
    renderAt('/agents/new?template=blank');

    expect(await screen.findByRole('heading', { level: 1, name: 'Name and language' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByText('Enter a name.')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveFocus();
    expect(fetchCalls(fetchMock)).not.toContain('POST /api/agents');

    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Support desk');
    await user.click(screen.getByRole('button', { name: 'Change key' }));
    expect(screen.getByRole('textbox', { name: 'Key' })).toHaveValue('support-desk');
    await user.type(screen.getByRole('textbox', { name: /^Purpose/ }), 'Helps customers.');
    await continueTo(user, 'How it talks');
    expect(screen.getByRole('radio', { name: 'Own words' })).toBeChecked();
    await user.click(screen.getByRole('radio', { name: 'Brief' }));
    await continueTo(user, 'What it knows');

    await user.click(screen.getByRole('radio', { name: 'Chosen folders' }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByText('Choose at least one folder, or All sources.')).toBeInTheDocument();
    await user.click(await screen.findByRole('checkbox', { name: 'Legal' }));
    const contracts = screen.getByRole('checkbox', { name: /Contracts/ });
    expect(contracts).toBeChecked();
    expect(contracts).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: 'Legal' })).toHaveAccessibleDescription('1 source');
    expect(screen.getByText(/1 source in the chosen folders/)).toBeInTheDocument();
    await user.click(screen.getByRole('switch', { name: 'May answer from general knowledge' }));
    await continueTo(user, 'Tools and handover');

    expect(screen.getByRole('checkbox', { name: 'Research hard questions' })).toBeChecked();
    await user.click(screen.getByRole('checkbox', { name: 'Research hard questions' }));
    await continueTo(user, 'Review and create');

    expect(screen.getByTestId('agent-sentence')).toHaveTextContent("This agent answers from 1 folder in the visitor's language, brief answers, fills gaps from general knowledge");
    await user.click(screen.getByRole('button', { name: 'Change knows' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'What it knows' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Chosen folders' })).toBeChecked();
    await user.click(within(step()).getByRole('button', { name: 'General, done' }));
    expect(await screen.findByRole('textbox', { name: 'Name' })).toHaveValue('Support desk');
    await continueTo(user, 'How it talks');
    await continueTo(user, 'What it knows');
    await continueTo(user, 'Tools and handover');
    await continueTo(user, 'Review and create');
    await user.click(screen.getByRole('button', { name: 'Create agent' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Support desk' })).toBeInTheDocument();
    expect(body(fetchMock, 'POST')).toEqual({
      name: 'Support desk',
      key: 'support-desk',
      description: 'Helps customers.',
      instructions: TONE_SENTENCES.brief,
      language: null,
      profileName: 'orchestrator',
      folderIds: ['legal'],
      tools: ['search_knowledge_base', 'grep_sources', 'find_sources', 'read_source'],
      allowGeneralKnowledge: true,
      handoverPolicy: 'onRequest',
    });
  });

  it('returns to the step of a field the API refused, and lists the others on the review', async () => {
    const user = userEvent.setup();
    mockFetch(
      routes(() => [builtInAgent], {
        'POST /api/agents': () =>
          problemResponse(
            {
              title: 'One or more validation errors occurred.',
              errors: {
                key: ["An agent with the key 'customer-support' already exists."],
                instructions: ['The instructions must be at most 8000 characters long.'],
              },
            },
            400,
          ),
      }),
    );
    renderAt('/agents/new?template=support&step=review');

    await user.click(await screen.findByRole('button', { name: 'Create agent' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Name and language' })).toBeInTheDocument();
    const key = screen.getByRole('textbox', { name: 'Key' });
    expect(key).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText("An agent with the key 'customer-support' already exists.")).toBeInTheDocument();
    await waitFor(() => expect(key).toHaveFocus());
    await continueTo(user, 'How it talks');
    expect(screen.getByText('The instructions must be at most 8000 characters long.')).toBeInTheDocument();
  });

  it('keeps the step in the address, opens the first step left to do, and keeps a draft over a reload', async () => {
    mockFetch(routes(() => [builtInAgent]));
    const { unmount } = renderAt('/agents/new?template=support&step=people');
    expect(await screen.findByRole('heading', { level: 1, name: 'Tools and handover' })).toBeInTheDocument();
    expect(step()).toHaveTextContent('Step 5 of 5');
    unmount();

    const second = renderAt('/agents/new?template=blank&step=review');
    expect(await screen.findByRole('heading', { level: 1, name: 'Name and language' })).toBeInTheDocument();
    await waitFor(() => expect(address()).toBe('/agents/new?template=blank&step=general'));
    second.unmount();

    sessionStorage.setItem('ka.new-agent-draft', JSON.stringify({ origin: 'template:blank', values: { name: 'Kept name' } }));
    renderAt('/agents/new?template=blank&step=knowledge');
    expect(await screen.findByRole('heading', { level: 1, name: 'What it knows' })).toBeInTheDocument();
    await userEvent.setup().click(within(step()).getByRole('button', { name: 'General, done' }));
    expect(await screen.findByRole('textbox', { name: 'Name' })).toHaveValue('Kept name');
  });

  it('cancels with Esc and asks first once something was entered', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [builtInAgent]));
    renderInRouter('/agents/new?template=support');

    const name = await screen.findByRole('textbox', { name: 'Name' });
    await continueTo(user, 'How it talks');
    await user.click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Name and language' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();

    await user.clear(screen.getByRole('textbox', { name: 'Name' }));
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Changed');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    const confirm = await screen.findByRole('dialog', { name: 'Leave without saving?' });
    expect(confirm).toHaveTextContent('This agent is not created yet.');
    await user.click(within(confirm).getByRole('button', { name: 'Cancel' }));
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('Changed');

    await user.keyboard('{Escape}');
    await user.click(within(await screen.findByRole('dialog', { name: 'Leave without saving?' })).getByRole('button', { name: 'Leave' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Agents' })).toBeInTheDocument();
    expect(name).not.toBeInTheDocument();
  });

  it('leaves at once with Esc while nothing was entered', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [builtInAgent]));
    renderInRouter('/agents/new');

    expect(await screen.findByRole('heading', { level: 1, name: 'How should it start?' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(await screen.findByRole('heading', { level: 1, name: 'Agents' })).toBeInTheDocument();
  });
});

describe('AgentPage', () => {
  it('shows the agent at a glance with its widgets, last 7 days and an Edit per setting', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [builtInAgent, helper]));
    renderAt(`/agents/${helper.id}`);

    expect(await screen.findByRole('heading', { level: 1, name: 'Website helper' })).toBeInTheDocument();
    expect(screen.getByText('Answers visitors of the public website.')).toBeInTheDocument();
    expect(await screen.findByTestId('agent-status')).toHaveTextContent('Used by 1 widget');
    expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Overview', 'General', 'Behavior', 'Knowledge', 'Tools', 'Handover', 'Specialists', 'Use elsewhere', 'Advanced']);
    expect(screen.getByRole('link', { name: 'Test in Playground' })).toHaveAttribute('href', `/playground?agentId=${helper.id}`);
    expect(await screen.findByTestId('agent-sentence')).toHaveTextContent(
      "This agent answers from 1 folder in the visitor's language, tone in your own words, brings in a person when asked, used by Shop help.",
    );
    expect(await screen.findByText('Legal / Contracts')).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Widgets using this agent' })).getByRole('link', { name: 'Shop help' })).toHaveAttribute(
      'href',
      `/widgets/${widgetDto().id}`,
    );
    const channels = screen.getByRole('region', { name: 'Where it answers' });
    expect(channels).toHaveTextContent('Widgets show this agent on your websites.');
    expect(within(channels).getByText('Website widget')).toBeInTheDocument();
    const activity = await screen.findByRole('region', { name: 'Last 7 days' });
    expect(await within(activity).findByText('12')).toBeInTheDocument();
    expect(within(activity).getByText('9')).toBeInTheDocument();
    expect(within(activity).getByText('75% of conversations')).toBeInTheDocument();
    expect(within(activity).getByText('4.5')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save changes' })).toBeNull();

    await user.click(screen.getByRole('button', { name: 'Edit knows' }));
    expect(screen.getByRole('tab', { name: 'Knowledge' })).toHaveAttribute('aria-selected', 'true');
    expect(address()).toBe(`/agents/${helper.id}?tab=knowledge`);
    expect(screen.getByRole('radio', { name: 'Chosen folders' })).toBeChecked();
  });

  it('never offers the Internal folder in the Knowledge picker and leaves its sources out of All sources', async () => {
    const user = userEvent.setup();
    const withInternal = [
      ...folders,
      { ...sourceFolder('internal', 'Internal'), isInternal: true },
      sourceFolder('runbooks', 'Runbooks', 'internal'),
    ];
    const withInternalSources = [...sources, sourceDto({ id: 's3', folderId: 'runbooks' })];
    // Runbooks has since moved into Internal.
    const stale = agentDto({ folderIds: ['contracts', 'runbooks'] });
    const fetchMock = mockFetch({
      ...routes(() => [builtInAgent, stale], {
        [`PUT /api/agents/${stale.id}`]: (_url: URL, init: RequestInit) => jsonResponse({ ...stale, ...(JSON.parse(init.body as string) as object) }),
      }),
      ...sourcesRoutes(
        () => withInternalSources,
        () => withInternal,
      ),
    });
    renderAt(`/agents/${stale.id}?tab=knowledge`);

    expect(await screen.findByRole('checkbox', { name: 'Contracts' })).toBeChecked();
    expect(screen.queryByRole('checkbox', { name: /Internal|Runbooks/ })).toBeNull();
    expect(screen.getByText(/1 chosen folder no longer exists and is removed when you save/)).toBeInTheDocument();
    expect(screen.getByText(/Sources in the Internal folder are only for the help assistant/)).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /All sources/ }).closest('label')).toHaveTextContent('2 sources');

    await user.click(screen.getByRole('checkbox', { name: 'HR' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(body(fetchMock, 'PUT')).toMatchObject({ folderIds: ['contracts', 'hr'] }));
  });

  it('saves each tab on its own, with a save bar only while that tab has changes', async () => {
    const user = userEvent.setup();
    let agents: AgentDto[] = [builtInAgent, helper];
    const save = (saved: AgentDto) => (agents = agents.map((item) => (item.id === saved.id ? saved : item)));
    const fetchMock = mockFetch(
      routes(() => agents, {
        [`PUT /api/agents/${helper.id}`]: (_url: URL, init: RequestInit) => {
          const current = agents.find((item) => item.id === helper.id)!;
          const saved = { ...current, ...(JSON.parse(init.body as string) as object) } as AgentDto;
          save(saved);
          return jsonResponse(saved);
        },
      }),
    );
    renderAt(`/agents/${helper.id}?tab=general`);

    const name = await screen.findByRole('textbox', { name: 'Name' });
    expect(screen.queryByText('Unsaved changes')).toBeNull();
    expect(screen.queryByRole('textbox', { name: 'Key' })).toBeNull();
    await user.clear(name);
    await user.type(name, 'Website guide');
    expect(screen.getByText('Unsaved changes')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(name).toHaveValue('Website helper');
    expect(screen.queryByText('Unsaved changes')).toBeNull();

    await user.clear(name);
    await user.type(name, 'Website guide');
    await user.click(screen.getByRole('combobox', { name: 'Language' }));
    await user.click(await screen.findByRole('option', { name: /^Slovenčina/ }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('Agent saved')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('Unsaved changes')).toBeNull());
    expect(await screen.findByRole('heading', { level: 1, name: 'Website guide' })).toBeInTheDocument();
    expect(body(fetchMock, 'PUT')).toEqual({
      name: 'Website guide',
      description: 'Answers visitors of the public website.',
      instructions: 'Be friendly.',
      language: 'Slovak',
      profileName: 'utility',
      folderIds: ['contracts'],
      tools: ['search_knowledge_base', 'read_source'],
      allowGeneralKnowledge: false,
      handoverPolicy: 'onRequest',
    });

    const puts = () => fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method === 'PUT').length;

    await user.click(screen.getByRole('tab', { name: 'Behavior' }));
    await user.click(await screen.findByRole('button', { name: 'Stay on topic' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(puts()).toBe(2));
    expect(body(fetchMock, 'PUT')).toMatchObject({ name: 'Website guide', language: 'Slovak', instructions: `Be friendly.\n${INSTRUCTION_SNIPPETS[0]!.text}` });

    await user.click(screen.getByRole('tab', { name: 'Tools' }));
    await user.click(await screen.findByRole('checkbox', { name: 'Find exact words' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(puts()).toBe(3));

    await user.click(screen.getByRole('tab', { name: 'Handover' }));
    await user.click(await screen.findByRole('radio', { name: 'Never' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(puts()).toBe(4));
    expect(body(fetchMock, 'PUT')).toMatchObject({
      name: 'Website guide',
      handoverPolicy: 'never',
      tools: ['search_knowledge_base', 'grep_sources', 'read_source'],
      folderIds: ['contracts'],
    });

    await user.click(screen.getByRole('tab', { name: 'Knowledge' }));
    await user.click(await screen.findByRole('switch', { name: 'May answer from general knowledge' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(puts()).toBe(5));

    await user.click(screen.getByRole('tab', { name: 'Advanced' }));
    const model = screen.getByRole('combobox', { name: 'Model' });
    expect(model).toHaveTextContent('Background tasks model (utility)');
    expect(screen.getByText('website-helper')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Answering agent' })).toHaveAttribute('href', '/prompts/orchestrator-system');
    await chooseOption(user, model, 'Answering model (orchestrator)');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(body(fetchMock, 'PUT')).toMatchObject({ profileName: 'orchestrator', allowGeneralKnowledge: true, handoverPolicy: 'never' }));
  });

  it('checks the knowledge tab and shows the field errors the API sends back', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch(
      routes(() => [builtInAgent, helper], {
        [`PUT /api/agents/${helper.id}`]: () =>
          problemResponse({ title: 'One or more validation errors occurred.', errors: { name: ['The name is taken.'], folderIds: ['These folders do not exist: 4f1c.'] } }, 400),
      }),
    );
    renderAt(`/agents/${helper.id}?tab=knowledge`);

    await user.click(await screen.findByRole('checkbox', { name: 'Contracts' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(screen.getByText('Choose at least one folder, or All sources.')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).not.toContain(`PUT /api/agents/${helper.id}`);

    await user.click(screen.getByRole('checkbox', { name: 'HR' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('These folders do not exist: 4f1c.')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('The name is taken.');
  });

  it('asks before a tab or the page is left with unsaved changes', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [builtInAgent, helper]));
    renderInRouter(`/agents/${helper.id}?tab=general`);

    const name = await screen.findByRole('textbox', { name: 'Name' });
    await user.type(name, ' 2');
    await user.click(screen.getByRole('tab', { name: 'Knowledge' }));
    const confirm = await screen.findByRole('dialog', { name: 'Leave without saving?' });
    await user.click(within(confirm).getByRole('button', { name: 'Cancel' }));
    expect(screen.getByRole('tab', { name: 'General' })).toHaveAttribute('aria-selected', 'true');
    expect(name).toHaveValue('Website helper 2');

    await user.click(screen.getByRole('tab', { name: 'Knowledge' }));
    await user.click(within(await screen.findByRole('dialog', { name: 'Leave without saving?' })).getByRole('button', { name: 'Leave' }));
    expect(screen.getByRole('tab', { name: 'Knowledge' })).toHaveAttribute('aria-selected', 'true');
    await user.click(screen.getByRole('tab', { name: 'General' }));
    expect(await screen.findByRole('textbox', { name: 'Name' })).toHaveValue('Website helper');
  });

  it('lists where each widget of the agent sends requests for a person', async () => {
    const slack = widgetDto({
      id: 'w-slack',
      name: 'Demo shop',
      settings: widgetSettings({ behavior: { handoverEnabled: true }, handover: { channel: 'slack' } }),
    });
    const inbox = widgetDto({ id: 'w-inbox', name: 'Docs help', settings: widgetSettings({ behavior: { handoverEnabled: true } }) });
    const quiet = widgetDto({ id: 'w-quiet', name: 'Careers' });
    mockFetch(routes(() => [builtInAgent, helper], {}, [slack, inbox, quiet]));
    renderAt(`/agents/${helper.id}?tab=handover`);

    const usedBy = await screen.findByRole('list', { name: 'Used by' });
    expect(within(usedBy).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      'Demo shop: Inbox, Slack',
      'Docs help: Inbox',
      'Careers: no person offered',
    ]);
    expect(within(usedBy).getByRole('link', { name: 'Demo shop' })).toHaveAttribute('href', '/widgets/w-slack');
    expect(screen.queryByText(/No widget offers a person/)).toBeNull();
  });

  it('switches an existing widget to the agent after confirmation', async () => {
    const user = userEvent.setup();
    const other = agentDto({ id: agentGuid(5), key: 'careers', name: 'Careers bot' });
    const careers = widgetDto({ id: 'w-careers', name: 'Careers', agentId: other.id });
    let widgets = [widgetDto(), careers];
    const fetchMock = mockFetch(
      routes(
        () => [builtInAgent, helper, other],
        {
          'PUT /api/widgets/w-careers': (_url: URL, init: RequestInit) => {
            const saved = { ...careers, ...(JSON.parse(init.body as string) as object) };
            widgets = [widgetDto(), saved];
            return jsonResponse(saved);
          },
        },
        widgets,
      ),
    );
    renderAt(`/agents/${helper.id}`);

    await user.click(await screen.findByRole('button', { name: /Put it on your website/ }));
    expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Create a widget', 'Use on Careers']);
    await user.click(screen.getByRole('menuitem', { name: 'Use on Careers' }));
    const confirm = await screen.findByRole('dialog', { name: 'Use Website helper on Careers?' });
    expect(confirm).toHaveTextContent('Visitors of Careers will talk to Website helper instead of Careers bot.');
    await user.click(within(confirm).getByRole('button', { name: 'Use this agent' }));

    expect(await screen.findByText('Careers now answers with Website helper')).toBeInTheDocument();
    expect(body(fetchMock, 'PUT')).toMatchObject({ name: 'Careers', agentId: helper.id, enabled: true });
  });

  it('sends a link to the built-in agent, which is no agent of the team, to the agents list', async () => {
    mockFetch(routes(() => [builtInAgent, helper]));
    renderAt(`/agents/${builtInAgent.id}?tab=knowledge`);

    expect(await screen.findByRole('heading', { level: 1, name: 'Agents' })).toBeInTheDocument();
    expect(address()).toBe('/agents');
    expect(screen.queryByText('Knowledge Agent')).toBeNull();
  });

  it('duplicates an agent from its page', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [builtInAgent, helper]));
    renderAt(`/agents/${helper.id}`);

    await user.click(await screen.findByRole('button', { name: 'More actions for this agent' }));
    await user.click(screen.getByRole('menuitem', { name: 'Duplicate' }));
    expect(await screen.findByRole('textbox', { name: 'Name' })).toHaveValue('Website helper copy');
    expect(address()).toBe(`/agents/new?from=${helper.id}`);
  });

  it('adds a specialist on its own tab, never one that already hands off to this agent', async () => {
    const user = userEvent.setup();
    const billing = agentDto({ id: agentGuid(3), key: 'billing', name: 'Billing', description: 'Invoices and refunds' });
    const orders = agentDto({ id: agentGuid(4), key: 'orders', name: 'Orders', specialists: [{ agentId: helper.id, whenToUse: 'General questions' }] });
    let agents: AgentDto[] = [builtInAgent, helper, billing, orders];
    const fetchMock = mockFetch(
      routes(() => agents, {
        [`PUT /api/agents/${helper.id}`]: (_url: URL, init: RequestInit) => {
          const saved = { ...helper, ...(JSON.parse(init.body as string) as object) } as AgentDto;
          agents = agents.map((item) => (item.id === saved.id ? saved : item));
          return jsonResponse(saved);
        },
      }),
    );
    renderAt(`/agents/${helper.id}?tab=specialists`);

    expect(await screen.findByText('No specialists yet, so it answers every question itself.')).toBeInTheDocument();
    const add = screen.getByRole('combobox', { name: 'Add a specialist' });
    await user.click(add);
    expect(await screen.findByRole('option', { name: /Orders/ })).toHaveAttribute('aria-disabled', 'true');
    await user.click(screen.getByRole('option', { name: 'Billing' }));
    const when = screen.getByRole('textbox', { name: 'When to hand off' });
    expect(when).toHaveValue('Invoices and refunds');
    await user.clear(when);
    await user.type(when, 'Invoices, payments and refunds');
    await user.click(screen.getByRole('switch', { name: "Show the specialist's name" }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    expect(await screen.findByText('Agent saved')).toBeInTheDocument();
    expect(body(fetchMock, 'PUT')).toMatchObject({
      specialists: [{ agentId: billing.id, whenToUse: 'Invoices, payments and refunds' }],
      showSpecialistName: true,
    });
    await user.click(screen.getByRole('tab', { name: 'Overview' }));
    expect(await screen.findByText('Hands off to Billing')).toBeInTheDocument();
  });

  it('asks before deleting an agent that others hand conversations to', async () => {
    const user = userEvent.setup();
    const front = agentDto({ id: agentGuid(3), key: 'front', name: 'Front desk', specialists: [{ agentId: helper.id, whenToUse: 'Website questions' }] });
    let agents: AgentDto[] = [builtInAgent, helper, front];
    const asked: string[] = [];
    mockFetch(
      routes(() => agents, {
        [`DELETE /api/agents/${helper.id}`]: (url: URL) => {
          asked.push(url.search);
          if (!url.searchParams.has('removeFromTeams')) {
            return problemResponse({ title: 'Other agents hand off to this agent', code: 'agent_is_specialist', agents: [{ id: front.id, name: 'Front desk' }] }, 409);
          }
          agents = [builtInAgent, { ...front, specialists: [] }];
          return new Response(null, { status: 204 });
        },
      }),
    );
    renderAt(`/agents/${helper.id}`);

    await user.click(await screen.findByRole('button', { name: 'More actions for this agent' }));
    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));
    const confirm = await screen.findByRole('dialog', { name: 'Delete agent?' });
    await user.click(within(confirm).getByRole('button', { name: 'Delete' }));
    expect(await within(confirm).findByText(/Front desk hands conversations to “Website helper”/)).toBeInTheDocument();
    await user.click(within(confirm).getByRole('button', { name: 'Remove and delete' }));

    expect(await screen.findByText('Agent deleted')).toBeInTheDocument();
    expect(asked).toEqual(['', '?removeFromTeams=true']);
  });

  it('deletes an agent from its card after confirmation', async () => {
    const user = userEvent.setup();
    let agents: AgentDto[] = [builtInAgent, helper];
    const fetchMock = mockFetch(
      routes(() => agents, {
        [`DELETE /api/agents/${helper.id}`]: () => {
          agents = [builtInAgent];
          return new Response(null, { status: 204 });
        },
      }),
    );
    renderAt('/agents');

    await user.click(await screen.findByRole('button', { name: 'Actions for Website helper' }));
    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));
    const confirm = await screen.findByRole('dialog', { name: 'Delete agent?' });
    expect(confirm).toHaveTextContent('“Website helper” will be deleted. Its conversations continue with the built-in agent.');
    await user.click(within(confirm).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(screen.queryByRole('heading', { level: 2, name: 'Website helper' })).toBeNull());
    expect(await screen.findByText('Agent deleted')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain(`DELETE /api/agents/${helper.id}`);
  });

  it('deletes an agent from its page, and shows why the API refused a delete', async () => {
    const user = userEvent.setup();
    let attempts = 0;
    let agents: AgentDto[] = [builtInAgent, helper];
    mockFetch(
      routes(() => agents, {
        [`DELETE /api/agents/${helper.id}`]: () => {
          attempts += 1;
          if (attempts === 1) {
            return problemResponse({ title: 'One or more validation errors occurred.', errors: { id: ["The widget 'Shop help' uses this agent."] } }, 400);
          }
          agents = [builtInAgent];
          return new Response(null, { status: 204 });
        },
      }),
    );
    renderAt(`/agents/${helper.id}`);

    await user.click(await screen.findByRole('button', { name: 'More actions for this agent' }));
    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));
    const confirm = await screen.findByRole('dialog', { name: 'Delete agent?' });
    await user.click(within(confirm).getByRole('button', { name: 'Delete' }));
    expect(await within(confirm).findByRole('alert')).toHaveTextContent("The widget 'Shop help' uses this agent.");

    await user.click(within(confirm).getByRole('button', { name: 'Delete' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Agents' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 2, name: 'Website helper' })).toBeNull();
  });

  it('says when an agent does not exist', async () => {
    mockFetch(routes(() => [builtInAgent]));
    renderAt(`/agents/${agentGuid(9)}`);

    expect(await screen.findByText('This agent does not exist')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'All agents' })).toHaveAttribute('href', '/agents');
  });
});

describe('wizard steps and tab saves', () => {
  const options = agentOptions();

  it('checks a step only for its own fields and finds the first step left to do', () => {
    const blank = templateValues(findTemplate('blank'), options);
    const context = { isNew: true, options };
    expect(stepErrors('start', blank, false, context)).toEqual({ start: 'Choose a starting point.' });
    expect(stepErrors('general', blank, true, context)).toEqual({ name: 'Enter a name.' });
    expect(stepErrors('knowledge', blank, true, context)).toEqual({});
    expect(stepErrors('knowledge', { ...blank, scope: 'folders' }, true, context)).toEqual({ folderIds: 'Choose at least one folder, or All sources.' });
    expect(firstOpenStepBefore('review', blank, true, context)).toBe('general');
    expect(firstOpenStepBefore('review', { ...blank, name: 'A', key: 'a' }, true, context)).toBeNull();
    expect(firstOpenStepBefore('general', blank, false, context)).toBe('start');
    expect(stepOfField('key')).toBe('general');
    expect(stepOfField('instructions')).toBe('behavior');
    expect(stepOfField('profileName')).toBe('review');
  });

  it('puts one section of a draft on top of the saved agent', () => {
    const saved = initialAgentValues(helper, options);
    const draft = { ...saved, name: 'Changed', handoverPolicy: 'never' as const };
    expect(withSection(saved, draft, 'handover')).toEqual({ ...saved, handoverPolicy: 'never' });
    expect(sameFields(saved, draft, SECTION_FIELDS.knowledge)).toBe(true);
    expect(sameFields(saved, { ...saved, tools: [...saved.tools].reverse() }, SECTION_FIELDS.tools)).toBe(true);
    expect(sameFields(saved, draft, SECTION_FIELDS.general)).toBe(false);
    const untouched = { ...saved, mcpApps: [{ serverId: 's1', enabled: false, mode: 'all' as const, changesPolicy: 'confirm' as const, tools: [] }] };
    expect(sameFields(saved, untouched, SECTION_FIELDS.tools)).toBe(true);
    expect(sameFields(saved, { ...untouched, mcpApps: [{ ...untouched.mcpApps[0]!, enabled: true }] }, SECTION_FIELDS.tools)).toBe(false);
  });
});

describe('agent form', () => {
  const options = { tools: ['search_knowledge_base', 'grep_sources'], profiles: ['orchestrator', 'utility'] };

  it('derives a key from the name', () => {
    expect(keyFromName('Website helper')).toBe('website-helper');
    expect(keyFromName('  Medveď & spol. ')).toBe('medved-spol');
    expect(keyFromName('---')).toBe('');
  });

  it('fills a new agent from a template with only the tools the server offers, and copies an agent under a new name', () => {
    const support = templateValues(findTemplate('support'), options);
    expect(support).toMatchObject({ name: 'Customer support', key: 'customer-support', handoverPolicy: 'whenUnsure', tools: ['search_knowledge_base', 'grep_sources'] });
    expect(templateValues(findTemplate('blank'), options).tools).toEqual(options.tools);
    expect(copyValues(agentDto(), options)).toMatchObject({ name: 'Website helper copy', key: 'website-helper-copy', keyEdited: false });
  });

  it('drops folders deleted since the agent was saved and never sends an empty folder list for chosen folders', () => {
    const values = { ...initialAgentValues(agentDto({ folderIds: ['gone', 'legal'] }), options) };
    expect(toSaveRequest(values, { isNew: false, options, knownFolderIds: new Set(['legal']) }).request?.folderIds).toEqual(['legal']);
    expect(toSaveRequest(values, { isNew: false, options, knownFolderIds: new Set() })).toEqual({
      request: null,
      errors: { folderIds: 'Choose at least one folder, or All sources.' },
    });
    expect(toSaveRequest({ ...values, scope: 'all' }, { isNew: false, options }).request?.folderIds).toEqual([]);
  });

  it('checks the key of a new agent only', () => {
    const values = { ...initialAgentValues(null, options), name: 'Helper', key: 'Not valid' };
    expect(toSaveRequest(values, { isNew: true, options }).errors.key).toMatch(/lowercase letters/);
    expect(toSaveRequest(values, { isNew: false, options }).request).not.toHaveProperty('key');
  });

  it('summarizes the knowledge scope', () => {
    const tree = buildFolderTree(folders);
    expect(scopeSummary([], tree)).toBe('All sources');
    expect(scopeSummary(['contracts', 'hr'], tree)).toBe('Legal / Contracts, HR');
    expect(scopeSummary(['hr', 'gone'], tree)).toBe('HR, 1 deleted folder');
    expect(scopeSummary(['hr', 'gone'], undefined)).toBe('2 folders');
  });
});

describe('agent templates and summary', () => {
  it('swaps the tone line and leaves the rest of the instructions alone', () => {
    const text = `${TONE_SENTENCES.friendly}\nOnly talk about shoes.`;
    expect(toneOf(text)).toBe('friendly');
    expect(withTone(text, 'brief')).toBe(`${TONE_SENTENCES.brief}\nOnly talk about shoes.`);
    expect(withTone(text, 'own')).toBe('Only talk about shoes.');
    expect(withTone('', 'professional')).toBe(TONE_SENTENCES.professional);
    expect(toneOf('Only talk about shoes.')).toBe('own');
  });

  it('adds snippets on their own line and guides the length', () => {
    expect(withSnippet('Be kind.  \n', INSTRUCTION_SNIPPETS[1]!)).toBe(`Be kind.\n${INSTRUCTION_SNIPPETS[1]!.text}`);
    expect(instructionsGuidance(0, 8000).text).toMatch(/A few clear sentences/);
    expect(instructionsGuidance(1200, 8000)).toEqual({ text: '1,200 of 8,000 characters. Most good instructions are 200 to 1,500.', long: false });
    expect(instructionsGuidance(4000, 8000).long).toBe(true);
  });

  it('groups tools by purpose and keeps unknown ones under Other', () => {
    expect(groupTools(['read_source', 'record_feedback', 'search_knowledge_base', 'new_tool']).map((group) => [group.title, group.tools])).toEqual([
      ['Find answers', ['read_source', 'search_knowledge_base']],
      ['Collect feedback', ['record_feedback']],
      ['Other', ['new_tool']],
    ]);
  });

  it('says what the agent does in one sentence', () => {
    const values = { ...templateValues(findTemplate('support'), { tools: [], profiles: [] }), scope: 'folders' as const, folderIds: ['a', 'b', 'c'], language: 'English' };
    expect(agentSentence(values, { widgetNames: ['Demo shop'] })).toBe(
      'This agent answers from 3 folders in English, friendly tone, offers a person when unsure, used by Demo shop.',
    );
    expect(agentSentence(values, { widgetNames: [] })).toMatch(/not on your website yet\.$/);
    expect(namesList(['A', 'B', 'C'])).toBe('A and 2 more');
  });
});
