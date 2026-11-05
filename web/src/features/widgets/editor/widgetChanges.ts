import { pluralize } from '../../../lib/format';
import { HANDOVER_POLICIES } from '../../agents/agentForm';
import type { AgentDto } from '../../agents/types';
import type { WidgetFormValues } from '../widgetForm';

export interface WidgetChange {
  label: string;
  value: string;
  limited?: boolean;
}

export function agentFacts(agent: AgentDto): WidgetChange[] {
  const folders = agent.folderIds.length;
  const apps = (agent.mcpApps ?? []).filter((app) => app.enabled).length;
  const tools = [agent.tools.length > 0 ? pluralize(agent.tools.length, 'tool') : null, apps > 0 ? pluralize(apps, 'app') : null].filter(Boolean);
  const policy = HANDOVER_POLICIES.find((item) => item.value === agent.handoverPolicy);
  return [
    {
      label: 'Answers from',
      value: `${folders === 0 ? 'All your sources' : `Your sources in ${pluralize(folders, 'folder')}`}${agent.allowGeneralKnowledge ? ', and what the model knows' : ''}`,
    },
    { label: 'Tools', value: tools.length > 0 ? tools.join(' and ') : 'None' },
    { label: 'A person', value: policy ? policy.label : agent.handoverPolicy },
  ];
}

const PERSON_CHANNELS: Readonly<Record<string, string>> = {
  portal: 'the Inbox',
  email: 'email and the Inbox',
  slack: 'Slack and the Inbox',
};

const CITATIONS: Readonly<Record<string, string>> = {
  hidden: 'Hidden from visitors',
  titles: 'Titles only',
  links: 'Titles, with links to web pages',
};

function sites(values: WidgetFormValues): string {
  const count = values.allowedOrigins.split('\n').filter((line) => line.trim()).length;
  return count === 0 ? 'No site allowed yet' : pluralize(count, 'site');
}

function hours(values: WidgetFormValues): string {
  if (values.alwaysAvailable) {
    return 'at any time';
  }
  const days = values.hours.length;
  return days === 0 ? 'never, as no hours are set' : `during ${pluralize(days, 'opening slot')} (${values.timeZone || 'UTC'})`;
}

export function widgetChanges(values: WidgetFormValues, agent?: AgentDto): WidgetChange[] {
  return [
    { label: 'Runs on', value: sites(values) },
    {
      label: 'A person',
      value: values.handoverEnabled ? `Through ${PERSON_CHANNELS[values.channel] ?? values.channel}, ${hours(values)}` : 'Off on this widget',
      limited: !values.handoverEnabled && agent?.handoverPolicy !== 'never',
    },
    { label: 'Sources shown', value: CITATIONS[values.citationMode] ?? values.citationMode, limited: values.citationMode === 'hidden' },
  ];
}
