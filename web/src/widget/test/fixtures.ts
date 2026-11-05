// Builders for the public widget API in widget tests (/api/public/widgets/{key}).

export const WIDGET_KEY = 'wk_test';
export const WIDGET_API = `/api/public/widgets/${WIDGET_KEY}`;

export function widgetConfig(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    key: WIDGET_KEY,
    title: 'Shop help',
    subtitle: 'Answers in seconds',
    avatarUrl: null,
    launcherIcon: 'message-circle',
    launcherIconUrl: null,
    brandColor: '#7c3aed',
    theme: 'light',
    preset: 'glow',
    colors: { light: {}, dark: {} },
    showFooter: true,
    position: 'right',
    launcherLabel: null,
    greeting: 'Hi! How can I help you today?',
    suggestedQuestions: ['Can I return items?', 'Where is my order?'],
    placeholder: 'Ask about your order',
    citationMode: 'titles',
    feedbackEnabled: true,
    handoverEnabled: false,
    contactCapture: 'never',
    texts: {},
    maxMessageLength: 2000,
    ...overrides,
  };
}

export function visitorSession(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    token: 'visitor-token',
    visitorId: 'visitor-1',
    conversationId: null,
    expiresAt: '2099-01-01T00:00:00Z',
    name: null,
    email: null,
    ...overrides,
  };
}

export function visitorMessage(sequence: number, author: string, text: string, overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: `message-${sequence}`,
    sequence,
    author,
    authorName: null,
    text,
    citations: [],
    createdAt: '2026-10-31T10:00:00Z',
    rating: null,
    ...overrides,
  };
}

export function visitorConversation(messages: Array<Record<string, unknown>>, overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return { id: 'c1', status: 'ai', rating: null, messages, ...overrides };
}

// A citation as tool-result events send it.
export function streamCitation(ref: string, sourceTitle: string, sourceUrl: string | null = null): Record<string, unknown> {
  return {
    ref,
    sourceId: '00000000-0000-0000-0000-000000000000',
    chunkId: '00000000-0000-0000-0000-000000000000',
    sourceTitle,
    sourceUrl,
    page: null,
    snippet: 'The return window is thirty days.',
    score: 0,
  };
}

export function problem(status: number, body: Record<string, unknown>, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify({ status, ...body }), { status, headers: { 'Content-Type': 'application/problem+json', ...headers } });
}
