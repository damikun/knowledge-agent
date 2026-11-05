/** "none": sign-in is turned off in development and every request is a local admin. */
type AuthMode = 'oidc' | 'none';

export type Role = 'admin' | 'support';

/** GET /api/auth/me. */
export interface CurrentUserDto {
  name: string;
  email: string | null;
  roles: string[];
  mode: AuthMode;
  /** The Keycloak subject: assignments and what was read refer to it. */
  id?: string | null;
  /** May manage users in Keycloak's admin console (realm-management manage-users, view-users or realm-admin). */
  canManageUsers: boolean;
}
