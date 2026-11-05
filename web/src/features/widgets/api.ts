import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { asArray, asStrings, isRecord, request } from '../../lib/api';
import type { Guid } from '../../lib/types';
import { agentKeys, agentsApi } from '../agents/api';
import { integrationKeys, integrationsApi } from '../notifications/api';
import type {
  SaveWidgetRequest,
  Weekday,
  WidgetAssetDto,
  WidgetColors,
  WidgetDto,
  WidgetIdentitySecretDto,
  WidgetOpeningHours,
  WidgetOptionsDto,
  WidgetSettings,
} from './types';

export const widgetKeys = {
  list: ['widgets', 'list'] as const,
  options: ['widgets', 'options'] as const,
};

export const WEEKDAYS: readonly Weekday[] = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'];

/** The API's defaults, for settings an older API leaves out. */
export const FALLBACK_SETTINGS: WidgetSettings = {
  appearance: {
    title: 'Ask us anything',
    subtitle: null,
    avatarAssetId: null,
    launcherIcon: 'message-circle',
    launcherAssetId: null,
    brandColor: '#0f766e',
    theme: 'auto',
    preset: 'glow',
    colors: { light: {}, dark: {} },
    position: 'right',
    launcherLabel: null,
    showFooter: true,
  },
  behavior: {
    greeting: 'Hi! How can I help you today?',
    suggestedQuestions: [],
    placeholder: null,
    citationMode: 'titles',
    feedbackEnabled: true,
    handoverEnabled: false,
    contactCapture: 'before_handover',
    texts: {},
  },
  security: { allowedOrigins: [], messagesPerMinute: 6, dailyMessageCap: 500, maxMessageLength: 2000, retentionDays: 90, requireVerifiedUsers: false },
  handover: {
    channel: 'portal',
    emailTo: [],
    slackChannelId: null,
    escalateAfterMinutes: 10,
    waitMinutes: 15,
    availability: { always: true, timeZone: 'UTC', hours: [] },
  },
  conversations: { closeIdle: true, aiIdleMinutes: 30, humanIdleHours: 24 },
};

function record(value: unknown): Record<string, unknown> {
  return isRecord(value) ? value : {};
}

function str(value: unknown, fallback: string): string {
  return typeof value === 'string' ? value : fallback;
}

function nullable(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

function num(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function bool(value: unknown, fallback: boolean): boolean {
  return typeof value === 'boolean' ? value : fallback;
}

function texts(value: unknown): Record<string, string> {
  return Object.fromEntries(Object.entries(record(value)).filter((entry): entry is [string, string] => typeof entry[1] === 'string'));
}

function colors(value: unknown): WidgetColors {
  const modes = record(value);
  return { light: texts(modes.light), dark: texts(modes.dark) };
}

function hours(value: unknown): WidgetOpeningHours[] {
  return (Array.isArray(value) ? value : []).flatMap((item) => {
    const row = record(item);
    const day = typeof row.day === 'string' ? (row.day.toLowerCase() as Weekday) : null;
    return day && WEEKDAYS.includes(day) && typeof row.opens === 'string' && typeof row.closes === 'string' ? [{ day, opens: row.opens, closes: row.closes }] : [];
  });
}

export function normalizeSettings(value: unknown, base: WidgetSettings = FALLBACK_SETTINGS): WidgetSettings {
  const settings = record(value);
  const appearance = record(settings.appearance);
  const behavior = record(settings.behavior);
  const security = record(settings.security);
  const handover = record(settings.handover);
  const availability = record(handover.availability);
  const conversations = record(settings.conversations);
  return {
    appearance: {
      title: str(appearance.title, base.appearance.title),
      subtitle: nullable(appearance.subtitle),
      avatarAssetId: nullable(appearance.avatarAssetId),
      launcherIcon: str(appearance.launcherIcon, base.appearance.launcherIcon),
      launcherAssetId: nullable(appearance.launcherAssetId),
      brandColor: str(appearance.brandColor, base.appearance.brandColor),
      theme: str(appearance.theme, base.appearance.theme),
      preset: str(appearance.preset, base.appearance.preset),
      colors: colors(appearance.colors),
      position: str(appearance.position, base.appearance.position),
      launcherLabel: nullable(appearance.launcherLabel),
      showFooter: bool(appearance.showFooter, base.appearance.showFooter),
    },
    behavior: {
      greeting: 'greeting' in behavior ? nullable(behavior.greeting) : base.behavior.greeting,
      suggestedQuestions: asStrings(behavior.suggestedQuestions),
      placeholder: nullable(behavior.placeholder),
      citationMode: str(behavior.citationMode, base.behavior.citationMode),
      feedbackEnabled: bool(behavior.feedbackEnabled, base.behavior.feedbackEnabled),
      handoverEnabled: bool(behavior.handoverEnabled, base.behavior.handoverEnabled),
      contactCapture: str(behavior.contactCapture, base.behavior.contactCapture),
      texts: texts(behavior.texts),
    },
    security: {
      allowedOrigins: asStrings(security.allowedOrigins),
      messagesPerMinute: num(security.messagesPerMinute, base.security.messagesPerMinute),
      dailyMessageCap: num(security.dailyMessageCap, base.security.dailyMessageCap),
      maxMessageLength: num(security.maxMessageLength, base.security.maxMessageLength),
      retentionDays: num(security.retentionDays, base.security.retentionDays),
      requireVerifiedUsers: bool(security.requireVerifiedUsers, base.security.requireVerifiedUsers),
    },
    handover: {
      channel: str(handover.channel, base.handover.channel),
      emailTo: asStrings(handover.emailTo),
      slackChannelId: nullable(handover.slackChannelId),
      escalateAfterMinutes: num(handover.escalateAfterMinutes, base.handover.escalateAfterMinutes),
      waitMinutes: num(handover.waitMinutes, base.handover.waitMinutes),
      availability: {
        always: bool(availability.always, base.handover.availability.always),
        timeZone: str(availability.timeZone, base.handover.availability.timeZone),
        hours: hours(availability.hours),
      },
    },
    conversations: {
      closeIdle: bool(conversations.closeIdle, base.conversations.closeIdle),
      aiIdleMinutes: num(conversations.aiIdleMinutes, base.conversations.aiIdleMinutes),
      humanIdleHours: num(conversations.humanIdleHours, base.conversations.humanIdleHours),
    },
  };
}

function normalizeWidget(value: unknown): WidgetDto {
  const dto = record(value);
  return {
    id: str(dto.id, ''),
    publicKey: str(dto.publicKey, ''),
    name: str(dto.name, ''),
    agentId: str(dto.agentId, ''),
    enabled: dto.enabled !== false,
    settings: normalizeSettings(dto.settings),
    createdAt: str(dto.createdAt, ''),
    updatedAt: str(dto.updatedAt, ''),
    identityVerification: dto.identityVerification === true,
    identitySecretCreatedAt: nullable(dto.identitySecretCreatedAt),
  };
}

function toOptions(value: unknown): WidgetOptionsDto {
  const dto = record(value);
  const list = (key: string, fallback: string[]) => {
    const items = asStrings(dto[key]);
    return items.length > 0 ? items : fallback;
  };
  return {
    launcherIcons: list('launcherIcons', ['message-circle']),
    themes: list('themes', ['auto', 'light', 'dark']),
    presets: list('presets', ['glow', 'glass', 'solid']),
    colorTokens: asStrings(dto.colorTokens),
    positions: list('positions', ['right', 'left']),
    citationModes: list('citationModes', ['hidden', 'titles', 'links']),
    contactCaptures: list('contactCaptures', ['never', 'before_handover', 'before_chat']),
    handoverChannels: list('handoverChannels', ['portal', 'email', 'slack']),
    textKeys: asStrings(dto.textKeys),
    defaults: normalizeSettings(dto.defaults),
  };
}

const widgetPath = (id: Guid) => `/api/widgets/${encodeURIComponent(id)}`;

export function mediaUrl(assetId: Guid): string {
  return `/widget/v1/media/${encodeURIComponent(assetId)}`;
}

export const widgetsApi = {
  listWidgets: async (signal?: AbortSignal) => asArray<unknown>(await request<unknown>('/api/widgets', { signal })).map(normalizeWidget),
  getOptions: async (signal?: AbortSignal) => toOptions(await request<unknown>('/api/widgets/options', { signal })),
  createWidget: async (body: SaveWidgetRequest) => normalizeWidget(await request<unknown>('/api/widgets', { method: 'POST', body })),
  updateWidget: async (id: Guid, body: SaveWidgetRequest) => normalizeWidget(await request<unknown>(widgetPath(id), { method: 'PUT', body })),
  deleteWidget: (id: Guid) => request<void>(widgetPath(id), { method: 'DELETE' }),
  createIdentitySecret: async (id: Guid): Promise<WidgetIdentitySecretDto> => {
    const dto = record(await request<unknown>(`${widgetPath(id)}/identity-secret`, { method: 'POST' }));
    return { secret: str(dto.secret, ''), createdAt: str(dto.createdAt, '') };
  },
  removeIdentitySecret: async (id: Guid) => normalizeWidget(await request<unknown>(`${widgetPath(id)}/identity-secret`, { method: 'DELETE' })),
  uploadAsset: async (id: Guid, file: File): Promise<WidgetAssetDto> => {
    const formData = new FormData();
    formData.append('file', file);
    const dto = record(await request<unknown>(`${widgetPath(id)}/assets`, { method: 'POST', formData }));
    const assetId = str(dto.id, '');
    return { id: assetId, url: str(dto.url, mediaUrl(assetId)), contentType: str(dto.contentType, file.type), size: num(dto.size, file.size) };
  },
};

/** Support users get 403, so pass `enabled` false for them. */
export function useWidgets(enabled = true) {
  return useQuery({ queryKey: widgetKeys.list, queryFn: ({ signal }) => widgetsApi.listWidgets(signal), staleTime: 30_000, enabled });
}

const optionsQuery = { queryKey: widgetKeys.options, queryFn: ({ signal }: { signal?: AbortSignal }) => widgetsApi.getOptions(signal), staleTime: 5 * 60_000 };

export function useWidgetOptions() {
  return useQuery(optionsQuery);
}

/** Prefetches what the editor needs before it can draw. Call it on hover and focus. */
export function usePrefetchWidgetEditor(): () => void {
  const queryClient = useQueryClient();
  return useCallback(() => {
    void queryClient.prefetchQuery(optionsQuery);
    void queryClient.prefetchQuery({ queryKey: agentKeys.list, queryFn: ({ signal }) => agentsApi.listAgents(signal), staleTime: 30_000 });
    void queryClient.prefetchQuery({ queryKey: integrationKeys.all, queryFn: ({ signal }) => integrationsApi.get(signal), staleTime: 30_000 });
  }, [queryClient]);
}
