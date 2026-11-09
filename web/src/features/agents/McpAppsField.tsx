import { clsx } from 'clsx';
import { Blocks, ShieldCheck, Trash2 } from 'lucide-react';
import { Link } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Select } from '../../components/ui/Select';
import { Skeleton } from '../../components/ui/Skeleton';
import { StatusLabel } from '../../components/ui/StatusDot';
import { Switch } from '../../components/ui/Switch';
import { ApiError } from '../../lib/api';
import { pluralize } from '../../lib/format';
import { sameId } from '../../lib/ids';
import { useReturnState } from '../../lib/returnTo';
import {
  POLICY_CHOICES,
  serverStatus,
  toolEffect,
  toolLabel,
  toolsSummary,
  useMcpServers,
  type McpServerDto,
  type McpToolPolicy,
} from '../tools/mcp';
import { namesList } from './agentSummary';
import type { FieldKit } from './agentFields';
import { approvalOf, APPROVAL_LABELS, approve, defaultPolicy, needsApproval, withAllTools, type McpApproval } from './mcpApproval';
import type { AgentMcpAppDto, McpChangesPolicy } from './types';

const CHANGES_CHOICES: ReadonlyArray<{ value: McpChangesPolicy; label: string }> = [
  { value: 'confirm', label: 'Changes need confirmation' },
  { value: 'verifiedOnly', label: 'Only for verified visitors' },
];

const NOT_APPROVED_NOTE = 'Until you approve them, they run only after the person confirms.';

function ApprovalBadge({ approval }: { approval: McpApproval | null }) {
  return approval === 'changed' || approval === 'needed' ? <Badge tone="warning">{APPROVAL_LABELS[approval]}</Badge> : null;
}

export function appsSummary(apps: readonly AgentMcpAppDto[] | undefined, servers: readonly McpServerDto[] | undefined): string | null {
  const on = (apps ?? []).filter((app) => app.enabled);
  if (on.length === 0) {
    return null;
  }
  if (!servers) {
    return pluralize(on.length, 'app');
  }
  const names = on.map((app) => {
    const server = servers.find((item) => item.id.toLowerCase() === app.serverId.toLowerCase());
    if (!server) {
      return 'a deleted app';
    }
    if (app.mode === 'all') {
      return `${server.name} (all tools)`;
    }
    const offered = app.tools.filter((tool) => server.tools.some((item) => item.name === tool.name)).length;
    return `${server.name} (${pluralize(offered, 'tool')})`;
  });
  return names.length <= 3 ? namesList(names) : `${names.slice(0, 2).join(', ')} and ${names.length - 2} more`;
}

export function McpAppsField({ values, set, id }: FieldKit) {
  const returnState = useReturnState();
  const servers = useMcpServers();
  const apps = values.mcpApps ?? [];

  if (servers.isPending) {
    return <Skeleton shape="card" className="h-24" />;
  }
  if (servers.isError) {
    // Without the admin role, or on an API without MCP servers, there is nothing to pick from.
    if (servers.error instanceof ApiError && (servers.error.status === 404 || servers.error.status === 403)) {
      return <p className="text-small text-fg-secondary">Connected apps are not available here.</p>;
    }
    return <ErrorMessage error={servers.error} title="Could not load the connected apps" />;
  }
  if (servers.data.length === 0) {
    return (
      <div className="flex items-start gap-3 rounded-xl border border-dashed border-border p-4">
        <Blocks className="mt-0.5 size-5 shrink-0 text-fg-muted" aria-hidden />
        <p className="text-small text-fg-secondary">
          No app is connected yet. Connect one on the{' '}
          <Link to="/tools" state={returnState} className="font-medium text-accent-text hover:underline">
            Tools page
          </Link>
          , such as your shop or CRM, and turn it on here so the agent can look things up there or act for the person.
        </p>
      </div>
    );
  }

  const update = (serverId: string, next: AgentMcpAppDto) => set('mcpApps', [...apps.filter((app) => !sameId(app.serverId, serverId)), next]);

  // Apps of servers deleted since are dropped by the API when the connection goes.
  return (
    <div className="space-y-3">
      <ul aria-label="Connected apps" className="space-y-3">
        {servers.data.map((server) => (
          <AppRow
            key={server.id}
            server={server}
            app={apps.find((app) => sameId(app.serverId, server.id))}
            idPrefix={id(`app-${server.key}`)}
            onChange={(next) => update(server.id, next)}
          />
        ))}
      </ul>
      <p className="text-xs text-fg-muted">
        Over the agent&apos;s MCP server and HTTP API only tools that run on their own are offered, since nobody there can confirm. Add an app, or
        test one, on the{' '}
        <Link to="/tools" state={returnState} className="font-medium text-accent-text hover:underline">
          Tools page
        </Link>
        .
      </p>
    </div>
  );
}

function AppRow({
  server,
  app,
  idPrefix,
  onChange,
}: {
  server: McpServerDto;
  app: AgentMcpAppDto | undefined;
  idPrefix: string;
  onChange: (next: AgentMcpAppDto) => void;
}) {
  const headingId = `${idPrefix}-heading`;
  const summaryId = `${idPrefix}-summary`;
  const on = app?.enabled === true;
  const status = serverStatus(server);
  const current: AgentMcpAppDto = app ?? { serverId: server.id, enabled: false, mode: 'all', changesPolicy: 'confirm', tools: [] };
  const changes = server.tools.filter((tool) => !tool.readOnly).length;

  const chooseMode = (chosen: boolean) => {
    if (!chosen) {
      onChange(withAllTools(current, server));
      return;
    }
    // The first time, every tool starts picked with the policy it has now, so nothing changes until a tool is turned off.
    const tools =
      current.tools.length > 0
        ? current.tools
        : server.tools.map((tool) => ({ name: tool.name, policy: defaultPolicy(tool, current), fingerprint: tool.fingerprint }));
    onChange({ ...current, mode: 'chosen', tools });
  };

  // Turning an app on for all its tools approves the ones it lists now.
  const turnOn = (checked: boolean) =>
    onChange(
      checked && current.mode === 'all' && current.tools.length === 0
        ? withAllTools({ ...current, enabled: true }, server)
        : { ...current, enabled: checked },
    );

  return (
    <li>
      <section
        aria-labelledby={headingId}
        className={clsx('space-y-3 rounded-xl border p-4', on ? 'border-accent-edge bg-surface' : 'border-border bg-surface')}
      >
        <div className="flex flex-wrap items-start gap-3">
          <span aria-hidden className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-secondary">
            <Blocks className="size-4" />
          </span>
          <div className="min-w-48 flex-1 space-y-0.5">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <h3 id={headingId} className="text-sm font-semibold text-fg">
                {server.name}
              </h3>
              <StatusLabel tone={status.tone}>{status.label}</StatusLabel>
            </div>
            <p id={summaryId} className="text-small text-fg-secondary">
              {toolsSummary(server.tools)}
            </p>
            {!server.enabled ? <p className="text-xs text-fg-muted">The app is off on the Tools page, so no agent uses it right now.</p> : null}
          </div>
          <Switch
            checked={on}
            onChange={turnOn}
            describedBy={summaryId}
            label={
              <span>
                Use this app<span className="sr-only">: {server.name}</span>
              </span>
            }
          />
        </div>

        {on ? (
          <div className="space-y-3 border-t border-border-subtle pt-3">
            {current.mode === 'all' ? (
              <p className="text-small text-fg-secondary">
                {server.tools.length > 0
                  ? `Uses all ${pluralize(server.tools.length, 'tool')}, also ones the app adds later. Tools that only look things up run on their own.`
                  : 'Uses every tool the app lists, also ones it adds later. It lists none right now; test it on the Tools page.'}
              </p>
            ) : null}
            {current.mode === 'all' ? <PendingApprovals server={server} app={current} onChange={onChange} /> : null}
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <label htmlFor={`${idPrefix}-changes`} className="text-small font-medium text-fg">
                Tools that make changes
              </label>
              <Select
                id={`${idPrefix}-changes`}
                size="sm"
                className="w-64"
                value={current.changesPolicy}
                onChange={(next) => onChange({ ...current, changesPolicy: next })}
                options={CHANGES_CHOICES}
              />
              {server.tools.length > 0 ? (
                <span className="text-xs text-fg-muted">
                  {changes > 0
                    ? `${pluralize(changes, 'tool')} of this app ${changes === 1 ? 'makes' : 'make'} changes.`
                    : 'None of its tools make changes right now.'}
                </span>
              ) : null}
            </div>
            <CollapsibleSection
              title="Choose tools"
              description="Use only the tools you pick, each with when it may run. Tools the app adds later are not used then."
              defaultOpen={current.mode === 'chosen'}
            >
              <div className="space-y-3">
                <Switch
                  checked={current.mode === 'chosen'}
                  onChange={chooseMode}
                  label={
                    <span>
                      Only the tools I pick<span className="sr-only">: {server.name}</span>
                    </span>
                  }
                />
                {current.mode === 'chosen' ? <ToolChoices server={server} app={current} idPrefix={idPrefix} onChange={onChange} /> : null}
              </div>
            </CollapsibleSection>
          </div>
        ) : null}
      </section>
    </li>
  );
}

function PendingApprovals({ server, app, onChange }: { server: McpServerDto; app: AgentMcpAppDto; onChange: (next: AgentMcpAppDto) => void }) {
  const pending = needsApproval(server, app);
  if (pending.length === 0) {
    return null;
  }
  return (
    <Alert
      tone="warning"
      title={`${pluralize(pending.length, 'tool')} new or changed`}
      action={
        pending.length > 1 ? (
          <Button size="sm" icon={ShieldCheck} onClick={() => onChange(approve(app, pending))}>
            Approve all<span className="sr-only">: {server.name}</span>
          </Button>
        ) : null
      }
    >
      <p>{NOT_APPROVED_NOTE}</p>
      <ul aria-label={`Tools of ${server.name} to approve`} className="mt-2 space-y-1.5">
        {pending.map((tool) => (
          <li key={tool.name} className="flex flex-wrap items-center gap-2">
            <span className="min-w-0 font-medium text-fg">{toolLabel(tool)}</span>
            <ApprovalBadge approval={approvalOf(tool, app)} />
            <Button
              size="sm"
              variant="ghost"
              icon={ShieldCheck}
              onClick={() => onChange(approve(app, [tool]))}
              aria-label={`Approve ${toolLabel(tool)}`}
            >
              Approve
            </Button>
          </li>
        ))}
      </ul>
    </Alert>
  );
}

function ToolChoices({
  server,
  app,
  idPrefix,
  onChange,
}: {
  server: McpServerDto;
  app: AgentMcpAppDto;
  idPrefix: string;
  onChange: (next: AgentMcpAppDto) => void;
}) {
  // A newly picked tool is approved as the app lists it now; a changed policy keeps the approval.
  const setTool = (tool: { name: string; fingerprint?: string | null }, policy: McpToolPolicy | null) => {
    const { name } = tool;
    const others = app.tools.filter((item) => item.name !== name);
    const index = app.tools.findIndex((item) => item.name === name);
    const tools =
      policy === null
        ? others
        : index >= 0
          ? app.tools.map((item) => (item.name === name ? { ...item, policy } : item))
          : [...app.tools, { name, policy, fingerprint: tool.fingerprint ?? null }];
    onChange({ ...app, tools });
  };
  const unapproved = server.tools.some((tool) => {
    const approval = approvalOf(tool, app);
    return approval === 'changed' || approval === 'needed';
  });
  // Picks of tools the app no longer lists, such as after a rename: they are skipped while answering.
  const gone = app.tools.filter((tool) => !server.tools.some((item) => item.name === tool.name));

  if (server.tools.length === 0 && gone.length === 0) {
    return <p className="text-small text-fg-muted">The app lists no tools. Test it on the Tools page.</p>;
  }
  return (
    <>
      {unapproved ? <p className="text-small text-fg-secondary">Some picked tools are new or changed. {NOT_APPROVED_NOTE}</p> : null}
      <ul aria-label={`Tools of ${server.name}`} className="space-y-2">
        {server.tools.map((tool) => {
          const pick = app.tools.find((item) => item.name === tool.name);
          const policy = pick ? (pick.policy ?? defaultPolicy(tool, app)) : null;
          const effect = toolEffect(tool);
          const approval = approvalOf(tool, app);
          const labelId = `${idPrefix}-${tool.name}`;
          return (
            <li
              key={tool.name}
              className={clsx(
                'flex flex-wrap items-start gap-3 rounded-lg border p-3',
                policy ? 'border-accent-edge bg-accent-subtle' : 'border-border',
              )}
            >
              <input
                type="checkbox"
                checked={policy !== null}
                onChange={(event) => setTool(tool, event.target.checked ? defaultPolicy(tool, app) : null)}
                aria-labelledby={labelId}
                aria-describedby={`${labelId}-text`}
                className="mt-0.5 size-4 shrink-0"
              />
              <span className="min-w-0 flex-1">
                <span className="flex flex-wrap items-center gap-2">
                  <span id={labelId} className="text-sm font-medium text-fg">
                    {toolLabel(tool)}
                  </span>
                  <Badge tone={effect.tone}>{effect.label}</Badge>
                  <ApprovalBadge approval={approval} />
                </span>
                <span id={`${labelId}-text`} className="mt-0.5 line-clamp-2 block text-small text-fg-secondary">
                  {tool.description ?? tool.name}
                </span>
              </span>
              {policy ? (
                <span className="flex flex-wrap items-center gap-2">
                  {approval === 'changed' || approval === 'needed' ? (
                    <Button
                      size="sm"
                      variant="ghost"
                      icon={ShieldCheck}
                      onClick={() => onChange(approve(app, [tool]))}
                      aria-label={`Approve ${toolLabel(tool)}`}
                    >
                      Approve
                    </Button>
                  ) : null}
                  <Select
                    aria-label={`When ${toolLabel(tool)} may run`}
                    size="sm"
                    className="w-52"
                    value={policy}
                    onChange={(next) => setTool(tool, next)}
                    options={POLICY_CHOICES.map((choice) => ({ value: choice.value, label: choice.label }))}
                  />
                </span>
              ) : null}
            </li>
          );
        })}
        {gone.map((tool) => (
          <li key={tool.name} className="flex flex-wrap items-center gap-3 rounded-lg border border-dashed border-border p-3">
            <span className="min-w-0 flex-1">
              <span className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-small text-fg">{tool.name}</span>
                <Badge tone="neutral">No longer offered</Badge>
              </span>
              <span className="mt-0.5 block text-small text-fg-muted">The app does not list it any more, so the agent skips it.</span>
            </span>
            <Button size="sm" variant="ghost" icon={Trash2} onClick={() => setTool(tool, null)} aria-label={`Remove ${tool.name}`}>
              Remove
            </Button>
          </li>
        ))}
      </ul>
    </>
  );
}
