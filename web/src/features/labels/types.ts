import type { Guid, IsoDateTime } from '../../lib/types';

export const LABEL_COLORS = ['gray', 'blue', 'green', 'amber', 'red', 'purple', 'teal', 'pink'] as const;

export type LabelColor = (typeof LABEL_COLORS)[number];

export interface Label {
  id: Guid;
  name: string;
  color: LabelColor;
  description: string;
  autoApply: boolean;
  conversationCount: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface ConversationLabel {
  id: Guid;
  name: string;
  color: LabelColor;
  source: 'manual' | 'auto';
}

export interface LabelInput {
  name: string;
  color: LabelColor;
  description: string;
  autoApply: boolean;
}
