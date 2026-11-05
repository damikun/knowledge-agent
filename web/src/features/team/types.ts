export interface TeamMemberDto {
  id: string;
  name: string;
  email: string | null;
  role: string;
  /** First sign-in, shown as Added. */
  firstSeenAt: string;
  lastSeenAt: string;
}

export interface TeamDto {
  members: TeamMemberDto[];
  /** Null without sign-in. */
  keycloakConsoleUrl: string | null;
  /** Always false: roles are managed in Keycloak. */
  roleChanges: boolean;
}
