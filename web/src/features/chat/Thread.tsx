import { useLayoutEffect, useMemo, useRef } from 'react';
import { AssistantMessage, SystemNotice, UserMessage } from './AssistantMessage';
import { citationIndex, type TurnView } from './chatReducer';
import { ChatColumn } from './ChatColumn';
import { withSystemNotices } from './timeline';
import type { ConversationMessageDto } from './types';

const NO_MESSAGES: readonly ConversationMessageDto[] = [];

export function Thread({ turns, messages = NO_MESSAGES }: { turns: TurnView[]; messages?: readonly ConversationMessageDto[] }) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const stickToBottom = useRef(true);

  // Citations of finished turns: stable while a new answer streams, so older messages do not re-render.
  const finished = turns.filter((turn) => turn.status !== 'streaming');
  const finishedKey = finished.map((turn) => `${turn.key}:${turn.id}:${turn.citations.length}`).join('|');
  const index = useMemo(() => citationIndex(finished), [finishedKey]);
  const items = useMemo(() => withSystemNotices(turns, messages), [turns, messages]);

  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (el && stickToBottom.current) {
      el.scrollTop = el.scrollHeight;
    }
  }, [items]);

  return (
    <div
      ref={scrollRef}
      className="relative h-full overflow-y-auto"
      onScroll={(event) => {
        const el = event.currentTarget;
        stickToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
      }}
    >
      <ChatColumn>
        <ol className="space-y-8 pt-6 pb-4" aria-label="Conversation">
          {items.map((item) =>
            item.kind === 'turn' ? (
              <li key={item.turn.key} className="space-y-3">
                <UserMessage text={item.turn.userMessage} />
                {item.turn.status === 'pending' ? null : <AssistantMessage turn={item.turn} conversationCitations={index} />}
              </li>
            ) : (
              <li key={`system-${item.message.sequence}`}>
                <SystemNotice text={item.message.text} />
              </li>
            ),
          )}
        </ol>
      </ChatColumn>
    </div>
  );
}
