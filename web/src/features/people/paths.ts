export function personPath(id: string): string {
  return `/contacts/${encodeURIComponent(id)}`;
}

/** Names no view, so the contact is the list's context rather than the All view. */
export function personConversationsPath(id: string, conversationId?: string): string {
  const open = conversationId ? `&c=${encodeURIComponent(conversationId)}` : '';
  return `/inbox?person=${encodeURIComponent(id)}${open}`;
}
