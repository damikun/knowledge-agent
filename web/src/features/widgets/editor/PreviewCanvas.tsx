import { clsx } from 'clsx';
import { Monitor, Moon, Smartphone, Sun } from 'lucide-react';
import { useEffect, useId, useState, type Ref } from 'react';
import { Select } from '../../../components/ui/Select';
import { SegmentedControl, type SegmentedOption } from '../../../components/ui/SegmentedControl';
import { useElementSize } from '../../../lib/hooks/useElementSize';
import { PREVIEW_SCENES, type ColorScheme, type PreviewRect, type PreviewScene } from '../../../widget/shared/preview';
import { SCENE_LABELS } from './registry';
import { PREVIEW_URL } from './usePreviewBridge';

export type Device = 'desktop' | 'phone';

// Room for the 400 px panel beside the page, above the loader's 640 px breakpoint.
const MIN_DESKTOP_WIDTH = 720;
// The open panel is about 600 px tall; a shorter viewport scales the preview down instead of squeezing it.
const MIN_DESKTOP_HEIGHT = 760;
const PHONE = { width: 390, height: 780 };

interface Viewport {
  width: number;
  height: number;
  scale: number;
}

// The room is 0 before layout and while hidden; 1 px keeps the scale above zero.
export function fit(device: Device, space: { width: number; height: number }): Viewport {
  const width = Math.max(space.width, 1);
  const height = Math.max(space.height, 1);
  if (device === 'phone') {
    return { ...PHONE, scale: Math.min(1, width / PHONE.width, height / PHONE.height) };
  }
  const scale = Math.min(1, width / MIN_DESKTOP_WIDTH, height / MIN_DESKTOP_HEIGHT);
  return { width: width / scale, height: height / scale, scale };
}

function choiceLabel(text: string) {
  return <span className="@max-[36rem]:sr-only">{text}</span>;
}

const SCHEMES: readonly SegmentedOption<ColorScheme>[] = [
  { value: 'light', label: choiceLabel('Light'), icon: Sun },
  { value: 'dark', label: choiceLabel('Dark'), icon: Moon },
];

const DEVICES: readonly SegmentedOption<Device>[] = [
  { value: 'desktop', label: choiceLabel('Desktop'), icon: Monitor },
  { value: 'phone', label: choiceLabel('Phone'), icon: Smartphone },
];

// Line and label stay readable when the frame is scaled down.
function Outline({ rect, scale, label, selected }: { rect: PreviewRect; scale: number; label?: string | undefined; selected: boolean }) {
  const top = rect.y * scale;
  return (
    <div
      aria-hidden
      className={clsx('pointer-events-none absolute rounded-md outline-2 outline-offset-2', selected ? 'outline-focus' : 'outline-focus/70 outline-dashed')}
      style={{ left: rect.x * scale, top, width: rect.width * scale, height: rect.height * scale }}
    >
      {label ? (
        <span
          className={clsx(
            'absolute left-0 rounded-md border border-border bg-raised px-1.5 py-0.5 text-xs font-medium whitespace-nowrap text-fg shadow-menu',
            top >= 28 ? '-top-7' : '-bottom-7',
          )}
        >
          {label}
        </span>
      ) : null}
    </div>
  );
}

export interface PreviewCanvasProps {
  frameRef: Ref<HTMLIFrameElement>;
  scene: PreviewScene;
  onSceneChange: (scene: PreviewScene) => void;
  colorScheme: ColorScheme;
  onColorSchemeChange: (scheme: ColorScheme) => void;
  device: Device;
  onDeviceChange: (device: Device) => void;
  hovered: { rect: PreviewRect; label: string } | null;
  selected: { rect: PreviewRect; label: string } | null;
  painted: boolean;
}

// Show the page anyway if the preview never reports a paint, e.g. a stale cached build.
const PAINT_TIMEOUT_MS = 2500;

export function PreviewCanvas({
  frameRef,
  scene,
  onSceneChange,
  colorScheme,
  onColorSchemeChange,
  device,
  onDeviceChange,
  hovered,
  selected,
  painted,
}: PreviewCanvasProps) {
  const sceneId = useId();
  const [roomElement, setRoomElement] = useState<HTMLDivElement | null>(null);
  const room = useElementSize(roomElement);
  const [loadedPage, setLoadedPage] = useState(false);
  const [timedOut, setTimedOut] = useState(false);
  // The frame paints a light page before the widget arrives; fade it in once painted to avoid a flash in dark mode.
  const visible = painted || timedOut;

  useEffect(() => {
    if (!loadedPage || painted) {
      return undefined;
    }
    const timer = window.setTimeout(() => setTimedOut(true), PAINT_TIMEOUT_MS);
    return () => window.clearTimeout(timer);
  }, [loadedPage, painted]);

  const viewport = fit(device, room ?? { width: MIN_DESKTOP_WIDTH, height: 640 });

  return (
    <section aria-label="Preview" className="@container flex h-full min-h-0 flex-col gap-2.5">
      <div className="flex flex-wrap items-center gap-2">
        <div className="flex items-center gap-2">
          <label htmlFor={sceneId} className="text-small font-medium text-fg-secondary">
            Scene
          </label>
          {/* Fits the longest scene, "Waiting for a person". */}
          <Select
            id={sceneId}
            value={scene}
            onChange={onSceneChange}
            options={PREVIEW_SCENES.map((value) => ({ value, label: SCENE_LABELS[value] }))}
            className="w-52"
          />
        </div>
        <SegmentedControl label="Visitor's color scheme" options={SCHEMES} value={colorScheme} onChange={onColorSchemeChange} />
        <SegmentedControl label="Device" options={DEVICES} value={device} onChange={onDeviceChange} />
      </div>
      <div className="dot-grid min-h-0 flex-1 rounded-2xl border border-border-subtle bg-surface-subtle p-3">
        <div ref={setRoomElement} className="relative size-full">
          <div
            className={clsx(
              'absolute top-0 left-1/2 -translate-x-1/2 bg-fill-muted shadow-dialog',
              device === 'phone' ? 'rounded-[1.75rem] ring-4 ring-border-strong' : 'rounded-2xl ring-1 ring-border',
            )}
            style={{ width: viewport.width * viewport.scale, height: viewport.height * viewport.scale }}
          >
            <div className={clsx('relative size-full overflow-hidden', device === 'phone' ? 'rounded-[1.75rem]' : 'rounded-2xl')}>
              {visible ? null : <span aria-hidden className="absolute inset-0 animate-pulse bg-fill-muted" />}
              <iframe
                ref={frameRef}
                src={PREVIEW_URL}
                title="Widget preview"
                onLoad={() => setLoadedPage(true)}
                className={clsx('block origin-top-left border-0 transition-opacity duration-150 ease-out', visible ? 'opacity-100' : 'opacity-0')}
                style={{ width: viewport.width, height: viewport.height, transform: `scale(${viewport.scale})` }}
              />
            </div>
            {hovered ? <Outline rect={hovered.rect} scale={viewport.scale} label={hovered.label} selected={false} /> : null}
            {selected ? <Outline rect={selected.rect} scale={viewport.scale} label={selected.label} selected /> : null}
          </div>
        </div>
      </div>
    </section>
  );
}
