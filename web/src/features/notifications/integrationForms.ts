import { NO_SECRET, secretRequest, type SecretValue } from '../../components/ui/SecretField';
import type { EmailSettingsDto, MailSecurity, SaveEmailSettingsRequest, SaveSlackSettingsRequest, SlackSettingsDto } from './types';

export const MAIL_SECURITY_LABELS: Readonly<Record<MailSecurity, string>> = {
  auto: 'Automatic',
  none: 'None',
  starttls: 'STARTTLS',
  ssl: 'SSL/TLS',
};

export function splitList(value: string): string[] {
  return value
    .split(/[\n,]/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function optional(value: string): string | null {
  return value.trim() || null;
}

function parsePort(value: string): number | null {
  const trimmed = value.trim();
  if (!/^\d+$/.test(trimmed)) {
    return null;
  }
  const port = Number.parseInt(trimmed, 10);
  return port >= 1 && port <= 65535 ? port : null;
}

export interface EmailFormValues {
  enabled: boolean;
  smtpHost: string;
  smtpPort: string;
  smtpSecurity: MailSecurity;
  smtpUser: string;
  smtpPassword: SecretValue;
  fromAddress: string;
  fromName: string;
  supportAddress: string;
  replyAddress: string;
  plusAddressing: boolean;
  imapHost: string;
  imapPort: string;
  imapSecurity: MailSecurity;
  imapUser: string;
  imapPassword: SecretValue;
  processedFolder: string;
  staffAddresses: string;
  staffDomains: string;
  /** Not editable; sent back unchanged so an existing value stays as the fallback. */
  portalUrl: string;
}

export type EmailField = Exclude<keyof SaveEmailSettingsRequest, 'enabled' | 'plusAddressing'>;
export type EmailFormErrors = Partial<Record<EmailField, string>>;

export const EMAIL_FIELDS: readonly EmailField[] = [
  'smtpHost',
  'smtpPort',
  'smtpSecurity',
  'smtpUser',
  'smtpPassword',
  'fromAddress',
  'fromName',
  'supportAddress',
  'replyAddress',
  'imapHost',
  'imapPort',
  'imapSecurity',
  'imapUser',
  'imapPassword',
  'processedFolder',
  'staffAddresses',
  'staffDomains',
];

export function initialEmailValues(email: EmailSettingsDto): EmailFormValues {
  return {
    enabled: email.enabled,
    smtpHost: email.smtpHost ?? '',
    smtpPort: String(email.smtpPort),
    smtpSecurity: email.smtpSecurity,
    smtpUser: email.smtpUser ?? '',
    smtpPassword: NO_SECRET,
    fromAddress: email.fromAddress ?? '',
    fromName: email.fromName ?? '',
    supportAddress: email.supportAddress ?? '',
    replyAddress: email.replyAddress ?? '',
    plusAddressing: email.plusAddressing,
    imapHost: email.imapHost ?? '',
    imapPort: String(email.imapPort),
    imapSecurity: email.imapSecurity,
    imapUser: email.imapUser ?? '',
    imapPassword: NO_SECRET,
    processedFolder: email.processedFolder,
    staffAddresses: email.staffAddresses.join('\n'),
    staffDomains: email.staffDomains.join('\n'),
    portalUrl: email.portalUrl ?? '',
  };
}

export function toEmailRequest(values: EmailFormValues): { request: SaveEmailSettingsRequest | null; errors: EmailFormErrors } {
  const errors: EmailFormErrors = {};
  const smtpPort = parsePort(values.smtpPort);
  const imapPort = parsePort(values.imapPort);
  if (smtpPort === null) {
    errors.smtpPort = 'Enter a port from 1 to 65535.';
  }
  if (imapPort === null) {
    errors.imapPort = 'Enter a port from 1 to 65535.';
  }
  // The API needs both to send email; checked here so the message sits next to the empty field.
  if (values.enabled && !values.smtpHost.trim()) {
    errors.smtpHost = 'Enter the SMTP server to send email.';
  }
  if (values.enabled && !values.fromAddress.trim()) {
    errors.fromAddress = 'Enter the sender address.';
  }
  if (smtpPort === null || imapPort === null || Object.keys(errors).length > 0) {
    return { request: null, errors };
  }
  return {
    request: {
      enabled: values.enabled,
      smtpHost: optional(values.smtpHost),
      smtpPort,
      smtpSecurity: values.smtpSecurity,
      smtpUser: optional(values.smtpUser),
      smtpPassword: secretRequest(values.smtpPassword),
      fromAddress: optional(values.fromAddress),
      fromName: optional(values.fromName),
      supportAddress: optional(values.supportAddress),
      replyAddress: optional(values.replyAddress),
      plusAddressing: values.plusAddressing,
      imapHost: optional(values.imapHost),
      imapPort,
      imapSecurity: values.imapSecurity,
      imapUser: optional(values.imapUser),
      imapPassword: secretRequest(values.imapPassword),
      processedFolder: optional(values.processedFolder),
      staffAddresses: splitList(values.staffAddresses),
      staffDomains: splitList(values.staffDomains),
      portalUrl: optional(values.portalUrl),
    },
    errors,
  };
}

export interface SlackFormValues {
  enabled: boolean;
  botToken: SecretValue;
  appToken: SecretValue;
  channelId: string;
}

export type SlackField = Exclude<keyof SaveSlackSettingsRequest, 'enabled'>;
export type SlackFormErrors = Partial<Record<SlackField, string>>;
export const SLACK_FIELDS: readonly SlackField[] = ['botToken', 'appToken', 'channelId'];

const SLACK_CHANNEL = /^[CG][A-Z0-9]{6,}$/;

export function initialSlackValues(slack: SlackSettingsDto): SlackFormValues {
  return { enabled: slack.enabled, botToken: NO_SECRET, appToken: NO_SECRET, channelId: slack.channelId ?? '' };
}

export function toSlackRequest(values: SlackFormValues, slack: SlackSettingsDto): { request: SaveSlackSettingsRequest | null; errors: SlackFormErrors } {
  const errors: SlackFormErrors = {};
  const botToken = secretRequest(values.botToken);
  const appToken = secretRequest(values.appToken);
  const channelId = values.channelId.trim();
  if (botToken && !botToken.startsWith('xoxb-')) {
    errors.botToken = 'The bot token starts with xoxb-.';
  }
  if (appToken && !appToken.startsWith('xapp-')) {
    errors.appToken = 'The app-level token starts with xapp-.';
  }
  if (channelId && !SLACK_CHANNEL.test(channelId)) {
    errors.channelId = 'Use the channel id, such as C0123456789.';
  }
  const hasBotToken = botToken === null ? slack.hasBotToken : botToken !== '';
  if (values.enabled && !hasBotToken && !errors.botToken) {
    errors.botToken = 'Slack needs the bot token.';
  }
  if (values.enabled && !channelId && !errors.channelId) {
    errors.channelId = 'Slack needs a channel.';
  }
  if (Object.keys(errors).length > 0) {
    return { request: null, errors };
  }
  return { request: { enabled: values.enabled, botToken, appToken, channelId: channelId || null }, errors };
}
