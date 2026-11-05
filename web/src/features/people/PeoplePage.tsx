import { BookmarkPlus, PanelLeft, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Drawer } from '../../components/ui/Drawer';
import { Card } from '../../components/ui/feedback';
import { Page } from '../../components/ui/Page';
import { SegmentedControl, type SegmentedOption } from '../../components/ui/SegmentedControl';
import { Select, type SelectOption } from '../../components/ui/Select';
import { formatNumber } from '../../lib/format';
import { useKeptReturnState } from '../../lib/returnTo';
import { useDebouncedValue } from '../../lib/hooks/useDebouncedValue';
import { isAdmin, useMe } from '../auth/api';
import { useWidgets } from '../widgets/api';
import { useAttributeRegistry, usePeople, usePeopleCount, useSegments } from './api';
import { AttributesPanel } from './AttributesPanel';
import { FilterBuilder } from './FilterBuilder';
import { BUILT_IN_SEGMENTS, decodeFilter, encodeFilter } from './filters';
import { BUILT_IN_COLUMNS, ColumnChooser, PeopleTable, readColumns, storeColumns, type Column } from './PeopleTable';
import { SegmentDialog, SegmentList } from './Segments';
import type { ContactCondition, FilterAttribute, PeopleFilters, PersonSummary, Segment } from './types';

type View = 'contacts' | 'attributes';

const VIEWS: readonly SegmentedOption<View>[] = [
  { value: 'contacts', label: 'Contacts' },
  { value: 'attributes', label: 'Attributes' },
];

export function PeoplePage() {
  const [params, setParams] = useSearchParams();
  // Filters change in place; a way back from Analytics survives them.
  const kept = useKeptReturnState();
  const admin = isAdmin(useMe().data);
  // Admin only; support users can't read widgets.
  const widgets = useWidgets(admin);
  const registry = useAttributeRegistry();
  const segments = useSegments();
  const [columns, setColumns] = useState<string[]>(readColumns);
  const [dialog, setDialog] = useState<{ segment: Segment | null } | null>(null);
  const [segmentsOpen, setSegmentsOpen] = useState(false);
  // An index, the number of filters for a new one, or null while closed.
  const [editingFilter, setEditingFilter] = useState<number | null>(null);

  const view: View = params.get('view') === 'attributes' ? 'attributes' : 'contacts';
  const segmentId = params.get('segment') ?? 'all';
  const builtIn = BUILT_IN_SEGMENTS.find((segment) => segment.id === segmentId);
  const saved = builtIn ? undefined : segments.data?.find((segment) => segment.id === segmentId);
  const extra = useMemo(() => decodeFilter(params.get('filter')), [params]);
  const q = params.get('q') ?? '';
  const widgetId = params.get('widget')?.toLowerCase() ?? '';
  const query = useDebouncedValue(q.trim(), 300);

  const applied = useMemo<PeopleFilters>(() => {
    const conditions = [...(builtIn?.filter ?? []), ...extra];
    return {
      q: query,
      widgetId,
      identified: '',
      conditions,
      segmentId: builtIn ? '' : segmentId,
    };
  }, [builtIn, extra, query, widgetId, segmentId]);
  const people = usePeople(applied);
  const count = usePeopleCount(applied);
  const filtered = q.trim() !== '' || widgetId !== '' || extra.length > 0 || segmentId !== 'all';

  const setParam = (changes: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value) {
        next.set(key, value);
      } else {
        next.delete(key);
      }
    }
    setParams(next, { replace: true, state: kept });
  };

  const filterAttributes = useMemo<FilterAttribute[]>(
    () => [
      ...(registry.data?.builtIn ?? []).filter((attribute) => admin || attribute.key !== 'widget'),
      ...(registry.data?.items ?? []).map((item) => ({ key: item.key, label: item.label, type: item.type, builtIn: false, ops: registry.data?.ops[item.type] ?? [] })),
    ],
    [registry.data, admin],
  );
  const availableColumns = useMemo<Column[]>(
    () => [
      ...BUILT_IN_COLUMNS,
      ...(registry.data?.items ?? [])
        .filter((item) => item.visible)
        .map((item) => ({ key: item.key, label: item.label, type: item.type, value: (person: PersonSummary) => person.attributes[item.key] })),
    ],
    [registry.data],
  );
  const shownColumns = availableColumns.filter((column) => columns.includes(column.key));
  const chooseColumns = (next: string[]) => {
    setColumns(next);
    storeColumns(next);
  };

  // Saving keeps the segment's filter plus the added conditions.
  const currentFilter: ContactCondition[] = [...(builtIn?.filter ?? saved?.filter ?? []), ...extra];
  const canSave = currentFilter.length > 0 && !(saved && extra.length === 0);

  const widgetChoices: SelectOption<string>[] = [
    { value: '', label: 'All widgets' },
    ...(widgets.data ?? []).map((widget) => ({ value: widget.id.toLowerCase(), label: widget.name })),
  ];
  const title = builtIn?.name ?? saved?.name ?? 'Segment';

  const description =
    view === 'attributes' ? 'What your website tells you about contacts, and what each attribute may do.' : 'People who talked to a widget or signed in on your website.';
  const countText = count.data !== undefined ? `${formatNumber(count.data)} ${count.data === 1 ? 'contact' : 'contacts'}` : '';
  const chooseSegment = (id: string) => {
    setParam({ segment: id === 'all' ? null : id, filter: null });
    setSegmentsOpen(false);
  };
  const newSegment = () => {
    setSegmentsOpen(false);
    if (canSave) {
      setDialog({ segment: null });
    } else {
      setEditingFilter(extra.length);
    }
  };
  const segmentListProps = { value: segmentId, admin, onChange: chooseSegment, onEdit: (segment: Segment) => setDialog({ segment }), onNew: newSegment };

  return (
    <Page>
      <header className="flex min-h-8 flex-wrap items-center gap-x-4 gap-y-2">
        <div className="flex min-w-0 flex-1 items-baseline gap-3">
          <h1 className="shrink-0 text-title text-fg">Contacts</h1>
          <p className="hidden min-w-0 truncate text-small text-fg-muted lg:block" title={description}>
            {description}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <SegmentedControl label="Contacts views" options={VIEWS} value={view} onChange={(next) => setParam({ view: next === 'contacts' ? null : next })} />
          {view === 'contacts' && canSave ? (
            <Button variant="primary" icon={BookmarkPlus} onClick={() => setDialog({ segment: null })}>
              Save as segment
            </Button>
          ) : null}
        </div>
      </header>

      {view === 'attributes' ? (
        <AttributesPanel admin={admin} />
      ) : (
        <div className="flex items-start gap-6">
          <SegmentList {...segmentListProps} className="sticky top-6 hidden w-54 shrink-0 xl:block" />

          <section aria-label={title} className="min-w-0 flex-1">
            <Card className="overflow-hidden">
              <div className="flex flex-wrap items-center gap-2 border-b border-border-subtle px-4 py-2 lg:px-6">
                <Button size="sm" icon={PanelLeft} className="xl:hidden" aria-expanded={segmentsOpen} onClick={() => setSegmentsOpen(true)}>
                  <span className="max-w-40 truncate">{title}</span>
                </Button>
                <form role="search" aria-label="Search contacts" className="relative w-full min-w-48 sm:w-auto sm:max-w-80 sm:flex-1" onSubmit={(event) => event.preventDefault()}>
                  <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
                  <input
                    type="search"
                    aria-label="Search contacts"
                    value={q}
                    onChange={(event) => setParam({ q: event.target.value })}
                    placeholder="Name, email or user id"
                    className="field h-7 py-0 pl-8 text-small"
                  />
                </form>
                {admin && (widgets.data?.length ?? 0) > 1 ? (
                  <Select aria-label="Widget" size="sm" className="w-40" value={widgetId} options={widgetChoices} onChange={(value) => setParam({ widget: value })} />
                ) : null}
                <FilterBuilder
                  editing={editingFilter}
                  onEditingChange={setEditingFilter}
                  conditions={extra}
                  attributes={filterAttributes}
                  widgets={widgets.data ?? []}
                  onChange={(next) => setParam({ filter: next.length > 0 ? encodeFilter(next) : null })}
                />
                <div className="ml-auto flex shrink-0 items-center gap-3">
                  <p className="text-small whitespace-nowrap text-fg-muted tabular-nums" aria-live="polite">
                    <span className="sr-only">{title}: </span>
                    {countText}
                  </p>
                  <ColumnChooser columns={availableColumns} chosen={columns} onChange={chooseColumns} />
                </div>
              </div>

              <PeopleTable
                people={people}
                columns={shownColumns}
                filtered={filtered}
                onClearFilters={() => setParams(new URLSearchParams(), { replace: true, state: kept })}
              />
            </Card>
          </section>
        </div>
      )}
      <Drawer open={segmentsOpen} onClose={() => setSegmentsOpen(false)} label="Segments">
        <div className="overflow-y-auto p-3">
          <SegmentList {...segmentListProps} />
        </div>
      </Drawer>
      {dialog ? (
        <SegmentDialog
          key={dialog.segment?.id ?? 'new'}
          open
          onClose={() => setDialog(null)}
          segment={dialog.segment}
          filter={dialog.segment ? (dialog.segment.id === segmentId ? [...dialog.segment.filter, ...extra] : dialog.segment.filter) : currentFilter}
          onSaved={(segment) => setParam({ segment: segment.id, filter: null })}
        />
      ) : null}
    </Page>
  );
}
