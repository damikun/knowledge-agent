import { describe, expect, it } from 'vitest';
import { EMPTY_SELECTION, select, selectPage, type Selection } from './selection';

const page = ['a', 'b', 'c', 'd', 'e'];
const ids = (selection: Selection) => [...selection.ids].sort();

describe('selecting sources like files', () => {
  it('selects one row on a plain click and toggles rows with Cmd or Ctrl', () => {
    const one = select(EMPTY_SELECTION, page, 'b', 'only');
    const two = select(one, page, 'd', 'toggle');
    const back = select(two, page, 'b', 'toggle');
    const replaced = select(two, page, 'e', 'only');

    expect(ids(one)).toEqual(['b']);
    expect(ids(two)).toEqual(['b', 'd']);
    expect(ids(back)).toEqual(['d']);
    expect(ids(replaced)).toEqual(['e']);
    expect(replaced.anchor).toBe('e');
  });

  it('selects the rows between the last clicked row and a Shift-clicked one', () => {
    const anchored = select(EMPTY_SELECTION, page, 'b', 'only');
    const down = select(anchored, page, 'd', 'range');
    const up = select(down, page, 'a', 'range');
    const added = select(select(EMPTY_SELECTION, page, 'e', 'toggle'), page, 'c', 'toggle');

    expect(ids(down)).toEqual(['b', 'c', 'd']);
    expect(ids(up)).toEqual(['a', 'b']);
    expect(up.anchor).toBe('b');
    expect(ids(select(added, page, 'a', 'add-range'))).toEqual(['a', 'b', 'c', 'e']);
    expect(ids(select(EMPTY_SELECTION, page, 'c', 'range'))).toEqual(['c']);
    expect(ids(select({ ...anchored, anchor: 'gone' }, page, 'd', 'range'))).toEqual(['d']);
  });

  it('turns a selection of every matching source back into rows of this page', () => {
    const everything: Selection = { ids: new Set(), anchor: null, allMatching: true };

    const withoutOne = select(everything, page, 'c', 'toggle');

    expect(withoutOne.allMatching).toBe(false);
    expect(ids(withoutOne)).toEqual(['a', 'b', 'd', 'e']);
    expect(ids(selectPage(page, true))).toEqual(page);
    expect(selectPage(page, false)).toBe(EMPTY_SELECTION);
  });
});
