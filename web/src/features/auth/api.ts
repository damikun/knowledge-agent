import { useQuery } from '@tanstack/react-query';
import { ApiError, asStrings, buildUrl, isRecord, request } from '../../lib/api';
import type { CurrentUserDto, Role } from './types';

export const authKeys = {
  me: ['auth', 'me'] as const,
};

/** Set by the API when a sign-in fails or is cancelled at the identity provider. */
export const AUTH_ERROR_PARAM = 'authError';

// Roles are checked with includes(), so a malformed answer must not take the whole portal down.
function toCurrentUser(value: unknown): CurrentUserDto {
  const dto = isRecord(value) ? value : {};
  return {
    name: typeof dto.name === 'string' && dto.name.trim() ? dto.name.trim() : 'Unknown user',
    email: typeof dto.email === 'string' && dto.email.trim() ? dto.email.trim() : null,
    roles: asStrings(dto.roles),
    mode: dto.mode === 'none' ? 'none' : 'oidc',
    id: typeof dto.id === 'string' && dto.id ? dto.id : null,
    canManageUsers: dto.canManageUsers === true,
  };
}

const authApi = {
  me: async (signal?: AbortSignal): Promise<CurrentUserDto | null> => {
    try {
      return toCurrentUser(await request<unknown>('/api/auth/me', { signal }));
    } catch (error) {
      // Signed out is an answer, not a failure. As data it survives later checks, which would otherwise reset the query
      // and flash the loading screen.
      if (error instanceof ApiError && error.status === 401) {
        return null;
      }
      throw error;
    }
  },
};

export function useMe() {
  return useQuery({
    queryKey: authKeys.me,
    queryFn: ({ signal }) => authApi.me(signal),
    staleTime: 60_000,
    // Before a first answer (API down), a refetch resets the query and would flash the loading screen on every focus;
    // "Try again" and reconnecting still check again.
    refetchOnWindowFocus: (query) => query.state.data !== undefined,
  });
}

function hasRole(user: CurrentUserDto | null | undefined, role: Role): boolean {
  return user?.roles.includes(role) ?? false;
}

export function isAdmin(user: CurrentUserDto | null | undefined): boolean {
  return hasRole(user, 'admin');
}

/** Neither admin nor support: the API refuses everything but /api/auth/me. */
export function hasNoRole(user: CurrentUserDto): boolean {
  return !hasRole(user, 'admin') && !hasRole(user, 'support');
}

export function roleLabel(user: CurrentUserDto): string {
  if (hasRole(user, 'admin')) {
    return 'Admin';
  }
  return hasRole(user, 'support') ? 'Support' : 'No role';
}

export function returnUrlOf(location: { pathname: string; search: string; hash: string }): string {
  const params = new URLSearchParams(location.search);
  if (!params.has(AUTH_ERROR_PARAM)) {
    return `${location.pathname}${location.search}${location.hash}`;
  }
  params.delete(AUTH_ERROR_PARAM);
  const search = params.toString();
  return `${location.pathname}${search ? `?${search}` : ''}${location.hash}`;
}

function signInUrl(returnUrl: string): string {
  return buildUrl('/api/auth/login', { returnUrl });
}

function signOutUrl(): string {
  return buildUrl('/api/auth/logout');
}

/** A full-page navigation: the API signs in and out itself, through redirects to the identity provider. */
function navigateTo(url: string): void {
  window.location.assign(url);
}

// Kept per tab in sessionStorage, which survives the round trip through the identity provider. Storage can be
// unavailable (private windows, blocked site data); sign-in then still works, only without these hints.
const SIGNED_OUT_KEY = 'ka:signed-out';
const REDIRECTED_AT_KEY = 'ka:sign-in-redirected-at';

// A sign-in that comes back without a session this soon after the last redirect is failing, not just starting.
const REDIRECT_LOOP_WINDOW_MS = 30_000;

function readStorage(key: string): string | null {
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStorage(key: string, value: string | null): void {
  try {
    if (value === null) {
      window.sessionStorage.removeItem(key);
    } else {
      window.sessionStorage.setItem(key, value);
    }
  } catch {
    // Without storage the portal just redirects again next time.
  }
}

/** Signs out, remembering it so the portal shows the signed-out screen instead of signing straight back in. */
export function signOut(): void {
  writeStorage(SIGNED_OUT_KEY, '1');
  navigateTo(signOutUrl());
}

export function wasSignedOut(): boolean {
  return readStorage(SIGNED_OUT_KEY) === '1';
}

/** Goes to the identity provider; `automatic` marks redirects nobody clicked, which the loop guard counts. */
export function signIn(returnUrl: string, automatic = false): void {
  writeStorage(SIGNED_OUT_KEY, null);
  writeStorage(REDIRECTED_AT_KEY, automatic ? String(Date.now()) : null);
  navigateTo(signInUrl(returnUrl));
}

export function redirectedRecently(now = Date.now()): boolean {
  const at = Number(readStorage(REDIRECTED_AT_KEY));
  return Number.isFinite(at) && at > 0 && now - at < REDIRECT_LOOP_WINDOW_MS;
}

/** Called once someone is signed in, so a later expired session redirects again. */
export function clearSignInMarks(): void {
  writeStorage(SIGNED_OUT_KEY, null);
  writeStorage(REDIRECTED_AT_KEY, null);
}
