import { clsx } from 'clsx';
import { Headset } from 'lucide-react';
import { useCallback, useEffect, useEffectEvent, useRef, useState } from 'react';
import { AccentButton } from './AccentButton';
import { ActivityAnnouncer, useHostPanel } from './ChatActivity';
import { Composer } from './Composer';
import { ContactForm } from './ContactForm';
import { useWidget } from './context';
import type { ChatSession, FieldErrors } from './controller';
import { ConversationRating } from './Feedback';
import { Header, type MenuItem } from './Header';
import { PRESET_EFFECTS } from '../shared/theme';
import { postToHost, type HostIdentity } from './host';
import { MessageList } from './MessageList';
import { StatusBars } from './StatusBars';
import { useChatController, useChatState } from './useChatController';
import { useStickToBottom } from './useStickToBottom';
import { useWindowEvent } from './useWindowEvent';

type ContactPrompt = { reason: 'chat' | 'handover'; errors?: FieldErrors };

function prefersFinePointer(): boolean {
  return typeof window.matchMedia !== 'function' || window.matchMedia('(pointer: fine)').matches;
}

function closeFrame(): void {
  postToHost({ type: 'close' });
}

function tellBusy(busy: boolean): void {
  postToHost({ type: 'busy', busy });
}

export function LiveChat({ widgetKey, retryDelaysMs, identity }: { widgetKey: string; retryDelaysMs?: readonly number[]; identity?: HostIdentity }) {
  const controller = useChatController(widgetKey, retryDelaysMs, identity);
  return controller ? <ChatPanel session={controller} onClose={closeFrame} onBusy={tellBusy} /> : null;
}

export interface ChatPanelProps {
  session: ChatSession;
  onClose: () => void;
  // The glow around the panel is drawn outside the chat.
  onBusy?: (busy: boolean) => void;
}

export function ChatPanel({ session, onClose, onBusy }: ChatPanelProps) {
  const { config, t, mark } = useWidget();
  const state = useChatState(session);
  const [draft, setDraft] = useState('');
  const [contactPrompt, setContactPrompt] = useState<ContactPrompt | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const { scrollRef, contentRef, onScroll, stick } = useStickToBottom([
    state.messages,
    state.pending,
    state.followed,
    state.status,
    state.notice,
    contactPrompt,
  ]);

  const focusComposer = () => {
    if (prefersFinePointer()) {
      textareaRef.current?.focus({ preventScroll: true });
    }
  };

  useHostPanel(session, focusComposer);
  useWindowEvent('keydown', (event) => {
    if (event.key === 'Escape' && !event.defaultPrevented) {
      onClose();
    }
  });
  useWindowEvent('online', () => session.reconnect());

  const { pending, messages, status } = state;
  const answering = session.isAnswering;
  const busy = answering || state.followed !== null;

  const reportBusy = useEffectEvent((value: boolean) => {
    onBusy?.(value);
  });
  useEffect(() => {
    reportBusy(busy);
  }, [busy]);

  const needsContact = config.contactCapture === 'before_chat' && !state.visitor.email && !state.conversationId && state.phase === 'ready';
  const chatContact = contactPrompt?.reason === 'chat' || needsContact;
  const showSuggestions = config.suggestedQuestions.length > 0 && messages.length === 0 && !pending && !state.followed && !needsContact;
  const canHandOver =
    config.handoverEnabled && state.conversationId !== null && status === 'ai' && state.phase === 'ready' && !answering && !contactPrompt;
  // The offer bar has the same button.
  const showHandoverButton = canHandOver && !state.handoverOffered;

  // Stable, so typing doesn't re-render the message list.
  const send = useCallback(
    async (text: string) => {
      stick();
      setDraft('');
      const result = await session.send(text);
      if (result.status === 'needs-contact') {
        setContactPrompt({ reason: 'chat', errors: { email: result.error } });
      }
      if (result.status !== 'sent') {
        setDraft((current) => current || text);
      }
    },
    [session, stick],
  );

  const startNewChat = () => {
    setContactPrompt(null);
    stick();
    void session.newChat().then(focusComposer);
  };

  const askForPerson = async () => {
    if (config.contactCapture === 'before_handover' && !state.visitor.email) {
      setContactPrompt({ reason: 'handover' });
      return;
    }
    const result = await session.requestHandover();
    if (!result.ok && (result.errors.email || result.errors.name)) {
      setContactPrompt({ reason: 'handover', errors: result.errors });
    }
  };

  const menuItems: MenuItem[] = [];
  if (state.conversationId || messages.length > 0 || pending) {
    menuItems.push({ key: 'new', label: t('newChat'), onSelect: startNewChat });
  }
  if (state.conversationId && status !== 'closed') {
    menuItems.push({ key: 'end', label: t('endChat'), onSelect: () => void session.endChat() });
  }

  let bottom;
  if (status === 'closed') {
    bottom = (
      <div {...mark('status.ended')} className="space-y-3 px-4 pt-3 pb-1 text-center">
        <p className="text-sm font-medium text-ink">{t('ended')}</p>
        {config.feedbackEnabled ? (
          <ConversationRating rating={state.conversationRating} onRate={(rating) => session.rateConversation(rating)} />
        ) : null}
        <AccentButton onClick={startNewChat}>{t('newChat')}</AccentButton>
      </div>
    );
  } else if (contactPrompt?.reason === 'handover') {
    bottom = (
      <ContactForm
        autoFocus
        initialName={state.visitor.name}
        initialEmail={state.visitor.email}
        {...(contactPrompt.errors ? { initialErrors: contactPrompt.errors } : {})}
        onCancel={() => setContactPrompt(null)}
        onSubmit={async (contact) => {
          const result = await session.requestHandover(contact);
          if (result.ok) {
            setContactPrompt(null);
          }
          return result;
        }}
      />
    );
  } else if (chatContact) {
    bottom = (
      <ContactForm
        autoFocus={contactPrompt !== null}
        initialName={state.visitor.name}
        initialEmail={state.visitor.email}
        {...(contactPrompt?.errors ? { initialErrors: contactPrompt.errors } : {})}
        onSubmit={async (contact) => {
          const result = await session.saveContact(contact);
          if (result.ok) {
            setContactPrompt(null);
          }
          return result;
        }}
      />
    );
  } else if (state.phase === 'failed') {
    bottom = (
      <div className="px-4 pt-3 text-center">
        <AccentButton onClick={() => void session.start()}>{t('retry')}</AccentButton>
      </div>
    );
  } else {
    bottom = (
      <Composer
        value={draft}
        onChange={setDraft}
        onSubmit={(text) => void send(text)}
        sendBlocked={answering || state.phase !== 'ready'}
        glowing={busy && PRESET_EFFECTS[config.preset].glow}
        textareaRef={textareaRef}
      />
    );
  }

  // The chat is the frame's whole page, so it is the main landmark, named apart from the host page's own.
  return (
    <main aria-label={config.title} className="flex h-full min-h-0 flex-col text-ink">
      <Header menuItems={menuItems} onClose={onClose} />
      <div
        ref={scrollRef}
        {...mark('behavior.messages')}
        role="region"
        aria-label={t('messages')}
        tabIndex={0}
        onScroll={onScroll}
        className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 py-3 focus-visible:outline-offset-[-2px]"
      >
        <div ref={contentRef}>
          <MessageList state={state} session={session} showSuggestions={showSuggestions} onAsk={send} />
        </div>
      </div>
      <div className={clsx('shrink-0', !config.showFooter && 'pb-3')}>
        <StatusBars
          state={state}
          session={session}
          answering={answering}
          onLeftQueue={focusComposer}
          {...(canHandOver ? { onAskForPerson: () => void askForPerson() } : {})}
        />
        {showHandoverButton ? (
          <div className="px-4 pt-2">
            <button
              type="button"
              {...mark('behavior.handover')}
              onClick={() => void askForPerson()}
              className="inline-flex items-center gap-1.5 rounded-full border border-line bg-raised px-3.5 py-1.5 text-[13px] font-medium text-ink transition-colors hover:bg-accent-soft"
            >
              <Headset className="size-3.5" aria-hidden="true" />
              {t('talkToPerson')}
            </button>
          </div>
        ) : null}
        {bottom}
        {config.showFooter ? (
          <p {...mark('texts.poweredBy')} className="px-4 pt-2 pb-2.5 text-center text-[11px] tracking-wide text-muted">
            {t('poweredBy')}
          </p>
        ) : null}
      </div>
      <ActivityAnnouncer session={session} />
    </main>
  );
}
