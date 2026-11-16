export type MailSecurity = 'auto' | 'none' | 'starttls' | 'ssl';

export interface EmailSettingsDto {
  enabled: boolean;
  smtpHost: string | null;
  smtpPort: number;
  smtpSecurity: MailSecurity;
  smtpUser: string | null;
  hasSmtpPassword: boolean;
  fromAddress: string | null;
  fromName: string | null;
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
  portalUrl: string | null;
  canSend: boolean;
  canReceive: boolean;
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
  connected: boolean;
}

export interface IntegrationsDto {
  email: EmailSettingsDto;
  slack: SlackSettingsDto;
}

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

export interface IntegrationTestResultDto {
  ok: boolean;
  message: string;
  details: string[];
}
