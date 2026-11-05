// The API keeps the session in an HttpOnly cookie, so the portal never sees a token.

/** CSRF: another site can't set this header without a CORS preflight, so the API refuses changes without it. */
export const REQUEST_HEADERS: Readonly<Record<string, string>> = { 'X-KA-Request': '1' };

const listenersByStatus = new Map<number, Set<() => void>>([
  [401, new Set()],
  [403, new Set()],
]);

function subscribe(status: number, listener: () => void): () => void {
  const listeners = listenersByStatus.get(status)!;
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** Runs `listener` whenever the API answers 401, as when the session has expired. Returns the unsubscribe function. */
export function onUnauthorized(listener: () => void): () => void {
  return subscribe(401, listener);
}

/** Fires on any 403, such as when the identity provider took a role away. Returns the unsubscribe function. */
export function onForbidden(listener: () => void): () => void {
  return subscribe(403, listener);
}

export function checkSession(response: Response): void {
  for (const listener of [...(listenersByStatus.get(response.status) ?? [])]) {
    listener();
  }
}
