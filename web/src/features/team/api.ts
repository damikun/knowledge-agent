import { useQuery } from '@tanstack/react-query';
import { asArray, isRecord, request } from '../../lib/api';
import type { TeamDto, TeamMemberDto } from './types';

const teamKeys = {
  all: ['team'] as const,
};

function toTeam(value: unknown): TeamDto {
  const dto = isRecord(value) ? value : {};
  return {
    members: asArray<TeamMemberDto>(dto.members),
    keycloakConsoleUrl: typeof dto.keycloakConsoleUrl === 'string' && dto.keycloakConsoleUrl ? dto.keycloakConsoleUrl : null,
    roleChanges: dto.roleChanges === true,
  };
}

const teamApi = {
  getTeam: async (signal?: AbortSignal) => toTeam(await request<unknown>('/api/team', { signal })),
};

export function useTeam() {
  return useQuery({ queryKey: teamKeys.all, queryFn: ({ signal }) => teamApi.getTeam(signal), staleTime: 30_000 });
}
