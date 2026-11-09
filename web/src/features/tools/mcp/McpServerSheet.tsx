import { useMutation, useQueryClient } from '@tanstack/react-query';
import { PlugZap, Save, Trash2 } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { fieldAria } from '../../../components/ui/aria';
import { Button } from '../../../components/ui/Button';
import { Alert, ErrorMessage } from '../../../components/ui/feedback';
import { Field, inputProps } from '../../../components/ui/Field';
import { ConfirmDialog } from '../../../components/ui/Modal';
import { SegmentedControl } from '../../../components/ui/SegmentedControl';
import { SideSheet } from '../../../components/ui/SideSheet';
import { Switch } from '../../../components/ui/Switch';
import { toast } from '../../../components/ui/Toast';
import { ApiError } from '../../../lib/api';
import { pluralize } from '../../../lib/format';
import { NO_SECRET, SecretField, secretRequest, type SecretValue } from '../../../components/ui/SecretField';
import { TestResult } from '../../../components/ui/TestResult';
import { mcpApi, mcpKeys } from './api';
import { McpToolList } from './McpToolList';
import type { McpAuthType, McpServerDto, McpTestResultDto, SaveMcpServerRequest } from './types';
import { useFocusFirstInvalid } from '../../../lib/hooks/useFocusFirstInvalid';

interface Values {
  name: string;
  url: string;
  authType: McpAuthType;
  headerName: string;
  secret: SecretValue;
  enabled: boolean;
  sendIdentity: boolean;
}

type FieldName = 'name' | 'url' | 'authType' | 'headerName' | 'secret';
const FIELDS: readonly FieldName[] = ['name', 'url', 'authType', 'headerName', 'secret'];

const AUTH_OPTIONS: ReadonlyArray<{ value: McpAuthType; label: string }> = [
  { value: 'none', label: 'None' },
  { value: 'bearer', label: 'Bearer token' },
  { value: 'header', label: 'Custom header' },
];

function initialValues(server: McpServerDto | null): Values {
  return {
    name: server?.name ?? '',
    url: server?.url ?? '',
    authType: server?.authType ?? 'none',
    headerName: server?.headerName ?? '',
    secret: NO_SECRET,
    enabled: server?.enabled ?? true,
    sendIdentity: server?.sendIdentity ?? false,
  };
}

/** Mirrors the API's checks so errors show next to the field. */
export function validateMcpServer(
  values: Values,
  server: McpServerDto | null,
): { request: SaveMcpServerRequest | null; errors: Partial<Record<FieldName, string>> } {
  const errors: Partial<Record<FieldName, string>> = {};
  const name = values.name.trim();
  if (!name) {
    errors.name = 'Enter a name.';
  }
  const url = values.url.trim();
  if (!/^https?:\/\/\S+$/i.test(url)) {
    errors.url = 'Enter the address, such as https://mcp.example.com/mcp.';
  }
  const headerName = values.headerName.trim();
  if (values.authType === 'header' && !/^[A-Za-z0-9-]{1,100}$/.test(headerName)) {
    errors.headerName = 'Enter the header name, such as X-Api-Key.';
  }
  const secret = secretRequest(values.secret);
  const keepsSaved = secret === null && Boolean(server?.hasSecret) && server?.authType !== 'none';
  if (values.authType !== 'none' && !secret && !keepsSaved) {
    errors.secret = 'Enter the token.';
  }
  if (Object.keys(errors).length > 0) {
    return { request: null, errors };
  }
  return {
    request: {
      name,
      url,
      authType: values.authType,
      headerName: values.authType === 'header' ? headerName : null,
      secret: values.authType === 'none' ? null : secret,
      enabled: values.enabled,
      sendIdentity: values.sendIdentity,
    },
    errors,
  };
}

export interface McpServerSheetProps {
  /** Null adds a new server. */
  server: McpServerDto | null;
  onClose: () => void;
  onSaved?: (server: McpServerDto) => void;
}

/** Mount it only while open. */
export function McpServerSheet({ server, onClose, onSaved }: McpServerSheetProps) {
  const queryClient = useQueryClient();
  const prefix = useId();
  const id = (field: string) => `${prefix}-${field}`;
  const [values, setValues] = useState<Values>(() => initialValues(server));
  const [clientErrors, setClientErrors] = useState<Partial<Record<FieldName, string>>>({});
  const [invalidAttempts, setInvalidAttempts] = useState(0);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [testResult, setTestResult] = useState<McpTestResultDto | null>(null);
  const dirty = JSON.stringify(values) !== JSON.stringify(initialValues(server));

  const refresh = () => void queryClient.invalidateQueries({ queryKey: mcpKeys.all });

  const save = useMutation({
    mutationFn: (body: SaveMcpServerRequest) => (server ? mcpApi.update(server.id, body) : mcpApi.create(body)),
    // The request may carry a token, so don't keep it in the mutation cache.
    gcTime: 0,
    onSuccess: (saved) => {
      refresh();
      toast({
        title: server ? 'MCP server saved' : 'MCP server added',
        description: saved.lastCheck && !saved.lastCheck.ok ? `It could not be reached: ${saved.lastCheck.error ?? 'unknown error'}` : undefined,
        tone: saved.lastCheck && !saved.lastCheck.ok ? 'warning' : 'success',
      });
      onSaved?.(saved);
      onClose();
    },
  });

  const test = useMutation({
    mutationFn: (body: SaveMcpServerRequest | null) => (server && !body ? mcpApi.test(server.id) : mcpApi.testDraft(body!)),
    gcTime: 0,
    onSuccess: (result) => {
      setTestResult(result);
      if (server && !dirty) {
        refresh();
      }
    },
  });

  const remove = useMutation({
    mutationFn: () => mcpApi.remove(server!.id),
    onSuccess: () => {
      refresh();
      toast({ title: 'MCP server deleted', tone: 'success' });
      onClose();
    },
  });

  const validation = save.error instanceof ApiError && save.error.isValidation ? save.error : null;
  const errorsFor = (field: FieldName): string[] | undefined => {
    const client = clientErrors[field];
    if (client) {
      return [client];
    }
    const fromServer = validation?.errorsFor(field) ?? [];
    return fromServer.length > 0 ? fromServer : undefined;
  };
  const otherErrors = validation ? validation.errorsExcept(FIELDS) : [];

  const formRef = useFocusFirstInvalid(invalidAttempts, validation);

  const set = <K extends keyof Values>(field: K, value: Values[K]) => {
    setValues((current) => ({ ...current, [field]: value }));
    setClientErrors((current) => ({ ...current, [field]: undefined }));
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const { request, errors } = validateMcpServer(values, server);
    setClientErrors(errors);
    if (request) {
      save.mutate(request);
    } else {
      setInvalidAttempts((count) => count + 1);
    }
  };

  const runTest = () => {
    setTestResult(null);
    // Unchanged settings test with the saved token; changed ones are tested as typed, before saving.
    if (server && !dirty) {
      test.mutate(null);
      return;
    }
    const { request, errors } = validateMcpServer(values, null);
    setClientErrors(errors);
    if (request) {
      test.mutate(request);
    } else {
      setInvalidAttempts((count) => count + 1);
    }
  };

  const formId = id('form');
  const tools = testResult?.tools ?? server?.tools ?? [];
  const busy = save.isPending || remove.isPending;
  return (
    <SideSheet
      open
      onClose={onClose}
      size="lg"
      title={server ? server.name : 'Add an MCP server'}
      description="A server that offers tools over MCP (Streamable HTTP), such as your shop, CRM or ticket system. Agents then pick which of its tools they may use."
      footer={
        <>
          {server ? (
            <Button variant="danger-ghost" icon={Trash2} onClick={() => setConfirmDelete(true)} disabled={busy} className="mr-auto">
              Delete
            </Button>
          ) : null}
          <Button onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button icon={PlugZap} onClick={runTest} loading={test.isPending} disabled={busy}>
            Test
          </Button>
          <Button type="submit" form={formId} variant="primary" icon={Save} loading={save.isPending} disabled={remove.isPending}>
            {server ? 'Save' : 'Add server'}
          </Button>
        </>
      }
    >
      <form
        ref={formRef}
        id={formId}
        onSubmit={submit}
        noValidate
        className="space-y-5"
        aria-label={server ? `Settings of ${server.name}` : 'New MCP server'}
      >
        <Field label="Name" htmlFor={id('name')} errors={errorsFor('name')} hint="How your team and agents recognize it, such as Shop.">
          <input
            {...fieldAria(id('name'), errorsFor('name'), true)}
            value={values.name}
            maxLength={100}
            onChange={(event) => set('name', event.target.value)}
            className="field"
          />
        </Field>
        <Field
          label="Address"
          htmlFor={id('url')}
          errors={errorsFor('url')}
          hint="The server's MCP endpoint. Only https, unless your server allows private networks."
        >
          <input
            {...fieldAria(id('url'), errorsFor('url'), true)}
            {...inputProps}
            type="url"
            value={values.url}
            placeholder="https://mcp.example.com/mcp"
            onChange={(event) => set('url', event.target.value)}
            className="field font-mono text-xs"
          />
        </Field>
        <div className="space-y-2">
          <p id={id('auth-label')} className="text-small font-medium text-fg">
            Sign-in
          </p>
          <SegmentedControl label="Sign-in" options={AUTH_OPTIONS} value={values.authType} onChange={(type) => set('authType', type)} />
          {values.authType === 'header' ? (
            <Field label="Header name" htmlFor={id('headerName')} errors={errorsFor('headerName')}>
              <input
                {...fieldAria(id('headerName'), errorsFor('headerName'))}
                {...inputProps}
                value={values.headerName}
                placeholder="X-Api-Key"
                onChange={(event) => set('headerName', event.target.value)}
                className="field font-mono text-xs"
              />
            </Field>
          ) : null}
          {values.authType !== 'none' ? (
            <SecretField
              id={id('secret')}
              label="Token"
              saved={Boolean(server?.hasSecret)}
              value={values.secret}
              onChange={(value) => set('secret', value)}
              errors={errorsFor('secret')}
              placeholder={server?.secretHint ? `Saved, ends in ${server.secretHint.replace('…', '')}` : 'Paste the token'}
              hint="Stored encrypted and never shown again."
            />
          ) : null}
        </div>
        {server ? <Switch checked={values.enabled} onChange={(checked) => set('enabled', checked)} label="Agents may use its tools" /> : null}
        <div className="space-y-1">
          <Switch
            checked={values.sendIdentity}
            onChange={(checked) => set('sendIdentity', checked)}
            label="Send the visitor's verified user ID"
            describedBy={id('sendIdentity')}
          />
          <p id={id('sendIdentity')} className="text-xs text-fg-muted">
            Each tool call then carries the user ID your site signed, in _meta under knowledge-agent/caller, so the app can return only that
            person&apos;s data.
          </p>
        </div>

        {testResult ? <TestResult result={testResult} /> : null}
        {test.isError ? <ErrorMessage error={test.error} title="The test did not run" /> : null}
        {save.isError && !validation ? <ErrorMessage error={save.error} title="The server was not saved" /> : null}
        {otherErrors.length > 0 ? (
          <Alert tone="danger" title="The server was not saved" live>
            {otherErrors.join(' ')}
          </Alert>
        ) : null}

        <section aria-labelledby={id('tools')} className="space-y-2 border-t border-border-subtle pt-5">
          <h3 id={id('tools')} className="text-sm font-semibold text-fg">
            Tools {tools.length > 0 ? <span className="font-normal text-fg-muted">({tools.length})</span> : null}
          </h3>
          <p className="text-xs text-fg-muted">
            What the server says each tool does. Tools that may change something ask the person before they run, unless you choose otherwise on the
            agent.
          </p>
          <McpToolList tools={tools} label={`Tools of ${values.name || 'the server'}`} />
        </section>
      </form>

      <ConfirmDialog
        open={confirmDelete}
        title={`Delete ${server?.name ?? 'this server'}?`}
        message={
          server && server.usedBy.length > 0
            ? `${pluralize(server.usedBy.length, 'agent')} use its tools (${server.usedBy.map((use) => use.agentName).join(', ')}). They lose them.`
            : 'Agents can no longer use its tools.'
        }
        confirmLabel="Delete"
        busy={remove.isPending}
        error={remove.isError ? <ErrorMessage error={remove.error} title="Not deleted" /> : null}
        onConfirm={() => remove.mutate()}
        onCancel={() => setConfirmDelete(false)}
      />
    </SideSheet>
  );
}
