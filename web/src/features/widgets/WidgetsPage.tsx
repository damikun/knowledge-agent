import { AppWindow, Bot, FlaskConical, MessagesSquare, Plus, Trash2 } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router';
import { StatusLabel } from '../../components/ui/StatusDot';
import { buttonClass } from '../../components/ui/Button';
import type { EntityMeta } from '../../components/ui/EntityCard';
import { Card, EmptyState, ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { CardGrid, Page } from '../../components/ui/Page';
import { Menu } from '../../components/ui/Menu';
import { formatRelativeTime, hostname, pluralize } from '../../lib/format';
import { ownAgents, useAgents } from '../agents/api';
import { useIntegrations } from '../notifications/api';
import { OVERVIEW_DAYS, useOverview } from '../overview/api';
import type { WidgetActivityDto } from '../overview/types';
import { usePrefetchWidgetEditor, useWidgetOptions, useWidgets } from './api';
import { DeleteWidgetDialog } from './DeleteWidgetDialog';
import { EditorSkeleton } from './editor/EditorSkeleton';
import { NewWidgetPage } from './NewWidgetWizard';
import type { WidgetDto } from './types';
import { WidgetCard } from './WidgetCard';
import { WidgetEditor } from './WidgetEditor';
import { WidgetPreviewDialog } from './WidgetPreviewDialog';

const NEW_WIDGET = 'new';

function PageFrame({ children }: { children: ReactNode }) {
  return <Page>{children}</Page>;
}

function sites(widget: WidgetDto): string | undefined {
  const [first, ...rest] = widget.settings.security.allowedOrigins.map((origin) => hostname(origin) ?? origin);
  if (!first) {
    return undefined;
  }
  return rest.length > 0 ? `On ${first} and ${pluralize(rest.length, 'more site')}` : `On ${first}`;
}

function activityLabel(activity: WidgetActivityDto | undefined): string {
  if (!activity?.lastConversationAt) {
    return 'No conversations yet';
  }
  return activity.conversations > 0
    ? `${pluralize(activity.conversations, 'conversation')} in ${OVERVIEW_DAYS} days`
    : `Last conversation ${formatRelativeTime(activity.lastConversationAt)}`;
}

function GridSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Loading widgets
      </p>
      <CardGrid>
        {Array.from({ length: 3 }, (_, index) => (
          <Skeleton key={index} shape="card" className="h-[16.5rem]" />
        ))}
      </CardGrid>
    </>
  );
}

function WidgetList() {
  const widgets = useWidgets();
  const agents = useAgents();
  const overview = useOverview();
  const prefetchEditor = usePrefetchWidgetEditor();
  const [pendingDelete, setPendingDelete] = useState<WidgetDto | null>(null);
  const [previewed, setPreviewed] = useState<WidgetDto | null>(null);
  const sorted = useMemo(() => [...(widgets.data ?? [])].sort((a, b) => a.name.localeCompare(b.name)), [widgets.data]);
  const activity = useMemo(() => new Map((overview.data?.widgets ?? []).map((item) => [item.widgetId.toLowerCase(), item])), [overview.data]);
  const agentName = (id: string) => agents.data?.find((agent) => agent.id.toLowerCase() === id.toLowerCase())?.name ?? 'Unknown agent';

  const meta = (widget: WidgetDto): EntityMeta[] => [
    ...(agents.data ? [{ icon: Bot, label: agentName(widget.agentId) }] : []),
    // Hidden until loaded, rather than showing a wrong "no conversations".
    ...(overview.data ? [{ icon: MessagesSquare, label: activityLabel(activity.get(widget.id.toLowerCase())) }] : []),
  ];

  return (
    <PageFrame>
      <PageHeader
        title="Widgets"
        description="A widget puts a chat on your website. Each one answers with one of your agents and shows only what that agent may search."
        actions={
          <Link to={`/widgets/${NEW_WIDGET}`} onPointerEnter={prefetchEditor} onFocus={prefetchEditor} className={buttonClass({ variant: 'primary' })}>
            <Plus className="size-4" aria-hidden />
            New widget
          </Link>
        }
      />
      {widgets.isPending ? (
        <GridSkeleton />
      ) : widgets.isError ? (
        <ErrorMessage error={widgets.error} title="Could not load widgets" />
      ) : sorted.length === 0 ? (
        <Card>
          <EmptyState
            icon={AppWindow}
            title="Put a chat on your website"
            description="A widget answers visitors with one of your agents. Create one, then paste its snippet into your site."
            action={
              <Link to={`/widgets/${NEW_WIDGET}`} className={buttonClass({ variant: 'secondary' })}>
                <Plus className="size-4" aria-hidden />
                New widget
              </Link>
            }
          />
        </Card>
      ) : (
        <CardGrid label="Widgets">
          {sorted.map((widget) => (
            <li key={widget.id}>
              <WidgetCard
                widget={widget}
                to={`/widgets/${widget.id}`}
                description={sites(widget)}
                warning={sites(widget) === undefined ? 'Any site may embed it' : undefined}
                onIntent={prefetchEditor}
                status={<StatusLabel tone={widget.enabled ? 'success' : 'neutral'}>{widget.enabled ? 'Enabled' : 'Disabled'}</StatusLabel>}
                actions={
                  <Menu
                    label={`Actions for ${widget.name}`}
                    items={[
                      { label: 'Preview', icon: FlaskConical, onSelect: () => setPreviewed(widget) },
                      { label: 'Delete widget', icon: Trash2, danger: true, onSelect: () => setPendingDelete(widget) },
                    ]}
                  />
                }
                meta={meta(widget)}
              />
            </li>
          ))}
        </CardGrid>
      )}
      <DeleteWidgetDialog widget={pendingDelete} onClose={() => setPendingDelete(null)} />
      <WidgetPreviewDialog widget={previewed} onClose={() => setPreviewed(null)} />
    </PageFrame>
  );
}

function WidgetEditorPage({ widgetId }: { widgetId: string }) {
  const widgets = useWidgets();
  const options = useWidgetOptions();
  const agents = useAgents();
  const integrations = useIntegrations();
  const widget = widgets.data?.find((item) => item.id.toLowerCase() === widgetId.toLowerCase()) ?? null;
  // The API rejects the built-in agent for widgets.
  const usable = useMemo(() => ownAgents(agents.data ?? []), [agents.data]);

  const error = options.error ?? agents.error ?? widgets.error;
  if (error) {
    return (
      <PageFrame>
        <ErrorMessage error={error} title="Could not load the widget editor" />
      </PageFrame>
    );
  }
  if (!options.data || !agents.data || !widgets.data) {
    return <EditorSkeleton title={widget?.name} />;
  }
  if (!widget) {
    return (
      <PageFrame>
        <Card>
          <EmptyState
            icon={AppWindow}
            title="This widget does not exist"
            description="It may have been deleted."
            action={
              <Link to="/widgets" className={buttonClass()}>
                All widgets
              </Link>
            }
          />
        </Card>
      </PageFrame>
    );
  }
  return <WidgetEditor key={widget.id} widget={widget} options={options.data} agents={usable} integrations={integrations.data} />;
}

export function WidgetsPage() {
  const { widgetId } = useParams();
  return widgetId === NEW_WIDGET ? <NewWidgetPage /> : widgetId ? <WidgetEditorPage widgetId={widgetId} /> : <WidgetList />;
}
