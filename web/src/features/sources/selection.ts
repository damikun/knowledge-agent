import type { Guid } from '../../lib/types';

export interface Selection {
  ids: ReadonlySet<Guid>;
  // The row a Shift-click extends from.
  anchor: Guid | null;
  // Every source the current filters match, on all pages.
  allMatching: boolean;
}

export const EMPTY_SELECTION: Selection = { ids: new Set(), anchor: null, allMatching: false };

export type SelectMode = 'only' | 'toggle' | 'range' | 'add-range';

// The rules of a file manager: a click selects one row, Cmd or Ctrl toggles a row, Shift selects the rows between the
// last clicked row and this one, and a checkbox works like Cmd with Shift adding a range.
export function select(selection: Selection, pageIds: readonly Guid[], id: Guid, mode: SelectMode): Selection {
  const current = new Set(selection.allMatching ? pageIds : selection.ids);
  if (mode === 'only') {
    return { ids: new Set([id]), anchor: id, allMatching: false };
  }
  if (mode === 'toggle') {
    if (!current.delete(id)) {
      current.add(id);
    }
    return { ids: current, anchor: id, allMatching: false };
  }

  const from = pageIds.indexOf(selection.anchor ?? id);
  const to = pageIds.indexOf(id);
  if (from < 0 || to < 0) {
    return { ids: new Set([id]), anchor: id, allMatching: false };
  }
  const range = pageIds.slice(Math.min(from, to), Math.max(from, to) + 1);
  const ids = mode === 'add-range' ? current : new Set<Guid>();
  range.forEach((rangeId) => ids.add(rangeId));
  return { ids, anchor: selection.anchor ?? id, allMatching: false };
}

export function selectPage(pageIds: readonly Guid[], selected: boolean): Selection {
  return selected ? { ids: new Set(pageIds), anchor: null, allMatching: false } : EMPTY_SELECTION;
}
