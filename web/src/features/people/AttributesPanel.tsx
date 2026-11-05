import { Search, Tags, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Button } from '../../components/ui/Button';
import { Card, EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { ConfirmDialog } from '../../components/ui/Modal';
import { Select } from '../../components/ui/Select';
import { Skeleton } from '../../components/ui/Skeleton';
import { Switch } from '../../components/ui/Switch';
import { Table, TableCell, TableHead, TableHeader, TableRow } from '../../components/ui/Table';
import { toast } from '../../components/ui/Toast';
import { errorMessage } from '../../lib/api';
import { formatNumber, formatRelativeTime } from '../../lib/format';
import { useAttributeRegistry, useDeleteAttribute, useUpdateAttribute } from './api';
import { TYPE_LABELS } from './filters';
import type { AttributeTrust, ContactAttribute } from './types';

const TRUST_OPTIONS: readonly { value: AttributeTrust; label: string }[] = [
  { value: 'any', label: 'Page, user or server' },
  { value: 'verified', label: 'Signed-in user or server' },
  { value: 'server', label: 'Server only' },
];

// Saves on blur or Enter; Escape restores the saved value.
function LabelInput({ attribute, disabled }: { attribute: ContactAttribute; disabled: boolean }) {
  const update = useUpdateAttribute();
  const [label, setLabel] = useState(attribute.label);
  const save = () => {
    const trimmed = label.trim();
    if (!trimmed || trimmed === attribute.label) {
      setLabel(attribute.label);
      return;
    }
    update.mutate(
      { key: attribute.key, body: { label: trimmed } },
      { onError: (error) => toast({ title: 'The label could not be saved', description: errorMessage(error), tone: 'danger' }) },
    );
  };
  return (
    <input
      aria-label={`Label of ${attribute.key}`}
      className="field h-7 text-small"
      value={label}
      maxLength={60}
      disabled={disabled}
      onChange={(event) => setLabel(event.target.value)}
      onBlur={save}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.currentTarget.blur();
        } else if (event.key === 'Escape') {
          setLabel(attribute.label);
        }
      }}
    />
  );
}

function AttributeRow({ attribute, admin, onDelete }: { attribute: ContactAttribute; admin: boolean; onDelete: () => void }) {
  const update = useUpdateAttribute();
  const change = (body: Partial<Pick<ContactAttribute, 'visible' | 'exposeToAgent' | 'trust'>>) =>
    update.mutate({ key: attribute.key, body }, { onError: (error) => toast({ title: 'The attribute could not be saved', description: errorMessage(error), tone: 'danger' }) });
  return (
    <TableRow>
      <TableCell className="min-w-44">
        <LabelInput key={attribute.label} attribute={attribute} disabled={!admin} />
        <p className="mt-1 font-mono text-xs text-fg-muted">{attribute.key}</p>
      </TableCell>
      <TableCell>{TYPE_LABELS[attribute.type]}</TableCell>
      <TableCell numeric>{formatNumber(attribute.contacts)}</TableCell>
      <TableCell className="whitespace-nowrap text-fg-muted">{formatRelativeTime(attribute.lastSeenAt)}</TableCell>
      <TableCell>
        <Switch checked={attribute.visible} disabled={!admin} onChange={(visible) => change({ visible })} label={<span className="sr-only">Show {attribute.label}</span>} />
      </TableCell>
      <TableCell>
        <Switch
          checked={attribute.exposeToAgent}
          disabled={!admin}
          onChange={(exposeToAgent) => change({ exposeToAgent })}
          label={<span className="sr-only">Give {attribute.label} to the agent</span>}
        />
      </TableCell>
      <TableCell className="min-w-48">
        <Select aria-label={`Who may set ${attribute.label}`} size="sm" value={attribute.trust} options={TRUST_OPTIONS} disabled={!admin} onChange={(trust) => change({ trust })} />
      </TableCell>
      <TableCell>
        {admin ? <Button size="icon-sm" variant="danger-ghost" icon={Trash2} aria-label={`Delete ${attribute.label}`} onClick={onDelete} /> : null}
      </TableCell>
    </TableRow>
  );
}

export function AttributesPanel({ admin }: { admin: boolean }) {
  const [search, setSearch] = useState('');
  const registry = useAttributeRegistry();
  const remove = useDeleteAttribute();
  const [deleting, setDeleting] = useState<ContactAttribute | null>(null);
  const items = registry.data?.items ?? [];

  const term = search.trim().toLowerCase();
  const shown = term ? items.filter((item) => item.label.toLowerCase().includes(term) || item.key.toLowerCase().includes(term)) : items;

  return (
    <section aria-label="Attributes">
      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-2 border-b border-border-subtle px-4 py-2 lg:px-6">
          <form role="search" aria-label="Search attributes" className="relative w-full min-w-48 sm:w-auto sm:max-w-80 sm:flex-1" onSubmit={(event) => event.preventDefault()}>
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
            <input
              type="search"
              aria-label="Search attributes"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Label or key"
              className="field h-7 py-0 pl-8 text-small"
            />
          </form>
          <p className="ml-auto text-small whitespace-nowrap text-fg-muted tabular-nums" aria-live="polite">
            {registry.data ? `${formatNumber(shown.length)} ${shown.length === 1 ? 'attribute' : 'attributes'}` : ''}
          </p>
        </div>
        {registry.isPending ? (
          <div className="space-y-2 p-6" aria-busy="true" aria-label="Loading attributes">
            <Skeleton className="h-8" />
            <Skeleton className="h-8" />
          </div>
        ) : registry.isError ? (
          <div className="p-6">
            <ErrorMessage error={registry.error} title="Attributes could not be loaded" />
          </div>
        ) : items.length === 0 ? (
          <EmptyState icon={Tags} title="No attributes yet" description="They show here once your website sends one, for example plan or company. See Embed code in the widget editor." />
        ) : shown.length === 0 ? (
          <EmptyState icon={Tags} title="No attribute matches" description="Try another label or key." />
        ) : (
          <Table aria-label="Attributes">
            <TableHead>
              <TableHeader>Label</TableHeader>
              <TableHeader>Type</TableHeader>
              <TableHeader numeric>Contacts</TableHeader>
              <TableHeader>Last sent</TableHeader>
              <TableHeader title="Offered as a column on the contacts list">Shown</TableHeader>
              <TableHeader title="Given to the assistant in conversations with that person">Agent</TableHeader>
              <TableHeader>Who may set it</TableHeader>
              <TableHeader>
                <span className="sr-only">Delete</span>
              </TableHeader>
            </TableHead>
            <tbody>
              {shown.map((attribute) => (
                <AttributeRow key={attribute.key} attribute={attribute} admin={admin} onDelete={() => setDeleting(attribute)} />
              ))}
            </tbody>
          </Table>
        )}
        <p className="border-t border-border-subtle bg-surface-subtle px-4 py-2.5 text-xs text-fg-secondary lg:px-6">
          Your website sends these with identify(), update() or from your server, and the first value sets the type. Shown offers the attribute as a column on
          the contacts list; Agent gives it to the assistant in conversations with that person. Values from a page nobody signed in to are not verified.
        </p>
      </Card>
      <ConfirmDialog
        open={deleting !== null}
        title={`Delete ${deleting?.label ?? 'attribute'}?`}
        message={`This removes the value from ${formatNumber(deleting?.contacts ?? 0)} ${deleting?.contacts === 1 ? 'contact' : 'contacts'} and cannot be undone. Segments that filter by it then treat everyone as not having it. If your website keeps sending it, it comes back.`}
        confirmLabel="Delete attribute"
        busy={remove.isPending}
        error={remove.error ? <ErrorMessage error={remove.error} /> : null}
        onCancel={() => setDeleting(null)}
        onConfirm={() => {
          const target = deleting;
          if (target) {
            remove.mutate(target.key, {
              onSuccess: () => {
                toast({ title: `Deleted ${target.label}`, tone: 'success' });
                setDeleting(null);
              },
            });
          }
        }}
      />
    </section>
  );
}
