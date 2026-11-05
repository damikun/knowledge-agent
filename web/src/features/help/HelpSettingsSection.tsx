import { CircleHelp } from 'lucide-react';
import { useId } from 'react';
import { ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Switch } from '../../components/ui/Switch';
import { toast } from '../../components/ui/Toast';
import { useHelpStatus, useSaveHelpSettings } from './api';

export function HelpSettingsSection() {
  const status = useHelpStatus();
  const save = useSaveHelpSettings();
  const headingId = useId();
  const descriptionId = useId();

  return (
    <section aria-labelledby={headingId} className="space-y-3">
      <div>
        <h2 id={headingId} className="text-section text-fg">
          For your team
        </h2>
        <p className="mt-0.5 text-sm text-fg-secondary">Built into the portal, for the people who work in it.</p>
      </div>
      <div className="flex items-start gap-3 rounded-2xl border border-border bg-surface p-4">
        <span aria-hidden className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-muted">
          <CircleHelp className="size-5" />
        </span>
        <div className="min-w-0 flex-1 space-y-2">
          {status.isPending ? (
            <Spinner label="Loading the help settings" />
          ) : status.isError ? (
            <ErrorMessage error={status.error} title="Could not load the help settings" />
          ) : (
            <Switch
              checked={status.data.enabled}
              disabled={save.isPending || !status.data.canManage}
              describedBy={descriptionId}
              label={<span className="font-medium">In-app help assistant</span>}
              onChange={(enabled) =>
                save.mutate(enabled, {
                  onSuccess: () => toast({ title: enabled ? 'The help assistant is on' : 'The help assistant is off', tone: 'success' }),
                  onError: () => toast({ title: 'The setting could not be saved', tone: 'danger' }),
                })
              }
            />
          )}
          <p className="text-small text-fg-secondary" data-testid="help-assistant-sources">
            Answers your team from the Knowledge Agent docs of this version and your Internal sources.
          </p>
          <p id={descriptionId} className="text-small text-fg-muted">
            The "?" button in the header, for admins and support users. It reads no customer conversations and keeps none of its own. Off hides it for
            everyone.
          </p>
        </div>
      </div>
    </section>
  );
}
