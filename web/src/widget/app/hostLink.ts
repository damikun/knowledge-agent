import { isBusy, onActivity, type ChatStore } from './chatState';
import type { HostChannel } from './host';
import type { WidgetConfigState } from './widgetRuntime';

export interface WidgetHost {
  close(): void;
  onOpen(listener: () => void): () => void;
}

export const noHost: WidgetHost = {
  close: () => undefined,
  onOpen: () => () => undefined,
};

export class HostLink implements WidgetHost {
  private readonly openListeners = new Set<() => void>();
  private panelOpen = true;
  private unread = 0;
  private busy = false;

  constructor(private readonly channel: HostChannel) {}

  ready(): void {
    this.channel.post({ type: 'ready' });
  }

  settings(state: WidgetConfigState): void {
    if (state.status === 'missing') {
      this.channel.post({ type: 'unavailable' });
    } else if (state.status === 'failed') {
      this.channel.post({ type: 'failed' });
    }
  }

  follow(chat: ChatStore, lifetime: AbortSignal): void {
    const stops = [
      chat.subscribe((state) => {
        const busy = isBusy(state);
        if (busy !== this.busy) {
          this.busy = busy;
          this.channel.post({ type: 'busy', busy });
        }
      }),
      onActivity(chat, (activity) => {
        if (activity.kind === 'message' && (activity.message.author === 'assistant' || activity.message.author === 'staff') && !this.panelOpen) {
          this.unread += 1;
          this.channel.post({ type: 'unread', count: this.unread });
        }
      }),
      this.channel.listen((command) => {
        this.panelOpen = command === 'open';
        if (command !== 'open') {
          return;
        }
        if (this.unread > 0) {
          this.unread = 0;
          this.channel.post({ type: 'unread', count: 0 });
        }
        for (const listener of [...this.openListeners]) {
          listener();
        }
      }),
    ];
    lifetime.addEventListener('abort', () => {
      for (const stop of stops) {
        stop();
      }
      this.openListeners.clear();
    });
  }

  close(): void {
    this.channel.post({ type: 'close' });
  }

  onOpen(listener: () => void): () => void {
    this.openListeners.add(listener);
    return () => {
      this.openListeners.delete(listener);
    };
  }
}
