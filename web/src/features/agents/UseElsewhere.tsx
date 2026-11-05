import { useMutation, useQueryClient } from '@tanstack/react-query';
import { KeyRound, Plus, Trash2 } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Field } from '../../components/ui/Field';
import { ConfirmDialog } from '../../components/ui/Modal';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { Skeleton } from '../../components/ui/Skeleton';
import { Switch } from '../../components/ui/Switch';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import { formatRelativeTime } from '../../lib/format';
import { CopyBlock } from '../../components/ui/CopyBlock';
import { accessApi, accessKeys, apiSnippet, mcpSnippets, useAgentAccess, useAgentApiKeys, type AgentApiKeyDto, type CreatedAgentApiKeyDto } from './accessApi';
import { FieldGroup } from './agentFields';
import { agentKeys } from './api';
import { PublicAddressHint, usePublicOrigin } from '../system/publicAddress';
import type { AgentDto } from './types';

const KEY_PLACEHOLDER = '<your API key>';

type SnippetClient = 'claude' | 'cursor' | 'other';

const CLIENTS: ReadonlyArray<{ value: SnippetClient; label: string }> = [
  { value: 'claude', label: 'Claude Desktop' },
  { value: 'cursor', label: 'Cursor' },
  { value: 'other', label: 'Other clients' },
];

export function UseElsewhere({ agent }: { agent: AgentDto }) {
  const queryClient = useQueryClient();
  const prefix = useId();
  const access = useAgentAccess(agent.id);
  const keys = useAgentApiKeys(agent.id);
  const [client, setClient] = useState<SnippetClient>('claude');
  const [created, setCreated] = useState<CreatedAgentApiKeyDto | null>(null);
  // The portal proxies /mcp and /api to the API, so the public address serves both.
  const { origin, placeholder } = usePublicOrigin();

  const saveAccess = useMutation({
    mutationFn: (body: { mcpServerEnabled?: boolean; httpApiEnabled?: boolean }) => accessApi.saveAccess(agent.id, body),
    onSuccess: (saved) => {
      queryClient.setQueryData(accessKeys.access(agent.id), saved);
      void queryClient.invalidateQueries({ queryKey: agentKeys.list });
    },
  });

  if (access.isPending) {
    return <Skeleton shape="card" className="h-64 max-w-3xl" />;
  }
  if (access.isError) {
    return <ErrorMessage error={access.error} title="Could not load how the agent is used elsewhere" />;
  }

  const mcpUrl = `${origin}${access.data.mcpPath}`;
  const apiUrl = `${origin}${access.data.apiPath}`;
  const key = created?.secret ?? KEY_PLACEHOLDER;
  const snippets = mcpSnippets(agent.key, mcpUrl, key);

  return (
    <div className="@container max-w-3xl space-y-6">
      <FieldGroup
        id={`${prefix}-ways`}
        title="Where else it answers"
        why="Other AI apps and your own software can ask this agent, with its instructions and its knowledge. They need an API key of this agent."
      >
        <div className="space-y-4">
          <div className="space-y-1">
            <Switch
              checked={access.data.mcpServerEnabled}
              disabled={saveAccess.isPending}
              onChange={(on) => saveAccess.mutate({ mcpServerEnabled: on })}
              label="MCP server, for AI apps such as Claude Desktop and Cursor"
              describedBy={`${prefix}-mcp-hint`}
            />
            <p id={`${prefix}-mcp-hint`} className="pl-10 text-xs text-fg-muted">
              Tools: ask (an answer with sources) and search_knowledge (the passages themselves). Address: <span className="font-mono">{mcpUrl}</span>
            </p>
          </div>
          <div className="space-y-1">
            <Switch
              checked={access.data.httpApiEnabled}
              disabled={saveAccess.isPending}
              onChange={(on) => saveAccess.mutate({ httpApiEnabled: on })}
              label="HTTP API, for your own software"
              describedBy={`${prefix}-api-hint`}
            />
            <p id={`${prefix}-api-hint`} className="pl-10 text-xs text-fg-muted">
              POST {apiUrl}/ask and /search with JSON. Conversations show in the Inbox under All.
            </p>
          </div>
          {saveAccess.isError ? <ErrorMessage error={saveAccess.error} title="Not saved" /> : null}
        </div>
      </FieldGroup>

      <FieldGroup id={`${prefix}-keys`} title="API keys" why="Each key works only for this agent. Give each app its own key, so you can revoke one without the others.">
        {created ? (
          <Alert tone="success" title={`Copy the key for ${created.key.name} now`} live>
            <p className="mb-2">It is shown only this once. The snippets below already contain it.</p>
            <CopyBlock code={created.secret} label="Copy key" copiedText="The key is on your clipboard." />
          </Alert>
        ) : null}
        <NewKeyForm agentId={agent.id} onCreated={setCreated} />
        {keys.isPending ? (
          <Skeleton className="h-16" />
        ) : keys.isError ? (
          <ErrorMessage error={keys.error} title="Could not load the keys" />
        ) : keys.data.length === 0 ? (
          <p className="text-small text-fg-secondary">No keys yet.</p>
        ) : (
          <ul aria-label="API keys" className="divide-y divide-border-subtle rounded-xl border border-border">
            {keys.data.map((item) => (
              <KeyRow key={item.id} agentId={agent.id} apiKey={item} />
            ))}
          </ul>
        )}
      </FieldGroup>

      <FieldGroup id={`${prefix}-connect`} title="Connect an app" why="Paste this into the app's settings. Replace the key placeholder unless you just created a key.">
        <SegmentedControl label="App" options={CLIENTS} value={client} onChange={setClient} />
        {placeholder ? <PublicAddressHint /> : null}
        {client === 'claude' ? (
          <div className="space-y-2">
            <p className="text-small text-fg-secondary">
              In Claude Desktop: Settings, Developer, Edit config, then add this to claude_desktop_config.json. It needs Node.js for mcp-remote.
            </p>
            <CopyBlock code={snippets.claudeDesktop} />
          </div>
        ) : client === 'cursor' ? (
          <div className="space-y-2">
            <p className="text-small text-fg-secondary">In Cursor: Settings, MCP, Add new MCP server, or add this to .cursor/mcp.json.</p>
            <CopyBlock code={snippets.cursor} />
          </div>
        ) : (
          <div className="space-y-4">
            <div className="space-y-2">
              <p className="text-small text-fg-secondary">Any MCP client that speaks Streamable HTTP:</p>
              <CopyBlock code={snippets.generic} />
            </div>
            <div className="space-y-2">
              <p className="text-small text-fg-secondary">The HTTP API, here with curl:</p>
              <CopyBlock code={apiSnippet(apiUrl, key)} />
            </div>
          </div>
        )}
      </FieldGroup>
    </div>
  );
}

function NewKeyForm({ agentId, onCreated }: { agentId: string; onCreated: (created: CreatedAgentApiKeyDto) => void }) {
  const queryClient = useQueryClient();
  const prefix = useId();
  const [name, setName] = useState('');
  const [perMinute, setPerMinute] = useState('30');
  const [error, setError] = useState<string | null>(null);
  const create = useMutation({
    mutationFn: (body: { name: string; requestsPerMinute: number }) => accessApi.createKey(agentId, body),
    // The response carries the key: keep it out of the cache.
    gcTime: 0,
    onSuccess: (created) => {
      setName('');
      onCreated(created);
      void queryClient.invalidateQueries({ queryKey: accessKeys.keys(agentId) });
      void queryClient.invalidateQueries({ queryKey: accessKeys.access(agentId) });
    },
  });
  const validation = create.error instanceof ApiError && create.error.isValidation ? create.error : null;
  const nameErrors = error ? [error] : validation?.errorsFor('name');
  const rateErrors = validation?.errorsFor('requestsPerMinute');

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const rate = Number.parseInt(perMinute, 10);
    if (!name.trim()) {
      setError('Name the key, such as the app that uses it.');
      return;
    }
    setError(null);
    create.mutate({ name: name.trim(), requestsPerMinute: Number.isFinite(rate) ? rate : 30 });
  };

  return (
    <form onSubmit={submit} noValidate aria-label="New API key" className="grid items-end gap-3 @min-[36rem]:grid-cols-[minmax(0,1fr)_10rem_auto]">
      <Field label="Name" htmlFor={`${prefix}-name`} errors={nameErrors}>
        <input
          {...fieldAria(`${prefix}-name`, nameErrors)}
          value={name}
          maxLength={100}
          placeholder="Claude Desktop of the support team"
          onChange={(event) => setName(event.target.value)}
          className="field"
        />
      </Field>
      <Field label="Requests a minute" htmlFor={`${prefix}-rate`} errors={rateErrors}>
        <input
          {...fieldAria(`${prefix}-rate`, rateErrors)}
          type="number"
          min={1}
          max={600}
          value={perMinute}
          onChange={(event) => setPerMinute(event.target.value)}
          className="field"
        />
      </Field>
      <Button type="submit" icon={Plus} loading={create.isPending}>
        Create key
      </Button>
      {create.isError && !validation ? <ErrorMessage error={create.error} title="No key was created" className="@min-[36rem]:col-span-3" /> : null}
    </form>
  );
}

function KeyRow({ agentId, apiKey }: { agentId: string; apiKey: AgentApiKeyDto }) {
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const revoke = useMutation({
    mutationFn: () => accessApi.revokeKey(agentId, apiKey.id),
    onSuccess: () => {
      setConfirming(false);
      toast({ title: `${apiKey.name} revoked`, tone: 'success' });
      void queryClient.invalidateQueries({ queryKey: accessKeys.keys(agentId) });
      void queryClient.invalidateQueries({ queryKey: accessKeys.access(agentId) });
    },
  });
  const revoked = apiKey.revokedAt !== null;
  return (
    <li className="flex flex-wrap items-center gap-3 px-3 py-2.5">
      <KeyRound className="size-4 shrink-0 text-fg-muted" aria-hidden />
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-fg">
          {apiKey.name}
          {revoked ? <Badge tone="neutral">Revoked</Badge> : null}
        </p>
        <p className="text-xs text-fg-muted">
          <span className="font-mono">{apiKey.prefix}…</span> · {apiKey.requestsPerMinute} a minute · created {formatRelativeTime(apiKey.createdAt)}
          {apiKey.createdBy ? ` by ${apiKey.createdBy}` : ''} · {apiKey.lastUsedAt ? `last used ${formatRelativeTime(apiKey.lastUsedAt)}` : 'never used'}
        </p>
      </div>
      {revoked ? null : (
        <Button size="sm" variant="danger-ghost" icon={Trash2} onClick={() => setConfirming(true)} aria-label={`Revoke ${apiKey.name}`}>
          Revoke
        </Button>
      )}
      <ConfirmDialog
        open={confirming}
        title={`Revoke ${apiKey.name}?`}
        message="Apps that use this key stop reaching the agent at once. This cannot be undone."
        confirmLabel="Revoke"
        busy={revoke.isPending}
        error={revoke.isError ? <ErrorMessage error={revoke.error} title="Not revoked" /> : null}
        onConfirm={() => revoke.mutate()}
        onCancel={() => setConfirming(false)}
      />
    </li>
  );
}
