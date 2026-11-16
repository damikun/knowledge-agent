let counter = 0;

export function createClientId(): string {
  counter += 1;
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return `local-${crypto.randomUUID()}`;
  }
  return `local-${Date.now().toString(36)}-${counter}-${Math.random().toString(36).slice(2, 8)}`;
}

export function sameId(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}
