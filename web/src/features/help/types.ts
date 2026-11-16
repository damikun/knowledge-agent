import type { CitationDto } from '../chat/types';

export interface HelpSetupDto {
  chatModel: boolean;
  embeddingModel: boolean;
  sources: boolean;
  agent: boolean;
  widget: boolean;
}

export interface HelpStatusDto {
  enabled: boolean;
  canManage: boolean;
  modelReady: boolean;
  setup: HelpSetupDto | null;
}

export interface HelpHistoryMessage {
  role: 'user' | 'assistant';
  text: string;
}

export interface HelpChatRequest {
  message: string;
  page?: string;
  history?: HelpHistoryMessage[];
  sessionId?: string;
}

export interface HelpTurn {
  id: string;
  question: string;
  text: string;
  citations: CitationDto[];
  error: string | null;
}
