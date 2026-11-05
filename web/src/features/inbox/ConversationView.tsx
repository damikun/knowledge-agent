import { ArrowLeft } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, type To } from 'react-router';
import { EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { SideSheet } from '../../components/ui/SideSheet';
import { ApiError } from '../../lib/api';
import { useKeptReturnState } from '../../lib/returnTo';
import { useAgents } from '../agents/api';
import { useConversation } from '../chat/api';
import { turnFromDto, type TurnView } from '../chat/chatReducer';
import { useLiveConversation } from '../chat/useLiveConversation';
import { AddToDatasetDialog, type DatasetQuestionSource } from '../evals/AddToDatasetDialog';
import { asInboxConversation, useConversationHistory, useHandover, useReplyToConversation } from './api';
import { ConversationDetails } from './ConversationDetails';
import { ConversationHeader } from './ConversationHeader';
import { ConversationThread } from './ConversationThread';
import { answerTurns, inboxTimeline } from './inboxTimeline';
import { ReplyBox } from './ReplyBox';
import { useMarkRead } from './useMarkRead';

const NO_TURNS: TurnView[] = [];

export interface ConversationViewProps {
  conversationId: string;
  widgetNames: ReadonlyMap<string, string> | undefined;
  /** A column beside the thread, or a sheet over it when the Inbox is narrow. */
  detailsMode: 'inline' | 'sheet';
  detailsOpen: boolean;
  onDetailsOpenChange: (open: boolean) => void;
  /** Null when the list and the thread show side by side. */
  backTo: To | null;
  canAddToDataset: boolean;
}

export function ConversationView({ conversationId, widgetNames, detailsMode, detailsOpen, onDetailsOpenChange, backTo, canAddToDataset }: ConversationViewProps) {
  const conversation = useConversation(conversationId);
  const handover = useHandover(conversationId);
  const history = useConversationHistory(conversationId);
  const agents = useAgents();
  const [adding, setAdding] = useState<DatasetQuestionSource | null>(null);
  const kept = useKeptReturnState();

  const turnHistory = useMemo(
    () => (conversation.data?.turns ?? []).slice().sort((a, b) => a.sequence - b.sequence).map(turnFromDto),
    [conversation.data],
  );
  const live = useLiveConversation({
    conversationId,
    enabled: conversation.data !== undefined,
    conversation: conversation.data,
    history: turnHistory,
    ownTurns: NO_TURNS,
  });
  const answers = useMemo(() => answerTurns(live.messages, turnHistory), [live.messages, turnHistory]);
  const items = useMemo(() => inboxTimeline(live.messages, turnHistory, live.turns, answers), [live.messages, turnHistory, live.turns, answers]);
  const latestSequence = live.messages.reduce<number | null>((latest, message) => (latest === null || message.sequence > latest ? message.sequence : latest), null);
  useMarkRead(conversationId, conversation.data ? latestSequence : null);

  const reply = useReplyToConversation(conversationId);

  const backLink = backTo ? (
    <Link to={backTo} state={kept} className="inline-flex items-center gap-1 rounded-sm text-small font-medium text-fg-secondary hover:text-fg">
      <ArrowLeft className="size-4" aria-hidden />
      All conversations
    </Link>
  ) : null;

  if (conversation.isPending) {
    return (
      <div className="flex h-full items-center justify-center">
        <Spinner label="Loading conversation" />
      </div>
    );
  }
  if (conversation.isError) {
    const notFound = conversation.error instanceof ApiError && conversation.error.status === 404;
    return (
      <div className="space-y-4 p-4">
        {backLink}
        {notFound ? (
          <EmptyState title="This conversation does not exist" description="It may have been deleted or expired." />
        ) : (
          <ErrorMessage error={conversation.error} title="Could not load the conversation" />
        )}
      </div>
    );
  }

  const data = asInboxConversation(conversation.data);
  const status = data.status;
  const fromWidget = data.channel === 'widget';
  const agentName = data.agentId ? agents.data?.find((agent) => agent.id.toLowerCase() === data.agentId!.toLowerCase())?.name : undefined;
  const visitorName = [...live.messages].reverse().find((message) => message.author === 'visitor' && message.authorName)?.authorName ?? null;
  const title = data.title || 'New conversation';
  // Nobody joined in time, so the team may follow up even after the conversation closed.
  const followUp = status === 'closed' && handover.data?.status === 'expired';

  const details = (
    <ConversationDetails
      conversation={data}
      visitorName={visitorName}
      agentName={agentName}
      widgetNames={widgetNames}
      handover={handover.data}
      handoverError={handover.isError ? handover.error : null}
      history={history.data}
      historyPending={history.isPending}
      historyError={history.isError ? history.error : null}
      className={detailsMode === 'inline' ? 'h-full overflow-y-auto' : 'rounded-xl'}
    />
  );

  return (
    <div className="flex h-full min-h-0">
      <div className="flex min-w-0 flex-1 flex-col">
        <ConversationHeader conversation={data} title={title} backLink={backLink} detailsOpen={detailsOpen} onDetailsOpenChange={onDetailsOpenChange} />

        <div className="min-h-0 flex-1">
          <ConversationThread
            items={items}
            messages={live.messages}
            history={history.data}
            assistantName={agentName ?? 'Assistant'}
            conversationId={data.id}
            onAddToDataset={canAddToDataset ? setAdding : null}
          />
        </div>

        {fromWidget ? (
          <div className="shrink-0">
            {status === 'closed' && !followUp ? (
              <p className="border-t border-border-subtle px-4 py-3 text-small text-fg-muted">This conversation is closed. The visitor can start a new one.</p>
            ) : (
              <ReplyBox
                conversationId={data.id}
                onSend={(text) => reply.mutateAsync(text)}
                sending={reply.isPending}
                error={reply.error}
                note={
                  status === 'human'
                    ? undefined
                    : followUp
                      ? 'Sending a reply opens the conversation again. A visitor who left gets it by email, if they left an address.'
                      : 'Sending a reply takes the conversation over from the assistant.'
                }
              />
            )}
          </div>
        ) : null}
      </div>

      {detailsMode === 'inline' && detailsOpen ? (
        <aside aria-label="Conversation details" className="w-80 shrink-0 border-l border-border-subtle">
          {details}
        </aside>
      ) : null}
      <SideSheet open={detailsMode === 'sheet' && detailsOpen} onClose={() => onDetailsOpenChange(false)} title="Details" description={title}>
        {details}
      </SideSheet>

      {adding ? <AddToDatasetDialog source={adding} onClose={() => setAdding(null)} /> : null}
    </div>
  );
}
