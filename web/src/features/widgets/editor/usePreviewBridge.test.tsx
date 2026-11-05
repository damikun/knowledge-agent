import { act, render, screen } from '@testing-library/react';
import { useMemo, useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { envelope, parseEditorMessage, PREVIEW_VERSION, type EditorMessage, type EditTarget, type PreviewMessage, type PreviewScene } from '../../../widget/shared/preview';
import { widgetDto, widgetOptions } from '../../../test/widgetsApi';
import type { WidgetPreviewConfig } from '../types';
import { initialWidgetValues, toPreviewConfig } from '../widgetForm';
import { buildRegistry } from './registry';
import { PREVIEW_URL, usePreviewBridge } from './usePreviewBridge';

const registry = buildRegistry(widgetOptions());
const shop = widgetDto();
const config = toPreviewConfig(initialWidgetValues(shop, widgetOptions().defaults), shop.publicKey, shop.settings);

interface HarnessProps {
  config?: WidgetPreviewConfig;
  scene?: PreviewScene;
  selected?: EditTarget | null;
  onPick?: (target: EditTarget | null) => void;
}

function Harness({ config: shown = config, scene = 'start', selected = null, onPick = () => undefined }: HarnessProps) {
  const [frame, setFrame] = useState<HTMLIFrameElement | null>(null);
  const tracking = useMemo(() => (selected ? { target: selected, fallbacks: registry.fallbacks(selected) } : null), [selected]);
  const bridge = usePreviewBridge({ frame, config: shown, scene, colorScheme: 'light', tracking, onPick });
  return (
    <>
      <iframe ref={setFrame} src={PREVIEW_URL} title="Widget preview" />
      <output aria-label="Shown">{bridge.shown ? [...bridge.shown].join(' ') : 'unknown'}</output>
      <output aria-label="Anchor">{bridge.anchor ? `${bridge.anchor.target} at ${bridge.anchor.rect.y}` : 'none'}</output>
      <output aria-label="Hovered">{bridge.hovered?.target ?? 'none'}</output>
    </>
  );
}

function setUp(props: HarnessProps = {}) {
  const view = render(<Harness {...props} />);
  const frame = screen.getByTitle<HTMLIFrameElement>('Widget preview');
  const toFrame = vi.spyOn(frame.contentWindow!, 'postMessage').mockImplementation(() => undefined);
  const sent = () => toFrame.mock.calls.map(([data]) => parseEditorMessage(data)).filter((message): message is EditorMessage => message !== null);
  const fromFrame = (message: PreviewMessage, init: MessageEventInit = {}) =>
    act(() => {
      window.dispatchEvent(new MessageEvent('message', { data: envelope(message), origin: window.location.origin, source: frame.contentWindow, ...init }));
    });
  return { ...view, frame, toFrame, sent, fromFrame };
}

const rect = (y: number) => ({ x: 10, y, width: 120, height: 20 });

describe('preview bridge (editor side)', () => {
  it('sends nothing until the preview is ready, then the whole state, and every change at once, to its own origin', () => {
    const { toFrame, sent, fromFrame, rerender } = setUp();
    expect(toFrame).not.toHaveBeenCalled();

    fromFrame({ type: 'ready' });
    expect(sent()).toEqual([
      { type: 'render', config, scene: 'start', colorScheme: 'light' },
      { type: 'track', target: null, fallbacks: [] },
    ]);
    expect(toFrame.mock.calls[0]![1]).toBe(window.location.origin);

    rerender(<Harness config={{ ...config, title: 'Need help?' }} scene="conversation" />);
    expect(sent().at(-1)).toEqual({ type: 'render', config: { ...config, title: 'Need help?' }, scene: 'conversation', colorScheme: 'light' });

    toFrame.mockClear();
    fromFrame({ type: 'ready' });
    expect(sent().map((message) => message.type)).toEqual(['render', 'track']);
  });

  it('ignores messages from other origins, other windows and other protocol versions', () => {
    const { toFrame, fromFrame } = setUp();

    fromFrame({ type: 'ready' }, { origin: 'https://evil.example' });
    fromFrame({ type: 'ready' }, { source: window });
    act(() => {
      window.dispatchEvent(
        new MessageEvent('message', {
          data: { ...envelope({ type: 'ready' }), version: PREVIEW_VERSION + 1 },
          origin: window.location.origin,
          source: screen.getByTitle<HTMLIFrameElement>('Widget preview').contentWindow,
        }),
      );
    });
    expect(toFrame).not.toHaveBeenCalled();
  });

  it('asks for the selected element with its parents as fallbacks, and points where the preview places it', () => {
    const { sent, fromFrame, rerender } = setUp({ selected: 'appearance.subtitle' });
    fromFrame({ type: 'ready' });
    expect(sent().at(-1)).toEqual({ type: 'track', target: 'appearance.subtitle', fallbacks: ['appearance.title', 'appearance.header'] });
    expect(screen.getByRole('status', { name: 'Anchor' })).toHaveTextContent('none');

    fromFrame({ type: 'rect', target: 'appearance.subtitle', anchor: 'appearance.title', rect: rect(30) });
    expect(screen.getByRole('status', { name: 'Anchor' })).toHaveTextContent('appearance.title at 30');
    fromFrame({ type: 'rect', target: 'appearance.subtitle', anchor: 'appearance.subtitle', rect: rect(48) });
    expect(screen.getByRole('status', { name: 'Anchor' })).toHaveTextContent('appearance.subtitle at 48');
    fromFrame({ type: 'rect', target: 'appearance.subtitle', anchor: null, rect: null });
    expect(screen.getByRole('status', { name: 'Anchor' })).toHaveTextContent('none');

    rerender(<Harness selected="appearance.title" />);
    fromFrame({ type: 'rect', target: 'appearance.subtitle', anchor: 'appearance.title', rect: rect(30) });
    expect(screen.getByRole('status', { name: 'Anchor' })).toHaveTextContent('none');
    rerender(<Harness selected={null} />);
    expect(sent().at(-1)).toEqual({ type: 'track', target: null, fallbacks: [] });
  });

  it('knows what shows only for the scene the preview rendered last', () => {
    const { fromFrame, rerender } = setUp();
    fromFrame({ type: 'ready' });
    fromFrame({ type: 'rendered', scene: 'start', targets: ['appearance.header', 'appearance.title'] });
    expect(screen.getByRole('status', { name: 'Shown' })).toHaveTextContent('appearance.header appearance.title');

    rerender(<Harness scene="conversation" />);
    expect(screen.getByRole('status', { name: 'Shown' })).toHaveTextContent('unknown');
    fromFrame({ type: 'rendered', scene: 'conversation', targets: ['behavior.feedback'] });
    expect(screen.getByRole('status', { name: 'Shown' })).toHaveTextContent('behavior.feedback');
  });

  it('passes on what the admin clicks and points at in the preview', () => {
    const onPick = vi.fn();
    const { fromFrame, rerender } = setUp({ onPick });
    fromFrame({ type: 'ready' });

    fromFrame({ type: 'hover', target: 'behavior.greeting', rect: rect(80) });
    expect(screen.getByRole('status', { name: 'Hovered' })).toHaveTextContent('behavior.greeting');
    fromFrame({ type: 'hover', target: null, rect: null });
    expect(screen.getByRole('status', { name: 'Hovered' })).toHaveTextContent('none');

    fromFrame({ type: 'select', target: 'behavior.greeting', rect: rect(80) });
    expect(onPick).toHaveBeenLastCalledWith('behavior.greeting');
    rerender(<Harness onPick={onPick} selected="behavior.greeting" />);
    expect(screen.getByRole('status', { name: 'Anchor' })).toHaveTextContent('behavior.greeting at 80');
    fromFrame({ type: 'select', target: null, rect: null });
    expect(onPick).toHaveBeenLastCalledWith(null);
  });
});
