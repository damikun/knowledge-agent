export function personPath(id: string): string {
  return `/contacts/${encodeURIComponent(id)}`;
}

export function personConversationsPath(id: string, conversationId?: string): string {
  const open = conversationId ? `&c=${encodeURIComponent(conversationId)}` : '';
  return `/inbox?person=${encodeURIComponent(id)}${open}`;
}
