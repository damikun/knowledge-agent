import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Hash, Save, Send, Settings2 } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Field, inputProps } from '../../components/ui/Field';
import { SecretField } from '../../components/ui/SecretField';
import { SideSheet } from '../../components/ui/SideSheet';
import { Switch } from '../../components/ui/Switch';
import { TestResult } from '../../components/ui/TestResult';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import type { WidgetDto } from '../widgets/types';
import { integrationKeys, integrationsApi } from './api';
import { initialSlackValues, SLACK_FIELDS, toSlackRequest, type SlackField, type SlackFormErrors, type SlackFormValues } from './integrationForms';
import { IntegrationCard, TestDialog, UsedBy, widgetsSendingTo, type IntegrationStatus } from './IntegrationParts';
import type { IntegrationsDto, IntegrationTestResultDto, SaveSlackSettingsRequest, SlackSettingsDto } from './types';
import { useFocusFirstInvalid } from '../../lib/hooks/useFocusFirstInvalid';

const MANIFEST = 'docs/slack-app-manifest.yml';

function slackStatus(slack: SlackSettingsDto): IntegrationStatus {
  if (!slack.enabled) {
    return { tone: 'neutral', label: 'Off' };
  }
  if (!slack.canPost) {
    return { tone: 'danger', label: 'Cannot post' };
  }
  if (!slack.canReceive) {
    return { tone: 'warning', label: 'Replies not set up' };
  }
  return slack.connected ? { tone: 'success', label: 'On' } : { tone: 'warning', label: 'Not connected' };
}

/** Mounted while open, so each opening starts from the saved settings. */
function SlackSettingsSheet({ slack, onClose }: { slack: SlackSettingsDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const prefix = useId();
  const id = (field: string) => `${prefix}-${field}`;
  const [values, setValues] = useState<SlackFormValues>(() => initialSlackValues(slack));
  const [clientErrors, setClientErrors] = useState<SlackFormErrors>({});
  const [invalidAttempts, setInvalidAttempts] = useState(0);

  const save = useMutation({
    mutationFn: (body: SaveSlackSettingsRequest) => integrationsApi.saveSlack(body),
    // The request may carry tokens, so don't keep it in the mutation cache.
    gcTime: 0,
    onSuccess: (saved) => {
      queryClient.setQueryData<IntegrationsDto>(integrationKeys.all, (old) => (old ? { ...old, slack: saved } : old));
      void queryClient.invalidateQueries({ queryKey: integrationKeys.all });
      toast({ title: 'Slack settings saved', tone: 'success' });
      onClose();
    },
  });

  const validation = save.error instanceof ApiError && save.error.isValidation ? save.error : null;
  const errorsFor = (field: SlackField): string[] | undefined => {
    const client = clientErrors[field];
    if (client) {
      return [client];
    }
    const server = validation?.errorsFor(field) ?? [];
    return server.length > 0 ? server : undefined;
  };
  const otherErrors = validation ? validation.errorsExcept(SLACK_FIELDS) : [];

  const formRef = useFocusFirstInvalid(invalidAttempts, validation);

  const set = <K extends keyof SlackFormValues>(field: K, value: SlackFormValues[K]) => {
    setValues((current) => ({ ...current, [field]: value }));
    setClientErrors((current) => ({ ...current, [field]: undefined }));
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const { request, errors } = toSlackRequest(values, slack);
    setClientErrors(errors);
    if (request) {
      save.mutate(request);
    } else {
      setInvalidAttempts((count) => count + 1);
    }
  };

  const formId = id('form');
  return (
    <SideSheet
      open
      onClose={onClose}
      title="Slack settings"
      description="Socket Mode connects out to Slack with the app-level token, so no public URL is needed."
      footer={
        <>
          <Button onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="primary" icon={Save} loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form ref={formRef} id={formId} onSubmit={submit} noValidate className="space-y-5" aria-label="Slack settings">
        <Switch checked={values.enabled} onChange={(checked) => set('enabled', checked)} label="Use Slack for requests for a person" />
        <SecretField
          id={id('botToken')}
          label="Bot token"
          saved={slack.hasBotToken}
          value={values.botToken}
          onChange={(value) => set('botToken', value)}
          errors={errorsFor('botToken')}
          placeholder="xoxb-…"
          hint="Starts with xoxb-. Stored encrypted and never shown again."
        />
        <SecretField
          id={id('appToken')}
          label="App-level token"
          saved={slack.hasAppToken}
          value={values.appToken}
          onChange={(value) => set('appToken', value)}
          errors={errorsFor('appToken')}
          placeholder="xapp-…"
          hint="Starts with xapp-. Needed to receive replies and button clicks."
        />
        <Field label="Channel id" htmlFor={id('channelId')} errors={errorsFor('channelId')} hint="In Slack: channel details, at the bottom.">
          <input
            {...fieldAria(id('channelId'), errorsFor('channelId'), true)}
            {...inputProps}
            value={values.channelId}
            placeholder="C0123456789"
            onChange={(event) => set('channelId', event.target.value)}
            className="field font-mono text-xs"
          />
        </Field>

        {save.isError && !validation ? <ErrorMessage error={save.error} title="The Slack settings were not saved" /> : null}
        {otherErrors.length > 0 ? (
          <Alert tone="danger" title="The Slack settings were not saved" live>
            {otherErrors.join(' ')}
          </Alert>
        ) : null}
      </form>
    </SideSheet>
  );
}

/** Mounted while open, so a failed run from an earlier opening isn't shown again. */
function TestSlackDialog({
  slack,
  onDone,
  onCancel,
}: {
  slack: SlackSettingsDto;
  onDone: (result: IntegrationTestResultDto) => void;
  onCancel: () => void;
}) {
  const test = useMutation({ mutationFn: () => integrationsApi.testSlack(), onSuccess: onDone });
  return (
    <TestDialog open title="Send a test message" busy={test.isPending} onSend={() => test.mutate()} onCancel={onCancel}>
      <p>
        {slack.channelId ? (
          <>
            Send a test message to the channel <strong className="font-mono text-xs font-medium text-fg">{slack.channelId}</strong>?
          </>
        ) : (
          'No channel is set, so the test only signs in to Slack and posts nothing.'
        )}
      </p>
      <p className="text-xs text-fg-muted">To use another channel, change the channel id in the Slack settings.</p>
      {test.isError ? <ErrorMessage error={test.error} title="The test did not run" /> : null}
    </TestDialog>
  );
}

export function SlackSettingsCard({ slack, widgets }: { slack: SlackSettingsDto; widgets: readonly WidgetDto[] | undefined }) {
  const [open, setOpen] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [result, setResult] = useState<IntegrationTestResultDto | null>(null);
  const using = widgetsSendingTo(widgets, 'slack');
  const started = slack.enabled || slack.saved;

  return (
    <>
      <IntegrationCard
        icon={Hash}
        title="Slack"
        status={slackStatus(slack)}
        fromConfiguration={!slack.saved}
        purpose="Posts each request for a person to a channel with Take, Back to the assistant and Close buttons, and relays replies in the thread to the visitor."
        steps={[
          {
            label: 'Create the Slack app',
            done: slack.hasBotToken,
            detail: (
              <>
                From the manifest in <code className="font-mono text-xs">{MANIFEST}</code> in the repository.
              </>
            ),
          },
          {
            label: 'Add the bot token and a channel',
            done: slack.canPost,
            detail: slack.canPost && slack.channelId ? `Posts to ${slack.channelId}` : 'Requests are posted there.',
          },
          {
            label: 'Add the app-level token for replies',
            done: slack.canReceive,
            detail: slack.canReceive && !slack.connected ? 'Saved, but not connected right now.' : 'Replies in the thread reach the visitor.',
          },
          {
            label: "Send a widget's requests to Slack",
            done: Boolean(using?.length),
            detail: using?.length ? <UsedBy widgets={using} /> : 'Choose Slack under Handover in a widget.',
          },
        ]}
        actions={
          <>
            <Button variant="secondary" icon={started ? Settings2 : Hash} onClick={() => setOpen(true)}>
              {started ? 'Settings' : 'Set up Slack'}
            </Button>
            <Button
              icon={Send}
              disabled={!started}
              onClick={() => setConfirming(true)}
              title={started ? 'Uses the saved settings' : 'Set up Slack first'}
            >
              Send test message
            </Button>
          </>
        }
      >
        {result ? <TestResult result={result} fixLabel="Open Slack settings" onFix={() => setOpen(true)} /> : null}
      </IntegrationCard>
      {confirming ? (
        <TestSlackDialog
          slack={slack}
          onDone={(done) => {
            setResult(done);
            setConfirming(false);
          }}
          onCancel={() => setConfirming(false)}
        />
      ) : null}
      {open ? <SlackSettingsSheet slack={slack} onClose={() => setOpen(false)} /> : null}
    </>
  );
}
