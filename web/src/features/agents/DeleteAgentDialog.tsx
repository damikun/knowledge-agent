import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { ErrorMessage } from '../../components/ui/feedback';
import { ConfirmDialog } from '../../components/ui/Modal';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import { namesList } from './agentSummary';
import { agentKeys, agentsApi, teamsOf } from './api';
import type { AgentDto, AgentReferenceDto } from './types';

export interface DeleteAgentDialogProps {
  agent: AgentDto | null;
  onClose: () => void;
  onDeleted?: () => void;
}

export function DeleteAgentDialog({ agent, onClose, onDeleted }: DeleteAgentDialogProps) {
  const queryClient = useQueryClient();
  // Agents that hand off to this one; the API wants a confirmation before removing it from them.
  const [teams, setTeams] = useState<AgentReferenceDto[]>([]);
  const remove = useMutation({
    mutationFn: ({ id, removeFromTeams }: { id: string; removeFromTeams: boolean }) => agentsApi.deleteAgent(id, removeFromTeams),
    onSuccess: (_result, { id }) => {
      queryClient.setQueryData<AgentDto[]>(agentKeys.list, (old) => old?.filter((item) => item.id !== id));
      void queryClient.invalidateQueries({ queryKey: agentKeys.list });
      toast({ title: 'Agent deleted', tone: 'success' });
      setTeams([]);
      onClose();
      onDeleted?.();
    },
    onError: (failure) => {
      const found = teamsOf(failure);
      if (found.length > 0) {
        setTeams(found);
      }
    },
  });
  const askedToConfirm = teamsOf(remove.error).length > 0;
  // The API puts these errors on `id`, which needs no field name in front.
  const idErrors = remove.error instanceof ApiError ? remove.error.errorsFor('id') : [];
  const error = idErrors.length > 0 ? new Error(idErrors.join(' ')) : remove.error;

  return (
    <ConfirmDialog
      open={agent !== null}
      title="Delete agent?"
      message={
        teams.length > 0 ? (
          <>
            {namesList(teams.map((team) => team.name))} {teams.length === 1 ? 'hands' : 'hand'} conversations to “{agent?.name}”. Deleting it also
            removes it from their specialists. This cannot be undone.
          </>
        ) : (
          <>“{agent?.name}” will be deleted. Its conversations continue with the built-in agent. This cannot be undone.</>
        )
      }
      confirmLabel={teams.length > 0 ? 'Remove and delete' : 'Delete'}
      busy={remove.isPending}
      error={remove.isError && !askedToConfirm ? <ErrorMessage error={error} /> : null}
      onConfirm={() => agent && remove.mutate({ id: agent.id, removeFromTeams: teams.length > 0 })}
      onCancel={() => {
        remove.reset();
        setTeams([]);
        onClose();
      }}
    />
  );
}
