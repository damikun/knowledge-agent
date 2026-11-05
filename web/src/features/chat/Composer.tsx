import { ArrowUp, Square } from 'lucide-react';
import { useCallback, useEffect, useEffectEvent, useLayoutEffect, useRef, type KeyboardEvent, type ReactNode, type RefObject } from 'react';
import { Button } from '../../components/ui/Button';
import { ComposerBox } from '../../components/ui/ComposerBox';
import { useOnChange } from '../../lib/hooks/useOnChange';
import { ChatColumn } from './ChatColumn';

export interface ComposerProps {
  value: string;
  onChange: (value: string) => void;
  onSubmit: (message: string) => void;
  onStop: () => void;
  streaming: boolean;
  stopping?: boolean;
  disabled?: boolean;
  toolbar?: ReactNode;
  textareaRef?: RefObject<HTMLTextAreaElement | null>;
  placeholder?: string;
}

const MAX_HEIGHT = 200;

const DEFAULT_PLACEHOLDER = 'Ask a question about your sources';
const KEYS_HINT = 'Enter to send, Shift+Enter for a new line';

export function Composer({
  value,
  onChange,
  onSubmit,
  onStop,
  streaming,
  stopping = false,
  disabled = false,
  toolbar,
  textareaRef,
  placeholder = DEFAULT_PLACEHOLDER,
}: ComposerProps) {
  const innerRef = useRef<HTMLTextAreaElement>(null);
  const ref = textareaRef ?? innerRef;
  const canSend = value.trim().length > 0 && !streaming && !disabled;

  const fitHeight = useCallback(() => {
    const el = ref.current;
    if (!el) {
      return;
    }
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, MAX_HEIGHT)}px`;
  }, [ref]);

  useLayoutEffect(fitHeight, [value, fitHeight]);

  useEffect(() => {
    // The wrapped height depends on the width, so re-measure when the viewport changes.
    window.addEventListener('resize', fitHeight);
    return () => window.removeEventListener('resize', fitHeight);
  }, [fitHeight]);

  // Focus the input when an answer finishes, and on first render on devices with a fine pointer: on touch devices that
  // would open the on-screen keyboard unasked.
  const focusOnMount = useEffectEvent(() => {
    const finePointer = typeof window.matchMedia !== 'function' || window.matchMedia('(pointer: fine)').matches;
    if (!streaming && finePointer) {
      ref.current?.focus({ preventScroll: true });
    }
  });
  useEffect(() => focusOnMount(), []);
  useOnChange(streaming, (now) => {
    if (!now) {
      ref.current?.focus({ preventScroll: true });
    }
  });

  const submit = () => {
    if (canSend) {
      onSubmit(value.trim());
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.altKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      submit();
    }
  };

  return (
    <ChatColumn>
      <form
        className="pt-2 pb-3"
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <ComposerBox>
          <label htmlFor="chat-message" className="sr-only">
            Message
          </label>
          <span id="chat-message-keys" className="sr-only">
            {KEYS_HINT}
          </span>
          <textarea
            id="chat-message"
            ref={ref}
            rows={1}
            value={value}
            disabled={disabled}
            onChange={(event) => onChange(event.target.value)}
            onKeyDown={onKeyDown}
            placeholder={placeholder}
            title={KEYS_HINT}
            aria-describedby="chat-message-keys"
            className="block max-h-[200px] w-full resize-none bg-transparent px-3.5 pt-3 pb-1 text-sm leading-6 text-fg placeholder:text-fg-muted focus:outline-none"
          />
          <div className="flex items-center justify-between gap-2 px-2 pb-2">
            <div className="flex min-w-0 items-center gap-1">{toolbar}</div>
            {streaming ? (
              <Button variant="secondary" size="sm" icon={Square} onClick={onStop} loading={stopping} aria-label={stopping ? 'Stopping' : 'Stop generating'}>
                {stopping ? 'Stopping' : 'Stop'}
              </Button>
            ) : (
              <Button type="submit" variant="primary" size="sm" icon={ArrowUp} disabled={!canSend} aria-label="Send message" title="Send (Enter)">
                Send
              </Button>
            )}
          </div>
        </ComposerBox>
      </form>
    </ChatColumn>
  );
}
