import { Headset, LoaderCircle, X } from 'lucide-react';
import { useState } from 'react';
import { useWidget } from './context';
import type { ChatSession, ChatState, Notice } from './controller';
import type { VisitorMessage } from './types';

function lastStaffName(messages: readonly VisitorMessage[]): string | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const message = messages[i]!;
    if (message.author === 'staff' && message.authorName) {
      return message.authorName;
    }
  }
  return null;
}

function NoticeBar({ notice, onDismiss }: { notice: Notice; onDismiss: () => void }) {
  const { t } = useWidget();
  return (
    <div
      role="alert"
      className="mx-4 mt-2 flex items-start gap-2 rounded-2xl border border-danger/30 bg-panel px-3.5 py-2.5 text-[13px] text-danger shadow-sm motion-safe:animate-rise"
    >
      <p className="flex-1 leading-relaxed">{notice.text ?? t(notice.kind)}</p>
      <button
        type="button"
        onClick={onDismiss}
        aria-label={t('dismiss')}
        className="-m-1 grid size-6 shrink-0 place-items-center rounded-full hover:bg-raised"
      >
        <X className="size-3.5" aria-hidden="true" />
      </button>
    </div>
  );
}

function WaitingForPerson({ state, session, onLeft }: { state: ChatState; session: ChatSession; onLeft: () => void }) {
  const { t, mark } = useWidget();
  const [leaving, setLeaving] = useState(false);

  const keepChatting = async () => {
    setLeaving(true);
    try {
      if (await session.cancelHandover()) {
        onLeft();
      }
    } finally {
      setLeaving(false);
    }
  };

  return (
    <div
      {...mark('status.waiting')}
      className="mx-4 mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 rounded-2xl bg-accent-soft px-3.5 py-2.5 text-[13px] text-ink motion-safe:animate-rise"
    >
      <p role="status" className="flex min-w-0 flex-auto items-center gap-2">
        <LoaderCircle className="size-3.5 shrink-0 text-accent-text motion-safe:animate-spin" aria-hidden="true" />
        {t('waitingForPerson')}
      </p>
      {state.phase === 'ready' ? (
        <button
          type="button"
          disabled={leaving}
          onClick={() => void keepChatting()}
          className="-mx-1 rounded-full px-1 font-medium text-accent-text underline-offset-2 hover:underline disabled:opacity-60"
        >
          {t('keepChatting')}
        </button>
      ) : null}
    </div>
  );
}

function HandoverOffer({ onAsk }: { onAsk: () => void }) {
  const { t } = useWidget();
  return (
    <div
      role="group"
      aria-label={t('handoverOffer')}
      className="mx-4 mt-2 flex flex-wrap items-center gap-x-3 gap-y-2 rounded-2xl bg-accent-soft px-3.5 py-2.5 text-[13px] text-ink motion-safe:animate-rise"
    >
      <p className="flex min-w-0 flex-auto items-center gap-2">
        <Headset className="size-3.5 shrink-0 text-accent-text" aria-hidden="true" />
        {t('handoverOffer')}
      </p>
      <button type="button" onClick={onAsk} className="rounded-full bg-accent px-3 py-1 font-medium text-on-accent hover:bg-accent-hover">
        {t('talkToPerson')}
      </button>
    </div>
  );
}

export interface StatusBarsProps {
  state: ChatState;
  session: ChatSession;
  answering: boolean;
  onLeftQueue: () => void;
  // Shown when the agent offered a person and the widget allows one.
  onAskForPerson?: () => void;
}

export function StatusBars({ state, session, answering, onLeftQueue, onAskForPerson }: StatusBarsProps) {
  const { t, mark } = useWidget();
  const { status } = state;
  return (
    <>
      {state.notice ? <NoticeBar notice={state.notice} onDismiss={() => session.dismissNotice()} /> : null}
      {state.handoverOffered && onAskForPerson && !answering && status === 'ai' ? <HandoverOffer onAsk={onAskForPerson} /> : null}
      {state.confirmations.length > 0 && !answering && status === 'ai'
        ? state.confirmations.map((item) => (
            <div
              key={item.callId}
              role="group"
              aria-label={item.title}
              className="mx-4 mt-2 flex flex-wrap items-center gap-x-3 gap-y-2 rounded-2xl bg-accent-soft px-3.5 py-2.5 text-[13px] text-ink motion-safe:animate-rise"
            >
              <div className="min-w-0 flex-auto">
                <p className="font-medium">{item.title}</p>
                {item.details.length > 0 ? (
                  <dl className="mt-1 space-y-0.5 text-[12px] leading-snug">
                    {item.details.map((detail, index) => (
                      <div key={`${detail.label}-${index}`} className="flex flex-wrap gap-x-1">
                        <dt>{detail.label}:</dt>
                        <dd className="min-w-0 font-medium break-words">{detail.value}</dd>
                      </div>
                    ))}
                  </dl>
                ) : null}
              </div>
              <button
                type="button"
                onClick={() => void session.send(t('cancelAction'), { callId: item.callId, approved: false })}
                className="rounded-full px-3 py-1 font-medium text-accent-text hover:bg-raised"
              >
                {t('cancelAction')}
              </button>
              <button
                type="button"
                onClick={() => void session.send(t('confirmAction'), { callId: item.callId, approved: true })}
                className="rounded-full bg-accent px-3 py-1 font-medium text-on-accent hover:bg-accent-hover"
              >
                {t('confirmAction')}
              </button>
            </div>
          ))
        : null}
      {status === 'handover_pending' ? <WaitingForPerson state={state} session={session} onLeft={onLeftQueue} /> : null}
      {status === 'human' ? (
        <p
          {...mark('status.joined')}
          role="status"
          className="mx-4 mt-2 flex items-center gap-2 rounded-2xl bg-accent-soft px-3.5 py-2.5 text-[13px] text-ink motion-safe:animate-rise"
        >
          <Headset className="size-3.5 shrink-0 text-accent-text" aria-hidden="true" />
          {t('personJoined', { name: lastStaffName(state.messages) ?? t('team') })}
        </p>
      ) : null}
    </>
  );
}
