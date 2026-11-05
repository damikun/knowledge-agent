import { useQuery } from '@tanstack/react-query';
import { asStrings, isRecord, request } from '../../lib/api';
import type {
  EmailSettingsDto,
  IntegrationsDto,
  IntegrationTestResultDto,
  MailSecurity,
  SaveEmailSettingsRequest,
  SaveSlackSettingsRequest,
  SlackSettingsDto,
} from './types';

export const integrationKeys = {
  all: ['integrations'] as const,
};

const SECURITY: readonly MailSecurity[] = ['auto', 'none', 'starttls', 'ssl'];

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null;
}

function port(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function security(value: unknown): MailSecurity {
  return SECURITY.find((choice) => choice === value) ?? 'auto';
}

function toEmailSettings(value: unknown): EmailSettingsDto {
  const dto = isRecord(value) ? value : {};
  return {
    enabled: dto.enabled === true,
    smtpHost: text(dto.smtpHost),
    smtpPort: port(dto.smtpPort, 587),
    smtpSecurity: security(dto.smtpSecurity),
    smtpUser: text(dto.smtpUser),
    hasSmtpPassword: dto.hasSmtpPassword === true,
    fromAddress: text(dto.fromAddress),
    fromName: text(dto.fromName),
    supportAddress: text(dto.supportAddress),
    replyAddress: text(dto.replyAddress),
    plusAddressing: dto.plusAddressing !== false,
    imapHost: text(dto.imapHost),
    imapPort: port(dto.imapPort, 993),
    imapSecurity: security(dto.imapSecurity),
    imapUser: text(dto.imapUser),
    hasImapPassword: dto.hasImapPassword === true,
    processedFolder: text(dto.processedFolder) ?? 'Processed',
    staffAddresses: asStrings(dto.staffAddresses),
    staffDomains: asStrings(dto.staffDomains),
    portalUrl: text(dto.portalUrl),
    canSend: dto.canSend === true,
    canReceive: dto.canReceive === true,
    saved: dto.saved === true,
  };
}

function toSlackSettings(value: unknown): SlackSettingsDto {
  const dto = isRecord(value) ? value : {};
  return {
    enabled: dto.enabled === true,
    hasBotToken: dto.hasBotToken === true,
    hasAppToken: dto.hasAppToken === true,
    channelId: text(dto.channelId),
    canPost: dto.canPost === true,
    canReceive: dto.canReceive === true,
    saved: dto.saved === true,
    connected: dto.connected === true,
  };
}

function toTestResult(value: unknown): IntegrationTestResultDto {
  const dto = isRecord(value) ? value : {};
  return { ok: dto.ok === true, message: typeof dto.message === 'string' ? dto.message : '', details: asStrings(dto.details) };
}

export const integrationsApi = {
  get: async (signal?: AbortSignal): Promise<IntegrationsDto> => {
    const dto = await request<unknown>('/api/integrations', { signal });
    const value = isRecord(dto) ? dto : {};
    return { email: toEmailSettings(value.email), slack: toSlackSettings(value.slack) };
  },
  saveEmail: async (body: SaveEmailSettingsRequest) => toEmailSettings(await request<unknown>('/api/integrations/email', { method: 'PUT', body })),
  saveSlack: async (body: SaveSlackSettingsRequest) => toSlackSettings(await request<unknown>('/api/integrations/slack', { method: 'PUT', body })),
  /** `to` overrides the support address; null with no support address only tests the mail server sign-in. */
  testEmail: async (to: string | null) => toTestResult(await request<unknown>('/api/integrations/email/test', { method: 'POST', body: { to } })),
  testSlack: async () => toTestResult(await request<unknown>('/api/integrations/slack/test', { method: 'POST' })),
};

/** Admin only. */
export function useIntegrations(enabled = true) {
  return useQuery({ queryKey: integrationKeys.all, queryFn: ({ signal }) => integrationsApi.get(signal), staleTime: 30_000, enabled });
}
