import { ChevronDown, UserRoundCheck, UserRoundPlus, UserRoundX } from 'lucide-react';
import { Avatar } from '../../components/ui/Avatar';
import { Menu, type MenuItem } from '../../components/ui/Menu';
import { useMe } from '../auth/api';
import { useAssignConversation, useStaff } from './api';
import type { ConversationAssignee } from './types';

export interface AssignControlProps {
  conversationId: string;
  assignee: ConversationAssignee | null;
}

export function AssignControl({ conversationId, assignee }: AssignControlProps) {
  const me = useMe().data;
  const staff = useStaff();
  const assign = useAssignConversation();
  const mine = Boolean(me?.id) && assignee?.id === me?.id;
  const set = (next: ConversationAssignee | null) => assign.mutate({ id: conversationId, assignee: next });

  const items: MenuItem[] = [
    ...(me?.id && !mine ? [{ label: 'Assign to me', icon: UserRoundCheck, onSelect: () => set({ id: me.id!, name: me.name }) }] : []),
    ...(staff.data ?? [])
      .filter((member) => member.id !== me?.id && member.id !== assignee?.id)
      .map((member) => ({ label: `Assign to ${member.name}`, icon: UserRoundPlus, onSelect: () => set({ id: member.id, name: member.name }) })),
    ...(assignee ? [{ label: 'Unassign', icon: UserRoundX, onSelect: () => set(null) }] : []),
  ];

  return (
    <Menu
      label="Assign conversation"
      align="end"
      busy={assign.isPending}
      items={items}
      trigger={(props) => (
        <button
          {...props}
          type="button"
          aria-label={assignee ? `Assigned to ${mine ? 'you' : assignee.name}. Change` : 'Assign conversation'}
          className="inline-flex h-8 max-w-48 items-center gap-1.5 rounded-md border border-transparent px-2 text-small text-fg-secondary hover:bg-fill-hover hover:text-fg"
        >
          {assignee ? (
            <>
              <Avatar name={assignee.name} size="sm" className="size-6!" />
              <span className="min-w-0 truncate">{mine ? 'You' : assignee.name}</span>
            </>
          ) : (
            <>
              <UserRoundPlus className="size-4 shrink-0" aria-hidden />
              <span>Assign</span>
            </>
          )}
          <ChevronDown className="size-3.5 shrink-0" aria-hidden />
        </button>
      )}
    />
  );
}
