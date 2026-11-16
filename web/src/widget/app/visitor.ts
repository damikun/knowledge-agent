import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Contact, VisitorAuth, WidgetApi } from './api';
import { noticeFor, quietly, type Notice } from './errors';
import type { HostIdentity, HostIdentityCommand } from './host';
import type { AttributeRejection, VisitorSession } from './types';

export interface VisitorOptions {
  storageKey: string;
  identity?: HostIdentity;
  session?: () => Promise<VisitorSession>;
}

export interface VisitorState {
  phase: 'starting' | 'ready' | 'failed';
  visitorId: string | null;
  name: string | null;
  email: string | null;
  conversationId: string | null;
  notice: Notice | null;
}

export interface Visitor extends VisitorState, VisitorAuth {
  start(): Promise<void>;
  saveContact(contact: Contact): Promise<void>;
  rememberContact(contact: Contact): void;
}

export type VisitorStore = StoreApi<Visitor>;

export const STARTING: VisitorState = { phase: 'starting', visitorId: null, name: null, email: null, conversationId: null, notice: null };

const RENEW_AFTER = 0.9;

interface Session {
  token: string;
  userId: string | null;
  renewAt: number;
}

export function createVisitorStore(api: WidgetApi, options: VisitorOptions, lifetime: AbortSignal): VisitorStore {
  const { storageKey, identity } = options;
  let scope = new AbortController();
  let session: Session | null = null;
  let renewing: Promise<string> | null = null;
  let unwatch: (() => void) | null = null;

  const store = createStore<Visitor>()(() => ({ ...STARTING, signal: scope.signal, start, token, renew, saveContact, rememberContact }));

  lifetime.addEventListener('abort', () => {
    scope.abort();
    unwatch?.();
  });

  async function start(): Promise<void> {
    if (lifetime.aborted) {
      return;
    }
    scope.abort();
    scope = new AbortController();
    renewing = null;
    const { signal } = scope;
    store.setState({ ...STARTING, signal });
    try {
      await identity?.ready;
      signal.throwIfAborted();
      unwatch ??= identity?.subscribe(onIdentity) ?? null;
      await renew();
    } catch (error) {
      if (!signal.aborted) {
        store.setState({ ...STARTING, signal, phase: 'failed', notice: noticeFor(error) });
      }
    }
  }

  async function token(): Promise<string> {
    return session && store.getState().phase === 'ready' && Date.now() < session.renewAt ? session.token : renew();
  }

  function renew(): Promise<string> {
    if (!renewing) {
      const request = requestSession(scope.signal).finally(() => {
        if (renewing === request) {
          renewing = null;
        }
      });
      renewing = request;
    }
    return renewing;
  }

  async function saveContact(contact: Contact): Promise<void> {
    const saved = await api.saveContact(contact);
    store.setState({ name: saved.name, email: saved.email });
  }

  function rememberContact(contact: Contact): void {
    store.setState((state) => ({ name: contact.name ?? state.name, email: contact.email ?? state.email }));
  }

  async function requestSession(signal: AbortSignal): Promise<string> {
    const user = identity?.current() ?? null;
    const started = options.session
      ? await options.session()
      : await api.startSession(session?.token ?? readStoredToken(storageKey), user, {
          attributes: identity?.attributes() ?? {},
          page: identity?.page() ?? null,
        });
    signal.throwIfAborted();
    warnAbout(started.rejectedAttributes ?? []);
    const now = Date.now();
    const expires = started.expiresAt ? Date.parse(started.expiresAt) : Number.NaN;
    session = {
      token: started.token,
      userId: user?.userId ?? null,
      renewAt: Number.isNaN(expires) ? Number.POSITIVE_INFINITY : now + (expires - now) * RENEW_AFTER,
    };
    if (!options.session) {
      storeToken(storageKey, started.token);
    }
    store.setState({
      phase: 'ready',
      visitorId: started.visitorId,
      name: started.name,
      email: started.email,
      conversationId: started.conversationId,
      notice: null,
    });
    return started.token;
  }

  function onIdentity(command: HostIdentityCommand): void {
    if (command.type === 'attributes') {
      if (session && Object.keys(command.attributes).length > 0) {
        void quietly(api.updateAttributes(command.attributes).then(warnAbout));
      }
    } else if (command.type === 'logout') {
      session = null;
      forgetStoredToken(storageKey);
      void start();
    } else if ((command.user?.userId ?? null) !== (session?.userId ?? null)) {
      void start();
    }
  }

  return store;
}

function warnAbout(rejected: readonly AttributeRejection[]): void {
  for (const item of rejected) {
    console.warn(`Knowledge Agent: attribute "${item.key}" was not saved. ${item.reason}`);
  }
}

function readStoredToken(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function storeToken(key: string, token: string): void {
  try {
    window.localStorage.setItem(key, token);
  } catch {}
}

function forgetStoredToken(key: string): void {
  try {
    window.localStorage.removeItem(key);
  } catch {}
}
