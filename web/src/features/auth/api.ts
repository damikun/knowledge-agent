import { useQuery } from '@tanstack/react-query';
import { ApiError, asStrings, call, isRecord, urlOf } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
import type { CurrentUserDto, Role } from './types';

export const authKeys = {
  me: ['auth', 'me'] as const,
};

export const AUTH_ERROR_PARAM = 'authError';

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
      return toCurrentUser(await call(endpoints.auth.me, { signal }));
    } catch (error) {
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
    refetchOnWindowFocus: (query) => query.state.data !== undefined,
  });
}

function hasRole(user: CurrentUserDto | null | undefined, role: Role): boolean {
  return user?.roles.includes(role) ?? false;
}

export function isAdmin(user: CurrentUserDto | null | undefined): boolean {
  return hasRole(user, 'admin');
}

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
  return urlOf(endpoints.auth.login, { query: { returnUrl } });
}

function signOutUrl(): string {
  return urlOf(endpoints.auth.logout);
}

function navigateTo(url: string): void {
  window.location.assign(url);
}

const SIGNED_OUT_KEY = 'ka:signed-out';
const REDIRECTED_AT_KEY = 'ka:sign-in-redirected-at';

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
  }
}

export function signOut(): void {
  writeStorage(SIGNED_OUT_KEY, '1');
  navigateTo(signOutUrl());
}

export function wasSignedOut(): boolean {
  return readStorage(SIGNED_OUT_KEY) === '1';
}

export function signIn(returnUrl: string, automatic = false): void {
  writeStorage(SIGNED_OUT_KEY, null);
  writeStorage(REDIRECTED_AT_KEY, automatic ? String(Date.now()) : null);
  navigateTo(signInUrl(returnUrl));
}

export function redirectedRecently(now = Date.now()): boolean {
  const at = Number(readStorage(REDIRECTED_AT_KEY));
  return Number.isFinite(at) && at > 0 && now - at < REDIRECT_LOOP_WINDOW_MS;
}

export function clearSignInMarks(): void {
  writeStorage(SIGNED_OUT_KEY, null);
  writeStorage(REDIRECTED_AT_KEY, null);
}
