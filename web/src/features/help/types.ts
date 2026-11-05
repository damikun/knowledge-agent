import type { CitationDto } from '../chat/types';

/** Computed by the API, so support users get it too. */
export interface HelpSetupDto {
  chatModel: boolean;
  embeddingModel: boolean;
  sources: boolean;
  agent: boolean;
  widget: boolean;
}

/** Setup is null while help is off; help is disabled while modelReady is false. */
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

/** Kept in session storage only. */
export interface HelpTurn {
  id: string;
  question: string;
  text: string;
  citations: CitationDto[];
  error: string | null;
}
