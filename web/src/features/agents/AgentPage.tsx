import { Bot, Copy, FlaskConical, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, Navigate, useLocation, useNavigate, useParams, useSearchParams } from 'react-router';
import { buttonClass } from '../../components/ui/Button';
import { Card, EmptyState, ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { Menu, type MenuItem } from '../../components/ui/Menu';
import { Page } from '../../components/ui/Page';
import { Skeleton } from '../../components/ui/Skeleton';
import { StatusLabel } from '../../components/ui/StatusDot';
import { TabPanel, Tabs } from '../../components/ui/Tabs';
import { useBreadcrumb } from '../../lib/breadcrumb';
import { pluralize } from '../../lib/format';
import { useSourceFolders } from '../sources/api';
import { buildFolderTree } from '../sources/folders';
import { useWidgets } from '../widgets/api';
import { playgroundLink } from './agentSummary';
import { useAgentOptions, useAgents } from './api';
import { DeleteAgentDialog } from './DeleteAgentDialog';
import { useLeaveGuard } from '../../components/ui/LeaveGuard';
import { UseElsewhere } from './UseElsewhere';
import type { AgentDto, AgentOptionsDto } from './types';
import { AgentOverview } from './AgentOverview';
import { SectionForm } from './AgentSectionForm';
import { parseTab, TABS, type AgentTab } from './agentTabs';
import { useReturnState } from '../../lib/returnTo';

function PageSkeleton() {
  return (
    <Page>
      <p role="status" className="sr-only">
        Loading the agent
      </p>
      <Skeleton className="h-8 w-64" />
      {Array.from({ length: 2 }, (_, index) => (
        <Skeleton key={index} shape="card" className="h-40 max-w-5xl" />
      ))}
    </Page>
  );
}

export function AgentPage() {
  const { agentId = '' } = useParams();
  const agents = useAgents();
  const options = useAgentOptions();
  const agent = agents.data?.find((item) => item.id.toLowerCase() === agentId.toLowerCase()) ?? null;
  useBreadcrumb(agent?.name);

  const error = options.error ?? agents.error;
  if (error) {
    return (
      <Page width="narrow">
        <ErrorMessage error={error} title="Could not load the agent" />
      </Page>
    );
  }
  if (!agents.data || !options.data) {
    return <PageSkeleton />;
  }
  if (agent?.isBuiltIn) {
    return <Navigate to="/agents" replace />;
  }
  if (!agent) {
    return (
      <Page width="narrow">
        <Card>
          <EmptyState
            icon={Bot}
            title="This agent does not exist"
            headingLevel={1}
            description="It may have been deleted."
            action={
              <Link to="/agents" className={buttonClass()}>
                All agents
              </Link>
            }
          />
        </Card>
      </Page>
    );
  }
  return <AgentView key={agent.id} agent={agent} options={options.data} />;
}

function AgentView({ agent, options }: { agent: AgentDto; options: AgentOptionsDto }) {
  const returnState = useReturnState();
  const navigate = useNavigate();
  const location = useLocation();
  const [search, setSearch] = useSearchParams();
  const tab = parseTab(search.get('tab'));
  const widgets = useWidgets();
  const folders = useSourceFolders();
  const tree = useMemo(() => (folders.data ? buildFolderTree(folders.data.folders) : undefined), [folders.data]);
  const using = useMemo(() => widgets.data?.filter((widget) => widget.agentId.toLowerCase() === agent.id.toLowerCase()), [agent.id, widgets.data]);
  const [deleting, setDeleting] = useState(false);
  const guard = useLeaveGuard();
  const justCreated = (location.state as { created?: boolean } | null)?.created === true;
  const idPrefix = `agent-${agent.id}`;

  const openTab = (next: AgentTab) =>
    setSearch(
      (current) => {
        const params = new URLSearchParams(current);
        if (next === 'overview') {
          params.delete('tab');
        } else {
          params.set('tab', next);
        }
        return params;
      },
      // Keep the new-agent state while switching tabs.
      { state: location.state },
    );

  const menuItems: MenuItem[] = [
    { label: 'Duplicate', icon: Copy, onSelect: () => navigate(`/agents/new?from=${encodeURIComponent(agent.id)}`) },
    { label: 'Delete', icon: Trash2, danger: true, onSelect: () => setDeleting(true) },
  ];

  const status = using ? (
    <StatusLabel tone={using.length > 0 ? 'success' : 'neutral'}>
      {using.length > 0 ? `Used by ${pluralize(using.length, 'widget')}` : 'Not used yet'}
    </StatusLabel>
  ) : null;

  return (
    <Page>
      <div className="max-w-5xl space-y-3">
        <PageHeader
          title={agent.name}
          description={agent.description ?? 'No purpose written yet.'}
          actions={
            <>
              <Menu label="More actions for this agent" items={menuItems} />
              <Link to={playgroundLink(agent)} state={returnState} className={buttonClass()}>
                <FlaskConical className="size-4" aria-hidden />
                Test in Playground
              </Link>
            </>
          }
        />
        {status ? <div data-testid="agent-status">{status}</div> : null}
      </div>

      <div className="max-w-5xl space-y-6">
        <Tabs idPrefix={idPrefix} label="Agent sections" items={TABS} value={tab} onChange={openTab} className="overflow-x-auto" />
        <TabPanel idPrefix={idPrefix} id={tab} className="focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-focus">
          {tab === 'overview' ? (
            <AgentOverview agent={agent} options={options} tree={tree} using={using} justCreated={justCreated} onEdit={openTab} />
          ) : tab === 'elsewhere' ? (
            <UseElsewhere agent={agent} />
          ) : (
            <SectionForm key={tab} agent={agent} options={options} section={tab} using={using} guard={guard} />
          )}
        </TabPanel>
      </div>

      <DeleteAgentDialog
        agent={deleting ? agent : null}
        onClose={() => setDeleting(false)}
        onDeleted={() => {
          guard.allowLeave();
          navigate('/agents', { replace: true });
        }}
      />
    </Page>
  );
}
