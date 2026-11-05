import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { plainText } from './citations';
import { useWidget } from './context';
import type { ChatActivity, ChatSession } from './controller';
import { postToHost, readHostCommand } from './host';
import { useWindowEvent } from './useWindowEvent';

// Its own component, so an announcement re-renders only this live region, not the chat.
export function ActivityAnnouncer({ session }: { session: ChatSession }) {
  const { config, t } = useWidget();
  const [announcement, setAnnouncement] = useState('');

  const announce = useEffectEvent((activity: ChatActivity) => {
    let text: string;
    if (activity.kind === 'error') {
      text = activity.text;
    } else {
      const { message } = activity;
      const author = message.author === 'assistant' ? config.title : message.author === 'staff' ? (message.authorName ?? t('team')) : null;
      const words = plainText(message.text).slice(0, 400);
      text = author ? `${author}: ${words}` : words;
    }
    // A trailing space toggles, so the same words are announced again.
    setAnnouncement((previous) => (previous === text ? `${text} ` : text));
  });

  useEffect(() => session.onActivity((activity) => announce(activity)), [session]);

  return (
    <div className="sr-only" role="status" aria-live="polite" aria-atomic="true">
      {announcement}
    </div>
  );
}

// Counts answers that arrive while the panel is closed, for the launcher's badge.
export function useHostPanel(session: ChatSession, onOpen: () => void): void {
  // A ref, not state: it is only posted to the host, and state would re-render the chat for nothing.
  const panelOpen = useRef(true);
  const unread = useRef(0);

  useWindowEvent('message', (event) => {
    const command = readHostCommand(event);
    if (command === 'open') {
      panelOpen.current = true;
      if (unread.current > 0) {
        unread.current = 0;
        postToHost({ type: 'unread', count: 0 });
      }
      onOpen();
    } else if (command === 'close') {
      panelOpen.current = false;
    }
  });

  useEffect(
    () =>
      session.onActivity((activity) => {
        if (activity.kind === 'message' && (activity.message.author === 'assistant' || activity.message.author === 'staff') && !panelOpen.current) {
          unread.current += 1;
          postToHost({ type: 'unread', count: unread.current });
        }
      }),
    [session],
  );
}
