import type { Guid, IsoDateTime } from '../../lib/types';

export interface WidgetAppearance {
  title: string;
  subtitle: string | null;
  avatarAssetId: Guid | null;
  launcherIcon: string;
  launcherAssetId: Guid | null;
  brandColor: string;
  theme: string;
  preset: string;
  colors: WidgetColors;
  position: string;
  launcherLabel: string | null;
  showFooter: boolean;
}

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
  texts: Record<string, string>;
}

interface WidgetSecurity {
  allowedOrigins: string[];
  messagesPerMinute: number;
  dailyMessageCap: number;
  maxMessageLength: number;
  retentionDays: number;
  requireVerifiedUsers: boolean;
}

export type Weekday = 'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday';

export interface WidgetOpeningHours {
  day: Weekday;
  opens: string;
  closes: string;
}

interface WidgetAvailability {
  always: boolean;
  timeZone: string;
  hours: WidgetOpeningHours[];
}

interface WidgetHandover {
  channel: string;
  emailTo: string[];
  slackChannelId: string | null;
  escalateAfterMinutes: number;
  waitMinutes: number;
  availability: WidgetAvailability;
}

interface WidgetConversations {
  closeIdle: boolean;
  aiIdleMinutes: number;
  humanIdleHours: number;
}

export interface WidgetSettings {
  appearance: WidgetAppearance;
  behavior: WidgetBehavior;
  security: WidgetSecurity;
  handover: WidgetHandover;
  conversations: WidgetConversations;
}

export interface WidgetDto {
  id: Guid;
  publicKey: string;
  name: string;
  agentId: Guid;
  enabled: boolean;
  settings: WidgetSettings;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  identityVerification: boolean;
  identitySecretCreatedAt: IsoDateTime | null;
}

export interface WidgetIdentitySecretDto {
  secret: string;
  createdAt: IsoDateTime;
}

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

export interface SaveWidgetRequest {
  name: string;
  agentId: Guid;
  enabled: boolean;
  settings: WidgetSettings;
}

export interface WidgetAssetDto {
  id: Guid;
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
  linkHosts?: string[];
}
