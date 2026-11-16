type AuthMode = 'oidc' | 'none';

export type Role = 'admin' | 'support';

export interface CurrentUserDto {
  name: string;
  email: string | null;
  roles: string[];
  mode: AuthMode;
  id?: string | null;
  canManageUsers: boolean;
}
