import type { AgentSection } from './agentForm';

export type AgentTab = 'overview' | AgentSection | 'elsewhere';

export const TABS: ReadonlyArray<{ id: AgentTab; label: string }> = [
  { id: 'overview', label: 'Overview' },
  { id: 'general', label: 'General' },
  { id: 'behavior', label: 'Behavior' },
  { id: 'knowledge', label: 'Knowledge' },
  { id: 'tools', label: 'Tools' },
  { id: 'handover', label: 'Handover' },
  { id: 'specialists', label: 'Specialists' },
  { id: 'elsewhere', label: 'Use elsewhere' },
  { id: 'advanced', label: 'Advanced' },
];

// Tab names from older links.
const OLD_TABS: Readonly<Record<string, AgentTab>> = { people: 'handover' };

export function parseTab(value: string | null): AgentTab {
  return TABS.find((tab) => tab.id === value)?.id ?? (value ? OLD_TABS[value] : undefined) ?? 'overview';
}
