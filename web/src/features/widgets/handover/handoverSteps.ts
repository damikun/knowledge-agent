import type { AgentDto, HandoverPolicy } from '../../agents/types';
import type { AlertDto } from '../../alerts';
import type { IntegrationsDto } from '../../notifications/types';
import { splitLines, type WidgetFormValues } from '../widgetForm';
import type { GraphEdge, GraphNode } from '../../../components/flow/graph';

type HandoverStepKind = 'visitor' | 'agent' | 'hours' | 'channel' | 'wait' | 'joined' | 'expired';

export interface HandoverStep extends GraphNode<HandoverStepKind> {
  /** Null for steps that are settings of this widget. */
  href: string | null;
}

export interface HandoverFlow {
  /** Top to bottom; a row of two or more is a branch. */
  rows: HandoverStep[][];
  edges: GraphEdge[];
}

export interface HandoverDraft {
  handoverEnabled: boolean;
  agentId: string;
  /** portal (Inbox only), email or slack. */
  channel: string;
  /** Addresses; empty uses the support address of the email integration. */
  emailTo: readonly string[];
  /** Empty uses the channel of the Slack integration. */
  slackChannelId: string;
  escalateAfterMinutes: number | null;
  waitMinutes: number | null;
  alwaysAvailable: boolean;
  timeZone: string;
  openDays: number;
  contactCapture: string;
}

const POLICY_LABELS: Record<HandoverPolicy, string> = {
  never: 'Never',
  onRequest: 'When asked',
  whenUnsure: 'When unsure',
};

type ChannelState = 'connected' | 'failing' | 'not-set-up' | 'unknown';

function channelState(integrations: IntegrationsDto | null | undefined, alerts: readonly AlertDto[], channel: 'email' | 'slack'): ChannelState {
  if (!integrations) {
    return 'unknown';
  }
  if (alerts.some((alert) => alert.kind === (channel === 'email' ? 'email_failing' : 'slack_failing'))) {
    return 'failing';
  }
  const ready = channel === 'email' ? integrations.email.canSend : integrations.slack.canPost;
  return ready ? 'connected' : 'not-set-up';
}

export interface HandoverContext {
  agents: readonly AgentDto[];
  integrations: IntegrationsDto | null | undefined;
  alerts: readonly AlertDto[];
}

function minutes(value: number | null): string {
  return value === null ? 'a while' : value === 1 ? '1 minute' : `${value} minutes`;
}

/** Mirrors the server's handover rules; keep the two in step. */
export function buildHandoverFlow(draft: HandoverDraft, context: HandoverContext): HandoverFlow {
  const edges: GraphEdge[] = [];
  const link = (source: string, target: string, label?: string) => edges.push({ id: `${source}->${target}`, source, target, ...(label ? { label } : {}) });
  const integrations = context.integrations;
  const email = channelState(integrations, context.alerts, 'email');
  const slack = channelState(integrations, context.alerts, 'slack');
  const emailAddress = draft.emailTo.length > 0 || Boolean(integrations?.email.supportAddress);
  const emailWorks = email === 'connected' && emailAddress;
  const slackWorks = slack === 'connected' && Boolean(draft.slackChannelId.trim() || integrations?.slack.channelId);

  // Who asks
  const agent = context.agents.find((candidate) => candidate.id.toLowerCase() === draft.agentId.toLowerCase());
  const visitor: HandoverStep = {
    id: 'visitor',
    kind: 'visitor',
    title: 'Visitor asks',
    status: null,
    tone: 'neutral',
    lines: [draft.contactCapture === 'before_handover' ? 'Leaves a name and email first' : 'With Talk to a person'],
    problem: null,
    href: null,
  };
  const offers: HandoverStep = agent
    ? {
        id: 'agent',
        kind: 'agent',
        title: agent.name,
        status: POLICY_LABELS[agent.handoverPolicy],
        tone: 'neutral',
        lines: [agent.handoverPolicy === 'never' ? 'Never offers a person' : agent.handoverPolicy === 'whenUnsure' ? 'Offers one when asked or unsure' : 'Offers one when asked'],
        problem: null,
        dimmed: agent.handoverPolicy === 'never',
        href: `/agents/${agent.id}`,
      }
    : { id: 'agent', kind: 'agent', title: 'Agent', status: null, tone: 'warning', lines: [], problem: 'Choose an agent', href: null };
  const rows: HandoverStep[][] = [[visitor, offers]];
  const askers = ['visitor', 'agent'];

  // When someone is there
  let from = askers;
  if (!draft.alwaysAvailable) {
    const days = draft.openDays;
    rows.push([
      {
        id: 'hours',
        kind: 'hours',
        title: 'Opening hours?',
        status: days === 0 ? 'None' : `${days} ${days === 1 ? 'day' : 'days'}`,
        tone: days === 0 ? 'warning' : 'neutral',
        lines: [draft.timeZone || 'UTC'],
        problem: days === 0 ? 'No hours, so the team is always away' : null,
        href: null,
      },
    ]);
    for (const asker of askers) {
      link(asker, 'hours');
    }
    from = ['hours'];
  }

  // Where the request goes
  const channels: HandoverStep[] = [
    { id: 'inbox', kind: 'channel', title: 'Inbox', status: 'Always', tone: 'success', lines: ['Every request shows here'], problem: null, href: '/inbox' },
  ];
  const away = !draft.alwaysAvailable;
  if (draft.channel === 'slack') {
    const fallback = emailWorks ? 'by email' : 'only to the Inbox';
    channels.push({
      id: 'slack',
      kind: 'channel',
      title: 'Slack',
      status: slack === 'failing' ? 'Failing' : slackWorks ? 'Connected' : 'Not set up',
      tone: slack === 'failing' ? 'danger' : slackWorks ? 'success' : 'warning',
      lines: [away && emailWorks ? 'Away hours go by email' : 'Posts to the team channel'],
      problem: slack === 'failing' ? 'Recent posts failed' : slackWorks ? null : `Not set up, so requests go ${fallback}`,
      href: '/notifications',
    });
    if (emailWorks && (away || !slackWorks)) {
      channels.push({ id: 'email', kind: 'channel', title: 'Email', status: 'Fallback', tone: 'neutral', lines: [slackWorks ? 'When the team is away' : 'Instead of Slack'], problem: null, href: '/notifications' });
    }
  } else if (draft.channel === 'email') {
    channels.push({
      id: 'email',
      kind: 'channel',
      title: 'Email',
      status: email === 'failing' ? 'Failing' : emailWorks ? 'Connected' : 'Not set up',
      tone: email === 'failing' ? 'danger' : emailWorks ? 'success' : 'warning',
      lines: [draft.emailTo.length > 0 ? `To ${draft.emailTo.length === 1 ? draft.emailTo[0] : `${draft.emailTo.length} addresses`}` : 'To the support address'],
      problem:
        email === 'failing'
          ? 'Recent emails failed'
          : email !== 'connected'
            ? 'Not set up, so requests stay in the Inbox'
            : !emailAddress
              ? 'No address to send to'
              : null,
      href: '/notifications',
    });
  }
  rows.push(channels);
  for (const channel of channels) {
    for (const source of from) {
      const label = source === 'hours' && channel.id === 'email' && draft.channel === 'slack' && slackWorks ? 'away' : source === 'hours' && channel.id === 'slack' ? 'open' : undefined;
      link(source, channel.id, label);
    }
  }

  // Waiting
  const escalates = draft.channel === 'slack' && slackWorks && email === 'connected';
  rows.push([
    {
      id: 'wait',
      kind: 'wait',
      title: `Waits ${minutes(draft.waitMinutes)}`,
      status: null,
      tone: 'neutral',
      lines: [escalates ? `Email after ${minutes(draft.escalateAfterMinutes)} without a reply` : 'The visitor can keep chatting'],
      problem: null,
      href: null,
    },
  ]);
  for (const channel of channels) {
    link(channel.id, 'wait');
  }

  // How it ends
  rows.push([
    { id: 'joined', kind: 'joined', title: 'A person joins', status: null, tone: 'success', lines: ['Replies reach the visitor here'], problem: null, href: null },
    {
      id: 'expired',
      kind: 'expired',
      title: 'Nobody joins',
      status: null,
      tone: 'neutral',
      lines: email === 'connected' && emailAddress ? ['Back to the assistant', 'Transcript goes by email'] : ['Back to the assistant'],
      problem: null,
      href: null,
    },
  ]);
  link('wait', 'joined');
  link('wait', 'expired');

  return { rows, edges };
}

function wholeNumber(value: string): number | null {
  const parsed = Number.parseInt(value.trim(), 10);
  return Number.isFinite(parsed) ? parsed : null;
}

export function handoverDraftFromForm(values: WidgetFormValues): HandoverDraft {
  return {
    handoverEnabled: values.handoverEnabled,
    agentId: values.agentId,
    channel: values.channel,
    emailTo: splitLines(values.emailTo),
    slackChannelId: values.slackChannelId,
    escalateAfterMinutes: wholeNumber(values.escalateAfterMinutes),
    waitMinutes: wholeNumber(values.waitMinutes),
    alwaysAvailable: values.alwaysAvailable,
    timeZone: values.timeZone,
    openDays: new Set(values.hours.map((row) => row.day)).size,
    contactCapture: values.contactCapture,
  };
}
