import { useCallback, useMemo, useReducer } from 'react';
import type { ColorMode, WidgetFormValues } from '../widgetForm';

// Changes are actions on a draft path: a form key, texts.<key>, or colors.<mode>.<token>.

type FieldKey = Exclude<keyof WidgetFormValues, 'texts' | 'colors'>;
export type TextPath = `texts.${string}`;
export type ColorPath = `colors.${ColorMode}.${string}`;
export type DraftPath = FieldKey | TextPath | ColorPath;
export type DraftValue<P extends DraftPath> = P extends FieldKey ? WidgetFormValues[P] : string;

type DraftChange =
  | { [P in FieldKey]: { path: P; value: WidgetFormValues[P] } }[FieldKey]
  | { path: TextPath; value: string }
  | { path: ColorPath; value: string };

export type DraftAction =
  | ({ type: 'set' } & DraftChange)
  // Ends the current undo step.
  | { type: 'checkpoint' }
  | { type: 'undo' }
  | { type: 'redo' }
  | { type: 'discard' }
  | { type: 'resetColors'; mode: ColorMode }
  | { type: 'saved'; values: WidgetFormValues };

export interface DraftState {
  saved: WidgetFormValues;
  values: WidgetFormValues;
  past: readonly WidgetFormValues[];
  future: readonly WidgetFormValues[];
  // Further changes to this path extend its undo step, so typing a word is one step.
  editing: DraftPath | null;
}

const MAX_HISTORY = 100;

function isTextPath(path: DraftPath): path is TextPath {
  return path.startsWith('texts.');
}

export function isColorPath(path: string): path is ColorPath {
  return path.startsWith('colors.light.') || path.startsWith('colors.dark.');
}

export function inMode(path: DraftPath, mode: ColorMode): boolean {
  return !isColorPath(path) || colorOf(path).mode === mode;
}

function colorOf(path: ColorPath): { mode: ColorMode; token: string } {
  const [, mode, token] = path.split('.') as [string, ColorMode, string];
  return { mode, token };
}

export function readPath<P extends DraftPath>(values: WidgetFormValues, path: P): DraftValue<P> {
  if (isTextPath(path)) {
    return (values.texts[path.slice('texts.'.length)] ?? '') as DraftValue<P>;
  }
  if (isColorPath(path)) {
    const { mode, token } = colorOf(path);
    return (values.colors[mode][token] ?? '') as DraftValue<P>;
  }
  return values[path as FieldKey] as DraftValue<P>;
}

function isTextChange(change: DraftChange): change is { path: TextPath; value: string } {
  return isTextPath(change.path);
}

function isColorChange(change: DraftChange): change is { path: ColorPath; value: string } {
  return isColorPath(change.path);
}

function writePath(values: WidgetFormValues, change: DraftChange): WidgetFormValues {
  if (isTextChange(change)) {
    const key = change.path.slice('texts.'.length);
    return (values.texts[key] ?? '') === change.value ? values : { ...values, texts: { ...values.texts, [key]: change.value } };
  }
  if (isColorChange(change)) {
    const { mode, token } = colorOf(change.path);
    if ((values.colors[mode][token] ?? '') === change.value) {
      return values;
    }
    return { ...values, colors: { ...values.colors, [mode]: { ...values.colors[mode], [token]: change.value } } };
  }
  return Object.is(values[change.path], change.value) ? values : { ...values, [change.path]: change.value };
}

// Sorted, without empty values: an empty text or color is the same as none.
function setEntries(record: Record<string, string>): Array<[string, string]> {
  return Object.entries(record)
    .filter(([, value]) => value !== '')
    .sort(([a], [b]) => a.localeCompare(b));
}

// Row keys are React-only, and empty values equal none.
function comparable(values: WidgetFormValues): string {
  return JSON.stringify({
    ...values,
    texts: setEntries(values.texts),
    colors: { light: setEntries(values.colors.light), dark: setEntries(values.colors.dark) },
    hours: values.hours.map(({ day, opens, closes }) => [day, opens, closes]),
  });
}

export function samePath(a: WidgetFormValues, b: WidgetFormValues, path: DraftPath): boolean {
  const value = (values: WidgetFormValues) => readPath(values, path);
  if (path === 'hours') {
    const rows = (values: WidgetFormValues) => values.hours.map(({ day, opens, closes }) => [day, opens, closes]);
    return JSON.stringify(rows(a)) === JSON.stringify(rows(b));
  }
  return JSON.stringify(value(a)) === JSON.stringify(value(b));
}

export function sameValues(a: WidgetFormValues, b: WidgetFormValues): boolean {
  return a === b || comparable(a) === comparable(b);
}

export function createDraft(values: WidgetFormValues): DraftState {
  return { saved: values, values, past: [], future: [], editing: null };
}

function pushed(past: readonly WidgetFormValues[], values: WidgetFormValues): WidgetFormValues[] {
  return [...past, values].slice(-MAX_HISTORY);
}

export function draftReducer(state: DraftState, action: DraftAction): DraftState {
  switch (action.type) {
    case 'set': {
      const values = writePath(state.values, action);
      if (values === state.values) {
        return state;
      }
      const past = state.editing === action.path ? state.past : pushed(state.past, state.values);
      return { ...state, values, past, future: [], editing: action.path };
    }
    case 'checkpoint':
      return state.editing === null ? state : { ...state, editing: null };
    case 'undo': {
      const previous = state.past.at(-1);
      if (!previous) {
        return state;
      }
      return { ...state, values: previous, past: state.past.slice(0, -1), future: [state.values, ...state.future], editing: null };
    }
    case 'redo': {
      const [next, ...future] = state.future;
      if (!next) {
        return state;
      }
      return { ...state, values: next, past: pushed(state.past, state.values), future, editing: null };
    }
    case 'discard':
      if (sameValues(state.values, state.saved)) {
        return state;
      }
      return { ...state, values: state.saved, past: pushed(state.past, state.values), future: [], editing: null };
    case 'resetColors': {
      if (Object.values(state.values.colors[action.mode]).every((color) => color === '')) {
        return state;
      }
      const values = { ...state.values, colors: { ...state.values.colors, [action.mode]: {} } };
      return { ...state, values, past: pushed(state.past, state.values), future: [], editing: null };
    }
    case 'saved':
      return { ...state, saved: action.values, values: action.values, editing: null };
  }
}

export type SetPath = <P extends DraftPath>(path: P, value: DraftValue<P>) => void;

export interface WidgetDraft {
  values: WidgetFormValues;
  saved: WidgetFormValues;
  dirty: boolean;
  canUndo: boolean;
  canRedo: boolean;
  set: SetPath;
  dispatch: (action: DraftAction) => void;
}

export function useWidgetDraft(initial: () => WidgetFormValues): WidgetDraft {
  const [state, dispatch] = useReducer(draftReducer, undefined, () => createDraft(initial()));
  // TypeScript can't relate a generic path to its value in the action union; the SetPath signature does.
  const set = useCallback<SetPath>((path, value) => dispatch({ type: 'set', path, value } as DraftAction), []);
  const dirty = useMemo(() => !sameValues(state.values, state.saved), [state.values, state.saved]);
  return { values: state.values, saved: state.saved, dirty, canUndo: state.past.length > 0, canRedo: state.future.length > 0, set, dispatch };
}
