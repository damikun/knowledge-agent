import { useMutation } from '@tanstack/react-query';
import { ChevronLeft, ChevronRight, Copy, Pencil, Search, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { Card, EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { FilterChips } from '../../components/ui/FilterChips';
import { Menu } from '../../components/ui/Menu';
import { ConfirmDialog } from '../../components/ui/Modal';
import { Table, TableCell, TableHead, TableHeader, TableRow } from '../../components/ui/Table';
import { toast } from '../../components/ui/Toast';
import { formatNumber, pluralize } from '../../lib/format';
import { useReturnState } from '../../lib/returnTo';
import { onlyConversationPath } from '../inbox/inboxState';
import { evalsApi } from './api';
import {
  duplicateRequest,
  expectedPreview,
  filterDatasetItems,
  ITEM_SOURCE_LABELS,
  itemSource,
  ITEMS_PAGE_SIZE,
  pageOf,
  SOURCE_ORDER,
  sourceCounts,
  type ItemSourceFilter,
} from './datasetItems';
import type { EvalDatasetItemDto } from './types';

function SourceBadge({ item }: { item: EvalDatasetItemDto }) {
  const returnState = useReturnState();
  const source = itemSource(item);
  if (source === 'conversation' && item.conversationId) {
    return (
      <Link to={onlyConversationPath(item.conversationId)} state={returnState} className="text-xs font-medium text-accent-text hover:underline">
        {ITEM_SOURCE_LABELS.conversation}
      </Link>
    );
  }
  return <span className="text-xs text-fg-secondary">{ITEM_SOURCE_LABELS[source]}</span>;
}

interface QuestionsTableProps {
  items: EvalDatasetItemDto[];
  selected: ReadonlySet<string>;
  onSelect: (ids: string[], selected: boolean) => void;
  onEdit: (item: EvalDatasetItemDto) => void;
  onDuplicate: (item: EvalDatasetItemDto) => void;
  onDelete: (item: EvalDatasetItemDto) => void;
}

function QuestionsTable({ items, selected, onSelect, onEdit, onDuplicate, onDelete }: QuestionsTableProps) {
  const allSelected = items.length > 0 && items.every((item) => selected.has(item.id));
  return (
    <Table aria-label="Questions" className="min-w-[44rem] table-fixed">
      <colgroup>
        <col className="w-12" />
        <col />
        <col className="w-[32%]" />
        <col className="w-32" />
        <col className="w-40" />
        <col className="w-14" />
      </colgroup>
      <TableHead>
        <TableHeader>
          <input
            type="checkbox"
            className="size-4"
            checked={allSelected}
            onChange={() => onSelect(
              items.map((item) => item.id),
              !allSelected,
            )}
            aria-label={allSelected ? 'Clear the selection on this page' : 'Select every question on this page'}
          />
        </TableHeader>
        <TableHeader>Question</TableHeader>
        <TableHeader>Expected</TableHeader>
        <TableHeader>Added from</TableHeader>
        <TableHeader>Tags</TableHeader>
        <TableHeader>
          <span className="sr-only">Actions</span>
        </TableHeader>
      </TableHead>
      <tbody>
        {items.map((item) => {
          const preview = expectedPreview(item);
          const turns = item.history?.length ?? 0;
          return (
            <TableRow key={item.id} selected={selected.has(item.id)} data-item={item.id}>
              <TableCell className="align-top">
                <input
                  type="checkbox"
                  className="size-4"
                  checked={selected.has(item.id)}
                  onChange={(event) => onSelect([item.id], event.target.checked)}
                  aria-label={`Select “${item.question}”`}
                />
              </TableCell>
              <TableCell className="align-top whitespace-normal">
                <button type="button" onClick={() => onEdit(item)} className="line-clamp-2 text-left font-medium break-words text-fg hover:underline">
                  {item.question}
                </button>
                {turns > 0 ? (
                  <Badge className="mt-1" title={pluralize(turns, 'earlier turn')}>
                    Follow-up
                  </Badge>
                ) : null}
              </TableCell>
              <TableCell className="align-top whitespace-normal">
                {item.answerable ? (
                  <span className="line-clamp-2 break-words text-fg-secondary">{preview || <span className="text-fg-muted">Not set</span>}</span>
                ) : (
                  <Badge tone="warning">{preview}</Badge>
                )}
              </TableCell>
              <TableCell className="align-top">
                <SourceBadge item={item} />
              </TableCell>
              <TableCell className="align-top whitespace-normal">
                <span className="flex flex-wrap gap-1">
                  {item.tags.map((tag) => (
                    <Badge key={tag}>{tag}</Badge>
                  ))}
                </span>
              </TableCell>
              <TableCell className="align-top">
                <Menu
                  label={`Actions for “${item.question}”`}
                  items={[
                    { label: 'Edit', icon: Pencil, onSelect: () => onEdit(item) },
                    { label: 'Duplicate', icon: Copy, onSelect: () => onDuplicate(item) },
                    { label: 'Delete', icon: Trash2, onSelect: () => onDelete(item), danger: true },
                  ]}
                />
              </TableCell>
            </TableRow>
          );
        })}
      </tbody>
    </Table>
  );
}

export interface DatasetQuestionsProps {
  datasetId: string;
  items: EvalDatasetItemDto[];
  onChanged: () => void;
  onEdit: (item: EvalDatasetItemDto) => void;
}

export function DatasetQuestions({ datasetId: id, items: all, onChanged, onEdit }: DatasetQuestionsProps) {
  const [query, setQuery] = useState('');
  const [source, setSource] = useState<ItemSourceFilter>('all');
  const [page, setPage] = useState(0);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [deleting, setDeleting] = useState<EvalDatasetItemDto[] | null>(null);
  const filtered = filterDatasetItems(all, query, source);
  const { rows, page: current } = pageOf(filtered, page);
  const counts = sourceCounts(all);

  const duplicate = useMutation({
    mutationFn: (item: EvalDatasetItemDto) => evalsApi.addItem(id, duplicateRequest(item)),
    onSuccess: (copy) => {
      onChanged();
      toast({ title: 'Question duplicated', description: 'Change the copy as needed.', tone: 'success' });
      onEdit(copy);
    },
    onError: () => toast({ title: 'The question was not duplicated', tone: 'danger' }),
  });
  const removeItems = useMutation({
    mutationFn: (targets: EvalDatasetItemDto[]) =>
      targets.length === 1 ? evalsApi.deleteItem(id, targets[0]!.id).then(() => ({ deleted: 1 })) : evalsApi.deleteItems(id, targets.map((item) => item.id)),
    onSuccess: (result) => {
      setDeleting(null);
      setSelected(new Set());
      onChanged();
      toast({ title: `Deleted ${pluralize(result.deleted, 'question')}`, tone: 'success' });
    },
  });
  const select = (ids: string[], on: boolean) =>
    setSelected((currentSelection) => {
      const next = new Set(currentSelection);
      for (const itemId of ids) {
        if (on) {
          next.add(itemId);
        } else {
          next.delete(itemId);
        }
      }
      return next;
    });
  const selectedItems = all.filter((item) => selected.has(item.id));

  const chips = [
    { value: 'all' as const, label: 'All', count: all.length },
    ...SOURCE_ORDER.filter((key) => counts[key] > 0).map((key) => ({ value: key, label: ITEM_SOURCE_LABELS[key], count: counts[key] })),
  ];
return (
  <>
    <section aria-labelledby="dataset-questions-heading" className="space-y-3">
      <h2 id="dataset-questions-heading" className="text-section text-fg">
        Questions
      </h2>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <div className="relative w-full sm:w-72">
          <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
          <input
            type="search"
            aria-label="Search the questions"
            placeholder="Search the questions"
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setPage(0);
            }}
            className="field pl-8"
          />
        </div>
        {chips.length > 2 ? (
          <FilterChips
            label="Added from"
            options={chips}
            value={source}
            onChange={(value) => {
              setSource(value);
              setPage(0);
            }}
          />
        ) : null}
      </div>
      <Card className="overflow-hidden">
        {selected.size > 0 ? (
          <div role="toolbar" aria-label="Selected questions" className="flex flex-wrap items-center gap-2 border-b border-border-subtle bg-surface-subtle px-4 py-2">
            <span className="text-small text-fg">{pluralize(selected.size, 'question')} selected</span>
            <Button size="sm" variant="danger-ghost" icon={Trash2} onClick={() => setDeleting(selectedItems)}>
              Delete selected
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setSelected(new Set())}>
              Clear selection
            </Button>
          </div>
        ) : null}
        {filtered.length === 0 ? (
          <EmptyState icon={Search} title="No questions match" description="Try other words or another filter." />
        ) : (
          <QuestionsTable
            items={rows}
            selected={selected}
            onSelect={select}
            onEdit={onEdit}
            onDuplicate={(item) => duplicate.mutate(item)}
            onDelete={(item) => {
              removeItems.reset();
              setDeleting([item]);
            }}
          />
        )}
        {filtered.length > ITEMS_PAGE_SIZE ? (
          <div className="flex items-center justify-between gap-2 border-t border-border-subtle px-4 py-2">
            <span className="text-xs text-fg-muted tabular-nums">
              {formatNumber(current * ITEMS_PAGE_SIZE + 1)} to {formatNumber(Math.min(filtered.length, (current + 1) * ITEMS_PAGE_SIZE))} of{' '}
              {formatNumber(filtered.length)}
            </span>
            <div className="flex gap-1">
              <Button size="sm" variant="ghost" icon={ChevronLeft} disabled={current === 0} onClick={() => setPage(current - 1)}>
                Previous
              </Button>
              <Button size="sm" variant="ghost" icon={ChevronRight} disabled={(current + 1) * ITEMS_PAGE_SIZE >= filtered.length} onClick={() => setPage(current + 1)}>
                Next
              </Button>
            </div>
          </div>
        ) : null}
      </Card>
    </section>
      <ConfirmDialog
        open={deleting !== null}
        title={deleting && deleting.length > 1 ? `Delete ${deleting.length} questions?` : 'Delete question?'}
        message={
          deleting && deleting.length === 1 ? <>“{deleting[0]!.question}” will be removed from the dataset. Past runs keep their results.</> : 'They will be removed from the dataset. Past runs keep their results.'
        }
        confirmLabel={deleting && deleting.length > 1 ? `Delete ${deleting.length} questions` : 'Delete question'}
        busy={removeItems.isPending}
        error={removeItems.isError ? <ErrorMessage error={removeItems.error} /> : null}
        onConfirm={() => deleting && removeItems.mutate(deleting)}
        onCancel={() => setDeleting(null)}
      />
    </>
  );
}
