/** Shown in snippets while running locally, where a localhost address would mislead. */
export const PLACEHOLDER_ORIGIN = 'https://yourdomain.com';

const LOCAL_HOSTS = new Set(['localhost', '127.0.0.1', '[::1]', '::1']);
const MAX_LENGTH = 300;

/** Absolute http(s) with an optional base path; no user info, query, fragment or trailing slash. */
export function checkPublicAddress(value: string): { url: string | null; error: string | null } {
  const text = value.trim();
  if (text === '') {
    return { url: null, error: null };
  }
  if (text.length > MAX_LENGTH) {
    return { url: null, error: `Use at most ${MAX_LENGTH} characters.` };
  }
  let parsed: URL;
  try {
    parsed = new URL(text);
  } catch {
    return { url: null, error: 'Enter a full address that starts with https://, such as https://support.example.com.' };
  }
  if ((parsed.protocol !== 'https:' && parsed.protocol !== 'http:') || !parsed.hostname || !text.includes('://')) {
    return { url: null, error: 'Enter a full address that starts with https://, such as https://support.example.com.' };
  }
  if (parsed.username || parsed.password) {
    return { url: null, error: 'Leave out the user name and password.' };
  }
  if (text.includes('?') || text.includes('#')) {
    return { url: null, error: 'Leave out the part after ? or #. Only a path such as /support may follow the domain.' };
  }
  return { url: `${parsed.origin}${parsed.pathname}`.replace(/\/+$/, ''), error: null };
}

/** The saved public address, else this origin once reachable from outside, else a placeholder. */
export function publicOrigin(saved?: string | null): string {
  const configured = saved ? checkPublicAddress(saved).url : null;
  if (configured) {
    return configured;
  }
  if (typeof window === 'undefined') {
    return PLACEHOLDER_ORIGIN;
  }
  const { hostname, origin } = window.location;
  return LOCAL_HOSTS.has(hostname) || hostname.endsWith('.localhost') ? PLACEHOLDER_ORIGIN : origin;
}

export function usesPlaceholderOrigin(saved?: string | null): boolean {
  return publicOrigin(saved) === PLACEHOLDER_ORIGIN;
}
