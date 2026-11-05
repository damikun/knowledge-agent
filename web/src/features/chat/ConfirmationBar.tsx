import { Check, ShieldQuestion, X } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import type { ToolCallView } from './chatReducer';

export interface ConfirmationBarProps {
  calls: readonly ToolCallView[];
  onAnswer: (callId: string, approved: boolean) => void;
  disabled?: boolean;
}

/** They run only after Confirm, exactly as asked; sending a message instead drops them. */
export function ConfirmationBar({ calls, onAnswer, disabled = false }: ConfirmationBarProps) {
  if (calls.length === 0) {
    return null;
  }
  return (
    <div className="mx-auto w-full max-w-3xl space-y-2 px-4 pt-2" role="region" aria-label="Actions to confirm">
      {calls.map((call) => {
        const title = call.confirmation?.title ?? call.toolName;
        return (
          <div key={call.callId} className="flex flex-wrap items-center gap-3 rounded-xl border border-border bg-surface-subtle px-3 py-2">
            <ShieldQuestion className="size-4 shrink-0 text-fg-muted" aria-hidden />
            <p className="min-w-0 flex-1 text-small text-fg">
              <span className="font-medium">{title}</span>
              {call.confirmation?.connection ? <span className="text-fg-muted"> in {call.confirmation.connection}</span> : null}
              <span className="block text-xs text-fg-muted">Runs only if you confirm.</span>
            </p>
            <Button size="sm" variant="ghost" icon={X} disabled={disabled} onClick={() => onAnswer(call.callId, false)} aria-label={`Cancel ${title}`}>
              Cancel
            </Button>
            <Button size="sm" icon={Check} disabled={disabled} onClick={() => onAnswer(call.callId, true)} aria-label={`Confirm ${title}`}>
              Confirm
            </Button>
          </div>
        );
      })}
    </div>
  );
}
