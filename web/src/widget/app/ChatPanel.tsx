import { clsx } from 'clsx';
import { Headset } from 'lucide-react';
import { useCallback, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { AccentButton } from './AccentButton';
import { ActivityAnnouncer } from './ChatActivity';
import { Composer } from './Composer';
import { isAnswering, isBusy } from './chatState';
import { ContactForm } from './ContactForm';
import { ConversationList } from './ConversationList';
import { useChat, useChatState, useHost, useHostOpen, useVisitorState, useWidget } from './context';
import type { FieldErrors } from './errors';
import { ConversationRating } from './Feedback';
import { Header, type MenuItem } from './Header';
import { PRESET_EFFECTS } from '../shared/theme';
import { MessageList } from './MessageList';
import { StatusBars } from './StatusBars';
import { useStickToBottom } from './useStickToBottom';
import { useWindowEvent } from './useWindowEvent';

type ContactPrompt = { reason: 'chat' | 'handover'; errors?: FieldErrors };

function prefersFinePointer(): boolean {
  return typeof window.matchMedia !== 'function' || window.matchMedia('(pointer: fine)').matches;
}

export function ChatPanel() {
  const { config, t, mark } = useWidget();
  const session = useChat();
  const host = useHost();
  const state = useChatState();
  const visitor = useVisitorState();
  const [draft, setDraft] = useState('');
  const [contactPrompt, setContactPrompt] = useState<ContactPrompt | null>(null);
  const [listShown, setListShown] = useState(false);
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

  useHostOpen(focusComposer);
  useWindowEvent('keydown', (event) => {
    if (event.key === 'Escape' && !event.defaultPrevented) {
      host.close();
    }
  });
  useWindowEvent('online', () => session.reconnect());

  const { pending, messages, status } = state;
  const answering = isAnswering(state);
  const busy = isBusy(state);

  const needsContact = config.contactCapture === 'before_chat' && !visitor.email && !state.conversationId && state.phase === 'ready';
  const chatContact = contactPrompt?.reason === 'chat' || needsContact;
  const showSuggestions = config.suggestedQuestions.length > 0 && messages.length === 0 && !pending && !state.followed && !needsContact;
  const canHandOver =
    config.handoverEnabled && state.conversationId !== null && status === 'ai' && state.phase === 'ready' && !answering && !contactPrompt;
  const showHandoverButton = canHandOver && !state.handoverOffered;

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
    if (config.contactCapture === 'before_handover' && !visitor.email) {
      setContactPrompt({ reason: 'handover' });
      return;
    }
    const result = await session.requestHandover();
    if (!result.ok && (result.errors.email || result.errors.name)) {
      setContactPrompt({ reason: 'handover', errors: result.errors });
    }
  };

  const showList = () => {
    setContactPrompt(null);
    setListShown(true);
    void session.loadConversations();
  };

  const leaveList = () => {
    flushSync(() => setListShown(false));
    stick();
    const composer = prefersFinePointer() ? textareaRef.current : null;
    (composer ?? scrollRef.current)?.focus({ preventScroll: true });
  };

  const openConversation = (conversationId: string) => {
    leaveList();
    void session.showConversation(conversationId);
  };

  const earlier = state.conversations?.some((conversation) => conversation.id !== state.conversationId) ?? false;
  const menuItems: MenuItem[] = [];
  if (earlier && state.phase === 'ready' && !answering && !listShown) {
    menuItems.push({
      key: 'conversations',
      label: t('conversations'),
      onSelect: showList,
    });
  }
  if (!listShown && (state.conversationId || messages.length > 0 || pending)) {
    menuItems.push({ key: 'new', label: t('newChat'), onSelect: startNewChat });
  }
  if (!listShown && state.conversationId && status !== 'closed') {
    menuItems.push({
      key: 'end',
      label: t('endChat'),
      onSelect: () => void session.endChat(),
    });
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
        initialName={visitor.name}
        initialEmail={visitor.email}
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
        initialName={visitor.name}
        initialEmail={visitor.email}
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
        <AccentButton onClick={() => void session.retry()}>{t('retry')}</AccentButton>
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

  return (
    <main aria-label={config.title} className="flex h-full min-h-0 flex-col text-ink">
      <Header menuItems={menuItems} onClose={() => host.close()} />
      {listShown ? <ConversationList onOpen={openConversation} onBack={leaveList} /> : null}
      <div
        hidden={listShown}
        ref={scrollRef}
        {...mark('behavior.messages')}
        role="region"
        aria-label={t('messages')}
        tabIndex={0}
        onScroll={onScroll}
        className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 py-3 focus-visible:outline-offset-[-2px]"
      >
        <div ref={contentRef}>
          <MessageList showSuggestions={showSuggestions} onAsk={send} />
        </div>
      </div>
      <div hidden={listShown} className={clsx('shrink-0', !config.showFooter && 'pb-3')}>
        <StatusBars answering={answering} onLeftQueue={focusComposer} {...(canHandOver ? { onAskForPerson: () => void askForPerson() } : {})} />
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
      <ActivityAnnouncer />
    </main>
  );
}
