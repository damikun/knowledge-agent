import type { Guid, IsoDateTime } from '../../lib/types';

export interface WidgetAppearance {
  title: string;
  subtitle: string | null;
  avatarAssetId: Guid | null;
  /** A lucide icon name, unless an uploaded image replaces it. */
  launcherIcon: string;
  launcherAssetId: Guid | null;
  /** #rrggbb */
  brandColor: string;
  /** auto, light or dark. The portal calls this the mode, and the preset the theme. */
  theme: string;
  preset: string;
  colors: WidgetColors;
  position: string;
  launcherLabel: string | null;
  showFooter: boolean;
}

/** Overrides of the preset's colors, by token key, as #rrggbb. */
export interface WidgetColors {
  light: Record<string, string>;
  dark: Record<string, string>;
}

export interface WidgetBehavior {
  greeting: string | null;
  suggestedQuestions: string[];
  placeholder: string | null;
  citationMode: string;
  feedbackEnabled: boolean;
  handoverEnabled: boolean;
  contactCapture: string;
  /** Replaces the widget's own texts, by text key. */
  texts: Record<string, string>;
}

interface WidgetSecurity {
  /** Sites that may embed the widget; empty means any site. */
  allowedOrigins: string[];
  messagesPerMinute: number;
  dailyMessageCap: number;
  maxMessageLength: number;
  retentionDays: number;
  /** Only users the host website identified with a valid user hash may chat. */
  requireVerifiedUsers: boolean;
}

export type Weekday = 'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday';

export interface WidgetOpeningHours {
  day: Weekday;
  /** "09:00:00" */
  opens: string;
  closes: string;
}

interface WidgetAvailability {
  always: boolean;
  /** IANA, such as Europe/Prague. */
  timeZone: string;
  hours: WidgetOpeningHours[];
}

interface WidgetHandover {
  channel: string;
  /** Empty uses the support address of the email integration. */
  emailTo: string[];
  /** Empty uses the channel of the Slack integration. */
  slackChannelId: string | null;
  escalateAfterMinutes: number;
  /** How long a request waits for a person; then the visitor goes on with the assistant. */
  waitMinutes: number;
  availability: WidgetAvailability;
}

interface WidgetConversations {
  closeIdle: boolean;
  /** With the assistant, after the last message. */
  aiIdleMinutes: number;
  /** With a person, after the team's last reply; never while the visitor waits for an answer. */
  humanIdleHours: number;
}

export interface WidgetSettings {
  appearance: WidgetAppearance;
  behavior: WidgetBehavior;
  security: WidgetSecurity;
  handover: WidgetHandover;
  conversations: WidgetConversations;
}

/** GET /api/widgets */
export interface WidgetDto {
  id: Guid;
  publicKey: string;
  name: string;
  agentId: Guid;
  enabled: boolean;
  settings: WidgetSettings;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  /** The secret itself is only returned once, when it is created. */
  identityVerification: boolean;
  identitySecretCreatedAt: IsoDateTime | null;
}

/** POST /api/widgets/{id}/identity-secret: shown once. */
export interface WidgetIdentitySecretDto {
  secret: string;
  createdAt: IsoDateTime;
}

/** GET /api/widgets/options: the choices of every setting and the defaults of a new widget. */
export interface WidgetOptionsDto {
  launcherIcons: string[];
  themes: string[];
  presets: string[];
  colorTokens: string[];
  positions: string[];
  citationModes: string[];
  contactCaptures: string[];
  handoverChannels: string[];
  textKeys: string[];
  defaults: WidgetSettings;
}

/** Body of POST and PUT /api/widgets; PUT replaces the whole widget. */
export interface SaveWidgetRequest {
  name: string;
  agentId: Guid;
  enabled: boolean;
  settings: WidgetSettings;
}

/** POST /api/widgets/{id}/assets */
export interface WidgetAssetDto {
  id: Guid;
  /** Such as /widget/v1/media/{id}. */
  url: string;
  contentType: string;
  size: number;
}

export interface WidgetPreviewConfig {
  key: string;
  title: string;
  subtitle: string | null;
  avatarUrl: string | null;
  launcherIcon: string;
  launcherIconUrl: string | null;
  brandColor: string;
  theme: string;
  preset: string;
  colors: WidgetColors;
  showFooter: boolean;
  position: string;
  launcherLabel: string | null;
  greeting: string | null;
  suggestedQuestions: string[];
  placeholder: string | null;
  citationMode: string;
  feedbackEnabled: boolean;
  handoverEnabled: boolean;
  contactCapture: string;
  texts: Record<string, string>;
  maxMessageLength: number;
}
