import { FALLBACK_SETTINGS } from '../features/widgets/api';
import { COLOR_TOKENS } from '../widget/shared/theme';
import type { WidgetDto, WidgetOptionsDto, WidgetSettings } from '../features/widgets/types';
import { agentGuid } from './agentsApi';
import { jsonResponse, type FetchHandler } from './utils';

export const WIDGET_TEXT_KEYS = [
  'launcherLabel', 'close', 'send', 'newChat', 'endChat', 'talkToPerson', 'waitingForPerson', 'keepChatting', 'personJoined',
  'feedbackPrompt', 'feedbackThanks', 'feedbackComment', 'ratingPrompt', 'contactTitle', 'contactName', 'contactEmail',
  'contactSubmit', 'sources', 'offline', 'rateLimited', 'dailyCap', 'error', 'poweredBy',
];

/** A GUID-shaped widget id (`n` = 1 → "0199c1e2-0000-7000-8000-0000000b0001"). */
export function widgetGuid(n: number): string {
  return `0199c1e2-0000-7000-8000-0000000b${String(n).padStart(4, '0')}`;
}

/** The defaults of a new widget, as GET /api/widgets/options sends them. */
export function widgetSettings(overrides: { [K in keyof WidgetSettings]?: Partial<WidgetSettings[K]> } = {}): WidgetSettings {
  const base = structuredClone(FALLBACK_SETTINGS);
  return {
    appearance: { ...base.appearance, ...overrides.appearance },
    behavior: { ...base.behavior, ...overrides.behavior },
    security: { ...base.security, ...overrides.security },
    handover: { ...base.handover, ...overrides.handover },
    conversations: { ...base.conversations, ...overrides.conversations },
  };
}

export function widgetOptions(overrides: Partial<WidgetOptionsDto> = {}): WidgetOptionsDto {
  return {
    launcherIcons: ['message-circle', 'message-square', 'messages-square', 'sparkles', 'bot', 'circle-help', 'headset'],
    themes: ['auto', 'light', 'dark'],
    presets: ['glow', 'glass', 'solid'],
    colorTokens: [...COLOR_TOKENS],
    positions: ['right', 'left'],
    citationModes: ['hidden', 'titles', 'links'],
    contactCaptures: ['never', 'before_handover', 'before_chat'],
    handoverChannels: ['portal', 'email', 'slack'],
    textKeys: [...WIDGET_TEXT_KEYS],
    defaults: widgetSettings(),
    ...overrides,
  };
}

/** "Shop help" for the website helper agent of agentsApi. */
export function widgetDto(overrides: Partial<WidgetDto> = {}): WidgetDto {
  return {
    id: widgetGuid(1),
    publicKey: 'wk_abcdefghijklmnopqrstuvwx',
    name: 'Shop help',
    agentId: agentGuid(2),
    enabled: true,
    settings: widgetSettings({ appearance: { title: 'Shop help' }, security: { allowedOrigins: ['https://shop.example.com'] } }),
    createdAt: '2026-10-30T08:00:00Z',
    updatedAt: '2026-10-31T09:00:00Z',
    identityVerification: false,
    identitySecretCreatedAt: null,
    ...overrides,
  };
}

/** mockFetch routes for GET /api/widgets and /options; the getter is read per request. */
export function widgetsRoutes(getWidgets: () => readonly WidgetDto[], options: WidgetOptionsDto = widgetOptions()): Record<string, FetchHandler> {
  return {
    'GET /api/widgets': () => jsonResponse(getWidgets()),
    'GET /api/widgets/options': () => jsonResponse(options),
  };
}
