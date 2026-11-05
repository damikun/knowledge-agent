import { pluralize } from '../../../lib/format';
import type { BadgeTone } from '../../../components/ui/Badge';
import type { Tone } from '../../../components/ui/tone';
import type { McpServerDto, McpToolDto, McpToolPolicy } from './types';

export function serverStatus(server: Pick<McpServerDto, 'enabled' | 'lastCheck'>): { tone: Tone; label: string } {
  if (!server.enabled) {
    return { tone: 'neutral', label: 'Off' };
  }
  if (!server.lastCheck) {
    return { tone: 'warning', label: 'Not tested' };
  }
  return server.lastCheck.ok ? { tone: 'success', label: 'Connected' } : { tone: 'danger', label: 'Cannot connect' };
}

export const POLICY_CHOICES: ReadonlyArray<{ value: McpToolPolicy; label: string; description: string }> = [
  { value: 'auto', label: 'Runs on its own', description: 'The agent uses it whenever it helps. Best for tools that only look things up.' },
  { value: 'confirm', label: 'Asks first', description: 'The person in the chat presses Confirm before it runs.' },
  {
    value: 'verifiedOnly',
    label: 'Signed-in visitors only',
    description: 'Only for visitors your website identified, and they confirm first. Not offered to anyone else.',
  },
];

export function policyLabel(policy: McpToolPolicy): string {
  return POLICY_CHOICES.find((choice) => choice.value === policy)?.label ?? policy;
}

/** The server's own claim, shown as such. */
export function toolEffect(tool: Pick<McpToolDto, 'readOnly' | 'destructive'>): { label: string; tone: BadgeTone } {
  if (tool.readOnly) {
    return { label: 'Only reads', tone: 'success' };
  }
  if (tool.destructive === false) {
    return { label: 'Changes things', tone: 'warning' };
  }
  return { label: 'Can change or delete', tone: 'danger' };
}

export function toolLabel(tool: Pick<McpToolDto, 'title' | 'name'>): string {
  return tool.title ?? tool.name;
}

/** e.g. "12 tools: 9 look things up, 3 make changes". */
export function toolsSummary(tools: readonly Pick<McpToolDto, 'readOnly'>[]): string {
  if (tools.length === 0) {
    return 'No tools listed yet';
  }
  const reads = tools.filter((tool) => tool.readOnly).length;
  const changes = tools.length - reads;
  const parts = [reads > 0 ? `${reads} ${reads === 1 ? 'looks' : 'look'} things up` : null, changes > 0 ? `${changes} ${changes === 1 ? 'makes' : 'make'} changes` : null];
  return `${pluralize(tools.length, 'tool')}: ${parts.filter(Boolean).join(', ')}`;
}
