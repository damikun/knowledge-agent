import { keepPreviousData, useInfiniteQuery, useMutation } from '@tanstack/react-query';
import { ArrowLeft, MessageSquare, Search, ThumbsDown, UserRound } from 'lucide-react';
import { useDeferredValue, useState } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { FilterSelect } from '../../components/ui/FilterSelect';
import { SideSheet } from '../../components/ui/SideSheet';
import { formatRelativeTime, pluralize } from '../../lib/format';
import { inboxApi } from '../inbox/api';
import { DEFAULT_STATE } from '../inbox/inboxState';
import type { InboxState, PeriodFilter } from '../inbox/types';
import { labelsOrEmpty, useLabels } from '../labels/api';
import { evalKeys, evalsApi } from './api';
import type { ImportEvalItemsResultDto, SaveEvalItemRequest } from './types';
import { PickThread, type ConversationPick } from './PickThread';

type Show = 'all' | 'poor' | 'thumbs' | 'handover';

const SHOW_CHOICES = [
  ['all', 'All conversations'],
  ['poor', 'Rated poorly'],
  ['thumbs', 'Thumbs down'],
  ['handover', 'Handed to a person'],
] as const;

const PERIOD_CHOICES = [
  ['', 'Any time'],
  ['7', 'Last 7 days'],
  ['30', 'Last 30 days'],
  ['90', 'Last 90 days'],
] as const;

function pickToRequest(pick: ConversationPick): SaveEvalItemRequest {
  return {
    question: pick.question.trim(),
    referenceAnswer: pick.answer.trim() || null,
    conversationId: pick.conversationId,
    messageId: pick.answerMessageId,
    source: 'conversation',
    history: pick.withContext ? pick.context : [],
  };
}

function searchState(show: Show, labelId: string, period: PeriodFilter, q: string): InboxState {
  return {
    ...DEFAULT_STATE,
    q,
    labelId,
    period,
    rating: show === 'poor' ? 'poor' : '',
    thumbsDown: show === 'thumbs',
    handover: show === 'handover' ? 'yes' : '',
  };
}

/** A person's reply wins over the agent's, since it is what the visitor was told last. */
export interface ConversationQuestionsSheetProps {
  datasetId: string;
  /** duplicateKey values of the dataset's questions. */
  existing: ReadonlySet<string>;
  onClose: () => void;
  onAdded: (result: ImportEvalItemsResultDto) => void;
}

export function ConversationQuestionsSheet({ datasetId, existing, onClose, onAdded }: ConversationQuestionsSheetProps) {
  const [show, setShow] = useState<Show>('poor');
  const [labelId, setLabelId] = useState('');
  const [period, setPeriod] = useState<PeriodFilter>('');
  const [q, setQ] = useState('');
  const words = useDeferredValue(q.trim());
  const [open, setOpen] = useState<string | null>(null);
  const [picks, setPicks] = useState<ReadonlyMap<string, ConversationPick>>(new Map());
  const labels = labelsOrEmpty(useLabels().data);
  const state = searchState(show, labelId, period, words);

  const search = useInfiniteQuery({
    queryKey: evalKeys.conversationPicker({ show, labelId, period, words }),
    queryFn: ({ signal, pageParam }) => inboxApi.search(state, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
    placeholderData: keepPreviousData,
  });
  const conversations = search.data?.pages.flatMap((page) => page.items) ?? [];
  const pickedPerConversation = new Map<string, number>();
  for (const pick of picks.values()) {
    pickedPerConversation.set(pick.conversationId, (pickedPerConversation.get(pick.conversationId) ?? 0) + 1);
  }

  const update = (pick: ConversationPick) => setPicks((current) => new Map(current).set(pick.key, pick));
  const remove = (key: string) =>
    setPicks((current) => {
      const next = new Map(current);
      next.delete(key);
      return next;
    });

  const add = useMutation({
    mutationFn: () => evalsApi.importItems(datasetId, [...picks.values()].map(pickToRequest)),
    onSuccess: (result) => {
      onAdded(result);
      onClose();
    },
  });
  const empty = [...picks.values()].some((pick) => !pick.question.trim());
  const openTitle = conversations.find((conversation) => conversation.id === open)?.title;

  return (
    <SideSheet
      open
      onClose={onClose}
      size="lg"
      title="Add questions from conversations"
      description="Real questions make the best tests. Pick them from conversations that went wrong, and correct the answer where needed."
      footer={
        <div className="flex w-full flex-wrap items-center justify-between gap-2">
          <span className="text-small text-fg-secondary" aria-live="polite">
            {picks.size === 0 ? 'No questions picked yet' : `${pluralize(picks.size, 'question')} picked`}
          </span>
          <div className="flex gap-2">
            <Button onClick={onClose} disabled={add.isPending}>
              Cancel
            </Button>
            <Button variant="primary" loading={add.isPending} disabled={picks.size === 0 || empty} onClick={() => add.mutate()}>
              {picks.size > 1 ? `Add ${picks.size} questions` : 'Add question'}
            </Button>
          </div>
        </div>
      }
    >
      <div className="space-y-4">
        {add.isError ? <ErrorMessage error={add.error} title="The questions were not added" /> : null}
        {open ? (
          <div className="space-y-3">
            <div className="flex min-w-0 items-center gap-2">
              <Button size="sm" variant="ghost" icon={ArrowLeft} onClick={() => setOpen(null)}>
                All conversations
              </Button>
              {openTitle ? <span className="truncate text-small font-medium text-fg">{openTitle}</span> : null}
            </div>
            <PickThread conversationId={open} picks={picks} existing={existing} onPick={update} onUnpick={remove} onChange={update} />
          </div>
        ) : (
          <>
            <div className="space-y-2">
              <div className="relative">
                <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
                <input
                  type="search"
                  aria-label="Search conversations"
                  placeholder="Search conversations"
                  value={q}
                  onChange={(event) => setQ(event.target.value)}
                  className="field pl-8"
                />
              </div>
              <div className="flex flex-wrap gap-2">
                <FilterSelect label="Show" value={show} choices={SHOW_CHOICES} onChange={setShow} wide />
                {labels.length > 0 ? (
                  <FilterSelect
                    label="Label"
                    value={labelId}
                    choices={[['', 'Any label'], ...labels.map((label) => [label.id, label.name] as const)]}
                    onChange={setLabelId}
                  />
                ) : null}
                <FilterSelect label="When" value={period} choices={PERIOD_CHOICES} onChange={setPeriod} />
              </div>
            </div>
            {search.isPending ? (
              <Spinner label="Searching conversations" />
            ) : search.isError ? (
              <ErrorMessage error={search.error} title="Could not search the conversations" />
            ) : conversations.length === 0 ? (
              <EmptyState icon={MessageSquare} title="No conversations match" description="Try another filter, such as All conversations or Any time." />
            ) : (
              <ul className="divide-y divide-border-subtle rounded-xl border border-border" aria-label="Conversations">
                {conversations.map((conversation) => {
                  const picked = pickedPerConversation.get(conversation.id) ?? 0;
                  return (
                    <li key={conversation.id}>
                      <button
                        type="button"
                        onClick={() => setOpen(conversation.id)}
                        className="flex w-full flex-col gap-1 px-3 py-2.5 text-left hover:bg-fill-hover"
                      >
                        <span className="flex w-full items-baseline justify-between gap-2">
                          <span className="truncate text-sm font-medium text-fg">{conversation.title || 'Untitled conversation'}</span>
                          <span className="shrink-0 text-xs text-fg-muted">{formatRelativeTime(conversation.updatedAt)}</span>
                        </span>
                        {conversation.match || conversation.preview ? (
                          <span className="line-clamp-2 text-small text-fg-secondary">{conversation.match ?? conversation.preview}</span>
                        ) : null}
                        <span className="flex flex-wrap gap-1.5">
                          {conversation.rating !== null ? (
                            <Badge tone={conversation.rating <= 2 ? 'danger' : 'neutral'}>Rated {conversation.rating} of 5</Badge>
                          ) : null}
                          {conversation.thumbsDown > 0 ? (
                            <Badge tone="danger">
                              <ThumbsDown className="size-3" aria-hidden />
                              {pluralize(conversation.thumbsDown, 'thumbs down', 'thumbs down')}
                            </Badge>
                          ) : null}
                          {conversation.handoverStatus ? (
                            <Badge>
                              <UserRound className="size-3" aria-hidden />
                              Handed to a person
                            </Badge>
                          ) : null}
                          {conversation.labels.map((label) => (
                            <Badge key={label.id}>{label.name}</Badge>
                          ))}
                          {picked > 0 ? <Badge tone="accent">{pluralize(picked, 'question')} picked</Badge> : null}
                        </span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
            {search.hasNextPage ? (
              <Button size="sm" variant="ghost" loading={search.isFetchingNextPage} onClick={() => void search.fetchNextPage()}>
                Show more conversations
              </Button>
            ) : null}
          </>
        )}
      </div>
    </SideSheet>
  );
}
