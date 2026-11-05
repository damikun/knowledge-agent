import { clsx } from 'clsx';
import { Check, ExternalLink, Minus, Search, UsersRound } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { Avatar } from '../../components/ui/Avatar';
import { Badge } from '../../components/ui/Badge';
import { Button, buttonClass } from '../../components/ui/Button';
import { Card, EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { Page, PageHeader } from '../../components/ui/Page';
import { Skeleton } from '../../components/ui/Skeleton';
import { Table, TableCell, TableHead, TableHeader, TableRow, TableRowHeader } from '../../components/ui/Table';
import { formatDate, formatDateTime, formatNumber, formatRelativeTime, safeExternalUrl } from '../../lib/format';
import { useMe } from '../auth/api';
import { useTeam } from './api';
import type { TeamMemberDto } from './types';

/** As the API enforces them. */
const ROLE_ABILITIES: Array<{ label: string; admin: boolean; support: boolean }> = [
  { label: 'Answer conversations in the Inbox and take over from the agent', admin: true, support: true },
  { label: 'Find and edit contacts, label conversations, read feedback', admin: true, support: true },
  { label: 'See Home and Analytics', admin: true, support: false },
  { label: 'Set up agents, widgets, sources and prompts, and use the Playground', admin: true, support: false },
  { label: 'Change connections, AI models and system settings, see the team', admin: true, support: false },
  { label: 'Sign in to Langfuse', admin: true, support: false },
];

function matches(member: TeamMemberDto, query: string): boolean {
  const needle = query.trim().toLocaleLowerCase();
  return !needle || [member.name, member.email ?? '', member.role].some((value) => value.toLocaleLowerCase().includes(needle));
}

function RoleBadge({ role }: { role: string }) {
  return role === 'admin' ? <Badge tone="accent">Admin</Badge> : <Badge>Support</Badge>;
}

function Allowed({ allowed, role }: { allowed: boolean; role: string }) {
  return allowed ? (
    <span className="inline-flex text-success">
      <Check className="size-4" aria-hidden />
      <span className="sr-only">{role} can</span>
    </span>
  ) : (
    <span className="inline-flex text-fg-muted">
      <Minus className="size-4" aria-hidden />
      <span className="sr-only">{role} cannot</span>
    </span>
  );
}

const CONSOLE_HINT = 'You need user admin rights in Keycloak (manage-users) to open it.';

/** Admin here doesn't grant access to Keycloak's console, so the link is only live for its user admins. */
function ConsoleLink({ url, allowed }: { url: string; allowed: boolean }) {
  const hintId = useId();
  if (allowed) {
    return (
      <a href={url} target="_blank" rel="noopener noreferrer" className={clsx(buttonClass({ size: 'sm' }), 'shrink-0')}>
        Open Keycloak
        <ExternalLink className="size-3.5" aria-hidden />
      </a>
    );
  }
  // Disabled buttons get no pointer events, so the wrapper carries the hover hint; screen readers get aria-describedby.
  return (
    <span title={CONSOLE_HINT} className="inline-flex shrink-0">
      <Button size="sm" disabled aria-describedby={hintId}>
        Open Keycloak
        <ExternalLink className="size-3.5" aria-hidden />
      </Button>
      <span id={hintId} className="sr-only">
        {CONSOLE_HINT}
      </span>
    </span>
  );
}

function LoadingTable() {
  return (
    <div role="status" className="space-y-3 p-6">
      <span className="sr-only">Loading the team</span>
      {[0, 1, 2].map((row) => (
        <div key={row} className="flex items-center gap-3">
          <Skeleton className="size-8 rounded-full" />
          <Skeleton className="h-3.5 w-48" />
          <Skeleton className="ml-auto h-3.5 w-24" />
        </div>
      ))}
    </div>
  );
}

function MembersTable({ members }: { members: TeamMemberDto[] }) {
  return (
    <Table aria-label="Team members">
      <TableHead>
        <TableHeader>Person</TableHeader>
        <TableHeader>Role</TableHeader>
        <TableHeader>Last seen</TableHeader>
        <TableHeader title="Their first sign-in">Added</TableHeader>
      </TableHead>
      <tbody>
        {members.map((member) => (
          <TableRow key={member.id}>
            <TableRowHeader>
              <div className="flex min-w-0 items-center gap-3">
                <Avatar name={member.name} size="sm" />
                <div className="min-w-0">
                  <div className="truncate text-fg">{member.name}</div>
                  {member.email ? <div className="truncate text-xs font-normal text-fg-muted">{member.email}</div> : null}
                </div>
              </div>
            </TableRowHeader>
            <TableCell>
              <RoleBadge role={member.role} />
            </TableCell>
            <TableCell className="whitespace-nowrap" title={formatDateTime(member.lastSeenAt)}>
              {formatRelativeTime(member.lastSeenAt)}
            </TableCell>
            <TableCell className="whitespace-nowrap" title={formatDateTime(member.firstSeenAt)}>
              {formatDate(member.firstSeenAt)}
            </TableCell>
          </TableRow>
        ))}
      </tbody>
    </Table>
  );
}

export function TeamPage() {
  const team = useTeam();
  const me = useMe();
  const [query, setQuery] = useState('');
  const members = team.data?.members;
  const shown = useMemo(() => (members ?? []).filter((member) => matches(member, query)), [members, query]);
  const consoleUrl = safeExternalUrl(team.data?.keycloakConsoleUrl);

  return (
    <Page>
      <PageHeader
        title="Team"
        description="Everyone who signed in, with their role. People and roles are managed in your company login."
        actions={consoleUrl ? <ConsoleLink url={consoleUrl} allowed={me.data?.canManageUsers === true} /> : null}
      />

      <section aria-labelledby="team-members-heading" className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
          <h2 id="team-members-heading" className="text-section text-fg">
            Members
            {members ? <span className="ml-2 text-small font-normal text-fg-muted tabular-nums">{formatNumber(members.length)}</span> : null}
          </h2>
          <div className="relative w-full sm:w-72">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
            <input
              type="search"
              aria-label="Search the team"
              placeholder="Search by name, email or role"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              className="field pl-8"
            />
          </div>
        </div>
        <Card className="overflow-hidden">
          {team.isPending ? (
            <LoadingTable />
          ) : team.isError ? (
            <div className="p-6">
              <ErrorMessage error={team.error} title="Could not load the team" />
            </div>
          ) : members && members.length === 0 ? (
            <EmptyState icon={UsersRound} title="Nobody signed in yet" description="People appear here after their first sign-in." className="py-10" />
          ) : shown.length === 0 ? (
            <EmptyState icon={Search} title="No match" description={`Nobody matches “${query.trim()}”.`} className="py-10" />
          ) : (
            <MembersTable members={shown} />
          )}
        </Card>
      </section>

      <section aria-labelledby="team-roles-heading" className="max-w-3xl space-y-3">
        <h2 id="team-roles-heading" className="text-section text-fg">
          What each role can do
        </h2>
        <Card className="overflow-hidden">
          <Table aria-label="What each role can do">
            <TableHead>
              <TableHeader>Ability</TableHeader>
              <TableHeader className="w-24 text-center">Admin</TableHeader>
              <TableHeader className="w-24 text-center">Support</TableHeader>
            </TableHead>
            <tbody>
              {ROLE_ABILITIES.map((ability) => (
                <TableRow key={ability.label}>
                  <TableRowHeader className="font-normal text-fg-secondary">{ability.label}</TableRowHeader>
                  <TableCell className="text-center">
                    <Allowed allowed={ability.admin} role="Admin" />
                  </TableCell>
                  <TableCell className="text-center">
                    <Allowed allowed={ability.support} role="Support" />
                  </TableCell>
                </TableRow>
              ))}
            </tbody>
          </Table>
        </Card>
      </section>
    </Page>
  );
}
