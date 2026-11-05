import { Inbox } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { createPath, Navigate, useParams, useSearchParams, type Path } from 'react-router';
import { EmptyState } from '../../components/ui/feedback';
import { useKeptReturnState } from '../../lib/returnTo';
import { useElementSize } from '../../lib/hooks/useElementSize';
import { isAdmin, useMe } from '../auth/api';
import { labelsAvailable, labelsOrEmpty, useLabels } from '../labels/api';
import { ManageLabelsSheet } from '../labels/ManageLabelsSheet';
import { useWidgets } from '../widgets/api';
import { useInboxCounts } from './api';
import { ConversationView } from './ConversationView';
import { INBOX_VIEWS, inboxContext, readInboxState, toInboxSearch } from './inboxState';
import type { InboxState, InboxView } from './types';
import { ConversationList } from './views/ConversationList';
import { ViewTabs } from './views/ViewTabs';

// Widths of the Inbox's own box, not the window.
const WITH_DETAILS = 1180;
const TWO_PANES = 768;
const DETAILS_KEY = 'inbox.details';

const EMPTY_VIEWS: Readonly<Record<InboxView, { title: string; description: string }>> = {
  mine: { title: 'Nothing assigned to you', description: 'Conversations you take over or someone assigns to you show up here.' },
  unassigned: { title: 'Every conversation has an owner', description: 'Visitors waiting for a person or talking to one, with nobody assigned, show up here.' },
  waiting: { title: 'Nobody is waiting', description: 'Visitors who ask for a person show up here.' },
  ai: { title: 'No conversations with the assistant', description: 'Conversations the assistant answers on its own show up here.' },
  closed: { title: 'No closed conversations', description: 'Conversations that ended show up here.' },
  all: { title: 'No conversations yet', description: 'Conversations from your website widgets show up here.' },
};

function readDetailsPreference(): boolean {
  try {
    return localStorage.getItem(DETAILS_KEY) !== 'hidden';
  } catch {
    return true;
  }
}

export function InboxPage() {
  const { conversationId: routeId } = useParams();
  const [params, setParams] = useSearchParams();
  const state = readInboxState(params);
  const admin = isAdmin(useMe().data);
  // Support users can't read widgets (403), so they see widget ids.
  const widgets = useWidgets(admin);
  const widgetNames = useMemo(
    () => (admin && widgets.data ? new Map(widgets.data.map((widget) => [widget.id.toLowerCase(), widget.name] as const)) : undefined),
    [admin, widgets.data],
  );
  const labelsQuery = useLabels();
  const labels = labelsOrEmpty(labelsQuery.data);
  const counts = useInboxCounts({ channel: state.channel, widgetId: state.widgetId, contactId: state.contactId });
  const [managing, setManaging] = useState(false);
  // Null until measured; tests never measure and get the widest layout.
  const [root, setRoot] = useState<HTMLDivElement | null>(null);
  const size = useElementSize(root);
  const width = size && size.width > 0 ? size.width : null;
  const [inlineDetails, setInlineDetails] = useState(readDetailsPreference);
  const [sheetDetails, setSheetDetails] = useState(false);
  const listId = useId();
  // Opened from elsewhere (a contact, Analytics), so moves inside the Inbox keep that way back.
  const kept = useKeptReturnState();

  // Deep links to /inbox/{id} without a view show only that conversation; with one (?status=closed) it opens in that view.
  if (routeId) {
    const next = state.viewChosen ? { ...state, conversationId: routeId } : { ...state, conversationId: routeId, only: routeId };
    return <Navigate to={{ pathname: '/inbox', search: `?${toInboxSearch(next).toString()}` }} state={kept} replace />;
  }

  const layout = width === null || width >= WITH_DETAILS ? 'wide' : width >= TWO_PANES ? 'two' : 'one';
  const detailsMode = layout === 'wide' ? 'inline' : 'sheet';
  const linkFor = (next: InboxState): Path => ({ pathname: '/inbox', search: `?${toInboxSearch(next).toString()}`, hash: '' });
  const open = state.conversationId;
  const context = inboxContext(state);
  const viewName = context === 'only' ? 'This conversation' : context === 'contact' ? 'This contact' : (INBOX_VIEWS.find((view) => view.id === state.view)?.label ?? 'All');
  const showList = layout !== 'one' || !open;
  const showThread = layout !== 'one' || Boolean(open);
  const manageLabels = admin && labelsAvailable(labelsQuery.data) ? () => setManaging(true) : undefined;

  return (
    <div ref={setRoot} className="flex h-full min-h-0 flex-col">
      <h1 className="sr-only">Inbox</h1>
      {showList ? <ViewTabs
          state={state}
          counts={counts.data}
          // Choosing a tab ends a contact or single-conversation deep link.
          linkTo={(view) => linkFor({ ...state, view, viewChosen: true, only: '' })}
          panelId={listId}
          context={context !== null}
        /> : null}
      <div className="flex min-h-0 flex-1">
        {showList ? (
          <aside id={listId} aria-label="Inbox conversations" className={layout === 'one' ? 'flex min-w-0 flex-1' : 'flex w-90 shrink-0 border-r border-border-subtle'}>
            <ConversationList
              state={state}
              onChange={(next) => setParams(toInboxSearch(next), { replace: true, state: kept })}
              header={<h2 className="sr-only">{viewName}</h2>}
              linkTo={(id) => createPath(linkFor({ ...state, conversationId: id }))}
              widgetNames={widgetNames}
              canFilterByWidget={admin}
              labels={labels}
              onManageLabels={manageLabels}
              emptyText={EMPTY_VIEWS[state.view]}
            />
          </aside>
        ) : null}
        {showThread ? (
          <section aria-label="Conversation" className="flex min-w-0 flex-1 flex-col">
            {open ? (
              <ConversationView
                key={open}
                conversationId={open}
                widgetNames={widgetNames}
                detailsMode={detailsMode}
                detailsOpen={detailsMode === 'inline' ? inlineDetails : sheetDetails}
                onDetailsOpenChange={(next) => {
                  if (detailsMode === 'sheet') {
                    setSheetDetails(next);
                    return;
                  }
                  setInlineDetails(next);
                  try {
                    localStorage.setItem(DETAILS_KEY, next ? 'shown' : 'hidden');
                  } catch {
                    // Without storage the choice lasts until reload.
                  }
                }}
                backTo={layout === 'one' ? linkFor({ ...state, conversationId: null }) : null}
                canAddToDataset={admin}
              />
            ) : (
              <EmptyState
                icon={Inbox}
                title="Select a conversation"
                description="Conversations waiting for a person are marked. Take one over to reply to the visitor."
                className="h-full"
              />
            )}
          </section>
        ) : null}
      </div>
      {manageLabels ? <ManageLabelsSheet open={managing} onClose={() => setManaging(false)} /> : null}
    </div>
  );
}
