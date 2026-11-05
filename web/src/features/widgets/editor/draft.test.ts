import { describe, expect, it } from 'vitest';
import { widgetDto, widgetOptions } from '../../../test/widgetsApi';
import { initialWidgetValues } from '../widgetForm';
import { createDraft, draftReducer, inMode, readPath, samePath, sameValues, type DraftAction, type DraftState } from './draft';

const saved = initialWidgetValues(widgetDto(), widgetOptions().defaults);

function run(...actions: DraftAction[]): DraftState {
  return actions.reduce(draftReducer, createDraft(saved));
}

describe('widget draft', () => {
  it('changes one draft path per action, a setting or one text of the widget', () => {
    const state = run({ type: 'set', path: 'title', value: 'Need help?' }, { type: 'set', path: 'texts.send', value: 'Go' });

    expect(state.values.title).toBe('Need help?');
    expect(state.values.texts).toEqual({ send: 'Go' });
    expect(readPath(state.values, 'texts.send')).toBe('Go');
    expect(readPath(state.values, 'texts.close')).toBe('');
    expect(state.saved).toBe(saved);
  });

  it('makes one undo step of an edit in progress, and a new one after a checkpoint or another path', () => {
    const typed = run(
      { type: 'set', path: 'title', value: 'N' },
      { type: 'set', path: 'title', value: 'Ne' },
      { type: 'set', path: 'title', value: 'Need help?' },
    );
    expect(typed.past).toHaveLength(1);
    expect(draftReducer(typed, { type: 'undo' }).values.title).toBe('Shop help');

    const later = [{ type: 'checkpoint' }, { type: 'set', path: 'title', value: 'Need help now?' }, { type: 'set', path: 'brandColor', value: '#7c3aed' }] satisfies DraftAction[];
    const state = later.reduce(draftReducer, typed);
    expect(state.past).toHaveLength(3);

    const once = draftReducer(state, { type: 'undo' });
    expect(once.values).toMatchObject({ title: 'Need help now?', brandColor: '#0f766e' });
    const twice = draftReducer(once, { type: 'undo' });
    expect(twice.values.title).toBe('Need help?');
    const redone = draftReducer(twice, { type: 'redo' });
    expect(redone.values.title).toBe('Need help now?');
    expect(redone.future).toHaveLength(1);
  });

  it('keeps the state when nothing changes, and drops the redo steps on a new change', () => {
    const start = createDraft(saved);
    expect(draftReducer(start, { type: 'set', path: 'title', value: 'Shop help' })).toBe(start);
    expect(draftReducer(start, { type: 'undo' })).toBe(start);
    expect(draftReducer(start, { type: 'redo' })).toBe(start);
    expect(draftReducer(start, { type: 'discard' })).toBe(start);

    const undone = run({ type: 'set', path: 'feedbackEnabled', value: false }, { type: 'undo' });
    expect(undone.future).toHaveLength(1);
    expect(draftReducer(undone, { type: 'set', path: 'handoverEnabled', value: true }).future).toEqual([]);
  });

  it('discards back to the saved widget as a step that can be undone, and takes a saved widget as the new baseline', () => {
    const edited = run({ type: 'set', path: 'title', value: 'Need help?' }, { type: 'set', path: 'suggestedQuestions', value: ['Where is my order?'] });
    const discarded = draftReducer(edited, { type: 'discard' });
    expect(discarded.values).toBe(saved);
    expect(draftReducer(discarded, { type: 'undo' }).values.suggestedQuestions).toEqual(['Where is my order?']);

    const next = { ...edited.values };
    const afterSave = draftReducer(edited, { type: 'saved', values: next });
    expect(afterSave.saved).toBe(next);
    expect(afterSave.values).toBe(next);
    expect(sameValues(afterSave.values, afterSave.saved)).toBe(true);
  });

  it('overrides one color of one mode per action, and puts all colors of a mode back to the theme in one step', () => {
    const state = run(
      { type: 'set', path: 'colors.dark.glow', value: '#f472b6' },
      { type: 'set', path: 'colors.dark.panel', value: '#0b1220' },
      { type: 'set', path: 'colors.light.header', value: '#1e3a8a' },
    );
    expect(state.values.colors).toEqual({ light: { header: '#1e3a8a' }, dark: { glow: '#f472b6', panel: '#0b1220' } });
    expect(readPath(state.values, 'colors.dark.glow')).toBe('#f472b6');
    expect(readPath(state.values, 'colors.light.glow')).toBe('');
    expect(samePath(state.values, saved, 'colors.light.glow')).toBe(true);
    expect(samePath(state.values, saved, 'colors.dark.glow')).toBe(false);
    expect(inMode('colors.dark.glow', 'light')).toBe(false);
    expect(inMode('title', 'light')).toBe(true);

    const reset = draftReducer(state, { type: 'resetColors', mode: 'dark' });
    expect(reset.values.colors).toEqual({ light: { header: '#1e3a8a' }, dark: {} });
    expect(draftReducer(reset, { type: 'resetColors', mode: 'dark' })).toBe(reset);
    expect(draftReducer(reset, { type: 'undo' }).values.colors.dark).toEqual({ glow: '#f472b6', panel: '#0b1220' });
  });

  it('compares drafts as the API would see them', () => {
    expect(sameValues(saved, { ...saved, texts: { send: '' } })).toBe(true);
    expect(sameValues(saved, { ...saved, texts: { send: 'Go' } })).toBe(false);
    expect(sameValues(saved, { ...saved, colors: { light: { glow: '' }, dark: {} } })).toBe(true);
    expect(sameValues(saved, { ...saved, colors: { light: {}, dark: { glow: '#f472b6' } } })).toBe(false);
    const hours = [{ key: 'a', day: 'monday' as const, opens: '09:00', closes: '17:00' }];
    expect(sameValues({ ...saved, hours }, { ...saved, hours: [{ ...hours[0]!, key: 'b' }] })).toBe(true);
    expect(sameValues({ ...saved, hours }, { ...saved, hours: [{ ...hours[0]!, closes: '18:00' }] })).toBe(false);
  });
});
