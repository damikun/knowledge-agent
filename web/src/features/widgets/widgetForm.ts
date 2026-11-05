import { isHexColor } from '../../lib/color';
import { createClientId } from '../../lib/ids';
import type { Guid } from '../../lib/types';
import { mediaUrl } from './api';
import type { SaveWidgetRequest, Weekday, WidgetColors, WidgetDto, WidgetPreviewConfig, WidgetSettings } from './types';

// API limits.
export const MAX_NAME_LENGTH = 100;
export const MAX_TITLE_LENGTH = 60;
export const MAX_SUBTITLE_LENGTH = 120;
export const MAX_LAUNCHER_LABEL_LENGTH = 40;
export const MAX_GREETING_LENGTH = 500;
export const MAX_PLACEHOLDER_LENGTH = 80;
export const MAX_QUESTIONS = 4;
export const MAX_QUESTION_LENGTH = 120;
export const MAX_TEXT_LENGTH = 200;
const MAX_ORIGINS = 20;
export const MAX_SLACK_CHANNEL_LENGTH = 40;
export const MAX_IMAGE_BYTES = 512 * 1024;
export const IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/webp'];

export const NUMBER_LIMITS = {
  messagesPerMinute: { min: 1, max: 60, path: 'settings.security.messagesPerMinute' },
  dailyMessageCap: { min: 1, max: 100_000, path: 'settings.security.dailyMessageCap' },
  maxMessageLength: { min: 100, max: 4000, path: 'settings.security.maxMessageLength' },
  retentionDays: { min: 1, max: 3650, path: 'settings.security.retentionDays' },
  escalateAfterMinutes: { min: 1, max: 1440, path: 'settings.handover.escalateAfterMinutes' },
  waitMinutes: { min: 1, max: 1440, path: 'settings.handover.waitMinutes' },
  aiIdleMinutes: { min: 5, max: 1440, path: 'settings.conversations.aiIdleMinutes' },
  humanIdleHours: { min: 1, max: 168, path: 'settings.conversations.humanIdleHours' },
} as const;

export type NumberField = keyof typeof NUMBER_LIMITS;

/** Field paths as the API names them in validation problems. */
export const WIDGET_FIELDS = [
  'name',
  'agentId',
  'settings.appearance',
  'settings.appearance.title',
  'settings.appearance.subtitle',
  'settings.appearance.launcherLabel',
  'settings.appearance.launcherIcon',
  'settings.appearance.theme',
  'settings.appearance.preset',
  'settings.appearance.colors.light',
  'settings.appearance.colors.dark',
  'settings.appearance.position',
  'settings.appearance.brandColor',
  'settings.behavior.greeting',
  'settings.behavior.suggestedQuestions',
  'settings.behavior.placeholder',
  'settings.behavior.citationMode',
  'settings.behavior.contactCapture',
  'settings.behavior.texts',
  'settings.security.allowedOrigins',
  'settings.security.messagesPerMinute',
  'settings.security.dailyMessageCap',
  'settings.security.maxMessageLength',
  'settings.security.retentionDays',
  'settings.security.requireVerifiedUsers',
  'settings.handover.channel',
  'settings.handover.emailTo',
  'settings.handover.slackChannelId',
  'settings.handover.escalateAfterMinutes',
  'settings.handover.waitMinutes',
  'settings.handover.availability.timeZone',
  'settings.handover.availability.hours',
  'settings.conversations.aiIdleMinutes',
  'settings.conversations.humanIdleHours',
] as const;

export type WidgetField = (typeof WIDGET_FIELDS)[number];
export type WidgetFormErrors = Partial<Record<WidgetField, string>>;

export interface HoursRow {
  /** For React keys only. */
  key: string;
  day: Weekday;
  /** "09:00", as a time input has it. */
  opens: string;
  closes: string;
}

export interface WidgetFormValues {
  name: string;
  agentId: Guid;
  enabled: boolean;
  title: string;
  subtitle: string;
  avatarAssetId: Guid | null;
  launcherIcon: string;
  launcherAssetId: Guid | null;
  brandColor: string;
  theme: string;
  preset: string;
  /** An empty color keeps the preset's. */
  colors: WidgetColors;
  position: string;
  launcherLabel: string;
  showFooter: boolean;
  greeting: string;
  suggestedQuestions: string[];
  placeholder: string;
  citationMode: string;
  feedbackEnabled: boolean;
  handoverEnabled: boolean;
  contactCapture: string;
  texts: Record<string, string>;
  /** One site per line. */
  allowedOrigins: string;
  messagesPerMinute: string;
  dailyMessageCap: string;
  maxMessageLength: string;
  retentionDays: string;
  requireVerifiedUsers: boolean;
  channel: string;
  /** One address per line. */
  emailTo: string;
  slackChannelId: string;
  escalateAfterMinutes: string;
  waitMinutes: string;
  alwaysAvailable: boolean;
  timeZone: string;
  hours: HoursRow[];
  closeIdle: boolean;
  aiIdleMinutes: string;
  humanIdleHours: string;
}

function toInputTime(value: string): string {
  return value.slice(0, 5);
}

function toApiTime(value: string): string {
  return /^\d{2}:\d{2}$/.test(value) ? `${value}:00` : value;
}

export function hoursRow(day: Weekday = 'monday', opens = '09:00', closes = '17:00'): HoursRow {
  return { key: createClientId(), day, opens, closes };
}

export function initialWidgetValues(widget: WidgetDto | null, defaults: WidgetSettings, agentId: Guid = ''): WidgetFormValues {
  const { appearance, behavior, security, handover, conversations } = widget?.settings ?? defaults;
  return {
    name: widget?.name ?? '',
    agentId: widget?.agentId ?? agentId,
    enabled: widget?.enabled ?? true,
    title: appearance.title,
    subtitle: appearance.subtitle ?? '',
    avatarAssetId: appearance.avatarAssetId,
    launcherIcon: appearance.launcherIcon,
    launcherAssetId: appearance.launcherAssetId,
    brandColor: appearance.brandColor,
    theme: appearance.theme,
    preset: appearance.preset,
    colors: { light: { ...appearance.colors.light }, dark: { ...appearance.colors.dark } },
    position: appearance.position,
    launcherLabel: appearance.launcherLabel ?? '',
    showFooter: appearance.showFooter,
    greeting: behavior.greeting ?? '',
    suggestedQuestions: [...behavior.suggestedQuestions],
    placeholder: behavior.placeholder ?? '',
    citationMode: behavior.citationMode,
    feedbackEnabled: behavior.feedbackEnabled,
    handoverEnabled: behavior.handoverEnabled,
    contactCapture: behavior.contactCapture,
    texts: { ...behavior.texts },
    allowedOrigins: security.allowedOrigins.join('\n'),
    messagesPerMinute: String(security.messagesPerMinute),
    dailyMessageCap: String(security.dailyMessageCap),
    maxMessageLength: String(security.maxMessageLength),
    retentionDays: String(security.retentionDays),
    requireVerifiedUsers: security.requireVerifiedUsers,
    channel: handover.channel,
    emailTo: handover.emailTo.join('\n'),
    slackChannelId: handover.slackChannelId ?? '',
    escalateAfterMinutes: String(handover.escalateAfterMinutes),
    waitMinutes: String(handover.waitMinutes),
    alwaysAvailable: handover.availability.always,
    timeZone: handover.availability.timeZone,
    hours: handover.availability.hours.map((row) => hoursRow(row.day, toInputTime(row.opens), toInputTime(row.closes))),
    closeIdle: conversations.closeIdle,
    aiIdleMinutes: String(conversations.aiIdleMinutes),
    humanIdleHours: String(conversations.humanIdleHours),
  };
}

export function splitLines(value: string): string[] {
  return value
    .split(/[\n,]/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function optional(value: string): string | null {
  return value.trim() || null;
}

/** scheme://host[:port], where the host may start with *. for every subdomain. The API checks the rest. */
const ORIGIN = /^https?:\/\/(\*\.)?[^\s/:?#*]+(:\d{1,5})?\/?$/i;

export function isSiteOrigin(value: string): boolean {
  return ORIGIN.test(value.trim());
}

function isTimeZone(value: string): boolean {
  if (!value.trim()) {
    return false;
  }
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: value.trim() });
    return true;
  } catch {
    return false;
  }
}

function parseNumber(value: string, { min, max }: { min: number; max: number }): number | null {
  const trimmed = value.trim();
  if (!/^\d+$/.test(trimmed)) {
    return null;
  }
  const parsed = Number.parseInt(trimmed, 10);
  return parsed >= min && parsed <= max ? parsed : null;
}

export interface SaveWidgetResult {
  request: SaveWidgetRequest | null;
  errors: WidgetFormErrors;
}

export type ColorMode = keyof WidgetColors;

/** Set overrides only, trimmed and lower-cased as the API stores them. */
function colorsOf(colors: WidgetColors): WidgetColors {
  const set = (overrides: Record<string, string>) =>
    Object.fromEntries(
      Object.entries(overrides)
        .map(([token, color]) => [token, color.trim().toLowerCase()] as const)
        .filter(([, color]) => color !== ''),
    );
  return { light: set(colors.light), dark: set(colors.dark) };
}

function questionsOf(values: WidgetFormValues): string[] {
  return values.suggestedQuestions.map((question) => question.trim()).filter(Boolean);
}

function originsOf(values: WidgetFormValues): string[] {
  return splitLines(values.allowedOrigins).map((origin) => origin.replace(/\/+$/, ''));
}

function check(values: WidgetFormValues): { errors: WidgetFormErrors; numbers: Record<NumberField, number> } {
  const errors: WidgetFormErrors = {};
  if (!values.name.trim()) {
    errors.name = 'Enter a name.';
  }
  if (!values.agentId) {
    errors.agentId = 'Choose an agent.';
  }
  if (!values.title.trim()) {
    errors['settings.appearance.title'] = 'Enter a title.';
  }
  if (!isHexColor(values.brandColor.trim())) {
    errors['settings.appearance.brandColor'] = 'Enter a color such as #0f766e.';
  }
  for (const mode of ['light', 'dark'] as const) {
    if (Object.values(colorsOf(values.colors)[mode]).some((color) => !isHexColor(color))) {
      errors[`settings.appearance.colors.${mode}`] = 'Enter colors such as #0f766e, or leave them empty to keep the theme\'s.';
    }
  }
  const questions = questionsOf(values);
  if (questions.length > MAX_QUESTIONS || questions.some((question) => question.length > MAX_QUESTION_LENGTH)) {
    errors['settings.behavior.suggestedQuestions'] = `Add at most ${MAX_QUESTIONS} questions of up to ${MAX_QUESTION_LENGTH} characters.`;
  }
  const origins = originsOf(values);
  const badOrigin = origins.find((origin) => !isSiteOrigin(origin));
  if (badOrigin) {
    errors['settings.security.allowedOrigins'] = `'${badOrigin}' is not a site such as https://www.example.com or https://*.example.com.`;
  } else if (origins.length > MAX_ORIGINS) {
    errors['settings.security.allowedOrigins'] = `Add at most ${MAX_ORIGINS} sites.`;
  }
  const numbers = {} as Record<NumberField, number>;
  for (const [field, limits] of Object.entries(NUMBER_LIMITS) as Array<[NumberField, (typeof NUMBER_LIMITS)[NumberField]]>) {
    const parsed = parseNumber(values[field], limits);
    if (parsed === null) {
      errors[limits.path] = `Enter a number from ${limits.min} to ${limits.max}.`;
    } else {
      numbers[field] = parsed;
    }
  }
  if (!values.alwaysAvailable && !isTimeZone(values.timeZone)) {
    errors['settings.handover.availability.timeZone'] = 'Enter a time zone such as Europe/Prague or UTC.';
  }
  if (!values.alwaysAvailable && values.hours.some((row) => !row.opens || !row.closes || row.closes <= row.opens)) {
    errors['settings.handover.availability.hours'] = 'Each row must close after it opens.';
  }
  return { errors, numbers };
}

export function validateWidget(values: WidgetFormValues): WidgetFormErrors {
  return check(values).errors;
}

/** PUT replaces the whole widget, so `base` keeps settings this form has no control for. */
export function toSaveRequest(values: WidgetFormValues, base: WidgetSettings): SaveWidgetResult {
  const { errors, numbers } = check(values);
  if (Object.keys(errors).length > 0) {
    return { request: null, errors };
  }
  const name = values.name.trim();
  const title = values.title.trim();
  const brandColor = values.brandColor.trim().toLowerCase();
  const questions = questionsOf(values);
  const origins = originsOf(values);
  const timeZone = values.timeZone.trim();

  const settings: WidgetSettings = {
    ...base,
    appearance: {
      ...base.appearance,
      title,
      subtitle: optional(values.subtitle),
      avatarAssetId: values.avatarAssetId,
      launcherIcon: values.launcherIcon,
      launcherAssetId: values.launcherAssetId,
      brandColor,
      theme: values.theme,
      preset: values.preset,
      colors: colorsOf(values.colors),
      position: values.position,
      launcherLabel: optional(values.launcherLabel),
      showFooter: values.showFooter,
    },
    behavior: {
      ...base.behavior,
      greeting: optional(values.greeting),
      suggestedQuestions: questions,
      placeholder: optional(values.placeholder),
      citationMode: values.citationMode,
      feedbackEnabled: values.feedbackEnabled,
      handoverEnabled: values.handoverEnabled,
      contactCapture: values.contactCapture,
      texts: Object.fromEntries(
        Object.entries(values.texts)
          .map(([key, text]) => [key, text.trim()] as const)
          .filter(([, text]) => text !== ''),
      ),
    },
    security: {
      ...base.security,
      allowedOrigins: origins,
      messagesPerMinute: numbers.messagesPerMinute,
      dailyMessageCap: numbers.dailyMessageCap,
      maxMessageLength: numbers.maxMessageLength,
      retentionDays: numbers.retentionDays,
      requireVerifiedUsers: values.requireVerifiedUsers,
    },
    handover: {
      ...base.handover,
      channel: values.channel,
      emailTo: splitLines(values.emailTo),
      slackChannelId: optional(values.slackChannelId),
      escalateAfterMinutes: numbers.escalateAfterMinutes,
      waitMinutes: numbers.waitMinutes,
      availability: {
        ...base.handover.availability,
        always: values.alwaysAvailable,
        // The API checks the zone even when the widget is always available.
        timeZone: isTimeZone(timeZone) ? timeZone : base.handover.availability.timeZone,
        hours: values.hours.map((row) => ({ day: row.day, opens: toApiTime(row.opens), closes: toApiTime(row.closes) })),
      },
    },
    conversations: {
      ...base.conversations,
      closeIdle: values.closeIdle,
      aiIdleMinutes: numbers.aiIdleMinutes,
      humanIdleHours: numbers.humanIdleHours,
    },
  };
  return { request: { name, agentId: values.agentId, enabled: values.enabled, settings }, errors };
}

// A half-typed color keeps the theme's in the preview until it is valid.
function validColors(colors: WidgetColors): WidgetColors {
  const valid = colorsOf(colors);
  const keep = (overrides: Record<string, string>) => Object.fromEntries(Object.entries(overrides).filter(([, color]) => isHexColor(color)));
  return { light: keep(valid.light), dark: keep(valid.dark) };
}

/** Values that don't validate fall back to the saved ones. */
export function toPreviewConfig(values: WidgetFormValues, publicKey: string, saved: WidgetSettings): WidgetPreviewConfig {
  const maxMessageLength = parseNumber(values.maxMessageLength, NUMBER_LIMITS.maxMessageLength) ?? saved.security.maxMessageLength;
  return {
    key: publicKey,
    title: values.title.trim() || saved.appearance.title,
    subtitle: optional(values.subtitle),
    avatarUrl: values.avatarAssetId ? mediaUrl(values.avatarAssetId) : null,
    launcherIcon: values.launcherIcon,
    launcherIconUrl: values.launcherAssetId ? mediaUrl(values.launcherAssetId) : null,
    brandColor: isHexColor(values.brandColor) ? values.brandColor.trim().toLowerCase() : saved.appearance.brandColor,
    theme: values.theme,
    preset: values.preset,
    colors: validColors(values.colors),
    showFooter: values.showFooter,
    position: values.position,
    launcherLabel: optional(values.launcherLabel),
    greeting: optional(values.greeting),
    suggestedQuestions: values.suggestedQuestions
      .map((question) => question.trim())
      .filter(Boolean)
      .slice(0, MAX_QUESTIONS),
    placeholder: optional(values.placeholder),
    citationMode: values.citationMode,
    feedbackEnabled: values.feedbackEnabled,
    handoverEnabled: values.handoverEnabled,
    contactCapture: values.contactCapture,
    texts: Object.fromEntries(
      Object.entries(values.texts)
        .map(([key, text]) => [key, text.trim()] as const)
        .filter(([, text]) => text !== ''),
    ),
    maxMessageLength,
  };
}

export function embedSnippet(origin: string, publicKey: string): string {
  return `<script async src="${origin}/widget/v1/loader.js" data-widget="${publicKey}"></script>`;
}

/** USER_HASH is HMAC-SHA256(identity secret, USER_ID) as hex, computed on the website's server. */
export function identifySnippet(origin: string, publicKey: string): string {
  return [
    '<!-- Pages rendered on your server -->',
    `<script async src="${origin}/widget/v1/loader.js" data-widget="${publicKey}"`,
    '  data-user-id="USER_ID" data-user-hash="USER_HASH" data-user-name="Ada Lovelace" data-user-email="ada@example.com"></script>',
    '',
    '<!-- Single-page apps, once the loader has loaded -->',
    '<script>',
    '  // After sign-in:',
    "  window.KnowledgeAgent.identify({ userId: 'USER_ID', userHash: 'USER_HASH', name: 'Ada Lovelace', email: 'ada@example.com', attributes: { plan: 'pro', seats: 5 } });",
    '  // On sign-out:',
    '  window.KnowledgeAgent.logout();',
    '</script>',
  ].join('\n');
}

export function updateSnippet(): string {
  return [
    '<script>',
    '  // A key you send is saved (it replaces the old value), a key you leave out keeps its value, null clears it.',
    "  // Text, numbers, true or false, and dates as ISO text. Keys: lowercase letters, digits and _, such as trial_ends.",
    "  window.KnowledgeAgent.update({ attributes: { plan: 'pro', seats: 5, beta: true, trial_ends: '2026-12-01', coupon: null } });",
    '</script>',
  ].join('\n');
}

export type ServerLanguage = 'node' | 'csharp' | 'python';

/** PUT /api/public/widgets/{key}/contacts/{userId} from the website's backend, signed with the identity secret. */
export function serverSnippet(origin: string, publicKey: string, language: ServerLanguage): string {
  const url = `${origin}/api/public/widgets/${publicKey}/contacts/`;
  switch (language) {
    case 'csharp':
      return [
        'using System.Security.Cryptography;',
        'using System.Text;',
        '',
        'var body = """{"name":"Ada Lovelace","attributes":{"plan":"pro","seats":5}}""";',
        'var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();',
        'var signature = Convert.ToHexStringLower(HMACSHA256.HashData(',
        '    Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{userId}.{body}")));',
        `using var request = new HttpRequestMessage(HttpMethod.Put, "${url}" + Uri.EscapeDataString(userId))`,
        '{',
        '    Content = new StringContent(body, Encoding.UTF8, "application/json"),',
        '};',
        'request.Headers.Add("X-KA-Timestamp", timestamp);',
        'request.Headers.Add("X-KA-Signature", signature);',
        'var response = await http.SendAsync(request);',
      ].join('\n');
    case 'python':
      return [
        'import hashlib, hmac, json, time, urllib.parse, requests',
        '',
        "body = json.dumps({'name': 'Ada Lovelace', 'attributes': {'plan': 'pro', 'seats': 5}})",
        'timestamp = str(int(time.time()))',
        "signature = hmac.new(secret.encode(), f'{timestamp}.{user_id}.{body}'.encode(), hashlib.sha256).hexdigest()",
        `requests.put('${url}' + urllib.parse.quote(user_id, safe=''), data=body,`,
        "             headers={'Content-Type': 'application/json', 'X-KA-Timestamp': timestamp, 'X-KA-Signature': signature})",
      ].join('\n');
    default:
      return [
        "import { createHmac } from 'node:crypto';",
        '',
        "const body = JSON.stringify({ name: 'Ada Lovelace', attributes: { plan: 'pro', seats: 5 } });",
        'const timestamp = String(Math.floor(Date.now() / 1000));',
        "const signature = createHmac('sha256', process.env.KA_IDENTITY_SECRET).update(`${timestamp}.${userId}.${body}`).digest('hex');",
        `await fetch('${url}' + encodeURIComponent(userId), {`,
        "  method: 'PUT',",
        "  headers: { 'Content-Type': 'application/json', 'X-KA-Timestamp': timestamp, 'X-KA-Signature': signature },",
        '  body,',
        '});',
      ].join('\n');
  }
}
