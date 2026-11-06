import { isThemePreset, readThemeColors, resolveTheme, DEFAULT_PRESET, type ColorMode, type ThemeColors, type ThemePreset } from '../shared/theme';
import { CLOSE_ICON, createIcon, LAUNCHER_ICONS } from './icons';
import { LOADER_CSS } from './styles';

// The host page's side: a launcher in a shadow root and the chat iframe it opens. Messages are origin-checked both ways.

const MESSAGE_SOURCE = 'knowledge-agent';
const SCRIPT_SELECTOR = 'script[data-widget][src*="/widget/v1/loader.js"]';

// A signed-in user of the host page. userHash is HMAC-SHA256(identity secret, userId) as hex, computed on the
// host's server; the widget's server checks it, so a page cannot pretend to be someone else.
export type AttributeValue = string | number | boolean | null;

export interface KnowledgeAgentUser {
  userId: string;
  userHash: string;
  name?: string;
  email?: string;
  // Sent key: set (overwritten); key left out: kept as before; null: cleared. Dates as ISO text, such as '2025-01-31'.
  attributes?: Record<string, AttributeValue>;
}

// Where the visitor is: what the frame cannot see itself. The server keeps URLs without query or fragment.
export interface KnowledgeAgentPage {
  url: string;
  referrer: string | null;
  language: string | null;
}

export interface KnowledgeAgentApi {
  open: () => void;
  close: () => void;
  toggle: () => void;
  // Tells the chat who is signed in; call it again after another user signs in.
  identify: (user: KnowledgeAgentUser) => void;
  // Attributes of the visitor, also before identify() and for anonymous visitors (they count as unverified then).
  // They reach the server when the chat opens, and at once while it is open.
  update: (values: { attributes: Record<string, AttributeValue> }) => void;
  // Forgets the user and starts a fresh anonymous chat, for sign-out.
  logout: () => void;
}

export interface LoaderHandle {
  // Resolves once the launcher shows, or false when the widget could not load.
  ready: Promise<boolean>;
  destroy: () => void;
}

declare global {
  interface Window {
    KnowledgeAgent?: KnowledgeAgentApi;
    __knowledgeAgentLoader?: LoaderHandle;
  }
}

export interface LauncherConfig {
  title: string;
  launcherIcon: string;
  launcherIconUrl: string | null;
  brandColor: string;
  // The mode: auto, light or dark.
  theme: string;
  preset: ThemePreset;
  colors: ThemeColors;
  position: string;
  launcherLabel: string | null;
  texts: Record<string, string>;
  // Only identified users see the launcher.
  requireIdentity?: boolean;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function optionalText(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value.trim() : null;
}

function readConfig(value: unknown): LauncherConfig | null {
  if (!isRecord(value)) {
    return null;
  }
  const texts: Record<string, string> = {};
  if (isRecord(value.texts)) {
    for (const [key, text] of Object.entries(value.texts)) {
      if (typeof text === 'string') {
        texts[key] = text;
      }
    }
  }
  return {
    title: optionalText(value.title) ?? 'Chat',
    launcherIcon: optionalText(value.launcherIcon) ?? 'message-circle',
    launcherIconUrl: optionalText(value.launcherIconUrl),
    brandColor: optionalText(value.brandColor) ?? '',
    theme: optionalText(value.theme) ?? 'auto',
    preset: isThemePreset(value.preset) ? value.preset : DEFAULT_PRESET,
    colors: readThemeColors(value.colors),
    position: value.position === 'left' ? 'left' : 'right',
    launcherLabel: optionalText(value.launcherLabel),
    texts,
    requireIdentity: value.requireIdentity === true,
  };
}

const MAX_ATTRIBUTES = 100;

// Text, numbers, true or false, and null to clear; anything else is left out with a warning.
function readAttributes(value: unknown): Record<string, AttributeValue> {
  const attributes: Record<string, AttributeValue> = {};
  if (!isRecord(value)) {
    return attributes;
  }
  for (const [key, item] of Object.entries(value).slice(0, MAX_ATTRIBUTES)) {
    if (item === null || typeof item === 'string' || typeof item === 'boolean' || (typeof item === 'number' && Number.isFinite(item))) {
      attributes[key] = item;
    } else if (item instanceof Date && !Number.isNaN(item.getTime())) {
      attributes[key] = item.toISOString();
    } else {
      console.warn(`Knowledge Agent: attribute "${key}" was left out; values are text, numbers, true or false, dates or null.`);
    }
  }
  return attributes;
}

const LANGUAGE_TAG = /^[a-z]{2,3}(-[a-z0-9]{1,8})*$/i;

// The frame takes the host page's language, which the widget's title, greeting and answers usually share (WCAG 3.1.1).
function frameQuery(): string {
  const language = document.documentElement.lang.trim();
  return LANGUAGE_TAG.test(language) ? `?lang=${encodeURIComponent(language)}` : '';
}

function currentPage(): KnowledgeAgentPage {
  return {
    url: window.location.href,
    referrer: document.referrer || null,
    language: typeof navigator !== 'undefined' && navigator.language ? navigator.language : null,
  };
}

// Checks what the host page passes, so a typo shows in the console instead of a silently anonymous chat.
function readUser(value: unknown): KnowledgeAgentUser | null {
  if (!isRecord(value)) {
    return null;
  }
  const userId = typeof value.userId === 'number' ? String(value.userId) : optionalText(value.userId);
  const userHash = optionalText(value.userHash);
  if (!userId || !userHash || !/^[0-9a-fA-F]{64}$/.test(userHash)) {
    return null;
  }
  const user: KnowledgeAgentUser = { userId, userHash: userHash.toLowerCase() };
  const name = optionalText(value.name);
  const email = optionalText(value.email);
  if (name) {
    user.name = name;
  }
  if (email) {
    user.email = email;
  }
  if (isRecord(value.attributes)) {
    user.attributes = readAttributes(value.attributes);
  }
  return user;
}

// <script ... data-user-id="42" data-user-hash="..."> for pages that render the signed-in user on the server.
function userFromScript(script: HTMLScriptElement | null): KnowledgeAgentUser | null {
  const data = script?.dataset;
  if (!data?.userId) {
    return null;
  }
  return readUser({ userId: data.userId, userHash: data.userHash, name: data.userName, email: data.userEmail });
}

export function findLoaderScript(): HTMLScriptElement | null {
  const current = document.currentScript;
  if (current instanceof HTMLScriptElement && current.dataset.widget) {
    return current;
  }
  return document.querySelector<HTMLScriptElement>(SCRIPT_SELECTOR);
}

function element<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className: string,
  attributes: Record<string, string> = {},
): HTMLElementTagNameMap[K] {
  const created = document.createElement(tag);
  created.className = className;
  for (const [name, value] of Object.entries(attributes)) {
    created.setAttribute(name, value);
  }
  return created;
}

function whenBodyReady(): Promise<void> {
  if (document.body) {
    return Promise.resolve();
  }
  return new Promise((resolve) => document.addEventListener('DOMContentLoaded', () => resolve(), { once: true }));
}

function adoptStyles(root: ShadowRoot): void {
  // A constructed sheet also works under a host page CSP that forbids inline <style>.
  if ('adoptedStyleSheets' in root && typeof CSSStyleSheet === 'function' && 'replaceSync' in CSSStyleSheet.prototype) {
    try {
      const sheet = new CSSStyleSheet();
      sheet.replaceSync(LOADER_CSS);
      root.adoptedStyleSheets = [sheet];
      return;
    } catch {
      // Fall back to a <style> element.
    }
  }
  const style = document.createElement('style');
  style.textContent = LOADER_CSS;
  root.append(style);
}

export interface LauncherView {
  // The custom element that holds the shadow root; the caller adds it to the page.
  readonly host: HTMLElement;
  readonly root: HTMLDivElement;
  // Where the chat goes, on the panel's glass or solid surface: the frame on host pages, a slot in the editor's preview.
  readonly panel: HTMLDivElement;
  readonly button: HTMLButtonElement;
  render: (config: LauncherConfig, mode: ColorMode) => void;
  setOpen: (open: boolean) => void;
  setUnread: (count: number) => void;
  // While an answer is written: the glow around the panel, in presets with the glow.
  setBusy: (busy: boolean) => void;
}

// The panel's surface is drawn here in the host page, since only there can a backdrop filter blur the page behind it.
// `render` may run again with a changed config, as the editor preview does on every edit.
export function createLauncherView(origin: string): LauncherView {
  const host = document.createElement('knowledge-agent-widget');
  const shadow = host.attachShadow({ mode: 'open' });
  adoptStyles(shadow);

  const root = element('div', 'ka-root');
  // The outer box carries the glow, which must not be clipped; the surface clips the chat to the panel's corners.
  const frame = element('div', 'ka-panel', { id: 'ka-panel' });
  const panel = element('div', 'ka-surface');
  frame.append(panel);
  const button = element('button', 'ka-launcher', { type: 'button', 'aria-expanded': 'false', 'aria-controls': 'ka-panel' });
  const openIcon = element('span', 'ka-icon ka-open-icon', { 'aria-hidden': 'true' });
  const closeIcon = element('span', 'ka-icon ka-close-icon', { 'aria-hidden': 'true' });
  closeIcon.append(createIcon(CLOSE_ICON));
  const text = element('span', 'ka-label');
  const badge = element('span', 'ka-badge', { 'aria-hidden': 'true' });
  badge.hidden = true;
  button.append(openIcon, closeIcon, badge);
  root.append(frame, button);
  shadow.append(root);

  let label = '';
  let unread = 0;
  let isOpen = false;
  let isBusy = false;

  const showUnread = () => {
    const shown = isOpen ? 0 : unread;
    badge.textContent = shown > 9 ? '9+' : String(shown);
    badge.hidden = shown === 0;
    button.setAttribute('aria-label', shown > 0 ? `${label} (${shown})` : label);
  };

  return {
    host,
    root,
    panel,
    button,
    render: (config, mode) => {
      const theme = resolveTheme({ preset: config.preset, brandColor: config.brandColor, mode, overrides: config.colors[mode] });
      for (const [name, value] of Object.entries(theme.variables)) {
        host.style.setProperty(name, value);
      }
      const position = config.position === 'left' ? 'left' : 'right';
      root.className = [
        'ka-root',
        `ka-${position}`,
        `ka-${mode}`,
        theme.effects.glass ? 'ka-glass' : null,
        theme.effects.glow ? 'ka-glow' : null,
        config.launcherLabel ? null : 'ka-round',
        isOpen ? 'ka-is-open' : null,
        isBusy ? 'ka-busy' : null,
      ]
        .filter(Boolean)
        .join(' ');

      if (config.launcherIconUrl) {
        const image = element('img', '', { alt: '' });
        image.src = new URL(config.launcherIconUrl, origin).toString();
        openIcon.replaceChildren(image);
      } else {
        openIcon.replaceChildren(createIcon(LAUNCHER_ICONS[config.launcherIcon] ?? LAUNCHER_ICONS['message-circle']!));
      }
      if (config.launcherLabel) {
        text.textContent = config.launcherLabel;
        button.insertBefore(text, badge);
      } else {
        text.remove();
      }
      label = config.launcherLabel ?? optionalText(config.texts.launcherLabel) ?? 'Chat with us';
      showUnread();
    },
    setOpen: (open) => {
      isOpen = open;
      root.classList.toggle('ka-is-open', open);
      button.setAttribute('aria-expanded', String(open));
      if (open) {
        unread = 0;
      }
      showUnread();
    },
    setUnread: (count) => {
      unread = Math.max(0, Math.floor(count));
      showUnread();
    },
    setBusy: (busy) => {
      isBusy = busy;
      root.classList.toggle('ka-busy', busy);
    },
  };
}

export function startLoader(script: HTMLScriptElement | null = findLoaderScript()): LoaderHandle | null {
  if (window.__knowledgeAgentLoader) {
    return null;
  }
  const key = script?.dataset.widget?.trim();
  let origin = '';
  try {
    origin = script ? new URL(script.src, document.baseURI).origin : '';
  } catch {
    origin = '';
  }
  if (!key || !origin || origin === 'null') {
    console.warn('Knowledge Agent: the widget script needs a src and a data-widget attribute.');
    return null;
  }

  let view: LauncherView | null = null;
  let iframe: HTMLIFrameElement | null = null;
  let config: LauncherConfig | null = null;
  let isOpen = false;
  let wantOpen = false;
  let destroyed = false;
  let user = userFromScript(script ?? null);
  // Everything update() said so far; the frame sends it with each new session.
  let attributes: Record<string, AttributeValue> = {};
  const systemDark = typeof window.matchMedia === 'function' ? window.matchMedia('(prefers-color-scheme: dark)') : null;

  // "auto" follows the visitor's system, like the frame does.
  const modeOf = (loaded: LauncherConfig): ColorMode =>
    loaded.theme === 'light' || loaded.theme === 'dark' ? loaded.theme : systemDark?.matches ? 'dark' : 'light';

  const onSystemChange = () => {
    if (view && config) {
      view.render(config, modeOf(config));
    }
  };

  const post = (type: 'open' | 'close' | 'logout') => {
    iframe?.contentWindow?.postMessage({ source: MESSAGE_SOURCE, type }, origin);
  };

  // To the frame's origin only: the user hash is no secret, but nothing else needs to see it.
  const postUser = () => {
    iframe?.contentWindow?.postMessage({ source: MESSAGE_SOURCE, type: 'identity', user, attributes, page: currentPage() }, origin);
  };

  const focusFrame = () => {
    window.setTimeout(() => {
      if (isOpen) {
        iframe?.focus();
      }
    }, 0);
  };

  const createFrame = () => {
    if (!view || !config) {
      return;
    }
    iframe = document.createElement('iframe');
    iframe.src = `${origin}/widget/v1/frame/${encodeURIComponent(key)}${frameQuery()}`;
    iframe.title = config.title;
    // Browsers paint a frame opaque when its color scheme differs from the iframe element's, which would hide the glass.
    iframe.style.colorScheme = config.theme === 'light' || config.theme === 'dark' ? config.theme : 'light dark';
    // The frame needs its own origin (storage, same-origin API calls), forms and new tabs for source links.
    iframe.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-forms allow-popups allow-popups-to-escape-sandbox');
    iframe.addEventListener('load', () => {
      if (isOpen) {
        focusFrame();
      }
    });
    view.panel.append(iframe);
  };

  const open = () => {
    if (!view) {
      wantOpen = true;
      return;
    }
    if (isOpen) {
      return;
    }
    isOpen = true;
    if (!iframe) {
      createFrame();
    }
    view.setOpen(true);
    post('open');
    focusFrame();
  };

  const close = () => {
    if (!view) {
      wantOpen = false;
      return;
    }
    if (!isOpen) {
      return;
    }
    // Focus inside the frame shows as the shadow host; only then does it return to the launcher.
    const focusWasInside = document.activeElement === view.host;
    isOpen = false;
    view.setOpen(false);
    post('close');
    if (focusWasInside) {
      view.button.focus();
    }
  };

  const toggle = () => (isOpen ? close() : open());

  const onMessage = (event: MessageEvent) => {
    if (!iframe || event.origin !== origin || event.source !== iframe.contentWindow) {
      return;
    }
    const data: unknown = event.data;
    if (!isRecord(data) || data.source !== MESSAGE_SOURCE) {
      return;
    }
    switch (data.type) {
      case 'ready':
        postUser();
        post(isOpen ? 'open' : 'close');
        break;
      case 'close':
        close();
        break;
      case 'unread':
        if (typeof data.count === 'number') {
          view?.setUnread(data.count);
        }
        break;
      case 'busy':
        view?.setBusy(data.busy === true);
        break;
      default:
        break;
    }
  };

  const render = (loaded: LauncherConfig) => {
    config = loaded;
    if (loaded.requireIdentity && !user) {
      // The widget is for signed-in users; identify() shows it.
      return;
    }
    view = createLauncherView(origin);
    view.render(loaded, modeOf(loaded));
    systemDark?.addEventListener('change', onSystemChange);
    view.button.addEventListener('click', toggle);
    view.root.addEventListener('keydown', (event) => {
      if (event.key === 'Escape' && isOpen) {
        close();
      }
    });
    document.body.append(view.host);
    if (wantOpen) {
      open();
    }
  };

  const ready = (async () => {
    try {
      const response = await fetch(`${origin}/api/public/widgets/${encodeURIComponent(key)}/config`, {
        credentials: 'omit',
        headers: { Accept: 'application/json' },
      });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      const loaded = readConfig(await response.json());
      if (!loaded) {
        throw new Error('invalid configuration');
      }
      await whenBodyReady();
      if (destroyed) {
        return false;
      }
      render(loaded);
      return true;
    } catch (error) {
      console.warn(`Knowledge Agent: the chat widget could not load (${error instanceof Error ? error.message : String(error)}).`);
      return false;
    }
  })();

  const removeView = () => {
    systemDark?.removeEventListener('change', onSystemChange);
    view?.host.remove();
    view = null;
    iframe = null;
    isOpen = false;
  };

  const identify = (value: KnowledgeAgentUser) => {
    const next = readUser(value);
    if (!next) {
      console.warn('Knowledge Agent: identify() needs a userId and the userHash your server computed for it.');
      return;
    }
    user = next;
    postUser();
    if (!view && config && !destroyed) {
      render(config);
    }
  };

  const update = (values: { attributes: Record<string, AttributeValue> }) => {
    if (!isRecord(values) || !isRecord(values.attributes)) {
      console.warn('Knowledge Agent: update() takes { attributes: { key: value } }.');
      return;
    }
    const changed = readAttributes(values.attributes);
    attributes = { ...attributes, ...changed };
    iframe?.contentWindow?.postMessage({ source: MESSAGE_SOURCE, type: 'attributes', attributes: changed }, origin);
  };

  const logout = () => {
    user = null;
    // What the page said about the signed-in user is theirs, not the next visitor's.
    attributes = {};
    post('logout');
    if (config?.requireIdentity) {
      removeView();
    }
  };

  const destroy = () => {
    destroyed = true;
    window.removeEventListener('message', onMessage);
    systemDark?.removeEventListener('change', onSystemChange);
    view?.host.remove();
    if (window.__knowledgeAgentLoader === handle) {
      delete window.__knowledgeAgentLoader;
      delete window.KnowledgeAgent;
    }
  };

  const handle: LoaderHandle = { ready, destroy };
  window.addEventListener('message', onMessage);
  window.__knowledgeAgentLoader = handle;
  window.KnowledgeAgent = { open, close, toggle, identify, update, logout };
  return handle;
}
