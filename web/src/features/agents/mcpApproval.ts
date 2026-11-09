import type { McpServerDto, McpToolDto, McpToolPolicy } from '../tools/mcp';
import type { AgentMcpAppDto, AgentMcpToolDto } from './types';

/**
 * approved: the app lists the definition an admin approved. changed: it differs since. needed: never approved.
 * Only approved tools may run on their own; the rest wait for the person's Confirm.
 */
export type McpApproval = 'approved' | 'changed' | 'needed';

/** When a tool of the app may run without a pick of its own: read-only tools on their own, the rest as the app says. */
export function defaultPolicy(tool: McpToolDto, app: Pick<AgentMcpAppDto, 'changesPolicy'>): McpToolPolicy {
  return tool.readOnly ? 'auto' : app.changesPolicy;
}

/** Null for a tool the app does not use: one not picked in chosen mode. */
export function approvalOf(tool: McpToolDto, app: AgentMcpAppDto): McpApproval | null {
  const entry = app.tools.find((item) => item.name === tool.name);
  if (!entry) {
    return app.mode === 'all' ? 'needed' : null;
  }
  if (!entry.fingerprint) {
    return 'needed';
  }
  return entry.fingerprint === tool.fingerprint ? 'approved' : 'changed';
}

export function needsApproval(server: McpServerDto, app: AgentMcpAppDto): McpToolDto[] {
  return server.tools.filter((tool) => {
    const approval = approvalOf(tool, app);
    return approval === 'changed' || approval === 'needed';
  });
}

/** Approves what the app lists now for the given tools, keeping each tool's policy. */
export function approve(app: AgentMcpAppDto, tools: readonly McpToolDto[]): AgentMcpAppDto {
  const next: AgentMcpToolDto[] = app.tools.map((entry) => {
    const tool = tools.find((item) => item.name === entry.name);
    return tool ? { ...entry, fingerprint: tool.fingerprint } : entry;
  });
  for (const tool of tools) {
    if (!next.some((entry) => entry.name === tool.name)) {
      next.push({ name: tool.name, policy: defaultPolicy(tool, app), fingerprint: tool.fingerprint });
    }
  }
  return { ...app, tools: next };
}

/** Using every tool approves the ones the app lists now that have no entry yet; tools it adds later wait for approval. */
export function withAllTools(app: AgentMcpAppDto, server: McpServerDto): AgentMcpAppDto {
  const fresh = server.tools.filter((tool) => !app.tools.some((entry) => entry.name === tool.name));
  return approve({ ...app, mode: 'all' }, fresh);
}

export const APPROVAL_LABELS: Readonly<Record<Exclude<McpApproval, 'approved'>, string>> = {
  changed: 'Changed since approval',
  needed: 'Needs approval',
};
