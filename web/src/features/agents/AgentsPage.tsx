import { AppWindow, Copy, Library, MessagesSquare, Plus, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { buttonClass } from '../../components/ui/Button';
import { EntityCard } from '../../components/ui/EntityCard';
import { ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { CardGrid, Page } from '../../components/ui/Page';
import { Menu } from '../../components/ui/Menu';
import { pluralize } from '../../lib/format';
import { useSourceFolders } from '../sources/api';
import { buildFolderTree } from '../sources/folders';
import { OVERVIEW_DAYS, useOverview } from '../overview/api';
import { useWidgets } from '../widgets/api';
import { scopeSummary } from './agentForm';
import { namesList } from './agentSummary';
import { AGENT_FRAMING } from './agentTemplates';
import { ownAgents, useAgents } from './api';
import { DeleteAgentDialog } from './DeleteAgentDialog';
import type { AgentDto } from './types';

function usedBy(names: readonly string[]): string {
  return names.length > 0 ? `On ${namesList(names)}` : 'Not on your website yet';
}

function GridSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Loading agents
      </p>
      <CardGrid>
        {Array.from({ length: 3 }, (_, index) => (
          <Skeleton key={index} shape="card" className="h-40" />
        ))}
      </CardGrid>
    </>
  );
}

function FirstAgentTile() {
  return (
    <Link
      to="/agents/new"
      className="flex h-full min-h-40 flex-col items-center justify-center gap-2 rounded-2xl border border-dashed border-border-strong p-4 text-center hover:bg-fill-hover"
    >
      <span className="flex size-10 items-center justify-center rounded-xl border border-border bg-surface text-fg-muted">
        <Plus className="size-5" aria-hidden />
      </span>
      <span className="text-sm font-semibold text-fg">Create an agent for your website</span>
      <span className="max-w-64 text-small text-fg-secondary">Start from customer support, sales or a helpdesk, then make it yours.</span>
    </Link>
  );
}

export function AgentsPage() {
  const navigate = useNavigate();
  const agents = useAgents();
  const folders = useSourceFolders();
  const widgets = useWidgets();
  const overview = useOverview();
  const tree = useMemo(() => (folders.data ? buildFolderTree(folders.data.folders) : undefined), [folders.data]);
  const usage = useMemo(() => {
    const activity = new Map((overview.data?.widgets ?? []).map((item) => [item.widgetId.toLowerCase(), item.conversations]));
    const byAgent = new Map<string, { names: string[]; conversations: number }>();
    for (const widget of widgets.data ?? []) {
      const key = widget.agentId.toLowerCase();
      const entry = byAgent.get(key) ?? { names: [], conversations: 0 };
      entry.names.push(widget.name);
      entry.conversations += activity.get(widget.id.toLowerCase()) ?? 0;
      byAgent.set(key, entry);
    }
    return byAgent;
  }, [widgets.data, overview.data]);
  const [pendingDelete, setPendingDelete] = useState<AgentDto | null>(null);

  const meta = (agent: AgentDto) => {
    const use = usage.get(agent.id.toLowerCase()) ?? { names: [], conversations: 0 };
    return [
      { icon: Library, label: scopeSummary(agent.folderIds, tree) },
      // Unknown until the widgets load, rather than a wrong "not on your website".
      ...(widgets.data
        ? [
            { icon: AppWindow, label: usedBy(use.names) },
            ...(overview.data && use.names.length > 0
              ? [{ icon: MessagesSquare, label: `${pluralize(use.conversations, 'conversation')} in ${OVERVIEW_DAYS} days` }]
              : []),
          ]
        : []),
    ];
  };

  return (
    <Page>
      <PageHeader
        title="Agents"
        description={`${AGENT_FRAMING} Widgets put an agent on your website.`}
        actions={
          <Link to="/agents/new" className={buttonClass({ variant: 'primary' })}>
            <Plus className="size-4" aria-hidden />
            New agent
          </Link>
        }
      />
      {agents.isPending ? (
        <GridSkeleton />
      ) : agents.isError ? (
        <ErrorMessage error={agents.error} title="Could not load agents" />
      ) : (
        <CardGrid label="Agents">
          {ownAgents(agents.data).map((agent) => (
            <li key={agent.id}>
              <EntityCard
                title={agent.name}
                titleAs="h2"
                description={agent.description ?? 'No purpose written yet'}
                to={`/agents/${agent.id}`}
                actions={
                  <Menu
                    label={`Actions for ${agent.name}`}
                    items={[
                      { label: 'Duplicate', icon: Copy, onSelect: () => navigate(`/agents/new?from=${encodeURIComponent(agent.id)}`) },
                      { label: 'Delete', icon: Trash2, danger: true, onSelect: () => setPendingDelete(agent) },
                    ]}
                  />
                }
                meta={meta(agent)}
                className="h-full"
              />
            </li>
          ))}
          {ownAgents(agents.data).length === 0 ? (
            <li>
              <FirstAgentTile />
            </li>
          ) : null}
        </CardGrid>
      )}
      <DeleteAgentDialog agent={pendingDelete} onClose={() => setPendingDelete(null)} />
    </Page>
  );
}
