export type MailSecurity = 'auto' | 'none' | 'starttls' | 'ssl';

/** Passwords are never returned, only whether they are set. */
export interface EmailSettingsDto {
  enabled: boolean;
  smtpHost: string | null;
  smtpPort: number;
  smtpSecurity: MailSecurity;
  smtpUser: string | null;
  hasSmtpPassword: boolean;
  fromAddress: string | null;
  fromName: string | null;
  /** Used when the widget names no address. */
  supportAddress: string | null;
  replyAddress: string | null;
  plusAddressing: boolean;
  imapHost: string | null;
  imapPort: number;
  imapSecurity: MailSecurity;
  imapUser: string | null;
  hasImapPassword: boolean;
  processedFolder: string;
  staffAddresses: string[];
  staffDomains: string[];
  /** Used in links in the emails. */
  portalUrl: string | null;
  canSend: boolean;
  canReceive: boolean;
  /** Saved in the portal; otherwise configuration applies. */
  saved: boolean;
}

export interface SlackSettingsDto {
  enabled: boolean;
  hasBotToken: boolean;
  hasAppToken: boolean;
  channelId: string | null;
  canPost: boolean;
  canReceive: boolean;
  saved: boolean;
  /** The Socket Mode connection is up. */
  connected: boolean;
}

export interface IntegrationsDto {
  email: EmailSettingsDto;
  slack: SlackSettingsDto;
}

/** Secrets: null keeps the saved one, "" removes it, a value replaces it. */
export interface SaveEmailSettingsRequest {
  enabled: boolean;
  smtpHost: string | null;
  smtpPort: number;
  smtpSecurity: MailSecurity;
  smtpUser: string | null;
  smtpPassword: string | null;
  fromAddress: string | null;
  fromName: string | null;
  supportAddress: string | null;
  replyAddress: string | null;
  plusAddressing: boolean;
  imapHost: string | null;
  imapPort: number;
  imapSecurity: MailSecurity;
  imapUser: string | null;
  imapPassword: string | null;
  processedFolder: string | null;
  staffAddresses: string[];
  staffDomains: string[];
  portalUrl: string | null;
}

export interface SaveSlackSettingsRequest {
  enabled: boolean;
  botToken: string | null;
  appToken: string | null;
  channelId: string | null;
}

/** Ok only when the test reached the server and nothing is missing. */
export interface IntegrationTestResultDto {
  ok: boolean;
  message: string;
  details: string[];
}
