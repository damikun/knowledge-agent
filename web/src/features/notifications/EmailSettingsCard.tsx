import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Mail, Save, Send, Settings2 } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Field, inputProps } from '../../components/ui/Field';
import { SecretField } from '../../components/ui/SecretField';
import { Select } from '../../components/ui/Select';
import { SideSheet } from '../../components/ui/SideSheet';
import { Switch } from '../../components/ui/Switch';
import { TestResult } from '../../components/ui/TestResult';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import type { WidgetDto } from '../widgets/types';
import { integrationKeys, integrationsApi } from './api';
import {
  EMAIL_FIELDS,
  initialEmailValues,
  MAIL_SECURITY_LABELS,
  toEmailRequest,
  type EmailField,
  type EmailFormErrors,
  type EmailFormValues,
} from './integrationForms';
import { FormGroup, IntegrationCard, TestDialog, UsedBy, widgetsSendingTo, type IntegrationStatus } from './IntegrationParts';
import type { EmailSettingsDto, IntegrationsDto, IntegrationTestResultDto, MailSecurity, SaveEmailSettingsRequest } from './types';
import { useFocusFirstInvalid } from '../../lib/hooks/useFocusFirstInvalid';

const SECURITY_OPTIONS = (Object.entries(MAIL_SECURITY_LABELS) as Array<[MailSecurity, string]>).map(([value, label]) => ({ value, label }));

type TextField = Exclude<EmailField, 'smtpPassword' | 'imapPassword'>;

function emailStatus(email: EmailSettingsDto): IntegrationStatus {
  if (!email.enabled) {
    return { tone: 'neutral', label: 'Off' };
  }
  if (!email.canSend) {
    return { tone: 'danger', label: 'Cannot send' };
  }
  return email.canReceive ? { tone: 'success', label: 'On' } : { tone: 'warning', label: 'Replies not set up' };
}

/** Mounted while open, so each opening starts from the saved settings. */
function EmailSettingsSheet({ email, onClose }: { email: EmailSettingsDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const prefix = useId();
  const id = (field: string) => `${prefix}-${field}`;
  const [values, setValues] = useState<EmailFormValues>(() => initialEmailValues(email));
  const [clientErrors, setClientErrors] = useState<EmailFormErrors>({});
  const [invalidAttempts, setInvalidAttempts] = useState(0);

  const save = useMutation({
    mutationFn: (body: SaveEmailSettingsRequest) => integrationsApi.saveEmail(body),
    // The request may carry passwords, so don't keep it in the mutation cache.
    gcTime: 0,
    onSuccess: (saved) => {
      queryClient.setQueryData<IntegrationsDto>(integrationKeys.all, (old) => (old ? { ...old, email: saved } : old));
      void queryClient.invalidateQueries({ queryKey: integrationKeys.all });
      toast({ title: 'Email settings saved', tone: 'success' });
      onClose();
    },
  });

  const validation = save.error instanceof ApiError && save.error.isValidation ? save.error : null;
  const errorsFor = (field: EmailField): string[] | undefined => {
    const client = clientErrors[field];
    if (client) {
      return [client];
    }
    const server = validation?.errorsFor(field) ?? [];
    return server.length > 0 ? server : undefined;
  };
  const otherErrors = validation ? validation.errorsExcept(EMAIL_FIELDS) : [];

  const formRef = useFocusFirstInvalid(invalidAttempts, validation);

  const set = <K extends keyof EmailFormValues>(field: K, value: EmailFormValues[K]) => {
    setValues((current) => ({ ...current, [field]: value }));
    setClientErrors((current) => ({ ...current, [field]: undefined }));
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const { request, errors } = toEmailRequest(values);
    setClientErrors(errors);
    if (request) {
      save.mutate(request);
    } else {
      setInvalidAttempts((count) => count + 1);
    }
  };

  const text = (
    field: TextField,
    label: string,
    options: { placeholder?: string; hint?: string; type?: string; optional?: boolean; numeric?: boolean } = {},
  ) => (
    <Field label={label} htmlFor={id(field)} errors={errorsFor(field)} hint={options.hint} optional={options.optional}>
      <input
        {...fieldAria(id(field), errorsFor(field), Boolean(options.hint))}
        {...inputProps}
        type={options.type ?? 'text'}
        inputMode={options.numeric ? 'numeric' : undefined}
        value={values[field]}
        placeholder={options.placeholder}
        onChange={(event) => set(field, event.target.value)}
        className="field"
      />
    </Field>
  );

  const securitySelect = (field: 'smtpSecurity' | 'imapSecurity') => (
    <Field label="Security" htmlFor={id(field)} errors={errorsFor(field)}>
      <Select
        {...fieldAria(id(field), errorsFor(field))}
        value={values[field]}
        onChange={(security) => set(field, security)}
        options={SECURITY_OPTIONS}
      />
    </Field>
  );

  const list = (field: 'staffAddresses' | 'staffDomains', label: string, placeholder: string) => (
    <Field label={label} htmlFor={id(field)} errors={errorsFor(field)} hint="One per line." optional>
      <textarea
        {...fieldAria(id(field), errorsFor(field), true)}
        {...inputProps}
        rows={3}
        value={values[field]}
        placeholder={placeholder}
        onChange={(event) => set(field, event.target.value)}
        className="field resize-y font-mono text-xs leading-5"
      />
    </Field>
  );

  const formId = id('form');
  return (
    <SideSheet
      open
      onClose={onClose}
      size="lg"
      title="Email settings"
      description="SMTP sends each request for a person to your team; IMAP reads their replies."
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
      <form ref={formRef} id={formId} onSubmit={submit} noValidate className="space-y-5" aria-label="Email settings">
        <Switch checked={values.enabled} onChange={(checked) => set('enabled', checked)} label="Use email for requests for a person" />

        <FormGroup legend="Sending (SMTP)" description="Links in the emails go to the public address set under Settings > System.">
          {text('smtpHost', 'SMTP server', { placeholder: 'smtp.example.com' })}
          <div className="grid grid-cols-2 gap-3">
            {text('smtpPort', 'Port', { placeholder: '587', numeric: true })}
            {securitySelect('smtpSecurity')}
          </div>
          {text('smtpUser', 'User', { optional: true })}
          <SecretField
            id={id('smtpPassword')}
            label="Password"
            saved={email.hasSmtpPassword}
            value={values.smtpPassword}
            onChange={(value) => set('smtpPassword', value)}
            errors={errorsFor('smtpPassword')}
            placeholder="SMTP password"
          />
          {text('fromAddress', 'Sender address', { placeholder: 'support@example.com', type: 'email' })}
          {text('fromName', 'Sender name', { placeholder: 'Knowledge Agent', optional: true })}
          {text('supportAddress', 'Support address', {
            placeholder: 'team@example.com',
            type: 'email',
            hint: 'Transcripts go here when the widget names no address.',
            optional: true,
          })}
        </FormGroup>

        <FormGroup legend="Replies (IMAP)" description="Replies come back into the chat. With plus addressing, they go to box+ka-<token>@domain.">
          {text('replyAddress', 'Reply address', { placeholder: 'box@example.com', type: 'email', hint: 'The mailbox that receives replies.', optional: true })}
          <div className="flex items-end pb-1">
            <Switch checked={values.plusAddressing} onChange={(checked) => set('plusAddressing', checked)} label="Plus addressing" />
          </div>
          {text('imapHost', 'IMAP server', { placeholder: 'imap.example.com', optional: true })}
          <div className="grid grid-cols-2 gap-3">
            {text('imapPort', 'Port', { placeholder: '993', numeric: true })}
            {securitySelect('imapSecurity')}
          </div>
          {text('imapUser', 'User', { optional: true })}
          <SecretField
            id={id('imapPassword')}
            label="Password"
            saved={email.hasImapPassword}
            value={values.imapPassword}
            onChange={(value) => set('imapPassword', value)}
            errors={errorsFor('imapPassword')}
            placeholder="IMAP password"
          />
          {text('processedFolder', 'Folder for handled replies', { placeholder: 'Processed', optional: true })}
        </FormGroup>

        <FormGroup legend="Staff" description="Only replies from these senders reach the visitor. Leave both empty to trust the domain of the support address.">
          {list('staffAddresses', 'Staff addresses', 'ada@example.com')}
          {list('staffDomains', 'Staff domains', 'example.com')}
        </FormGroup>

        {save.isError && !validation ? <ErrorMessage error={save.error} title="The email settings were not saved" /> : null}
        {otherErrors.length > 0 ? (
          <Alert tone="danger" title="The email settings were not saved" live>
            {otherErrors.join(' ')}
          </Alert>
        ) : null}
      </form>
    </SideSheet>
  );
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Mounted while open, so each opening starts from the support address. */
function TestEmailDialog({
  email,
  onDone,
  onCancel,
}: {
  email: EmailSettingsDto;
  onDone: (result: IntegrationTestResultDto) => void;
  onCancel: () => void;
}) {
  const id = useId();
  const [to, setTo] = useState(email.supportAddress ?? '');
  const [clientError, setClientError] = useState<string | null>(null);
  const test = useMutation({ mutationFn: (recipient: string | null) => integrationsApi.testEmail(recipient), onSuccess: onDone });
  const validation = test.error instanceof ApiError && test.error.isValidation ? test.error : null;
  const serverErrors = validation?.errorsFor('to') ?? [];
  const errors = clientError ? [clientError] : serverErrors.length > 0 ? serverErrors : undefined;
  const recipient = to.trim();

  const send = () => {
    if (recipient && !EMAIL.test(recipient)) {
      setClientError('Enter an email address, such as team@example.com.');
      return;
    }
    test.mutate(recipient || null);
  };

  return (
    <TestDialog open title="Send a test email" busy={test.isPending} onSend={send} onCancel={onCancel}>
      <p>
        {recipient ? (
          <>
            Send a test email to <strong className="font-medium break-all text-fg">{recipient}</strong>?
          </>
        ) : (
          'With no recipient, the test only signs in to the mail server and sends nothing.'
        )}
      </p>
      <Field label="Recipient" htmlFor={id} errors={errors} hint="Prefilled with the support address." optional>
        <input
          {...fieldAria(id, errors, true)}
          {...inputProps}
          type="email"
          value={to}
          placeholder="team@example.com"
          onChange={(event) => {
            setTo(event.target.value);
            setClientError(null);
          }}
          className="field"
        />
      </Field>
      {test.isError && !validation ? <ErrorMessage error={test.error} title="The test did not run" /> : null}
    </TestDialog>
  );
}

export function EmailSettingsCard({ email, widgets }: { email: EmailSettingsDto; widgets: readonly WidgetDto[] | undefined }) {
  const [open, setOpen] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [result, setResult] = useState<IntegrationTestResultDto | null>(null);
  const using = widgetsSendingTo(widgets, 'email');
  const started = email.enabled || email.saved;

  return (
    <>
      <IntegrationCard
        icon={Mail}
        title="Email"
        status={emailStatus(email)}
        fromConfiguration={!email.saved}
        purpose="Emails each request for a person to your team, and brings their replies back into the chat."
        steps={[
          {
            label: 'Send email by SMTP',
            done: email.canSend,
            detail: email.canSend && email.fromAddress ? `From ${email.fromAddress}` : 'The mail server, a sender address and its password.',
          },
          {
            label: 'Read replies by IMAP',
            done: email.canReceive,
            detail: email.canReceive && email.replyAddress ? `Replies to ${email.replyAddress}` : 'The mailbox your team replies to.',
          },
          {
            label: "Send a widget's requests by email",
            done: Boolean(using?.length),
            detail: using?.length ? <UsedBy widgets={using} /> : 'Choose Email under Handover in a widget.',
          },
        ]}
        actions={
          <>
            <Button variant="secondary" icon={started ? Settings2 : Mail} onClick={() => setOpen(true)}>
              {started ? 'Settings' : 'Set up email'}
            </Button>
            <Button
              icon={Send}
              disabled={!started}
              onClick={() => setConfirming(true)}
              title={started ? 'Uses the saved settings' : 'Set up email first'}
            >
              Send test email
            </Button>
          </>
        }
      >
        {result ? <TestResult result={result} fixLabel="Open email settings" onFix={() => setOpen(true)} /> : null}
      </IntegrationCard>
      {confirming ? (
        <TestEmailDialog
          email={email}
          onDone={(done) => {
            setResult(done);
            setConfirming(false);
          }}
          onCancel={() => setConfirming(false)}
        />
      ) : null}
      {open ? <EmailSettingsSheet email={email} onClose={() => setOpen(false)} /> : null}
    </>
  );
}
