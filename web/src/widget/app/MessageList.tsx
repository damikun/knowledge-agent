import { LoaderCircle } from 'lucide-react';
import { memo } from 'react';
import { useChat, useChatState, useWidget } from './context';
import type { PendingTurn } from './chatState';
import { AnswerFeedback } from './Feedback';
import { AnswerContent, AssistantRow, StaffRow, StreamingAnswerView, SystemNote, ThinkingDots, VisitorBubble } from './Messages';
import type { VisitorMessage } from './types';
import { useOnline } from './useOnline';

function answerStored(pending: PendingTurn, messages: readonly VisitorMessage[]): boolean {
  const question = pending.questionSequence;
  return question !== null && messages.some((message) => message.author === 'assistant' && message.sequence > question);
}

function StoredMessage({ message }: { message: VisitorMessage }) {
  const { config } = useWidget();
  const session = useChat();
  const conversationId = useChatState((state) => state.conversationId);
  switch (message.author) {
    case 'visitor':
      return <VisitorBubble text={message.text} />;
    case 'assistant':
      return (
        <AssistantRow
          editable
          footer={
            config.feedbackEnabled && conversationId ? (
              <AnswerFeedback rating={message.rating} onRate={(rating, comment) => session.rateAnswer(message.id, rating, comment)} />
            ) : null
          }
        >
          <AnswerContent text={message.text} citations={message.citations} />
        </AssistantRow>
      );
    case 'staff':
      return <StaffRow name={message.authorName} text={message.text} />;
    case 'system':
      return <SystemNote text={message.text} />;
  }
}

export interface MessageListProps {
  showSuggestions: boolean;
  onAsk: (question: string) => Promise<void>;
}

export const MessageList = memo(function MessageList({ showSuggestions, onAsk }: MessageListProps) {
  const { config, t, mark } = useWidget();
  const state = useChatState();
  const online = useOnline();
  const { pending, messages, status } = state;
  return (
    <>
      {state.offline || !online ? (
        <p role="status" className="mb-3 rounded-full bg-raised px-3 py-1.5 text-center text-xs text-muted">
          {t('offline')}
        </p>
      ) : null}
      <ol className="flex flex-col gap-2.5">
        {config.greeting ? (
          <li {...mark('behavior.greeting')} className="motion-safe:animate-rise">
            <AssistantRow>
              <p className="leading-relaxed whitespace-pre-line">{config.greeting}</p>
            </AssistantRow>
          </li>
        ) : null}
        {messages.map((message) => (
          <li key={message.id} className="motion-safe:animate-rise">
            <StoredMessage message={message} />
          </li>
        ))}
        {pending && pending.questionSequence === null ? (
          <li className="motion-safe:animate-rise">
            <VisitorBubble text={pending.question} sending />
          </li>
        ) : null}
        {pending?.answer && !answerStored(pending, messages) ? (
          <li className="motion-safe:animate-rise">
            <StreamingAnswerView answer={pending.answer} />
          </li>
        ) : null}
        {pending && !pending.answer && status === 'ai' ? (
          <li>
            <AssistantRow>
              <ThinkingDots />
            </AssistantRow>
          </li>
        ) : null}
        {state.followed ? (
          <li>
            <StreamingAnswerView answer={state.followed} />
          </li>
        ) : null}
      </ol>
      {showSuggestions ? (
        <ul {...mark('behavior.suggestedQuestions')} className="mt-3 flex flex-wrap gap-2 pl-9 motion-safe:animate-rise">
          {config.suggestedQuestions.map((question) => (
            <li key={question}>
              <button
                type="button"
                disabled={state.phase !== 'ready'}
                onClick={() => void onAsk(question)}
                className="rounded-full border border-line bg-raised px-3.5 py-1.5 text-left text-[13px] font-medium text-accent-text transition-[background-color,border-color,transform] duration-200 ease-(--ease-spring) hover:border-accent-text/40 hover:bg-accent-soft active:scale-95 disabled:opacity-60"
              >
                {question}
              </button>
            </li>
          ))}
        </ul>
      ) : null}
      {state.phase === 'starting' ? (
        <p className="mt-3 flex justify-center text-muted">
          <LoaderCircle className="size-4 motion-safe:animate-spin" aria-hidden="true" />
          <span className="sr-only">{t('loading')}</span>
        </p>
      ) : null}
    </>
  );
});
