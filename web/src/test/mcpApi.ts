import type { McpServerDto, McpToolDto } from '../features/tools/mcp/types';

export function mcpTool(overrides: Partial<McpToolDto> = {}): McpToolDto {
  return {
    name: 'lookup_order',
    modelName: 'mcp_shop__lookup_order',
    title: 'Look up an order',
    description: 'Finds an order by its number.',
    inputSchema: { type: 'object', properties: { orderId: { type: 'string' } } },
    readOnly: true,
    destructive: null,
    idempotent: null,
    openWorld: null,
    defaultPolicy: 'auto',
    fingerprint: 'fp-lookup-order',
    ...overrides,
  };
}

export const cancelTool = mcpTool({
  name: 'cancel_order',
  modelName: 'mcp_shop__cancel_order',
  title: 'Cancel an order',
  description: 'Cancels an order that has not shipped.',
  readOnly: false,
  destructive: true,
  defaultPolicy: 'confirm',
  fingerprint: 'fp-cancel-order',
});

export function mcpServer(overrides: Partial<McpServerDto> = {}): McpServerDto {
  return {
    id: '0199c1e2-0000-7000-8000-0000000b0001',
    key: 'shop',
    name: 'Shop',
    url: 'https://mcp.shop.example/mcp',
    authType: 'bearer',
    headerName: null,
    hasSecret: true,
    secretHint: '…6789',
    enabled: true,
    sendIdentity: false,
    lastCheck: { checkedAt: '2026-11-05T08:00:00Z', ok: true, error: null },
    serverName: 'shop',
    serverVersion: '2.0',
    tools: [mcpTool(), cancelTool],
    usedBy: [],
    createdAt: '2026-11-05T08:00:00Z',
    updatedAt: '2026-11-05T08:00:00Z',
    ...overrides,
  };
}
