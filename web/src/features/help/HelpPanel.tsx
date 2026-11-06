import { clsx } from 'clsx';
import { ArrowUp, Check, Cpu, RotateCcw, Square } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent, type RefObject } from 'react';
import { Link } from 'react-router';
import { Alert, EmptyState } from '../../components/ui/feedback';
import { Announcer } from '../../components/ui/Announcer';
import { Button, buttonClass } from '../../components/ui/Button';
import { ComposerBox } from '../../components/ui/ComposerBox';
import { UserMessage } from '../chat/AssistantMessage';
import { HelpAnswer } from './HelpAnswer';
import { helpSuggestions } from './suggestions';
import type { HelpSetupDto, HelpStatusDto, HelpTurn } from './types';
import { useHelpChat } from './useHelpChat';
import { useReturnState } from '../../lib/returnTo';
import { useOnChange } from '../../lib/hooks/useOnChange';

interface SetupItem {
  title: string;
  done: boolean;
  to: string;
}

function setupItems(setup: HelpSetupDto): SetupItem[] {
  return [
    { title: 'Connect a model', done: setup.chatModel && setup.embeddingModel, to: '/models#providers' },
    { title: 'Add knowledge', done: setup.sources, to: '/sources' },
    { title: 'Create an agent', done: setup.agent, to: '/agents/new' },
    { title: 'Put a widget on your site', done: setup.widget, to: '/widgets/new' },
  ];
}

function SetupList({ setup, onNavigate }: { setup: HelpSetupDto; onNavigate: () => void }) {
  const returnState = useReturnState();
  const headingId = useId();
  const items = setupItems(setup);
  return (
    <section aria-labelledby={headingId} className="space-y-1.5">
      <h3 id={headingId} className="text-xs font-medium text-fg-muted">
        Setup, {items.filter((item) => item.done).length} of {items.length} done
      </h3>
      <ol className="space-y-1">
        {items.map((item) => (
          <li key={item.title} className="flex items-center gap-2 text-sm">
            <span
              aria-hidden
              className={clsx(
                'flex size-4.5 shrink-0 items-center justify-center rounded-full',
                item.done ? 'bg-success-subtle text-success-text' : 'border-2 border-border-strong',
              )}
            >
              {item.done ? <Check className="size-3" strokeWidth={3} /> : null}
            </span>
            {item.done ? (
              <span className="text-fg-secondary">{item.title}</span>
            ) : (
              <Link to={item.to} state={returnState} onClick={onNavigate} className="font-medium text-accent-text hover:underline">
                {item.title}
              </Link>
            )}
            <span className="sr-only">{item.done ? ', done' : ', to do'}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}

function NoModelState({ admin, onNavigate }: { admin: boolean; onNavigate: () => void }) {
  const returnState = useReturnState();
  return (
    <EmptyState
      icon={Cpu}
      title="No AI model connected"
      description={admin ? 'The help answers once a chat model is connected.' : 'The help answers once an admin connects a chat model.'}
      action={
        admin ? (
          <Link to="/models" state={returnState} onClick={onNavigate} className={buttonClass({ variant: 'primary', size: 'sm' })}>
            Connect a model
          </Link>
        ) : undefined
      }
    />
  );
}

function Suggestions({ questions, onPick, disabled }: { questions: readonly string[]; onPick: (question: string) => void; disabled: boolean }) {
  const headingId = useId();
  return (
    <section aria-labelledby={headingId} className="space-y-1.5">
      <h3 id={headingId} className="text-xs font-medium text-fg-muted">
        Suggested for this page
      </h3>
      <ul className="flex flex-wrap gap-1.5">
        {questions.map((question) => (
          <li key={question}>
            <button
              type="button"
              disabled={disabled}
              onClick={() => onPick(question)}
              className="rounded-full border border-border bg-surface px-3 py-1 text-left text-small text-fg-secondary hover:border-border-hover hover:bg-fill-hover hover:text-fg disabled:opacity-60"
            >
              {question}
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}

function TurnView({ turn, onNavigate }: { turn: HelpTurn; onNavigate: () => void }) {
  return (
    <li className="space-y-2">
      <UserMessage text={turn.question} />
      {turn.text ? <HelpAnswer text={turn.text} citations={turn.citations} onNavigate={onNavigate} /> : null}
      {turn.error ? (
        <Alert tone="danger">
          <p className="text-fg">{turn.error}</p>
        </Alert>
      ) : null}
    </li>
  );
}

const KEYS_HINT = 'Enter to send, Shift+Enter for a new line';

export interface HelpPanelProps {
  status: HelpStatusDto;
  page: string;
  admin: boolean;
  onNavigate: () => void;
  inputRef: RefObject<HTMLTextAreaElement | null>;
}

export function HelpPanel({ status, page, admin, onNavigate, inputRef }: HelpPanelProps) {
  const chat = useHelpChat(page);
  const disabled = !status.modelReady;
  const [draft, setDraft] = useState('');
  const inputId = useId();
  const hintId = useId();
  const endRef = useRef<HTMLDivElement>(null);
  const suggestions = helpSuggestions(page, status.setup, admin);
  const setupOpen = admin && status.setup !== null && setupItems(status.setup).some((item) => !item.done);
  const empty = chat.turns.length === 0 && chat.live === null;
  const intro = empty && !disabled;
  const [openedWith] = useState(() => chat.turns.at(-1)?.id);

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: 'end' });
  }, [chat.turns.length, chat.live?.text]);

  // Refocus the box once an answer is done.
  useOnChange(chat.busy, (busy) => {
    if (!busy) {
      inputRef.current?.focus({ preventScroll: true });
    }
  });

  const send = (question: string) => {
    const text = question.trim();
    if (text && !chat.busy && !disabled) {
      chat.ask(text);
      setDraft('');
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.altKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      send(draft);
    }
  };

  const searching = chat.live?.tools.some((tool) => tool.status === 'running') ?? false;
  // Answers already there when the panel opened are not announced; while one is written the region empties.
  const answered = chat.turns.length > 0 && chat.turns.at(-1)?.id !== openedWith;

  return (
    <div className="flex min-h-full flex-col gap-4">
      {disabled ? <NoModelState admin={admin} onNavigate={onNavigate} /> : null}
      {intro ? (
        <>
          <p className="text-sm text-fg-secondary">
            Ask how to set something up or what a setting does. Answers come from the product help and link to the pages.
          </p>
          <Suggestions questions={suggestions} onPick={send} disabled={chat.busy} />
          {setupOpen && status.setup ? <SetupList setup={status.setup} onNavigate={onNavigate} /> : null}
        </>
      ) : empty || disabled ? null : (
        <>
          <div className="flex justify-end">
            <Button variant="ghost" size="sm" icon={RotateCcw} onClick={chat.reset}>
              New chat
            </Button>
          </div>
          <ol aria-label="Help conversation" className="space-y-5">
            {chat.turns.map((turn) => (
              <TurnView key={turn.id} turn={turn} onNavigate={onNavigate} />
            ))}
            {chat.live ? (
              <li className="space-y-2">
                <UserMessage text={chat.live.userMessage} />
                <HelpAnswer
                  text={chat.live.text}
                  citations={chat.live.citations}
                  streaming
                  activity={searching ? 'Reading the help…' : undefined}
                  onNavigate={onNavigate}
                />
              </li>
            ) : chat.busy ? (
              <li role="status" className="text-xs text-fg-muted">
                Looking in the help…
              </li>
            ) : null}
          </ol>
        </>
      )}
      <Announcer message={answered && !chat.busy ? 'The answer is ready.' : ''} />
      <div ref={endRef} />
      <form
        className="sticky bottom-0 mt-auto bg-raised pt-2"
        onSubmit={(event) => {
          event.preventDefault();
          send(draft);
        }}
      >
        <ComposerBox>
          <label htmlFor={inputId} className="sr-only">
            Ask the help
          </label>
          <span id={hintId} className="sr-only">
            {KEYS_HINT}
          </span>
          <textarea
            id={inputId}
            ref={inputRef}
            rows={2}
            value={draft}
            disabled={disabled}
            maxLength={2000}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={onKeyDown}
            placeholder={disabled ? 'Connect a model to ask the help' : 'Ask about this portal'}
            title={KEYS_HINT}
            aria-describedby={hintId}
            className="block max-h-40 w-full resize-none bg-transparent px-3.5 pt-3 pb-1 text-sm leading-6 text-fg placeholder:text-fg-muted focus:outline-none disabled:cursor-not-allowed"
          />
          <div className="flex justify-end px-2 pb-2">
            {chat.busy ? (
              <Button variant="secondary" size="sm" icon={Square} onClick={chat.stop} aria-label="Stop the answer">
                Stop
              </Button>
            ) : (
              <Button type="submit" variant="primary" size="sm" icon={ArrowUp} disabled={disabled || draft.trim() === ''}>
                Ask
              </Button>
            )}
          </div>
        </ComposerBox>
      </form>
    </div>
  );
}
