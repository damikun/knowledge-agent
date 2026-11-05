import type { BadgeTone } from '../../components/ui/Badge';
import { shortId } from '../../lib/format';
import type { ConversationStatus } from '../chat/types';

export const STATUS_LABELS: Readonly<Record<ConversationStatus, string>> = {
  handover_pending: 'Waiting for a person',
  human: 'With a person',
  ai: 'Assistant',
  closed: 'Closed',
};

export const STATUS_TONES: Readonly<Record<ConversationStatus, BadgeTone>> = {
  handover_pending: 'warning',
  human: 'accent',
  ai: 'neutral',
  closed: 'neutral',
};

/** Support users can't read widget names, so widgets show by id. */
export function widgetLabel(widgetId: string | null, names: ReadonlyMap<string, string> | undefined): string {
  if (!widgetId) {
    return 'No widget';
  }
  return names?.get(widgetId.toLowerCase()) ?? `Widget ${shortId(widgetId)}`;
}
