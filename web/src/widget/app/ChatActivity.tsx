import { useEffect, useEffectEvent, useState } from 'react';
import { plainText } from './citations';
import { onActivity, type ChatActivity } from './chatState';
import { useWidget } from './context';

export function ActivityAnnouncer() {
  const { widget, config, t } = useWidget();
  const [announcement, setAnnouncement] = useState('');

  const announce = useEffectEvent((activity: ChatActivity) => {
    let text: string;
    if (activity.kind !== 'message') {
      text = activity.text;
    } else {
      const { message } = activity;
      const author = message.author === 'assistant' ? config.title : message.author === 'staff' ? (message.authorName ?? t('team')) : null;
      const words = plainText(message.text).slice(0, 400);
      text = author ? `${author}: ${words}` : words;
    }
    setAnnouncement((previous) => (previous === text ? `${text} ` : text));
  });

  useEffect(() => onActivity(widget.chat, (activity) => announce(activity)), [widget.chat]);

  return (
    <div className="sr-only" role="status" aria-live="polite" aria-atomic="true">
      {announcement}
    </div>
  );
}
