import { useQueryClient } from '@tanstack/react-query';
import { Bot, Library, Plus } from 'lucide-react';
import { useCallback, useEffectEvent, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router';
import { Button, buttonClass } from '../../components/ui/Button';
import { Drawer } from '../../components/ui/Drawer';
import { Alert, EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Page } from '../../components/ui/Page';
import { ApiError } from '../../lib/api';
import { useDebouncedSearch } from '../../lib/hooks/useDebouncedSearch';
import { useKeydown } from '../../lib/hooks/useKeydown';
import { useMediaQuery } from '../../lib/hooks/useMediaQuery';
import { useAgents } from '../agents/api';
import { isAdmin, useMe } from '../auth/api';
import { useSourceStats } from '../sources/api';
import { SourceScopePicker } from '../sources/SourceScopePicker';
import { useSourceTitles } from '../sources/useSourceTitles';
import { conversationKeys, findListItem, normalizeSummary, patchListItem, useConversation, useLangfuseUrl, useStopAnswer } from './api';
import { ChatColumn } from './ChatColumn';
import { ChatHeader, ConversationDetailsButton, promptLabel } from './ChatHeader';
import { ChatDisplayContext, type ChatDisplay } from './chatDisplay';
import { pendingConfirmations, turnFromDto, type TurnView } from './chatReducer';
import { SourceTitleContext } from './citationContext';
import { Composer } from './Composer';
import { ConfirmationBar } from './ConfirmationBar';
import { ConversationList } from './ConversationList';
import { EmptyChat } from './EmptyChat';
import { NO_FILTER, type ListFilter } from './LabelFilter';
import { TargetSummary, TestAsPicker, targetRequest, usePlaygroundTarget } from './playground';
import { isNewChatShortcut, isSearchShortcut } from './shortcuts';
import { Thread } from './Thread';
import { buildTimeline } from './timeline';
import type { ConversationDto, ConversationListItem } from './types';
import { useChatStream } from './useChatStream';
import { useConversationActions } from './useConversationActions';
import { useLiveConversation } from './useLiveConversation';

const SCOPE_TOOLTIP_TITLES = 10;
const BUSY_MESSAGE = 'The previous answer is still being written. Try again in a moment.';

/** /chat still works for old links. */
function useBasePath(): string {
  const { pathname } = useLocation();
  return pathname.startsWith('/playground') ? '/playground' : '/chat';
}

function useListItem(id: string | null): ConversationListItem | undefined {
  const queryClient = useQueryClient();
  const subscribe = useCallback((onChange: () => void) => queryClient.getQueryCache().subscribe(onChange), [queryClient]);
  return useSyncExternalStore(subscribe, () => (id ? findListItem(queryClient, id) : undefined));
}

function itemFromDetail(dto: ConversationDto): ConversationListItem {
  return normalizeSummary({
    id: dto.id,
    title: dto.title,
    turnCount: dto.turns.length,
    createdAt: dto.createdAt,
    updatedAt: dto.updatedAt,
    channel: dto.channel,
    status: dto.status,
    agentId: dto.agentId,
  });
}

/** Serialized, so the lookup only changes when a new title appears and memoized messages skip re-rendering on every token. */
function useCitationTitles(turns: readonly TurnView[]): (id: string) => string | undefined {
  const serialized = useMemo(() => {
    const titles = new Map<string, string>();
    for (const turn of turns) {
      for (const citation of turn.citations) {
        if (citation.sourceTitle) {
          titles.set(citation.sourceId.toLowerCase(), citation.sourceTitle);
        }
      }
    }
    return JSON.stringify([...titles]);
  }, [turns]);
  return useMemo(() => {
    const titles = new Map<string, string>(JSON.parse(serialized) as Array<[string, string]>);
    return (id: string) => titles.get(id.toLowerCase());
  }, [serialized]);
}

function scopeText(count: number): string {
  return count === 0 ? 'All sources' : `Scoped to ${count} ${count === 1 ? 'source' : 'sources'}`;
}

function ConversationScope({ scope, knownTitle }: { scope: readonly string[]; knownTitle: (id: string) => string | undefined }) {
  const preview = scope.slice(0, SCOPE_TOOLTIP_TITLES);
  const fetchedTitle = useSourceTitles(preview.filter((id) => knownTitle(id) === undefined));
  const titles = [
    ...preview.map((id) => knownTitle(id) ?? fetchedTitle(id) ?? id),
    ...(scope.length > preview.length ? [`and ${scope.length - preview.length} more`] : []),
  ].join(', ');
  return (
    <span className="inline-flex h-7 items-center gap-1.5 px-2 text-xs text-fg-muted" title={titles || 'All sources'}>
      <Library className="size-3.5" aria-hidden />
      {scopeText(scope.length)}
    </span>
  );
}

export function ChatPage() {
  const { conversationId: routeParam } = useParams();
  const routeId = routeParam ?? null;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const basePath = useBasePath();
  const admin = isAdmin(useMe().data);
  const wide = useMediaQuery('(min-width: 768px)');

  const [draft, setDraft] = useState('');
  const [scope, setScope] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [listOpen, setListOpen] = useState(false);
  const [threadKey, setThreadKey] = useState(0);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const search = useDebouncedSearch();
  const [filter, setFilter] = useState<ListFilter>(NO_FILTER);
  const playground = usePlaygroundTarget(admin);
  const langfuseUrl = useLangfuseUrl(admin);
  const stopAnswer = useStopAnswer();

  // The stream calls the latest of these, so they read the current route.
  const chat = useChatStream({
    onConversation: (event) => {
      if (event.title) {
        patchListItem(queryClient, event.conversationId, { title: event.title });
      }
      if (event.isNew) {
        void queryClient.invalidateQueries({ queryKey: conversationKeys.lists });
      }
    },
    onBusy: (request) => {
      setBusy(true);
      setDraft((current) => (current.trim() ? current : request.message));
    },
    onSettled: ({ conversationId, created }) => {
      void queryClient.invalidateQueries({ queryKey: conversationKeys.lists });
      if (conversationId) {
        void queryClient.invalidateQueries({ queryKey: conversationKeys.detail(conversationId), exact: true });
      }
      // A conversation this stream created gets its own address, unless a new chat or another one was opened meanwhile.
      if (created && conversationId && routeId === null) {
        navigate(`${basePath}/${conversationId}`, { replace: true });
      }
    },
  });
  const { reset } = chat;

  // Switching conversations resets the live stream state, except when navigating to the conversation the stream just
  // created. A layout effect, so no stale frame is painted.
  const openRoute = useEffectEvent((id: string | null) => {
    if (id !== chat.state.conversationId) {
      reset(id);
      setScope([]);
      setBusy(false);
      setThreadKey((key) => key + 1);
    }
  });
  useLayoutEffect(() => openRoute(routeId), [routeId]);

  const conversation = useConversation(routeId);
  const sourceStats = useSourceStats();

  const history = useMemo(
    () => (conversation.data?.turns ?? []).slice().sort((a, b) => a.sequence - b.sequence).map(turnFromDto),
    [conversation.data],
  );
  // While this tab's own stream runs it shows the turn, so the conversation's events are not followed then.
  const live = useLiveConversation({
    conversationId: routeId,
    enabled: conversation.data !== undefined && !chat.state.streaming,
    conversation: conversation.data,
    history,
    ownTurns: chat.state.turns,
  });
  const turns = useMemo(
    () => buildTimeline({ history, messages: live.messages, own: chat.state.turns, followed: live.turns }),
    [history, live.messages, chat.state.turns, live.turns],
  );

  // An answer this tab only follows (asked in another tab, or before a reload) can be stopped on the server too. The
  // followed stream then ends with the stopped answer; a failed call leaves it running, and Stop can be pressed again.
  const followedRunning = !chat.state.streaming && live.turns.some((turn) => turn.status === 'pending' || turn.status === 'streaming');
  const stopFollowed = () => {
    if (routeId && !stopAnswer.isPending) {
      stopAnswer.mutate(routeId);
    }
  };

  const knownSourceTitle = useCitationTitles(turns);

  const activeConversationId = routeId ?? chat.state.conversationId;
  const isNewConversation = activeConversationId === null;
  const title = chat.state.title ?? conversation.data?.title ?? (routeId ? '' : 'New conversation');
  const notFound = conversation.error instanceof ApiError && conversation.error.status === 404;
  // A conversation created by the running stream has the scope that was sent with its first message.
  const conversationScope = conversation.data?.sourceIds ?? (routeId === null ? scope : []);

  const listItem = useListItem(activeConversationId);
  const activeItem = listItem ?? (conversation.data ? itemFromDetail(conversation.data) : undefined);

  const target = playground.target;
  const agents = useAgents();
  const agentList = agents.data ?? [];
  const builtInAgent = agentList.find((agent) => agent.isBuiltIn);
  let agentName: string | null = null;
  if (agents.data && conversation.data) {
    // The API answers with the built-in agent when the conversation's agent no longer exists.
    const agentId = conversation.data.agentId?.toLowerCase();
    agentName = (agentId ? agentList.find((agent) => agent.id.toLowerCase() === agentId) : undefined)?.name ?? builtInAgent?.name ?? null;
  } else if (agents.data && routeId === null && !isNewConversation) {
    agentName = (target?.agent ?? builtInAgent)?.name ?? null;
  }

  const display = useMemo<ChatDisplay>(
    () => ({
      developer: admin,
      traceUrl: langfuseUrl ? (traceId) => `${langfuseUrl.replace(/\/+$/, '')}/trace/${encodeURIComponent(traceId)}` : undefined,
      prompt: promptLabel(conversation.data),
    }),
    [admin, langfuseUrl, conversation.data],
  );

  const startNewChat = () => {
    setListOpen(false);
    setDraft('');
    setBusy(false);
    // A new chat starts from the whole list.
    search.clear();
    setFilter(NO_FILTER);
    if (routeId === null) {
      reset(null);
      setScope([]);
      setThreadKey((key) => key + 1);
    } else {
      navigate(basePath);
    }
    window.setTimeout(() => textareaRef.current?.focus(), 0);
  };

  const actions = useConversationActions({
    onRemoved: (id) => {
      const open = id === routeId || id === chat.state.conversationId;
      if (open) {
        startNewChat();
      }
      return open;
    },
    onRestored: (id, wasOpen) => {
      if (wasOpen) {
        navigate(`${basePath}/${id}`);
      }
    },
  });

  useKeydown((event) => {
    if (isSearchShortcut(event)) {
      event.preventDefault();
      if (!wide) {
        setListOpen(true);
      }
      window.setTimeout(() => {
        searchRef.current?.focus();
        searchRef.current?.select();
      }, 0);
    } else if (isNewChatShortcut(event)) {
      event.preventDefault();
      startNewChat();
    }
  });

  // A Confirm or Cancel answers a call the agent asked about; the server runs exactly that call, once.
  const answerConfirmation = (callId: string, approved: boolean) => {
    void chat.send(
      { conversationId: activeConversationId, message: approved ? 'Yes, go ahead.' : 'No, cancel that.', confirmation: { callId, approved } },
      { sentAfterSequence: live.lastSequence },
    );
  };
  const confirmations = activeConversationId ? pendingConfirmations(turns) : [];

  const send = (message: string) => {
    const sourceIds = isNewConversation && target?.kind !== 'widget' ? scope : [];
    setDraft('');
    setBusy(false);
    void chat.send(
      {
        conversationId: activeConversationId,
        message,
        sourceIds: sourceIds.length > 0 ? sourceIds : null,
        // Existing conversations keep the widget or agent they started with.
        ...(isNewConversation ? targetRequest(target) : {}),
      },
      { sentAfterSequence: isNewConversation ? 0 : live.lastSequence },
    );
  };

  const toolbar = isNewConversation ? (
    target?.kind === 'widget' ? null : (
      <SourceScopePicker value={scope} onChange={setScope} disabled={chat.state.streaming} />
    )
  ) : (
    <ConversationScope scope={conversationScope} knownTitle={knownSourceTitle} />
  );

  let body;
  if (routeId && conversation.isPending && turns.length === 0) {
    body = (
      <div className="flex h-full items-center justify-center">
        <Spinner label="Loading conversation" />
      </div>
    );
  } else if (routeId && conversation.isError && turns.length === 0) {
    body = notFound ? (
      <EmptyState
        title="This conversation does not exist"
        description="It may have been deleted."
        action={
          <Button variant="primary" icon={Plus} onClick={startNewChat}>
            New chat
          </Button>
        }
        className="h-full"
      />
    ) : (
      <div className="mx-auto max-w-xl p-6">
        <ErrorMessage error={conversation.error} title="Could not load the conversation" />
      </div>
    );
  } else if (turns.length === 0 && isNewConversation && playground.empty) {
    body = (
      <EmptyState
        icon={Bot}
        title="No agent to test yet"
        description="Create an agent with what it knows and how it answers, then test it here before it goes on your website."
        action={
          admin ? (
            <Link to="/agents/new" className={buttonClass()}>
              <Plus className="size-4" aria-hidden />
              New agent
            </Link>
          ) : null
        }
        className="h-full"
      />
    );
  } else if (turns.length === 0) {
    const widgetQuestions = target?.kind === 'widget' ? target.widget.settings.behavior.suggestedQuestions : undefined;
    body = (
      <EmptyChat
        sourceStats={sourceStats.data}
        title="Test your agent"
        description={
          target?.kind === 'widget'
            ? "Ask what your visitors would ask. The agent answers with its own knowledge, under the chosen widget's rules for reaching a person. Test chats are not counted."
            : 'The agent searches your sources and cites the passages it used. Point at a number to see the source.'
        }
        summary={target ? <TargetSummary target={target} /> : null}
        questions={widgetQuestions}
        greeting={target?.kind === 'widget' ? target.widget.settings.behavior.greeting : null}
        onPick={(question) => {
          setDraft(question);
          textareaRef.current?.focus();
        }}
      />
    );
  } else {
    body = <Thread key={threadKey} turns={turns} messages={live.messages} />;
  }

  const list = (
    <ConversationList
      activeId={activeConversationId}
      basePath={basePath}
      onNewChat={startNewChat}
      onNavigate={wide ? undefined : () => setListOpen(false)}
      search={search}
      filter={filter}
      onFilterChange={setFilter}
      actions={actions}
      searchRef={searchRef}
    />
  );

  return (
    <ChatDisplayContext.Provider value={display}>
      <SourceTitleContext.Provider value={knownSourceTitle}>
        <Page variant="workspace">
          <div className="flex min-h-0 flex-1">
            {/* One h1 per page for screen-reader heading navigation; the visible chat headings are h2. */}
            <h1 className="sr-only">Playground</h1>
            {wide ? (
              <aside className="w-[clamp(20rem,28vw,28rem)] shrink-0 border-r border-border-subtle" aria-label="Conversation history">
                {list}
              </aside>
            ) : (
              <Drawer open={listOpen} onClose={() => setListOpen(false)} label="Conversation history">
                {list}
              </Drawer>
            )}

            <section className="flex min-w-0 flex-1 flex-col" aria-label="Chat">
              <ChatHeader
                // A rename or the label picker belongs to one conversation.
                key={activeConversationId ?? 'new'}
                title={title}
                item={activeItem}
                agentName={agentName}
                actions={actions}
                list={wide ? undefined : { open: listOpen, show: () => setListOpen(true) }}
                onBack={routeParam ? startNewChat : undefined}
                tools={
                  <>
                    {isNewConversation ? <TestAsPicker playground={playground} disabled={chat.state.streaming} /> : null}
                    {admin && conversation.data ? (
                      <ConversationDetailsButton conversation={conversation.data} agentName={agentName} scope={scopeText(conversationScope.length)} />
                    ) : null}
                  </>
                }
              />

              <div className="min-h-0 flex-1">{body}</div>

              <div className="shrink-0">
                {busy ? (
                  <ChatColumn className="pt-2">
                    <Alert tone="warning" live>
                      {BUSY_MESSAGE}
                    </Alert>
                  </ChatColumn>
                ) : null}
                <ConfirmationBar calls={confirmations} onAnswer={answerConfirmation} disabled={chat.state.streaming || followedRunning} />
                <Composer
                  value={draft}
                  onChange={setDraft}
                  onSubmit={send}
                  onStop={followedRunning ? stopFollowed : chat.stop}
                  streaming={chat.state.streaming || followedRunning}
                  stopping={chat.stopping || stopAnswer.isPending}
                  disabled={notFound || (isNewConversation && playground.empty)}
                  toolbar={toolbar}
                  textareaRef={textareaRef}
                  placeholder={isNewConversation && target?.kind === 'widget' ? `Ask as a visitor of ${target.widget.name}` : undefined}
                />
              </div>
            </section>
          </div>
        </Page>
      </SourceTitleContext.Provider>
    </ChatDisplayContext.Provider>
  );
}
