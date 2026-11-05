import type { EmailSettingsDto, IntegrationsDto, SlackSettingsDto } from '../features/notifications/types';
import { jsonResponse, type FetchHandler } from './utils';

/** Email that is off and not saved in the portal, as on a new installation. */
export function emailSettings(overrides: Partial<EmailSettingsDto> = {}): EmailSettingsDto {
  return {
    enabled: false,
    smtpHost: null,
    smtpPort: 587,
    smtpSecurity: 'auto',
    smtpUser: null,
    hasSmtpPassword: false,
    fromAddress: null,
    fromName: null,
    supportAddress: null,
    replyAddress: null,
    plusAddressing: true,
    imapHost: null,
    imapPort: 993,
    imapSecurity: 'auto',
    imapUser: null,
    hasImapPassword: false,
    processedFolder: 'Processed',
    staffAddresses: [],
    staffDomains: [],
    portalUrl: null,
    canSend: false,
    canReceive: false,
    saved: false,
    ...overrides,
  };
}

/** Email that sends and reads replies, saved in the portal with both passwords. */
export const workingEmail = emailSettings({
  enabled: true,
  smtpHost: 'smtp.example.com',
  smtpUser: 'mailer',
  hasSmtpPassword: true,
  fromAddress: 'support@example.com',
  fromName: 'Example support',
  supportAddress: 'team@example.com',
  replyAddress: 'box@example.com',
  imapHost: 'imap.example.com',
  imapUser: 'box@example.com',
  hasImapPassword: true,
  staffDomains: ['example.com'],
  portalUrl: 'https://support.example.com',
  canSend: true,
  canReceive: true,
  saved: true,
});

export function slackSettings(overrides: Partial<SlackSettingsDto> = {}): SlackSettingsDto {
  return {
    enabled: false,
    hasBotToken: false,
    hasAppToken: false,
    channelId: null,
    canPost: false,
    canReceive: false,
    saved: false,
    connected: false,
    ...overrides,
  };
}

export const workingSlack = slackSettings({
  enabled: true,
  hasBotToken: true,
  hasAppToken: true,
  channelId: 'C0123456789',
  canPost: true,
  canReceive: true,
  saved: true,
  connected: true,
});

/** mockFetch route for GET /api/integrations; the getter is read per request. */
export function integrationsRoutes(get: () => IntegrationsDto): Record<string, FetchHandler> {
  return { 'GET /api/integrations': () => jsonResponse(get()) };
}
