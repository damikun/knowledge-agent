import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Blocks, PlugZap, Plus, Settings2 } from 'lucide-react';
import { useId, useState } from 'react';
import { Link } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Card, EmptyState, ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { Page } from '../../components/ui/Page';
import { Skeleton } from '../../components/ui/Skeleton';
import { StatusLabel } from '../../components/ui/StatusDot';
import { formatRelativeTime } from '../../lib/format';
import { TestResult } from '../../components/ui/TestResult';
import { mcpApi, mcpKeys, useMcpServers } from './mcp/api';
import { McpServerSheet } from './mcp/McpServerSheet';
import { McpToolList } from './mcp/McpToolList';
import { serverStatus, toolsSummary } from './mcp/words';
import type { McpServerDto, McpTestResultDto } from './mcp/types';
import { useReturnState } from '../../lib/returnTo';

type Editing = { server: McpServerDto | null } | null;

export function ToolsPage() {
  const servers = useMcpServers();
  const [editing, setEditing] = useState<Editing>(null);

  return (
    <Page>
      <PageHeader
        title="Tools"
        description="Apps your agents can look things up in or act on, such as your orders or your CRM. Connect them here, then turn them on for an agent on its Tools tab."
        actions={
          <Button variant="primary" icon={Plus} onClick={() => setEditing({ server: null })}>
            Add MCP server
          </Button>
        }
      />
      <div className="max-w-5xl space-y-4">
        {servers.isPending ? (
          <>
            <p role="status" className="sr-only">
              Loading the tools
            </p>
            <Skeleton shape="card" className="h-40" />
          </>
        ) : servers.isError ? (
          <ErrorMessage error={servers.error} title="Could not load the MCP servers" />
        ) : servers.data.length === 0 ? (
          <Card>
            <EmptyState
              icon={Blocks}
              title="No apps connected yet"
              description="Connect an app that offers tools over MCP, such as a shop, a CRM or a ticket system. Tools that change something ask the person in the chat first."
            />
          </Card>
        ) : (
          <ul aria-label="MCP servers" className="space-y-4">
            {servers.data.map((server) => (
              <ServerCard key={server.id} server={server} onEdit={() => setEditing({ server })} />
            ))}
          </ul>
        )}
      </div>
      {editing ? <McpServerSheet server={editing.server} onClose={() => setEditing(null)} /> : null}
    </Page>
  );
}

function ServerCard({ server, onEdit }: { server: McpServerDto; onEdit: () => void }) {
  const returnState = useReturnState();
  const headingId = useId();
  const queryClient = useQueryClient();
  const [result, setResult] = useState<McpTestResultDto | null>(null);
  const test = useMutation({
    mutationFn: () => mcpApi.test(server.id),
    onSuccess: (outcome) => {
      setResult(outcome);
      void queryClient.invalidateQueries({ queryKey: mcpKeys.all });
    },
  });
  const status = serverStatus(server);

  return (
    <li>
      <section aria-labelledby={headingId} className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-card">
        <div className="flex flex-wrap items-start gap-3">
          <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-secondary">
            <Blocks className="size-5" aria-hidden />
          </span>
          <div className="min-w-0 flex-1 space-y-0.5">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <h2 id={headingId} className="text-section text-fg">
                {server.name}
              </h2>
              <StatusLabel tone={status.tone}>{status.label}</StatusLabel>
            </div>
            <p className="truncate font-mono text-xs text-fg-muted">{server.url}</p>
            <p className="text-small text-fg-secondary">
              {server.lastCheck ? `Checked ${formatRelativeTime(server.lastCheck.checkedAt)}` : 'Not tested yet'}
              {server.lastCheck && !server.lastCheck.ok && server.lastCheck.error ? `: ${server.lastCheck.error}` : null}
            </p>
            <p className="text-small text-fg-secondary">
              {server.usedBy.length === 0 ? (
                'No agent uses it yet. Turn it on for an agent on the agent\'s Tools tab.'
              ) : (
                <>
                  Used by{' '}
                  {server.usedBy.map((use, index) => (
                    <span key={use.agentId}>
                      {index > 0 ? ', ' : null}
                      <Link to={`/agents/${use.agentId}?tab=tools`} state={returnState} className="font-medium text-accent-text hover:underline">
                        {use.agentName}
                      </Link>{' '}
                      ({use.mode === 'chosen' ? `${use.toolCount} picked` : 'all tools'})
                    </span>
                  ))}
                </>
              )}
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button icon={PlugZap} loading={test.isPending} onClick={() => test.mutate()}>
              Test
            </Button>
            <Button icon={Settings2} onClick={onEdit} aria-label={`Edit ${server.name}`}>
              Edit
            </Button>
          </div>
        </div>
        {result ? <TestResult result={result} /> : null}
        {test.isError ? <ErrorMessage error={test.error} title="The test did not run" /> : null}
        <p className="text-small font-medium text-fg">{toolsSummary(server.tools)}</p>
        <McpToolList tools={server.tools} label={`Tools of ${server.name}`} />
      </section>
    </li>
  );
}
