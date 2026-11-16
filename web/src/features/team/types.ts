export interface TeamMemberDto {
  id: string;
  name: string;
  email: string | null;
  role: string;
  firstSeenAt: string;
  lastSeenAt: string;
}

export interface TeamDto {
  members: TeamMemberDto[];
  keycloakConsoleUrl: string | null;
  roleChanges: boolean;
}
