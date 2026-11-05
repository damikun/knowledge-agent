import type { TeamDto, TeamMemberDto } from '../features/team/types';

function teamMember(overrides: Partial<TeamMemberDto> = {}): TeamMemberDto {
  return {
    id: 'kc-ada',
    name: 'Ada Lovelace',
    email: 'ada@example.com',
    role: 'admin',
    firstSeenAt: '2026-10-01T08:00:00Z',
    lastSeenAt: new Date(Date.now() - 2 * 3_600_000).toISOString(),
    ...overrides,
  };
}

export function teamDto(overrides: Partial<TeamDto> = {}): TeamDto {
  return {
    members: [teamMember(), teamMember({ id: 'kc-sam', name: 'Sam Rivera', email: 'sam@example.com', role: 'support', firstSeenAt: '2026-10-20T09:00:00Z' })],
    keycloakConsoleUrl: 'http://localhost:8180/admin/knowledge-agent/console/',
    roleChanges: false,
    ...overrides,
  };
}
