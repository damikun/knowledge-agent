import { Send } from 'lucide-react';
import { useId, useState, type KeyboardEvent } from 'react';
import { Button } from '../../components/ui/Button';
import { ErrorMessage } from '../../components/ui/feedback';
import { ComposerBox } from '../../components/ui/ComposerBox';
import { Kbd } from '../../components/ui/Field';
import { SuggestReply } from './SuggestReply';

const MAX_REPLY_LENGTH = 8000;

export interface ReplyBoxProps {
  conversationId: string;
  onSend: (text: string) => Promise<unknown>;
  sending: boolean;
  error: unknown;
  note?: string;
}

export function ReplyBox({ conversationId, onSend, sending, error, note }: ReplyBoxProps) {
  const [text, setText] = useState('');
  const inputId = useId();
  const trimmed = text.trim();
  const canSend = trimmed.length > 0 && trimmed.length <= MAX_REPLY_LENGTH && !sending;

  const send = () => {
    if (!canSend) {
      return;
    }
    void onSend(trimmed).then(
      () => setText(''),
      () => undefined,
    );
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.altKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      send();
    }
  };

  return (
    <form
      className="space-y-2 px-4 pt-2 pb-3"
      onSubmit={(event) => {
        event.preventDefault();
        send();
      }}
    >
      {error ? <ErrorMessage error={error} title="The reply was not sent" /> : null}
      {/* Keyed, so a draft never carries over to another conversation. */}
      <SuggestReply key={conversationId} conversationId={conversationId} currentText={text} onDraft={setText} disabled={sending} />
      <ComposerBox>
        <label htmlFor={inputId} className="sr-only">
          Reply
        </label>
        <textarea
          id={inputId}
          rows={3}
          maxLength={MAX_REPLY_LENGTH}
          value={text}
          onChange={(event) => setText(event.target.value)}
          onKeyDown={onKeyDown}
          placeholder="Write a reply to the visitor…"
          className="block w-full resize-y bg-transparent px-3.5 pt-3 pb-1 text-sm leading-6 text-fg placeholder:text-fg-muted focus:outline-none"
        />
        <div className="flex items-center justify-between gap-2 px-2 pb-2">
          <p className="hidden text-xs text-fg-muted sm:block">
            <Kbd>Enter</Kbd> to send · <Kbd>Shift</Kbd> + <Kbd>Enter</Kbd> for a new line
          </p>
          <Button type="submit" variant="primary" size="sm" icon={Send} loading={sending} disabled={!canSend} className="ml-auto">
            Send reply
          </Button>
        </div>
      </ComposerBox>
      {note ? <p className="px-1 text-xs text-fg-muted">{note}</p> : null}
    </form>
  );
}
