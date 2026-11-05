import { describe, expect, it } from 'vitest';
import { envelope, parseEditorMessage, parsePreviewMessage, PREVIEW_SOURCE, PREVIEW_VERSION } from './preview';

describe('preview protocol', () => {
  it('wraps messages with the source and the version, and reads them back on the other side', () => {
    const render = envelope({ type: 'render', config: { title: 'Shop help' }, scene: 'conversation', colorScheme: 'dark' });
    expect(render).toEqual({ source: PREVIEW_SOURCE, version: PREVIEW_VERSION, type: 'render', config: { title: 'Shop help' }, scene: 'conversation', colorScheme: 'dark' });
    expect(parseEditorMessage(render)).toEqual({ type: 'render', config: { title: 'Shop help' }, scene: 'conversation', colorScheme: 'dark' });
    expect(parseEditorMessage(envelope({ type: 'track', target: 'appearance.subtitle', fallbacks: ['appearance.title'] }))).toEqual({
      type: 'track',
      target: 'appearance.subtitle',
      fallbacks: ['appearance.title'],
    });
    expect(parseEditorMessage(envelope({ type: 'track', target: null, fallbacks: [] }))).toEqual({ type: 'track', target: null, fallbacks: [] });
    const live = { type: 'live', widgetId: '0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b', colorScheme: 'dark' } as const;
    expect(parseEditorMessage(envelope(live))).toEqual(live);
    expect(parseEditorMessage(envelope({ ...live, widgetId: '../../api/admin' }))).toBeNull();

    const rect = { x: 1, y: 2, width: 30, height: 4 };
    expect(parsePreviewMessage(envelope({ type: 'ready' }))).toEqual({ type: 'ready' });
    expect(parsePreviewMessage(envelope({ type: 'rendered', scene: 'start', targets: ['appearance.title'] }))).toEqual({
      type: 'rendered',
      scene: 'start',
      targets: ['appearance.title'],
    });
    expect(parsePreviewMessage(envelope({ type: 'hover', target: 'appearance.title', rect }))).toEqual({ type: 'hover', target: 'appearance.title', rect });
    expect(parsePreviewMessage(envelope({ type: 'select', target: null, rect: null }))).toEqual({ type: 'select', target: null, rect: null });
    expect(parsePreviewMessage(envelope({ type: 'rect', target: 'behavior.feedback', anchor: null, rect: null }))).toEqual({
      type: 'rect',
      target: 'behavior.feedback',
      anchor: null,
      rect: null,
    });
    expect(parsePreviewMessage(envelope({ type: 'rect', target: 'appearance.subtitle', anchor: 'appearance.title', rect }))).toEqual({
      type: 'rect',
      target: 'appearance.subtitle',
      anchor: 'appearance.title',
      rect,
    });
  });

  it('refuses anything else: other sources and versions, unknown types, targets and scenes, and bad rectangles', () => {
    const render = { type: 'render', config: {}, scene: 'start', colorScheme: 'light' };
    expect(parseEditorMessage({ ...render, source: 'knowledge-agent', version: PREVIEW_VERSION })).toBeNull();
    expect(parseEditorMessage({ ...render, source: PREVIEW_SOURCE, version: PREVIEW_VERSION + 1 })).toBeNull();
    expect(parseEditorMessage({ ...render, source: PREVIEW_SOURCE })).toBeNull();
    expect(parseEditorMessage(envelope({ ...render, type: 'render', scene: 'start', colorScheme: 'light', config: 'title' }))).toBeNull();
    expect(parseEditorMessage({ source: PREVIEW_SOURCE, version: PREVIEW_VERSION, type: 'render', config: {}, scene: 'checkout', colorScheme: 'light' })).toBeNull();
    expect(parseEditorMessage({ source: PREVIEW_SOURCE, version: PREVIEW_VERSION, type: 'render', config: {}, scene: 'start', colorScheme: 'blue' })).toBeNull();
    expect(parseEditorMessage({ source: PREVIEW_SOURCE, version: PREVIEW_VERSION, type: 'track', target: 'settings.security', fallbacks: [] })).toBeNull();
    expect(parseEditorMessage({ source: PREVIEW_SOURCE, version: PREVIEW_VERSION, type: 'track', target: 'appearance.title' })).toBeNull();
    expect(parseEditorMessage({ source: PREVIEW_SOURCE, version: PREVIEW_VERSION, type: 'track', target: 'appearance.title', fallbacks: ['nope'] })).toBeNull();
    expect(parseEditorMessage({ source: PREVIEW_SOURCE, version: PREVIEW_VERSION, type: 'ready' })).toBeNull();
    expect(parseEditorMessage('render')).toBeNull();

    const base = { source: PREVIEW_SOURCE, version: PREVIEW_VERSION };
    expect(parsePreviewMessage({ ...base, type: 'select', target: 'appearance.title', rect: { x: 1, y: 2, width: -3, height: 4 } })).toBeNull();
    expect(parsePreviewMessage({ ...base, type: 'select', target: 'appearance.title', rect: { x: 1, y: Number.NaN, width: 3, height: 4 } })).toBeNull();
    expect(parsePreviewMessage({ ...base, type: 'hover', target: 'appearance.title' })).toBeNull();
    expect(parsePreviewMessage({ ...base, type: 'rect', target: null, anchor: null, rect: null })).toBeNull();
    expect(
      parsePreviewMessage({ ...base, type: 'rect', target: 'appearance.title', anchor: null, rect: { x: 1, y: 2, width: 3, height: 4 } }),
    ).toBeNull();
    expect(parsePreviewMessage({ ...base, type: 'rect', target: 'appearance.title', anchor: 'appearance.title', rect: null })).toBeNull();
    expect(parsePreviewMessage({ ...base, type: 'rendered', scene: 'start', targets: ['appearance.title', 'nope'] })).toBeNull();
    expect(parsePreviewMessage({ ...base, type: 'render', config: {}, scene: 'start', colorScheme: 'light' })).toBeNull();
  });
});
