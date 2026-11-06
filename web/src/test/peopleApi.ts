// Contacts in the shapes of /api/people, for page and accessibility tests.
import { widgetGuid } from './widgetsApi';

export const ADA_ID = '0199c1e2-0000-7000-8000-00000000ada1';
export const VISITOR_ID = '0199c1e2-0000-7000-8000-000000004f2a';
export const CONVERSATION_ID = '0199c1e2-0000-7000-8000-00000000c001';

export const ada = {
  id: ADA_ID,
  name: 'Ada Lovelace',
  email: 'ada@example.com',
  externalId: 'user-42',
  identified: true,
  firstSeenAt: '2026-10-01T08:00:00Z',
  lastSeenAt: '2026-11-04T09:00:00Z',
  conversationCount: 3,
  widgets: [{ id: widgetGuid(1), name: 'Shop help' }],
  sessionCount: 4,
  returning: true,
  language: 'en-GB',
  lastPageUrl: 'https://shop.example.com/pricing',
  attributes: { plan: 'pro', seats: 5 },
};

export const visitor = {
  id: VISITOR_ID,
  name: 'Visitor 4F2A',
  email: null,
  externalId: null,
  identified: false,
  firstSeenAt: '2026-11-02T08:00:00Z',
  lastSeenAt: '2026-11-03T09:00:00Z',
  conversationCount: 1,
  widgets: [],
};

export const adaProfile = {
  ...ada,
  savedName: 'Ada Lovelace',
  notes: 'Prefers email.',
  attributes: [
    { key: 'plan', label: 'Plan', type: 'string', value: 'pro', source: 'server', updatedAt: '2026-11-01T08:00:00Z', visible: true },
    { key: 'beta', label: 'Beta', type: 'boolean', value: true, source: 'browser', updatedAt: '2026-11-02T08:00:00Z', visible: true },
  ],
  widgets: [{ id: widgetGuid(1), name: 'Shop help', firstSeenAt: '2026-10-01T08:00:00Z', lastSeenAt: '2026-11-04T09:00:00Z' }],
  stats: { conversations: 3, handovers: 1, averageRating: 4.5, ratings: 2, lastSeenAt: '2026-11-04T09:00:00Z' },
};

export const registry = {
  items: [
    {
      key: 'plan',
      label: 'Plan',
      type: 'string',
      firstSeenAt: '2026-10-01T08:00:00Z',
      lastSeenAt: '2026-11-04T08:00:00Z',
      contacts: 120,
      visible: true,
      exposeToAgent: false,
      trust: 'any',
    },
    {
      key: 'seats',
      label: 'Seats',
      type: 'number',
      firstSeenAt: '2026-10-01T08:00:00Z',
      lastSeenAt: '2026-11-04T08:00:00Z',
      contacts: 80,
      visible: true,
      exposeToAgent: false,
      trust: 'any',
    },
  ],
  builtIn: [
    { key: 'identified', label: 'Identified', type: 'boolean', builtIn: true, ops: ['true', 'false'] },
    { key: 'last_seen', label: 'Last seen', type: 'date', builtIn: true, ops: ['before', 'after', 'within_days'] },
  ],
  ops: { string: ['is', 'is_not', 'contains', 'set', 'not_set'], number: ['eq', 'neq', 'gt', 'lt', 'between', 'set', 'not_set'] },
};

export const SEGMENT_ID = '0199c1e2-0000-7000-8000-0000000005e9';
export const proSegment = {
  id: SEGMENT_ID,
  name: 'Pro customers',
  description: null,
  filter: [{ attr: 'plan', op: 'is', value: 'pro' }],
  color: 'teal',
  icon: 'users',
  shared: true,
  mine: true,
  count: 120,
};

export const conversationsPage = {
  items: [
    {
      id: CONVERSATION_ID,
      title: 'Return an order',
      status: 'closed',
      widgetId: widgetGuid(1),
      updatedAt: '2026-11-04T09:00:00Z',
      messageCount: 4,
      preview: 'Thanks!',
      handoverStatus: 'resolved',
      rating: 5,
    },
  ],
  nextCursor: null,
};
