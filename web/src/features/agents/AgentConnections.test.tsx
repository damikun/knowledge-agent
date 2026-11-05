import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { dismissAllToasts, Toaster } from '../../components/ui/Toast';
import { agentDto, agentsRoutes, builtInAgent } from '../../test/agentsApi';
import { mcpServer } from '../../test/mcpApi';
import { chooseOption } from '../../test/select';
import { sourcesRoutes } from '../../test/sourcesApi';
import { fetchCalls, jsonResponse, mockFetch, renderWithProviders } from '../../test/utils';
import { widgetsRoutes } from '../../test/widgetsApi';
import { mcpSnippets } from './accessApi';
import { AgentPage } from './AgentPage';
import type { AgentDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
  sessionStorage.clear();
  dismissAllToasts();
});

const overview = { days: 7, widgets: [] };
const analytics = { days: 7, from: '2026-10-29T00:00:00Z', to: '2026-11-05T00:00:00Z', totals: {}, daily: [], widgets: [], unanswered: [], labels: [] };

function routes(getAgents: () => readonly AgentDto[], extra: Record<string, unknown> = {}) {
  return {
    ...agentsRoutes(getAgents),
    ...sourcesRoutes(() => []),
    ...widgetsRoutes(() => []),
    'GET /api/overview': overview,
    'GET /api/analytics': analytics,
    ...extra,
  };
}

function renderAt(path: string) {
  return renderWithProviders(
    <>
      <AgentPage />
      <Toaster />
    </>,
    { route: path, path: '/agents/:agentId' },
  );
}

function lastBody(fetchMock: ReturnType<typeof mockFetch>, route: string): unknown {
  const [method, path] = route.split(' ');
  const call = fetchMock.mock.calls
    .filter(([input, init]) => new URL(String(input), 'http://localhost').pathname === path && (init as RequestInit | undefined)?.method === method)
    .at(-1);
  return JSON.parse((call![1] as RequestInit).body as string);
}

describe('connected apps on an agent', () => {
  function saving(helper: AgentDto, setAgents: (agents: AgentDto[]) => void) {
    return {
      [`PUT /api/agents/${helper.id}`]: (_url: URL, init: RequestInit) => {
        const saved = { ...helper, ...(JSON.parse(init.body as string) as object) } as AgentDto;
        setAgents([builtInAgent, saved]);
        return jsonResponse(saved);
      },
    };
  }

  it('turns an app on with all its tools, sets what changes need and saves it with the agent', async () => {
    const user = userEvent.setup();
    let agents: AgentDto[] = [builtInAgent, agentDto()];
    const helper = agents[1]!;
    const fetchMock = mockFetch(
      routes(() => agents, {
        'GET /api/connections/mcp': [mcpServer()],
        ...saving(helper, (next) => (agents = next)),
      }),
    );
    renderAt(`/agents/${helper.id}?tab=tools`);

    const shop = await screen.findByRole('region', { name: 'Shop' });
    expect(within(shop).getByText('2 tools: 1 looks things up, 1 makes changes')).toBeInTheDocument();
    expect(within(shop).getByText('Connected')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Tools page' })[0]).toHaveAttribute('href', '/tools');

    await user.click(within(shop).getByRole('switch', { name: 'Use this app: Shop' }));
    expect(within(shop).getByText(/Uses all 2 tools, also ones the app adds later/)).toBeInTheDocument();
    await chooseOption(user, within(shop).getByRole('combobox', { name: 'Tools that make changes' }), 'Only for verified visitors');

    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('Agent saved')).toBeInTheDocument();
    expect(lastBody(fetchMock, `PUT /api/agents/${helper.id}`)).toMatchObject({
      mcpApps: [{ serverId: mcpServer().id, enabled: true, mode: 'all', changesPolicy: 'verifiedOnly', tools: [] }],
    });
  });

  it('picks only some tools, shows a pick the app no longer offers and lets it go', async () => {
    const user = userEvent.setup();
    let agents: AgentDto[] = [
      builtInAgent,
      agentDto({
        mcpApps: [
          {
            serverId: mcpServer().id,
            enabled: true,
            mode: 'chosen',
            changesPolicy: 'confirm',
            tools: [
              { name: 'lookup_order', policy: 'auto' },
              { name: 'track_parcel', policy: 'auto' },
            ],
          },
        ],
      }),
    ];
    const helper = agents[1]!;
    const fetchMock = mockFetch(
      routes(() => agents, {
        'GET /api/connections/mcp': [mcpServer()],
        ...saving(helper, (next) => (agents = next)),
      }),
    );
    renderAt(`/agents/${helper.id}?tab=tools`);

    const shop = await screen.findByRole('region', { name: 'Shop' });
    expect(within(shop).getByRole('switch', { name: 'Only the tools I pick: Shop' })).toBeChecked();
    expect(within(shop).getByRole('checkbox', { name: 'Look up an order' })).toBeChecked();
    expect(within(shop).getByRole('checkbox', { name: 'Cancel an order' })).not.toBeChecked();
    expect(within(shop).getByText('No longer offered')).toBeInTheDocument();

    await user.click(within(shop).getByRole('checkbox', { name: 'Cancel an order' }));
    const cancelPolicy = within(shop).getByRole('combobox', { name: 'When Cancel an order may run' });
    expect(cancelPolicy).toHaveTextContent('Asks first');
    await chooseOption(user, cancelPolicy, 'Signed-in visitors only');
    await user.click(within(shop).getByRole('button', { name: 'Remove track_parcel' }));

    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('Agent saved')).toBeInTheDocument();
    expect(lastBody(fetchMock, `PUT /api/agents/${helper.id}`)).toMatchObject({
      mcpApps: [
        {
          serverId: mcpServer().id,
          mode: 'chosen',
          tools: [
            { name: 'lookup_order', policy: 'auto' },
            { name: 'cancel_order', policy: 'verifiedOnly' },
          ],
        },
      ],
    });
  });

  it('starts a pick from every tool with the policy it has, so nothing changes until a tool is turned off', async () => {
    const user = userEvent.setup();
    const helper = agentDto({ mcpApps: [{ serverId: mcpServer().id, enabled: true, mode: 'all', changesPolicy: 'confirm', tools: [] }] });
    mockFetch(routes(() => [builtInAgent, helper], { 'GET /api/connections/mcp': [mcpServer()] }));
    renderAt(`/agents/${helper.id}?tab=tools`);

    const shop = await screen.findByRole('region', { name: 'Shop' });
    await user.click(within(shop).getByText('Choose tools'));
    await user.click(within(shop).getByRole('switch', { name: 'Only the tools I pick: Shop' }));

    expect(within(shop).getByRole('checkbox', { name: 'Look up an order' })).toBeChecked();
    expect(within(shop).getByRole('checkbox', { name: 'Cancel an order' })).toBeChecked();
    expect(within(shop).getByRole('combobox', { name: 'When Look up an order may run' })).toHaveTextContent('Runs on its own');
  });

  it('mentions the apps it uses on the overview', async () => {
    const helper = agentDto({ mcpApps: [{ serverId: mcpServer().id, enabled: true, mode: 'all', changesPolicy: 'confirm', tools: [] }] });
    mockFetch(routes(() => [builtInAgent, helper], { 'GET /api/connections/mcp': [mcpServer()] }));
    renderAt(`/agents/${helper.id}`);

    expect(await screen.findByText('Uses Shop (all tools)')).toBeInTheDocument();
  });

  it('points to the Tools page while no app is connected', async () => {
    const helper = agentDto();
    mockFetch(routes(() => [builtInAgent, helper], { 'GET /api/connections/mcp': [] }));
    renderAt(`/agents/${helper.id}?tab=tools`);

    expect(await screen.findByText(/No app is connected yet/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Tools page' })).toHaveAttribute('href', '/tools');
  });
});

describe('Use elsewhere', () => {
  const helper = agentDto({ mcpServerEnabled: true, httpApiEnabled: true });
  const accessPath = `/api/agents/${helper.id}/access`;
  const keysPath = `/api/agents/${helper.id}/api-keys`;

  it('lists the MCP server and the HTTP API under Where it answers when they are on', async () => {
    mockFetch(routes(() => [builtInAgent, helper]));
    renderAt(`/agents/${helper.id}`);

    const ways = await screen.findByRole('list', { name: 'Other ways it answers' });
    expect(within(ways).getByRole('button', { name: 'MCP server' })).toBeInTheDocument();
    expect(within(ways).getByRole('button', { name: 'HTTP API' })).toBeInTheDocument();
  });

  it('turns the MCP server on, shows a new key once with the snippets, and revokes a key', async () => {
    const user = userEvent.setup();
    const off = agentDto();
    let access = { mcpServerEnabled: false, httpApiEnabled: false, mcpPath: '/mcp/agents/website-helper', apiPath: '/api/public/agents/website-helper', activeKeys: 1 };
    const existing = {
      id: 'k1',
      name: 'Old laptop',
      prefix: 'ka_abc123',
      requestsPerMinute: 30,
      createdBy: 'Ada Lovelace',
      createdAt: '2026-11-01T08:00:00Z',
      lastUsedAt: null,
      revokedAt: null as string | null,
    };
    let keys = [existing];
    const created = { ...existing, id: 'k2', name: 'Cursor', prefix: 'ka_newkey' };
    const fetchMock = mockFetch(
      routes(() => [builtInAgent, off], {
        [`GET ${accessPath}`]: () => jsonResponse(access),
        [`PUT ${accessPath}`]: (_url: URL, init: RequestInit) => {
          access = { ...access, ...(JSON.parse(init.body as string) as object) };
          return jsonResponse(access);
        },
        [`GET ${keysPath}`]: () => jsonResponse(keys),
        [`POST ${keysPath}`]: () => {
          keys = [created, ...keys];
          return jsonResponse({ key: created, secret: 'ka_newkey-secret-value' }, 201);
        },
        [`DELETE ${keysPath}/k1`]: () => {
          keys = keys.map((key) => (key.id === 'k1' ? { ...key, revokedAt: '2026-11-05T09:00:00Z' } : key));
          return new Response(null, { status: 204 });
        },
      }),
    );
    renderAt(`/agents/${off.id}?tab=elsewhere`);

    const mcp = await screen.findByRole('switch', { name: /MCP server/ });
    expect(mcp).not.toBeChecked();
    await user.click(mcp);
    await waitFor(() => expect(lastBody(fetchMock, `PUT ${accessPath}`)).toEqual({ mcpServerEnabled: true }));
    await waitFor(() => expect(screen.getByRole('switch', { name: /MCP server/ })).toBeChecked());

    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Cursor');
    await user.click(screen.getByRole('button', { name: 'Create key' }));
    expect(await screen.findByText('Copy the key for Cursor now')).toBeInTheDocument();
    expect(lastBody(fetchMock, `POST ${keysPath}`)).toEqual({ name: 'Cursor', requestsPerMinute: 30 });
    await user.click(screen.getByRole('radio', { name: 'Cursor' }));
    expect(screen.getByText(/"Authorization": "Bearer ka_newkey-secret-value"/)).toBeInTheDocument();

    const list = await screen.findByRole('list', { name: 'API keys' });
    await within(list).findByText('Cursor');
    await user.click(within(list).getByRole('button', { name: 'Revoke Old laptop' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Revoke' }));
    expect(await screen.findByText('Old laptop revoked')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain(`DELETE ${keysPath}/k1`);
    expect(await within(list).findByText('Revoked')).toBeInTheDocument();
  });

  it('writes client snippets with the address and the key', () => {
    const snippets = mcpSnippets('website-helper', 'https://help.example.com/mcp/agents/website-helper', 'ka_x');
    expect(JSON.parse(snippets.cursor)).toEqual({
      mcpServers: { 'website-helper': { url: 'https://help.example.com/mcp/agents/website-helper', headers: { Authorization: 'Bearer ka_x' } } },
    });
    expect(JSON.parse(snippets.claudeDesktop).mcpServers['website-helper'].env).toEqual({ AUTH_HEADER: 'Bearer ka_x' });
    expect(snippets.generic).toContain('Streamable HTTP');
  });
});
