import type { JsonObject, JsonValue } from '../../lib/types';
import type { ModelSettingsDto } from '../models/types';

export interface PromptMessageDto {
  role: string;
  content: string;
}

type PromptOrigin = 'langfuse' | 'local';
export type PromptType = 'text' | 'chat';

export interface PromptSummaryDto {
  name: string;
  origin: PromptOrigin;
  version: number;
  type: PromptType;
  labels: string[];
  variables: string[];
  externalUrl: string | null;
}

export interface PromptDetailDto {
  name: string;
  origin: PromptOrigin;
  version: number;
  type: PromptType;
  text: string | null;
  messages: PromptMessageDto[];
  config: JsonObject | null;
  labels: string[];
  variables: string[];
  externalUrl: string | null;
}

export interface RenderPromptRequest {
  variables?: Record<string, JsonValue> | null;
  label?: string | null;
  version?: number | null;
}

export interface RenderedPromptDto {
  name: string;
  origin: PromptOrigin;
  version: number;
  type: PromptType;
  text: string | null;
  messages: PromptMessageDto[];
  config: JsonObject | null;
  profile: string | null;
  modelOverride: ModelSettingsDto | null;
}

export interface RenderTemplateRequest {
  template: string;
  variables?: Record<string, JsonValue> | null;
}

export interface RenderTemplateResponse {
  output: string;
  variables: string[];
}

export interface PromptVersionDto {
  version: number;
  type: PromptType;
  text: string | null;
  messages: PromptMessageDto[];
  config: JsonObject | null;
  labels: string[];
  commitMessage: string | null;
  live: boolean;
}

export interface PromptVersionsDto {
  name: string;
  editable: boolean;
  label: string;
  liveVersion: number | null;
  totalVersions: number;
  versions: PromptVersionDto[];
}

export interface SavePromptDraftRequest {
  text?: string | null;
  messages?: PromptMessageDto[] | null;
  commitMessage?: string | null;
  baseVersion?: number | null;
}

export interface PublishPromptRequest {
  version: number;
  expectedLiveVersion?: number | null;
}

export interface PublishPromptResponse {
  name: string;
  label: string;
  version: number;
  previousVersion: number | null;
}
