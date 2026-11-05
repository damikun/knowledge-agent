import type { Guid, IsoDateTime } from '../../lib/types';

/** Must match LabelColors on the server. */
export const LABEL_COLORS = ['gray', 'blue', 'green', 'amber', 'red', 'purple', 'teal', 'pink'] as const;

export type LabelColor = (typeof LABEL_COLORS)[number];

export interface Label {
  id: Guid;
  name: string;
  color: LabelColor;
  /** The auto-labeling reads it. */
  description: string;
  /** The auto-labeling may apply it. */
  autoApply: boolean;
  conversationCount: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

/** manual: set by staff; auto: set by the auto-labeling. */
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
