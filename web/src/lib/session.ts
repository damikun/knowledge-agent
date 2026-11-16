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

export function onUnauthorized(listener: () => void): () => void {
  return subscribe(401, listener);
}

export function onForbidden(listener: () => void): () => void {
  return subscribe(403, listener);
}

export function checkSession(response: Response): void {
  for (const listener of [...(listenersByStatus.get(response.status) ?? [])]) {
    listener();
  }
}
