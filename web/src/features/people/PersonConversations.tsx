import { MessageSquare } from 'lucide-react';
import { Link } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { Card, EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { formatNumber, formatRelativeTime } from '../../lib/format';
import { useReturnState, type ReturnState } from '../../lib/returnTo';
import { isConversationStatus } from '../inbox/api';
import { STATUS_LABELS, STATUS_TONES } from '../inbox/status';
import { usePersonConversations } from './api';
import { personConversationsPath } from './paths';
import type { Person, PersonConversation } from './types';

function ConversationItem({ personId, conversation, returnState }: { personId: string; conversation: PersonConversation; returnState: ReturnState }) {
  const status = isConversationStatus(conversation.status) ? conversation.status : null;
  return (
    <li>
      <Link to={personConversationsPath(personId, conversation.id)} state={returnState} className="flex items-start gap-3 px-4 py-3 hover:bg-fill-hover">
        <MessageSquare className="mt-0.5 size-4 shrink-0 text-fg-muted" aria-hidden />
        <div className="min-w-0 flex-1">
          <div className="flex min-w-0 items-center gap-2">
            <span className="truncate text-sm font-medium text-fg">{conversation.title}</span>
            {status ? <Badge tone={STATUS_TONES[status]}>{STATUS_LABELS[status]}</Badge> : null}
          </div>
          {conversation.preview ? <p className="line-clamp-1 text-small text-fg-secondary">{conversation.preview}</p> : null}
          <p className="text-xs text-fg-muted">
            {formatNumber(conversation.messageCount)} {conversation.messageCount === 1 ? 'message' : 'messages'}
            {conversation.rating ? `, rated ${conversation.rating} of 5` : ''}
            {', '}
            <time dateTime={conversation.updatedAt}>{formatRelativeTime(conversation.updatedAt)}</time>
          </p>
        </div>
      </Link>
    </li>
  );
}

export function PersonConversations({ person }: { person: Person }) {
  const conversations = usePersonConversations(person.id);
  const items = conversations.data?.pages.flatMap((page) => page.items) ?? [];
  const returnState = useReturnState(person.name);
  return (
    <Card>
      <section aria-labelledby="person-conversations">
        <div className="flex items-center justify-between gap-2 border-b border-border-subtle px-4 py-3">
          <h2 id="person-conversations" className="text-section text-fg">
            Conversations
          </h2>
          <Link to={personConversationsPath(person.id)} state={returnState} className="text-small text-accent-text hover:underline">
            Open in Inbox
          </Link>
        </div>
        {conversations.isPending ? (
          <div className="space-y-2 p-4" aria-busy="true" aria-label="Loading conversations">
            <Skeleton className="h-10" />
            <Skeleton className="h-10" />
          </div>
        ) : conversations.isError ? (
          <div className="p-4">
            <ErrorMessage error={conversations.error} title="The conversations could not be loaded" />
          </div>
        ) : items.length === 0 ? (
          <EmptyState icon={MessageSquare} title="No conversations" description="Retention may have deleted older ones." />
        ) : (
          <ul className="divide-y divide-border-subtle">
            {items.map((conversation) => (
              <ConversationItem key={conversation.id} personId={person.id} conversation={conversation} returnState={returnState} />
            ))}
          </ul>
        )}
        {conversations.hasNextPage ? (
          <div className="flex justify-center border-t border-border-subtle p-3">
            <Button size="sm" onClick={() => void conversations.fetchNextPage()} loading={conversations.isFetchingNextPage}>
              Show more
            </Button>
          </div>
        ) : null}
      </section>
    </Card>
  );
}
