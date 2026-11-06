import { AppWindow, Bot, FlaskConical, Library, MessageSquare, UserRound } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Select, type SelectOption } from '../../components/ui/Select';
import { ownAgents, useAgents } from '../agents/api';
import type { AgentDto } from '../agents/types';
import { useWidgets } from '../widgets/api';
import type { WidgetDto } from '../widgets/types';
import { useReturnState } from '../../lib/returnTo';

export type TestTarget = { kind: 'widget'; widget: WidgetDto; agent: AgentDto | undefined } | { kind: 'agent'; agent: AgentDto | undefined };

export interface PlaygroundTarget {
  target: TestTarget | null;
  options: SelectOption<string>[];
  value: string;
  setValue: (value: string) => void;
  reset: () => void;
  empty: boolean;
}

/** "Demo shop's", or "Help desks'" for a name that ends in s. */
function possessive(name: string): string {
  return /s$/i.test(name.trim()) ? `${name}'` : `${name}'s`;
}

/** The option runs the widget's agent with the widget's scope and handover, not the widget itself. */
function widgetRulesLabel(widget: Pick<WidgetDto, 'name'>): string {
  return `Agent with ${possessive(widget.name)} rules`;
}

const CHANNEL_NAMES: Record<string, string> = { portal: 'Inbox', inbox: 'Inbox', email: 'Email', slack: 'Slack' };

function channelNames(channel: string): string[] {
  return [
    ...new Set(
      channel
        .split(/[,\s]+/)
        .filter(Boolean)
        .map((name) => CHANNEL_NAMES[name.toLowerCase()] ?? name),
    ),
  ];
}

/** ?widgetId= or ?agentId= picks the target; otherwise the first enabled widget, then the first own agent. */
export function usePlaygroundTarget(admin: boolean): PlaygroundTarget {
  const [params] = useSearchParams();
  const widgets = useWidgets(admin);
  const agents = useAgents();
  const [picked, setPicked] = useState<string | null>(null);
  const widgetList = useMemo(() => widgets.data ?? [], [widgets.data]);
  const agentList = useMemo(() => ownAgents(agents.data ?? []), [agents.data]);

  const defaultValue = useMemo(() => {
    const widgetId = params.get('widgetId')?.toLowerCase();
    const agentId = params.get('agentId')?.toLowerCase();
    const linkedWidget = widgetId ? widgetList.find((widget) => widget.id.toLowerCase() === widgetId) : undefined;
    if (linkedWidget) {
      return `widget:${linkedWidget.id}`;
    }
    const linkedAgent = agentId ? agentList.find((agent) => agent.id.toLowerCase() === agentId || agent.key === agentId) : undefined;
    if (linkedAgent) {
      return `agent:${linkedAgent.key}`;
    }
    const firstWidget = widgetList.find((widget) => widget.enabled);
    if (firstWidget) {
      return `widget:${firstWidget.id}`;
    }
    const firstAgent = agentList[0];
    return firstAgent ? `agent:${firstAgent.key}` : '';
  }, [params, widgetList, agentList]);

  const value = picked ?? defaultValue;
  const options: SelectOption<string>[] = [
    ...widgetList.map((widget) => ({
      value: `widget:${widget.id}`,
      label: widgetRulesLabel(widget),
      hint: widget.enabled ? 'Widget rules' : 'Widget rules, off',
    })),
    ...agentList.map((agent) => ({ value: `agent:${agent.key}`, label: agent.name, hint: 'Agent' })),
  ];

  let target: TestTarget | null = null;
  if (value.startsWith('widget:')) {
    const widget = widgetList.find((w) => w.id === value.slice(7));
    if (widget) {
      target = { kind: 'widget', widget, agent: agentList.find((agent) => agent.id.toLowerCase() === widget.agentId.toLowerCase()) };
    }
  } else if (value.startsWith('agent:')) {
    target = { kind: 'agent', agent: agentList.find((agent) => agent.key === value.slice(6)) };
  }

  const empty = agents.isSuccess && agentList.length === 0 && widgetList.length === 0;
  return { target, options, value, setValue: setPicked, reset: () => setPicked(null), empty };
}

/** Also sends the agent, for API versions that don't know widgets. */
export function targetRequest(target: TestTarget | null): { widgetId?: string; agent?: string } {
  if (!target) {
    return {};
  }
  const agent = target.agent ? { agent: target.agent.key } : {};
  return target.kind === 'widget' ? { widgetId: target.widget.id, ...agent } : agent;
}

export function TestAsPicker({ playground, disabled }: { playground: PlaygroundTarget; disabled?: boolean }) {
  if (playground.options.length < 2) {
    return null;
  }
  return (
    <div className="flex min-w-0 items-center gap-1.5">
      <span className="hidden text-small text-fg-muted sm:inline">Test as</span>
      <Select
        size="sm"
        aria-label="Test as"
        value={playground.value}
        onChange={playground.setValue}
        options={playground.options}
        disabled={disabled}
        align="end"
        className="w-44 max-w-[45vw]"
      />
    </div>
  );
}

export function TestBadge() {
  return (
    <Badge tone="info" className="shrink-0" title="A test conversation: it is not counted in Analytics">
      <FlaskConical className="size-3" aria-hidden />
      Test
    </Badge>
  );
}

const HANDOVER_POLICY: Record<string, string> = {
  onRequest: 'when the visitor asks',
  whenUnsure: 'when the agent is unsure or the visitor asks',
};

export function TargetSummary({ target, folderCount }: { target: TestTarget; folderCount?: number }) {
  const returnState = useReturnState();
  const agent = target.agent;
  const folders = folderCount ?? agent?.folderIds.length ?? 0;
  const knowledge = folders === 0 ? 'All sources' : `${folders} ${folders === 1 ? 'folder' : 'folders'}`;
  const policy = agent ? HANDOVER_POLICY[agent.handoverPolicy] : undefined;
  let handover: string;
  if (target.kind === 'widget') {
    const channels = channelNames(target.widget.settings.handover.channel);
    handover =
      target.widget.settings.behavior.handoverEnabled && policy
        ? `Offers a person ${policy}${channels.length > 0 ? `, through ${channels.join(', ')}` : ''}`
        : 'Never offers a person';
  } else {
    handover = policy ? `Offers a person ${policy}` : 'Never offers a person';
  }
  const rows = [
    ...(target.kind === 'widget'
      ? [{ icon: MessageSquare, label: 'Rules of', value: `${target.widget.name}${target.widget.enabled ? '' : ' (off on the site)'}` }]
      : []),
    { icon: Bot, label: 'Agent', value: agent?.name ?? 'Unknown agent' },
    { icon: Library, label: 'Knowledge', value: knowledge },
    { icon: UserRound, label: 'Person', value: handover },
  ];
  return (
    <div className="space-y-2 rounded-xl border border-border bg-surface-subtle px-4 py-3 text-left text-small" data-testid="test-target">
      <dl className="grid gap-x-4 gap-y-1.5 sm:grid-cols-[auto_1fr]">
        {rows.map((row) => (
          <div key={row.label} className="contents">
            <dt className="flex items-center gap-1.5 text-fg-muted">
              <row.icon className="size-3.5" aria-hidden />
              {row.label}
            </dt>
            <dd className="min-w-0 break-words text-fg max-sm:mb-1 max-sm:pl-5">{row.value}</dd>
          </div>
        ))}
      </dl>
      {target.kind === 'widget' ? (
        <p className="border-t border-border-subtle pt-2 text-xs text-fg-muted">
          <Link
            to={`/widgets/${target.widget.id}?preview`}
            state={returnState}
            className="inline-flex min-h-6 items-center gap-1 font-medium text-accent-text hover:underline"
          >
            <AppWindow className="size-3.5" aria-hidden />
            Open the real widget preview
          </Link>
        </p>
      ) : null}
    </div>
  );
}
