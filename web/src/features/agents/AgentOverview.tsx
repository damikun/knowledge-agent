import { AppWindow, Blocks, Code, FlaskConical, PartyPopper, X } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Button, buttonClass } from '../../components/ui/Button';
import { Skeleton } from '../../components/ui/Skeleton';
import { StatCard } from '../../components/ui/StatCard';
import { capitalize, pluralize } from '../../lib/format';
import { useAnalytics } from '../analytics/api';
import { profileRole } from '../models/profileSettings';
import type { FolderNode } from '../sources/folders';
import type { WidgetDto } from '../widgets/types';
import { HANDOVER_POLICIES, initialAgentValues, scopeSummary, toolLabel, type AgentSection } from './agentForm';
import { agentSentence, namesList, playgroundLink, tonePhrase } from './agentSummary';
import { useAgents } from './api';
import { appsSummary } from './McpAppsField';
import { languageName } from './languages';
import { useMcpServers } from '../tools/mcp';
import { PutOnWebsite } from './PutOnWebsite';
import type { AgentDto, AgentOptionsDto } from './types';
import { useReturnState } from '../../lib/returnTo';
import type { AgentTab } from './agentTabs';

function NextSteps({ agent, onDismiss }: { agent: AgentDto; onDismiss: () => void }) {
  const returnState = useReturnState();
  return (
    <section aria-labelledby="next-steps-heading" className="flex items-start gap-3 rounded-2xl border border-border bg-surface-subtle p-4">
      <span aria-hidden className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-success-subtle text-success-text">
        <PartyPopper className="size-4" />
      </span>
      <div className="min-w-0 flex-1">
        <h2 id="next-steps-heading" className="text-sm font-semibold text-fg">
          {agent.name} is ready
        </h2>
        <p className="mt-0.5 text-small text-fg-secondary">Ask it a few real questions first, then put it on your website for your visitors.</p>
        <div className="mt-3 flex flex-wrap gap-2">
          <Link to={playgroundLink(agent)} state={returnState} className={buttonClass({ variant: 'primary' })}>
            <FlaskConical className="size-4" aria-hidden />
            Test it in the Playground
          </Link>
          <PutOnWebsite agent={agent} />
        </div>
      </div>
      <Button variant="ghost" size="icon-sm" icon={X} aria-label="Hide next steps" onClick={onDismiss} />
    </section>
  );
}

export interface OverviewProps {
  agent: AgentDto;
  options: AgentOptionsDto;
  tree: readonly FolderNode[] | undefined;
  using: readonly WidgetDto[] | undefined;
  justCreated: boolean;
  onEdit: (tab: AgentTab) => void;
}

export function AgentOverview({ agent, options, tree, using, justCreated, onEdit }: OverviewProps) {
  const returnState = useReturnState();
  const [showNext, setShowNext] = useState(justCreated);
  const values = initialAgentValues(agent, options);
  const tone = tonePhrase(values.instructions);
  const tools = options.tools.filter((tool) => values.tools.includes(tool)).map(toolLabel);
  const servers = useMcpServers();
  const apps = appsSummary(agent.mcpApps, servers.data);
  const allAgents = useAgents().data ?? [];
  const specialistNames = (values.specialists ?? []).map(
    (specialist) => allAgents.find((other) => other.id.toLowerCase() === specialist.agentId.toLowerCase())?.name ?? 'Deleted agent',
  );
  const rows: Array<{ label: string; value: ReactNode; tab: AgentSection }> = [
    { label: 'Language', value: values.language.trim() ? languageName(values.language) : 'Same as the visitor', tab: 'general' },
    { label: 'Tone', value: tone ? capitalize(tone) : 'Set in the instructions', tab: 'behavior' },
    {
      label: 'Knows',
      value: `${values.scope === 'all' ? 'All your sources' : scopeSummary(values.folderIds, tree)}${values.allowGeneralKnowledge ? ', and fills gaps from general knowledge' : ''}`,
      tab: 'knowledge',
    },
    { label: 'Tools', value: tools.length > 0 ? tools.join(', ') : 'None, so it cannot search your sources', tab: 'tools' },
    { label: 'Connected apps', value: apps ? `Uses ${apps}` : 'None', tab: 'tools' as const },
    {
      label: 'Talk to a person',
      value: HANDOVER_POLICIES.find((policy) => policy.value === values.handoverPolicy)?.label ?? '',
      tab: 'handover' as const,
    },
    {
      label: 'Specialists',
      value: specialistNames.length > 0 ? `Hands off to ${namesList(specialistNames)}` : 'None, it answers everything itself',
      tab: 'specialists',
    },
    { label: 'Model', value: profileRole(values.profileName).label, tab: 'advanced' },
  ];

  return (
    <div className="space-y-6">
      {showNext ? <NextSteps agent={agent} onDismiss={() => setShowNext(false)} /> : null}

      <div className="@container">
        <div className="grid gap-6 @min-[52rem]:grid-cols-[minmax(0,1fr)_19rem]">
          <section aria-labelledby="overview-summary-heading" className="min-w-0 rounded-2xl border border-border bg-surface shadow-card">
            <div className="border-b border-border-subtle p-5">
              <h2 id="overview-summary-heading" className="text-section text-fg">
                In short
              </h2>
              <p data-testid="agent-sentence" className="mt-1 text-sm text-fg-secondary">
                {agentSentence(values, { widgetNames: using?.map((widget) => widget.name) })}
              </p>
            </div>
            <dl className="divide-y divide-border-subtle px-5">
              {rows.map((row) => (
                <div key={row.label} className="flex items-start gap-4 py-3">
                  <dt className="w-36 shrink-0 text-small text-fg-muted">{row.label}</dt>
                  <dd className="min-w-0 flex-1 text-small text-fg">{row.value}</dd>
                  <dd className="shrink-0">
                    <Button size="sm" variant="ghost" onClick={() => onEdit(row.tab)} aria-label={`Edit ${row.label.toLowerCase()}`}>
                      Edit
                    </Button>
                  </dd>
                </div>
              ))}
            </dl>
          </section>

          <div className="min-w-0 space-y-6">
            <section aria-labelledby="overview-website-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-card">
              <h2 id="overview-website-heading" className="text-section text-fg">
                Where it answers
              </h2>
              <p className="mt-1 text-small text-fg-muted">Widgets show this agent on your websites. Each one sets its look and where requests for a person go.</p>
              <p className="mt-1 text-xs text-fg-muted" data-testid="agent-widget-note">
                This page sets what the agent knows, how it answers and which tools it uses. Each widget adds its own rules for its visitors:
                whether a person is offered and how, how sources show, feedback, contact details, who may chat and message limits. Open a widget
                and look under What this widget changes.
              </p>
              {using && using.length > 0 ? (
                <ul aria-label="Widgets using this agent" className="mt-3 space-y-1.5">
                  {using.map((widget) => (
                    <li key={widget.id} className="flex min-w-0 items-center gap-2 text-small">
                      <AppWindow className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
                      <Link to={`/widgets/${widget.id}`} state={returnState} className="min-w-0 truncate font-medium text-accent-text hover:underline">
                        {widget.name}
                      </Link>
                      <span className="shrink-0 text-xs text-fg-muted">{widget.enabled ? 'Website widget' : 'Website widget, disabled'}</span>
                    </li>
                  ))}
                </ul>
              ) : using ? (
                <p className="mt-3 text-small text-fg-secondary">No widget uses it yet, so only the Playground reaches it.</p>
              ) : null}
              {agent.mcpServerEnabled || agent.httpApiEnabled ? (
                <ul aria-label="Other ways it answers" className="mt-2 space-y-1.5">
                  {agent.mcpServerEnabled ? (
                    <li className="flex min-w-0 items-center gap-2 text-small">
                      <Blocks className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
                      <button type="button" onClick={() => onEdit('elsewhere')} className="min-w-0 truncate font-medium text-accent-text hover:underline">
                        MCP server
                      </button>
                      <span className="shrink-0 text-xs text-fg-muted">For AI apps</span>
                    </li>
                  ) : null}
                  {agent.httpApiEnabled ? (
                    <li className="flex min-w-0 items-center gap-2 text-small">
                      <Code className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
                      <button type="button" onClick={() => onEdit('elsewhere')} className="min-w-0 truncate font-medium text-accent-text hover:underline">
                        HTTP API
                      </button>
                      <span className="shrink-0 text-xs text-fg-muted">For your software</span>
                    </li>
                  ) : null}
                </ul>
              ) : null}
              {showNext ? null : (
                <div className="mt-3">
                  <PutOnWebsite agent={agent} variant="secondary" />
                </div>
              )}
            </section>
          </div>
        </div>
      </div>

      {using && using.length > 0 ? <AgentActivity widgets={using} /> : null}
    </div>
  );
}

function AgentActivity({ widgets }: { widgets: readonly WidgetDto[] }) {
  const analytics = useAnalytics(7);
  if (analytics.isError) {
    return null;
  }
  const ids = new Set(widgets.map((widget) => widget.id.toLowerCase()));
  const rows = analytics.data?.widgets.filter((row) => row.widgetId && ids.has(row.widgetId.toLowerCase())) ?? [];
  const conversations = rows.reduce((sum, row) => sum + row.conversations, 0);
  const handed = rows.reduce((sum, row) => sum + row.handedToPerson, 0);
  const ratings = rows.reduce((sum, row) => sum + row.ratings, 0);
  const rating = ratings > 0 ? rows.reduce((sum, row) => sum + (row.averageRating ?? 0) * row.ratings, 0) / ratings : null;
  const answered = Math.max(0, conversations - handed);

  return (
    <section aria-labelledby="overview-activity-heading" className="space-y-3">
      <h2 id="overview-activity-heading" className="text-section text-fg">
        Last 7 days
      </h2>
      {!analytics.data ? (
        <div aria-hidden className="grid gap-4 @min-[40rem]:grid-cols-3">
          {Array.from({ length: 3 }, (_, index) => (
            <Skeleton key={index} shape="card" className="h-24" />
          ))}
        </div>
      ) : (
        <div className="@container">
          <div className="grid gap-4 @min-[36rem]:grid-cols-3">
            <StatCard label="Conversations" value={conversations} caption={`On ${namesList(widgets.map((widget) => widget.name))}`} />
            <StatCard
              label="Answered without a person"
              value={answered}
              caption={conversations > 0 ? `${Math.round((answered / conversations) * 100)}% of conversations` : 'No conversations yet'}
            />
            <StatCard
              label="Rating"
              value={rating === null ? 'None yet' : rating.toFixed(1)}
              caption={ratings > 0 ? `From ${pluralize(ratings, 'rating')}` : 'Visitors rate a conversation at its end'}
            />
          </div>
        </div>
      )}
    </section>
  );
}

