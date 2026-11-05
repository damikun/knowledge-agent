import { BadgeCheck, Columns3, UserRound, Users } from 'lucide-react';
import { useId, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { Avatar } from '../../components/ui/Avatar';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { Popover } from '../../components/ui/Popover';
import { Skeleton } from '../../components/ui/Skeleton';
import { Table, TableCell, TableHead, TableHeader, TableRow } from '../../components/ui/Table';
import { formatDateTime, formatNumber, formatRelativeTime } from '../../lib/format';
import type { usePeople } from './api';
import { formatAttributeValue } from './filters';
import { personPath } from './paths';
import type { PersonSummary } from './types';

export interface Column {
  key: string;
  label: string;
  type: string;
  value: (person: PersonSummary) => unknown;
}

export const BUILT_IN_COLUMNS: readonly Column[] = [
  { key: 'sessions', label: 'Sessions', type: 'number', value: (person) => person.sessionCount },
  { key: 'returning', label: 'Returning', type: 'boolean', value: (person) => person.returning },
  { key: 'language', label: 'Language', type: 'string', value: (person) => person.language },
  { key: 'last_page', label: 'Last page', type: 'string', value: (person) => person.lastPageUrl },
];

const COLUMNS_KEY = 'ka-contacts-columns';

export function readColumns(): string[] {
  try {
    const stored: unknown = JSON.parse(window.localStorage.getItem(COLUMNS_KEY) ?? '[]');
    return Array.isArray(stored) ? stored.filter((item): item is string => typeof item === 'string') : [];
  } catch {
    return [];
  }
}

export function storeColumns(columns: string[]): void {
  try {
    window.localStorage.setItem(COLUMNS_KEY, JSON.stringify(columns));
  } catch {
    // Blocked storage only forgets the choice.
  }
}

function IdentifiedBadge() {
  return (
    <Badge tone="accent" title="Signed in on your website, verified with identity verification">
      <BadgeCheck className="size-3" aria-hidden />
      Signed in
    </Badge>
  );
}

export function ColumnChooser({ columns, chosen, onChange }: { columns: readonly Column[]; chosen: readonly string[]; onChange: (chosen: string[]) => void }) {
  const id = useId();
  const anchor = useRef<HTMLButtonElement | null>(null);
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button ref={anchor} size="sm" icon={Columns3} aria-expanded={open} onClick={() => setOpen(!open)}>
        Columns
      </Button>
      <Popover open={open} onClose={() => setOpen(false)} anchorRef={anchor} label="Columns" align="end">
        <fieldset className="max-h-80 w-60 space-y-1 overflow-y-auto p-3">
          <legend className="mb-1 text-small font-medium text-fg">Show columns</legend>
          {columns.map((column) => (
            <label key={column.key} htmlFor={`${id}-${column.key}`} className="flex h-7 items-center gap-2 text-small text-fg">
              <input
                id={`${id}-${column.key}`}
                type="checkbox"
                className="size-4 accent-accent"
                checked={chosen.includes(column.key)}
                onChange={(event) => onChange(event.target.checked ? [...chosen, column.key] : chosen.filter((key) => key !== column.key))}
              />
              <span className="min-w-0 truncate">{column.label}</span>
            </label>
          ))}
        </fieldset>
      </Popover>
    </>
  );
}

function PersonRow({ person, columns, onOpen }: { person: PersonSummary; columns: readonly Column[]; onOpen: () => void }) {
  return (
    <TableRow className="cursor-pointer" onClick={onOpen}>
      <TableCell className="max-w-0 w-[40%]">
        {/* 40px content plus cell padding gives the 56px row that fits a name over an email. */}
        <div className="flex min-h-10 min-w-0 items-center gap-3">
          <Avatar name={person.name} size="md" {...(person.identified || person.email ? {} : { icon: UserRound })} />
          <div className="min-w-0">
            <div className="flex min-w-0 items-center gap-2">
              {/* The link makes the row keyboard-reachable; the row click is a larger mouse target. */}
              <Link to={personPath(person.id)} className="truncate font-medium text-fg hover:underline" onClick={(event) => event.stopPropagation()}>
                {person.name}
              </Link>
              {person.identified ? <IdentifiedBadge /> : null}
            </div>
            <p className="truncate text-xs text-fg-muted">{person.email ?? person.externalId ?? 'No email'}</p>
          </div>
        </div>
      </TableCell>
      <TableCell className="hidden md:table-cell">
        <span className="line-clamp-1">{person.widgets.map((widget) => widget.name).join(', ') || 'None'}</span>
      </TableCell>
      {columns.map((column) => (
        <TableCell key={column.key} className="max-w-48">
          <span className="line-clamp-1 break-all">{formatAttributeValue(column.value(person), column.type) || <span className="text-fg-muted">Not set</span>}</span>
        </TableCell>
      ))}
      <TableCell numeric>{formatNumber(person.conversationCount)}</TableCell>
      <TableCell className="whitespace-nowrap">
        <time dateTime={person.lastSeenAt} title={formatDateTime(person.lastSeenAt)}>
          {formatRelativeTime(person.lastSeenAt)}
        </time>
      </TableCell>
    </TableRow>
  );
}

export interface PeopleTableProps {
  people: ReturnType<typeof usePeople>;
  columns: readonly Column[];
  /** Makes an empty list offer to clear the search and filters. */
  filtered: boolean;
  onClearFilters: () => void;
}

export function PeopleTable({ people, columns: shownColumns, filtered, onClearFilters }: PeopleTableProps) {
  const navigate = useNavigate();
  const rows = people.data?.pages.flatMap((page) => page.items) ?? [];
  return (
    <>
      {people.isPending ? (
        <div className="divide-y divide-border-subtle" aria-busy="true" aria-label="Loading people">
          {Array.from({ length: 6 }, (_, index) => (
            <div key={index} className="flex h-14 items-center gap-3 px-6">
              <Skeleton className="size-8" />
              <div className="flex-1 space-y-1.5">
                <Skeleton className="h-3 w-40" />
                <Skeleton className="h-3 w-56" />
              </div>
            </div>
          ))}
        </div>
      ) : people.isError ? (
        <div className="p-6">
          <ErrorMessage error={people.error} title="Contacts could not be loaded" />
        </div>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={Users}
          title={filtered ? 'Nobody matches' : 'No people yet'}
          description={
            filtered ? 'Try another name or filter, or clear the filters.' : 'Contacts show here once they start a conversation in a widget or your website signs them in.'
          }
          action={filtered ? <Button onClick={onClearFilters}>Clear filters</Button> : undefined}
        />
      ) : (
        <Table aria-label="Contacts" aria-busy={people.isFetching && !people.isFetchingNextPage}>
          <TableHead>
            <TableHeader>Contact</TableHeader>
            <TableHeader className="hidden md:table-cell">Widgets</TableHeader>
            {shownColumns.map((column) => (
              <TableHeader key={column.key}>{column.label}</TableHeader>
            ))}
            <TableHeader numeric>Conversations</TableHeader>
            <TableHeader>Last seen</TableHeader>
          </TableHead>
          <tbody>
            {rows.map((person) => (
              <PersonRow key={person.id} person={person} columns={shownColumns} onOpen={() => void navigate(personPath(person.id))} />
            ))}
          </tbody>
        </Table>
      )}

      {people.hasNextPage ? (
        <div className="flex justify-center border-t border-border-subtle px-6 py-2">
          <Button size="sm" variant="ghost" onClick={() => void people.fetchNextPage()} loading={people.isFetchingNextPage}>
            Show more
          </Button>
        </div>
      ) : null}
    </>
  );
}
