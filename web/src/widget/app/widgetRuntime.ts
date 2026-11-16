import { useEffect, useEffectEvent, useState } from 'react';
import { createStore, type StoreApi } from 'zustand/vanilla';
import { isApiError, WidgetApi } from './api';
import { createChatStore } from './chat';
import type { Chat, ChatStore } from './chatState';
import { silentHost, type HostChannel } from './host';
import { HostLink, type WidgetHost } from './hostLink';
import type { ReadonlyStore } from './store';
import { normalizeConfig, type WidgetConfig } from './types';
import { createVisitorStore, type VisitorOptions, type VisitorState, type VisitorStore } from './visitor';

export type WidgetConfigState = { status: 'loading' } | { status: 'ready'; config: WidgetConfig } | { status: 'missing' } | { status: 'failed' };

export type ConfigStore = ReadonlyStore<WidgetConfigState>;

export interface Widget {
  readonly config: ConfigStore;
  readonly visitor: ReadonlyStore<VisitorState>;
  readonly chat: ChatStore;
  readonly host: WidgetHost;
}

export interface WidgetRuntimeOptions extends VisitorOptions {
  config?: WidgetConfig;
  retryDelaysMs?: readonly number[];
  host?: HostChannel;
}

export function readyConfig(config: WidgetConfig): ConfigStore {
  return createStore<WidgetConfigState>()(() => ({ status: 'ready', config }));
}

export class WidgetRuntime implements Widget {
  readonly config: StoreApi<WidgetConfigState>;
  readonly visitor: VisitorStore;
  readonly chat: StoreApi<Chat>;
  readonly host: HostLink;
  private readonly api: WidgetApi;
  private readonly lifetime = new AbortController();

  constructor(widgetKey: string, options: WidgetRuntimeOptions) {
    const { config, retryDelaysMs, host, ...visitorOptions } = options;
    const initial: WidgetConfigState = config ? { status: 'ready', config } : widgetKey ? { status: 'loading' } : { status: 'missing' };
    this.config = createStore<WidgetConfigState>()(() => initial);
    this.host = new HostLink(host ?? silentHost);
    this.api = new WidgetApi(widgetKey, () => this.visitor.getState());
    this.visitor = createVisitorStore(this.api, visitorOptions, this.lifetime.signal);
    this.chat = createChatStore(this.api, this.visitor, this.lifetime.signal, { retryDelaysMs });
  }

  async start(): Promise<void> {
    this.host.ready();
    if (this.config.getState().status === 'loading') {
      const loaded = await this.loadConfig();
      if (this.lifetime.signal.aborted) {
        return;
      }
      this.config.setState(loaded, true);
    }
    const settings = this.config.getState();
    this.host.settings(settings);
    if (settings.status === 'ready') {
      this.host.follow(this.chat, this.lifetime.signal);
      await this.visitor.getState().start();
    }
  }

  dispose(): void {
    this.lifetime.abort();
  }

  private async loadConfig(): Promise<WidgetConfigState> {
    try {
      const config = normalizeConfig(await this.api.getConfig(this.lifetime.signal));
      return config ? { status: 'ready', config } : { status: 'failed' };
    } catch (error) {
      return { status: isApiError(error, 404) ? 'missing' : 'failed' };
    }
  }
}

export function useOwnedRuntime(key: string, create: () => WidgetRuntime): WidgetRuntime | null {
  const [runtime, setRuntime] = useState<WidgetRuntime | null>(null);
  const createRuntime = useEffectEvent(create);

  useEffect(() => {
    const created = createRuntime();
    setRuntime(created);
    void created.start();
    return () => created.dispose();
  }, [key]);

  return runtime;
}
