import { CircleX, PanelRight, Pin, PinOff, Undo2, UserCheck } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { ErrorMessage } from '../../components/ui/feedback';
import { ConfirmDialog } from '../../components/ui/Modal';
import { useReturnState } from '../../lib/returnTo';
import { useHandoverActions, usePinConversation } from './api';
import { AssignControl } from './AssignControl';
import { STATUS_LABELS, STATUS_TONES } from './status';
import type { InboxConversationDetail } from './types';

export interface ConversationHeaderProps {
  conversation: InboxConversationDetail;
  title: string;
  /** Only where the list and the thread take turns. */
  backLink: ReactNode;
  detailsOpen: boolean;
  onDetailsOpenChange: (open: boolean) => void;
}

export function ConversationHeader({ conversation, title, backLink, detailsOpen, onDetailsOpenChange }: ConversationHeaderProps) {
  const { join, giveBack, close } = useHandoverActions(conversation.id);
  const pin = usePinConversation();
  const fromInbox = useReturnState('Inbox');
  const [confirmClose, setConfirmClose] = useState(false);
  const { status } = conversation;
  const fromWidget = conversation.channel === 'widget';
  // A failed close shows in its own dialog.
  const actionError = join.error ?? giveBack.error ?? pin.error;
  const busy = join.isPending || giveBack.isPending || close.isPending;

  return (
    <div className="shrink-0 space-y-2 border-b border-border-subtle px-4 py-2">
      {backLink}
      {/* 48px with padding, level with the list's search row. */}
      <div className="flex min-h-8 flex-wrap items-center justify-between gap-x-3 gap-y-2">
        <div className="flex min-w-0 flex-wrap items-center gap-x-2.5 gap-y-1">
          <h2 className="min-w-0 truncate text-section text-fg" title={title}>
            {title}
          </h2>
          <Badge tone={STATUS_TONES[status]} dot>
            {STATUS_LABELS[status]}
          </Badge>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <AssignControl conversationId={conversation.id} assignee={conversation.assignee} />
          {fromWidget && (status === 'ai' || status === 'handover_pending') ? (
            <Button variant="primary" icon={UserCheck} loading={join.isPending} disabled={busy} onClick={() => join.mutate()}>
              Take over
            </Button>
          ) : null}
          {fromWidget && (status === 'human' || status === 'handover_pending') ? (
            <Button icon={Undo2} loading={giveBack.isPending} disabled={busy} onClick={() => giveBack.mutate()}>
              Back to the assistant
            </Button>
          ) : null}
          {fromWidget && status !== 'closed' ? (
            <Button variant="ghost" icon={CircleX} disabled={busy} onClick={() => setConfirmClose(true)}>
              Close
            </Button>
          ) : null}
          <Button
            variant="ghost"
            size="icon"
            icon={conversation.pinned ? PinOff : Pin}
            aria-label={conversation.pinned ? 'Unpin conversation' : 'Pin conversation'}
            title={conversation.pinned ? 'Unpin' : 'Pin for the whole team'}
            onClick={() => pin.mutate({ id: conversation.id, pinned: !conversation.pinned })}
          />
          <Button
            variant="ghost"
            size="icon"
            icon={PanelRight}
            aria-label="Details"
            aria-expanded={detailsOpen}
            title={detailsOpen ? 'Hide details' : 'Show details'}
            onClick={() => onDetailsOpenChange(!detailsOpen)}
          />
        </div>
      </div>
      {actionError ? <ErrorMessage error={actionError} title="That did not work" /> : null}
      {fromWidget ? null : (
        <p className="text-small text-fg-secondary">
          This is a playground test, so nobody can take it over.{' '}
          <Link to={`/playground/${conversation.id}`} state={fromInbox} className="font-medium text-accent-text hover:underline">
            Open it in the playground
          </Link>
        </p>
      )}
      <ConfirmDialog
        open={confirmClose}
        title="Close conversation?"
        message="The visitor sees that the conversation was closed and can start a new one."
        confirmLabel="Close conversation"
        tone="primary"
        busy={close.isPending}
        error={close.isError ? <ErrorMessage error={close.error} /> : null}
        onConfirm={() => close.mutate(undefined, { onSuccess: () => setConfirmClose(false) })}
        onCancel={() => {
          close.reset();
          setConfirmClose(false);
        }}
      />
    </div>
  );
}
