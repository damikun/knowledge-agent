import type { FeedbackDto, FeedbackSummaryDto } from '../features/feedback/types';
import { agentGuid } from './agentsApi';
import { CONVERSATION_ID } from './fixtures';
import { WIDGET_CONVERSATION_ID } from './inboxApi';
import { widgetGuid } from './widgetsApi';

/** The summary of the "Shop help" widget with the website helper agent. */
export function feedbackSummary(overrides: Partial<FeedbackSummaryDto> = {}): FeedbackSummaryDto {
  return {
    widgetId: widgetGuid(1),
    agentId: agentGuid(2),
    answers: 20,
    positive: 15,
    negative: 5,
    positiveRatio: 0.75,
    ratings: 4,
    averageRating: 4.25,
    agentRecorded: 2,
    ...overrides,
  };
}

/** A visitor's thumbs down on an answer of a widget conversation. */
export function feedbackDto(overrides: Partial<FeedbackDto> = {}): FeedbackDto {
  return {
    id: '0199c1e2-0000-7000-8000-0000000f0001',
    conversationId: WIDGET_CONVERSATION_ID,
    messageId: 'message-2',
    kind: 'answer',
    rating: -1,
    comment: 'It did not answer my question.',
    reasons: ['incomplete'],
    author: 'visitor',
    widgetId: widgetGuid(1),
    agentId: agentGuid(2),
    traceId: null,
    createdAt: '2026-10-31T09:10:00Z',
    conversationTitle: 'Return an order',
    messageText: 'Worn shoes cannot be returned [S1].',
    ...overrides,
  };
}

/** A staff member's five stars for a portal conversation with the built-in agent. */
export const portalRating = feedbackDto({
  id: '0199c1e2-0000-7000-8000-0000000f0002',
  conversationId: CONVERSATION_ID,
  messageId: null,
  kind: 'conversation',
  rating: 5,
  comment: null,
  reasons: [],
  author: 'staff',
  widgetId: null,
  agentId: null,
  conversationTitle: 'Vacation policy',
  messageText: null,
});
