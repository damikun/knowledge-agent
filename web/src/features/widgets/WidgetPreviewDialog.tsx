import { clsx } from 'clsx';
import { FlaskConical, Monitor, Moon, RotateCcw, Smartphone, Sun, X } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Button } from '../../components/ui/Button';
import { useFocusTrap, useScrollLock } from '../../components/ui/focus';
import { SegmentedControl, type SegmentedOption } from '../../components/ui/SegmentedControl';
import { useElementSize } from '../../lib/hooks/useElementSize';
import { envelope, parsePreviewMessage, type ColorScheme } from '../../widget/shared/preview';
import { fit, type Device } from './editor/PreviewCanvas';
import { PREVIEW_URL } from './editor/usePreviewBridge';
import type { WidgetDto } from './types';

const SCHEMES: readonly SegmentedOption<ColorScheme>[] = [
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
];

const DEVICES: readonly SegmentedOption<Device>[] = [
  { value: 'desktop', label: 'Desktop', icon: Monitor },
  { value: 'phone', label: 'Phone', icon: Smartphone },
];

function portalScheme(): ColorScheme {
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}

export interface WidgetPreviewDialogProps {
  widget: WidgetDto | null;
  onClose: () => void;
  unsaved?: boolean;
}

/** The widget build in a frame on the portal's origin. Its preview session counts nowhere and contacts no one. */
export function WidgetPreviewDialog({ widget, onClose, unsaved = false }: WidgetPreviewDialogProps) {
  const open = widget !== null;
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const descriptionId = useId();
  const [colorScheme, setColorScheme] = useState<ColorScheme>(portalScheme);
  const [device, setDevice] = useState<Device>('desktop');
  // Bumping this remounts the frame, which starts a fresh preview session.
  const [run, setRun] = useState(0);
  const [frame, setFrame] = useState<HTMLIFrameElement | null>(null);
  const [connected, setConnected] = useState(0);
  const [roomElement, setRoomElement] = useState<HTMLDivElement | null>(null);
  const room = useElementSize(roomElement);

  useFocusTrap(panelRef, { active: open, onEscape: onClose });
  useScrollLock(open);

  // The frame posts "ready" on every load, so it gets the widget each time.
  useEffect(() => {
    const preview = frame?.contentWindow;
    if (!preview) {
      return undefined;
    }
    const onMessage = (event: MessageEvent) => {
      if (event.origin === window.location.origin && event.source === preview && parsePreviewMessage(event.data)?.type === 'ready') {
        setConnected((count) => count + 1);
      }
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [frame]);

  const widgetId = widget?.id ?? null;
  useEffect(() => {
    if (connected > 0 && widgetId) {
      frame?.contentWindow?.postMessage(envelope({ type: 'live', widgetId, colorScheme }), window.location.origin);
    }
  }, [connected, frame, widgetId, colorScheme]);

  if (!widget) {
    return null;
  }

  const viewport = fit(device, room ?? { width: 1024, height: 700 });

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-stretch justify-center p-0 sm:p-4 lg:p-6">
      <div className="absolute inset-0 animate-fade-in bg-overlay" aria-hidden="true" onMouseDown={onClose} />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        tabIndex={-1}
        data-testid="widget-preview"
        className="relative flex w-full max-w-[90rem] animate-dialog-in flex-col overflow-hidden border border-border bg-raised shadow-dialog sm:rounded-2xl"
      >
        <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-3 px-5 pt-4 pb-3">
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="truncate text-section text-fg">
              Preview: {widget.name}
            </h2>
            <p id={descriptionId} className="mt-0.5 text-small text-fg-secondary">
              The real widget with its saved settings, exactly as visitors get it.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <SegmentedControl label="Visitor's color scheme" options={SCHEMES} value={colorScheme} onChange={setColorScheme} />
            <SegmentedControl label="Device" options={DEVICES} value={device} onChange={setDevice} />
            <Button size="sm" icon={RotateCcw} onClick={() => setRun((count) => count + 1)}>
              Reset conversation
            </Button>
            <Button variant="ghost" size="icon-sm" icon={X} aria-label="Close" onClick={onClose} />
          </div>
        </div>
        <p className="flex items-start gap-1.5 border-y border-border-subtle bg-surface-subtle px-5 py-2 text-xs text-fg-secondary">
          <FlaskConical className="mt-px size-3.5 shrink-0 text-info" aria-hidden />
          <span>
            A test conversation: it is not counted in Analytics or shown in the Inbox, no contact is made, and Talk to a person shows the
            waiting state without asking anyone.
            {unsaved ? <span className="font-medium text-warning-text"> You have unsaved changes; save them to try them here.</span> : null}
            {widget.enabled ? null : <span> The widget is off on your site; only you can try it here.</span>}
          </span>
        </p>
        <div className="dot-grid min-h-0 flex-1 bg-surface-subtle p-3 sm:p-4">
          <div ref={setRoomElement} className="relative size-full">
            <div
              className={clsx(
                'absolute top-0 left-1/2 -translate-x-1/2 bg-fill-muted shadow-dialog',
                device === 'phone' ? 'rounded-[1.75rem] ring-4 ring-border-strong' : 'rounded-2xl ring-1 ring-border',
              )}
              style={{ width: viewport.width * viewport.scale, height: viewport.height * viewport.scale }}
            >
              <div className={clsx('relative size-full overflow-hidden', device === 'phone' ? 'rounded-[1.75rem]' : 'rounded-2xl')}>
                <iframe
                  key={run}
                  ref={setFrame}
                  src={PREVIEW_URL}
                  title={`${widget.name}, live preview`}
                  className="block origin-top-left border-0"
                  style={{ width: viewport.width, height: viewport.height, transform: `scale(${viewport.scale})` }}
                />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
