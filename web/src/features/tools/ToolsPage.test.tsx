import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '../../components/ui/Toast';
import { cancelTool, mcpServer, mcpTool } from '../../test/mcpApi';
import { jsonResponse, mockFetch, problemResponse, renderWithProviders } from '../../test/utils';
import { validateMcpServer } from './mcp/McpServerSheet';
import type { McpServerDto } from './mcp/types';
import { ToolsPage } from './ToolsPage';

afterEach(() => {
  vi.unstubAllGlobals();
});

function lastBody(fetchMock: ReturnType<typeof mockFetch>, route: string): Record<string, unknown> {
  const [method, path] = route.split(' ');
  const call = fetchMock.mock.calls
    .filter(([input, init]) => new URL(String(input), 'http://localhost').pathname === path && (init as RequestInit | undefined)?.method === method)
    .at(-1);
  return JSON.parse((call![1] as RequestInit).body as string) as Record<string, unknown>;
}

function renderPage() {
  return renderWithProviders(
    <>
      <ToolsPage />
      <Toaster />
    </>,
  );
}

describe('ToolsPage', () => {
  it('lists the connected servers with their status, the agents using them and what each tool does', async () => {
    mockFetch({
      'GET /api/connections/mcp': [
        mcpServer({
          usedBy: [
            { agentId: 'a1', agentName: 'Shop helper', toolCount: 2, mode: 'all' },
            { agentId: 'a2', agentName: 'Orders', toolCount: 1, mode: 'chosen' },
          ],
        }),
        mcpServer({
          id: 's2',
          key: 'crm',
          name: 'CRM',
          lastCheck: { checkedAt: '2026-11-05T08:00:00Z', ok: false, error: 'The server could not be reached.' },
          tools: [],
        }),
      ],
    });
    renderPage();

    expect(await screen.findByRole('heading', { level: 1, name: 'Tools' })).toBeInTheDocument();
    const shop = await screen.findByRole('region', { name: 'Shop' });
    expect(within(shop).getByText('Connected')).toBeInTheDocument();
    expect(within(shop).getByRole('link', { name: 'Shop helper' })).toHaveAttribute('href', '/agents/a1?tab=tools');
    expect(within(shop).getByText(/Used by/)).toHaveTextContent('Used by Shop helper (all tools), Orders (1 picked)');
    expect(within(shop).getByText('2 tools: 1 looks things up, 1 makes changes')).toBeInTheDocument();
    const tools = within(shop).getByRole('list', { name: 'Tools of Shop' });
    expect(within(tools).getByText('Only reads')).toBeInTheDocument();
    expect(within(tools).getByText('Can change or delete')).toBeInTheDocument();
    expect(within(tools).getByText('By default: asks first')).toBeInTheDocument();
    const crm = screen.getByRole('region', { name: 'CRM' });
    expect(within(crm).getByText('Cannot connect')).toBeInTheDocument();
    expect(within(crm).getByText(/The server could not be reached/)).toBeInTheDocument();
  });

  it('adds a server with a bearer token, tests it first and never shows the token again', async () => {
    const user = userEvent.setup();
    let servers: McpServerDto[] = [];
    const fetchMock = mockFetch({
      'GET /api/connections/mcp': () => jsonResponse(servers),
      'POST /api/connections/mcp/test': {
        ok: true,
        message: 'Connected. The server offers 2 tool(s).',
        serverName: 'shop',
        serverVersion: '2.0',
        tools: [mcpTool(), cancelTool],
        durationMs: 40,
        checkedAt: '2026-11-05T08:00:00Z',
      },
      'POST /api/connections/mcp': () => {
        servers = [mcpServer()];
        return jsonResponse(servers[0], 201);
      },
    });
    renderPage();

    expect(await screen.findByText('No apps connected yet')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add MCP server' }));
    const sheet = await screen.findByRole('dialog', { name: 'Add an MCP server' });
    await user.type(within(sheet).getByRole('textbox', { name: 'Name' }), 'Shop');
    await user.type(within(sheet).getByRole('textbox', { name: 'Address' }), 'https://mcp.shop.example/mcp');
    await user.click(within(sheet).getByRole('radio', { name: 'Bearer token' }));
    await user.click(within(sheet).getByRole('button', { name: 'Add server' }));
    expect(await within(sheet).findByText('Enter the token.')).toBeInTheDocument();

    await user.type(within(sheet).getByLabelText('Token'), 'tok-123456789');
    await user.click(within(sheet).getByRole('switch', { name: "Send the visitor's verified user ID" }));
    await user.click(within(sheet).getByRole('button', { name: 'Test' }));
    expect(await within(sheet).findByText('Test passed')).toBeInTheDocument();
    expect(within(sheet).getByRole('list', { name: 'Tools of Shop' })).toBeInTheDocument();
    expect(lastBody(fetchMock, 'POST /api/connections/mcp/test')).toMatchObject({ authType: 'bearer', secret: 'tok-123456789' });

    await user.click(within(sheet).getByRole('button', { name: 'Add server' }));
    expect(await screen.findByText('MCP server added')).toBeInTheDocument();
    expect(lastBody(fetchMock, 'POST /api/connections/mcp')).toEqual({
      name: 'Shop',
      url: 'https://mcp.shop.example/mcp',
      authType: 'bearer',
      headerName: null,
      secret: 'tok-123456789',
      enabled: true,
      sendIdentity: true,
    });
    expect(await screen.findByRole('region', { name: 'Shop' })).toBeInTheDocument();
  });

  it('keeps the saved token when an edit leaves it empty, and shows the reason of a failed test', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      'GET /api/connections/mcp': [mcpServer()],
      [`POST /api/connections/mcp/${mcpServer().id}/test`]: {
        ok: false,
        message: 'The server refused the credentials (HTTP 401). Check the token.',
        tools: [],
        durationMs: 10,
        checkedAt: '2026-11-05T08:00:00Z',
      },
      [`PATCH /api/connections/mcp/${mcpServer().id}`]: (_url: URL, init: RequestInit) =>
        jsonResponse({ ...mcpServer(), ...(JSON.parse(init.body as string) as object) }),
    });
    renderPage();

    const shop = await screen.findByRole('region', { name: 'Shop' });
    await user.click(within(shop).getByRole('button', { name: 'Test' }));
    expect(await within(shop).findByText(/refused the credentials/)).toBeInTheDocument();

    await user.click(within(shop).getByRole('button', { name: 'Edit Shop' }));
    const sheet = await screen.findByRole('dialog', { name: 'Shop' });
    const name = within(sheet).getByRole('textbox', { name: 'Name' });
    await user.clear(name);
    await user.type(name, 'Our shop');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(lastBody(fetchMock, `PATCH /api/connections/mcp/${mcpServer().id}`)).toMatchObject({ name: 'Our shop', secret: null }),
    );
  });

  it('says what went wrong when the servers cannot load', async () => {
    mockFetch({ 'GET /api/connections/mcp': () => problemResponse({ title: 'Boom', detail: 'Database down.' }, 500) });
    renderPage();
    expect(await screen.findByText('Could not load the MCP servers')).toBeInTheDocument();
  });
});

describe('validateMcpServer', () => {
  const values = {
    name: 'Shop',
    url: 'https://mcp.example.com/mcp',
    authType: 'header' as const,
    headerName: '',
    secret: { value: '', remove: false },
    enabled: true,
    sendIdentity: false,
  };

  it('asks for the header name and the token of a custom header', () => {
    expect(validateMcpServer(values, null).errors).toEqual({ headerName: 'Enter the header name, such as X-Api-Key.', secret: 'Enter the token.' });
  });

  it('sends no token without sign-in, and keeps a saved one when left empty', () => {
    expect(validateMcpServer({ ...values, authType: 'none' }, null).request?.secret).toBeNull();
    expect(
      validateMcpServer({ ...values, headerName: 'X-Api-Key' }, mcpServer({ authType: 'header', headerName: 'X-Api-Key' })).request,
    ).toMatchObject({
      headerName: 'X-Api-Key',
      secret: null,
    });
  });
});
