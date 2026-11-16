import { ArrowLeft } from 'lucide-react';
import { plainText } from './citations';
import { useChatState, useWidget } from './context';
import type { Texts } from './texts';
import type { ConversationSummary } from './types';

function formatDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return new Intl.DateTimeFormat(document.documentElement.lang || undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date);
}

function preview(conversation: ConversationSummary, t: Texts): string {
  const last = conversation.lastMessage;
  if (!last) {
    return '';
  }
  const words = plainText(last.text);
  return last.author === 'visitor' ? `${t('you')}: ${words}` : words;
}

function focusOnMount(element: HTMLElement | null): void {
  element?.focus();
}

export interface ConversationListProps {
  onOpen: (conversationId: string) => void;
  onBack: () => void;
}

export function ConversationList({ onOpen, onBack }: ConversationListProps) {
  const { t } = useWidget();
  const conversations = useChatState((state) => state.conversations);
  const current = useChatState((state) => state.conversationId);

  return (
    <section aria-labelledby="ka-conversations" className="flex min-h-0 flex-1 flex-col">
      <div className="flex items-center gap-2 px-4 pt-3">
        <button
          type="button"
          onClick={onBack}
          className="inline-flex items-center gap-1.5 rounded-full px-2 py-1 text-[13px] font-medium text-accent-text hover:bg-raised"
        >
          <ArrowLeft className="size-3.5" aria-hidden="true" />
          {t('backToChat')}
        </button>
      </div>
      <h2 id="ka-conversations" ref={focusOnMount} tabIndex={-1} className="px-4 pt-2 pb-1 text-sm font-semibold focus-visible:outline-offset-2">
        {t('conversations')}
      </h2>
      {conversations === null ? (
        <p role="status" className="px-4 py-2 text-sm text-muted">
          {t('loading')}
        </p>
      ) : (
        <ul className="min-h-0 flex-1 space-y-1 overflow-y-auto overscroll-contain px-2 py-2">
          {conversations.map((conversation) => (
            <li key={conversation.id}>
              <button
                type="button"
                aria-current={conversation.id === current ? 'true' : undefined}
                onClick={() => onOpen(conversation.id)}
                className="block w-full rounded-xl px-2.5 py-2 text-left hover:bg-raised focus-visible:outline-offset-[-2px] aria-[current=true]:bg-accent-soft"
              >
                <span className="flex items-center justify-between gap-2 text-xs text-muted">
                  <time dateTime={conversation.updatedAt}>{formatDate(conversation.updatedAt)}</time>
                  {conversation.status === 'closed' ? <span>{t('conversationEnded')}</span> : null}
                </span>
                <span className="mt-0.5 line-clamp-2 text-sm text-ink">{preview(conversation, t)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
