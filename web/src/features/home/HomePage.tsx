import { ArrowRight, Globe, MessageSquare, MessagesSquare, type LucideIcon } from 'lucide-react';
import { useId, useMemo, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { buttonClass } from '../../components/ui/Button';
import { Card, ErrorMessage } from '../../components/ui/feedback';
import { Page, PageHeader } from '../../components/ui/Page';
import { SetupChecklist } from '../../components/ui/SetupChecklist';
import { Skeleton } from '../../components/ui/Skeleton';
import { StatCard, type StatTrend } from '../../components/ui/StatCard';
import { StatusDot } from '../../components/ui/StatusDot';
import { formatDateTime, formatNumber, formatRelativeTime, pluralize } from '../../lib/format';
import { ownAgents, useAgents } from '../agents/api';
import { NeedsAttention } from '../alerts';
import { STATUS_LABELS, STATUS_TONES, widgetLabel } from '../inbox/status';
import { useInboxConversations } from '../inbox/api';
import type { InboxConversationDto } from '../inbox/types';
import { useProfiles } from '../models/api';
import { OVERVIEW_DAYS, useOverview } from '../overview/api';
import type { OverviewDto } from '../overview/types';
import { useSourceStats } from '../sources/api';
import { setupMessage } from '../system/status';
import { useWidgets } from '../widgets/api';
import { useRecentConversations } from './api';
import { setupSteps } from './setupSteps';
import type { RecentConversation } from './types';
import { useReturnState } from '../../lib/returnTo';

const WAITING_SHOWN = 5;
const WAITING_FILTERS = { status: 'handover_pending', widgetId: '' } as const;

function Section({ title, description, action, children }: { title: ReactNode; description?: ReactNode; action?: ReactNode; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="space-y-3">
      <div className="flex min-h-7 flex-wrap items-end justify-between gap-x-4 gap-y-1">
        <div className="min-w-0">
          <h2 id={id} className="flex items-center gap-2 text-section text-fg">
            {title}
          </h2>
          {description ? <p className="text-small text-fg-muted">{description}</p> : null}
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}

function StatsSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Loading the numbers
      </p>
      {Array.from({ length: 4 }, (_, index) => (
        <div key={index} aria-hidden className="rounded-2xl border border-border bg-surface p-4">
          <Skeleton className="h-4 w-24" />
          <Skeleton className="mt-2 h-7 w-14" />
          <Skeleton className="mt-2 h-3.5 w-28" />
        </div>
      ))}
    </>
  );
}

function RowsSkeleton({ label }: { label: string }) {
  return (
    <div className="divide-y divide-border-subtle">
      <p role="status" className="sr-only">
        {label}
      </p>
      {Array.from({ length: 3 }, (_, index) => (
        <div key={index} aria-hidden className="flex items-center gap-3 px-4 py-3">
          <Skeleton shape="tile" className="size-8" />
          <div className="flex-1 space-y-1.5">
            <Skeleton className="h-4 w-2/5" />
            <Skeleton className="h-3.5 w-3/5" />
          </div>
        </div>
      ))}
    </div>
  );
}

function Quiet({ icon: Icon, title, children }: { icon: LucideIcon; title: string; children: ReactNode }) {
  return (
    <div className="flex items-start gap-3 px-4 py-5">
      <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-muted">
        <Icon className="size-4" aria-hidden />
      </span>
      <div className="min-w-0">
        <p className="text-sm font-medium text-fg">{title}</p>
        <p className="mt-0.5 text-small text-fg-secondary">{children}</p>
      </div>
    </div>
  );
}

function share(part: number, whole: number): string {
  return whole > 0 ? `${Math.round((part / whole) * 100)}% of conversations` : 'No conversations yet';
}

/** Against the week before; none without a previous week to compare. */
function conversationTrend(current: number, previous: number): StatTrend | undefined {
  if (previous === 0) {
    return undefined;
  }
  const change = Math.round(((current - previous) / previous) * 100);
  if (change === 0) {
    return { direction: 'flat', label: 'Same as the week before' };
  }
  return { direction: change > 0 ? 'up' : 'down', label: `${change > 0 ? '+' : ''}${change}% on the week before` };
}

function Stats({ overview }: { overview: OverviewDto }) {
  return (
    <>
      <StatCard
        label="Conversations"
        value={formatNumber(overview.conversations)}
        trend={conversationTrend(overview.conversations, overview.previousConversations)}
        caption={`${formatNumber(overview.websiteConversations)} on your website`}
      />
      <StatCard label="Answered by AI" value={formatNumber(overview.answeredByAi)} caption={share(overview.answeredByAi, overview.conversations)} />
      <StatCard label="Handed to a person" value={formatNumber(overview.handedToPerson)} caption={share(overview.handedToPerson, overview.conversations)} />
      <StatCard
        label="Average rating"
        value={overview.averageRating === null ? '–' : overview.averageRating.toFixed(1)}
        caption={overview.ratings > 0 ? `Out of 5, from ${pluralize(overview.ratings, 'rating')}` : 'No ratings yet'}
      />
    </>
  );
}

function Time({ value }: { value: string }) {
  return (
    <time dateTime={value} title={formatDateTime(value)} className="shrink-0 text-xs text-fg-muted tabular-nums">
      {formatRelativeTime(value)}
    </time>
  );
}

function WaitingList({ conversations, names }: { conversations: readonly InboxConversationDto[]; names: ReadonlyMap<string, string> }) {
  const returnState = useReturnState('Home');
  const more = conversations.length - WAITING_SHOWN;
  return (
    <>
      <ul aria-label="Waiting for a person" className="divide-y divide-border-subtle">
        {conversations.slice(0, WAITING_SHOWN).map((conversation) => (
          <li key={conversation.id}>
            <Link to={`/inbox/${conversation.id}`} state={returnState} className="flex items-start gap-3 px-4 py-3 hover:bg-fill-hover">
              <StatusDot tone="warning" className="mt-1.5" />
              <div className="min-w-0 flex-1">
                <div className="flex items-baseline justify-between gap-3">
                  <p className="truncate text-sm font-medium text-fg">{conversation.title || 'New conversation'}</p>
                  <Time value={conversation.updatedAt} />
                </div>
                {conversation.preview ? <p className="mt-0.5 truncate text-small text-fg-secondary">{conversation.preview}</p> : null}
                <p className="mt-0.5 truncate text-xs text-fg-muted">{widgetLabel(conversation.widgetId, names)}</p>
              </div>
            </Link>
          </li>
        ))}
      </ul>
      {more > 0 ? (
        <Link to="/inbox" state={returnState} className="block border-t border-border-subtle px-4 py-2.5 text-small font-medium text-accent-text hover:bg-fill-hover">
          {more === 1 ? '1 more in the Inbox' : `${formatNumber(more)} more in the Inbox`}
        </Link>
      ) : null}
    </>
  );
}

function RecentList({ conversations, names }: { conversations: readonly RecentConversation[]; names: ReadonlyMap<string, string> }) {
  const returnState = useReturnState('Home');
  return (
    <ul aria-label="Recent conversations" className="divide-y divide-border-subtle">
      {conversations.map((conversation) => {
        const website = conversation.channel === 'widget';
        const Icon = website ? Globe : MessageSquare;
        return (
          <li key={conversation.id}>
            <Link to={website ? `/inbox/${conversation.id}` : `/chat/${conversation.id}`} state={returnState} className="flex items-center gap-3 px-4 py-3 hover:bg-fill-hover">
              <span aria-hidden className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-muted">
                <Icon className="size-4" />
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-fg">{conversation.title || 'New conversation'}</p>
                <p className="flex min-w-0 items-center gap-1.5 text-xs text-fg-muted">
                  <span className="truncate">{website ? widgetLabel(conversation.widgetId, names) : 'Portal chat'}</span>
                  <span aria-hidden>·</span>
                  <Time value={conversation.updatedAt} />
                </p>
              </div>
              {website ? <Badge tone={STATUS_TONES[conversation.status]}>{STATUS_LABELS[conversation.status]}</Badge> : null}
            </Link>
          </li>
        );
      })}
    </ul>
  );
}

export function HomePage() {
  const returnState = useReturnState('Home');
  const overview = useOverview();
  const profiles = useProfiles();
  const sources = useSourceStats();
  const agents = useAgents();
  const widgets = useWidgets();
  const waiting = useInboxConversations(WAITING_FILTERS);
  const recent = useRecentConversations();

  const names = useMemo(() => new Map((widgets.data ?? []).map((widget) => [widget.id.toLowerCase(), widget.name])), [widgets.data]);
  const modelConnected = profiles.data ? setupMessage(profiles.data) === null : undefined;
  const steps = setupSteps({
    modelConnected,
    hasSources: sources.data ? sources.data.total > 0 : undefined,
    hasAgent: agents.data ? ownAgents(agents.data).length > 0 : undefined,
    firstWidgetId: widgets.data ? ([...widgets.data].sort((a, b) => a.name.localeCompare(b.name))[0]?.id ?? null) : undefined,
    widgetLive: overview.data ? overview.data.widgets.some((widget) => widget.lastConversationAt !== null) : undefined,
  });
  const waitingCount = waiting.data?.length ?? 0;

  return (
    <Page>
      <PageHeader title="Home" description="How your agents are doing, and what needs you now." />

      {steps && steps.some((step) => step.state !== 'done') ? <SetupChecklist title="Get your agent on your website" steps={steps} /> : null}

      <Section
        title={`Last ${OVERVIEW_DAYS} days`}
        description="Conversations on your website and in the portal chat."
        action={
          <Link to="/analytics" state={returnState} className={buttonClass({ variant: 'ghost', size: 'sm' })}>
            See analytics
            <ArrowRight className="size-3.5" aria-hidden />
          </Link>
        }
      >
        {overview.isError ? (
          <ErrorMessage error={overview.error} title="Could not load the numbers" />
        ) : (
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">{overview.data ? <Stats overview={overview.data} /> : <StatsSkeleton />}</div>
        )}
      </Section>

      <div className="grid gap-8 lg:grid-cols-2">
        <NeedsAttention
          action={
            <Link to="/inbox" state={returnState} className={buttonClass({ variant: 'ghost', size: 'sm' })}>
              Open inbox
              <ArrowRight className="size-3.5" aria-hidden />
            </Link>
          }
        >
          {waiting.isError ? (
            <ErrorMessage error={waiting.error} title="Could not load the requests" className="m-4" />
          ) : waitingCount > 0 && waiting.data ? (
            <>
              <h3 className="px-4 pt-3 text-xs font-medium text-fg-muted">
                Waiting for a person
                <span className="sr-only">: {formatNumber(waitingCount)}</span>
              </h3>
              <WaitingList conversations={waiting.data} names={names} />
            </>
          ) : null}
        </NeedsAttention>

        <Section title="Recent activity" description="The latest conversations, newest first.">
          <Card className="overflow-hidden">
            {recent.isError ? (
              <ErrorMessage error={recent.error} title="Could not load the conversations" className="m-4" />
            ) : !recent.data ? (
              <RowsSkeleton label="Loading the conversations" />
            ) : recent.data.length === 0 ? (
              <Quiet icon={MessagesSquare} title="No conversations yet">
                Conversations in the portal chat and on your website show up here.
              </Quiet>
            ) : (
              <RecentList conversations={recent.data} names={names} />
            )}
          </Card>
        </Section>
      </div>
    </Page>
  );
}
