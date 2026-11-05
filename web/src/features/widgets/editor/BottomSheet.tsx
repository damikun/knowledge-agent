import { clsx } from 'clsx';
import { Maximize2, Minimize2 } from 'lucide-react';
import { useId, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react';
import { Button } from '../../../components/ui/Button';

export type SheetSnap = 'collapsed' | 'half' | 'full';

export const SHEET_HANDLE_HEIGHT = 28;
// Gap above a full sheet, so the preview still peeks out.
const TOP_GAP = 12;
// px of movement before a drag starts, so a tap stays a tap.
const DRAG_THRESHOLD = 6;
// px/ms; a faster release goes on to the next snap point.
const FLING = 0.5;
const SIZES: readonly SheetSnap[] = ['collapsed', 'half', 'full'];

/** The next snap point in the fling's direction, else the nearest one. */
export function snapAfterDrag(heights: Record<SheetSnap, number>, height: number, velocity: number): SheetSnap {
  if (Math.abs(velocity) >= FLING) {
    // Positive velocity is downward, toward smaller.
    const ahead = velocity > 0 ? [...SIZES].reverse().filter((snap) => heights[snap] < height) : SIZES.filter((snap) => heights[snap] > height);
    if (ahead[0]) {
      return ahead[0];
    }
  }
  return SIZES.reduce((best, snap) => (Math.abs(heights[snap] - height) < Math.abs(heights[best] - height) ? snap : best));
}

interface Gesture {
  pointerId: number;
  startY: number;
  startHeight: number;
  heights: Record<SheetSnap, number>;
  samples: Array<{ y: number; time: number }>;
  dragging: boolean;
}

export interface BottomSheetProps {
  label: string;
  snap: SheetSnap;
  onSnapChange: (snap: SheetSnap) => void;
  peek: number;
  children: ReactNode;
}

/** Resizes rather than moves, so its content always scrolls within the visible part. */
export function BottomSheet({ label, snap, onSnapChange, peek, children }: BottomSheetProps) {
  const sheetRef = useRef<HTMLElement>(null);
  const handleRef = useRef<HTMLButtonElement>(null);
  const bodyId = useId();
  const hintId = useId();
  const [dragHeight, setDragHeight] = useState<number | null>(null);
  const gesture = useRef<Gesture | null>(null);

  const heights = (): Record<SheetSnap, number> => {
    const room = sheetRef.current?.parentElement?.clientHeight ?? 0;
    return { collapsed: peek, half: Math.max(peek, room / 2), full: Math.max(peek, room - TOP_GAP) };
  };
  const clamp = (height: number, limits: Record<SheetSnap, number>) => Math.min(limits.full, Math.max(limits.collapsed, height));

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) {
      return;
    }
    // Capture at once, or a quick flick leaves the strip for the iframe before the drag starts.
    event.currentTarget.setPointerCapture(event.pointerId);
    gesture.current = {
      pointerId: event.pointerId,
      startY: event.clientY,
      startHeight: sheetRef.current?.offsetHeight ?? 0,
      heights: heights(),
      samples: [{ y: event.clientY, time: event.timeStamp }],
      dragging: false,
    };
  };

  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;
    if (!current || current.pointerId !== event.pointerId) {
      return;
    }
    const dy = event.clientY - current.startY;
    if (!current.dragging) {
      if (Math.abs(dy) < DRAG_THRESHOLD) {
        return;
      }
      current.dragging = true;
    }
    current.samples = [...current.samples.slice(-4), { y: event.clientY, time: event.timeStamp }];
    setDragHeight(clamp(current.startHeight - dy, current.heights));
  };

  const onPointerEnd = (event: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;
    if (!current || current.pointerId !== event.pointerId) {
      return;
    }
    gesture.current = null;
    if (!current.dragging) {
      // The strip holds the pointer capture, so a tap toggles here rather than as a click on the handle.
      if (event.type === 'pointerup') {
        toggle();
      }
      return;
    }
    setDragHeight(null);
    if (event.type === 'pointerup') {
      const first = current.samples[0]!;
      const last = current.samples[current.samples.length - 1]!;
      const velocity = (last.y - first.y) / Math.max(1, last.time - first.time);
      onSnapChange(snapAfterDrag(current.heights, clamp(current.startHeight - (event.clientY - current.startY), current.heights), velocity));
    }
  };

  const toggle = () => onSnapChange(snap === 'collapsed' ? 'half' : 'collapsed');

  const onHandleKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    const step = event.key === 'ArrowUp' ? 1 : event.key === 'ArrowDown' ? -1 : 0;
    const next = SIZES[SIZES.indexOf(snap) + step];
    if (step !== 0 && next) {
      event.preventDefault();
      onSnapChange(next);
    }
  };

  const resting = { collapsed: `${peek}px`, half: '50%', full: `calc(100% - ${TOP_GAP}px)` }[snap];
  return (
    <section
      ref={sheetRef}
      aria-label={label}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && snap !== 'collapsed') {
          event.preventDefault();
          onSnapChange('collapsed');
          // The focused content goes out of view, so keep focus on the handle.
          handleRef.current?.focus();
        }
      }}
      className={clsx(
        // An upward shadow, which no elevation token has.
        'absolute inset-x-0 bottom-0 z-20 flex flex-col overflow-hidden rounded-t-2xl border border-b-0 border-border bg-raised shadow-[0_-16px_40px_-16px_rgb(0_0_0/0.28)]',
        dragHeight === null && 'transition-[height] duration-300 ease-(--ease-hero) motion-reduce:transition-none',
      )}
      style={{ height: dragHeight === null ? resting : `${dragHeight}px` }}
    >
      <div
        className="flex shrink-0 touch-none items-center justify-center"
        style={{ height: SHEET_HANDLE_HEIGHT }}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerEnd}
        onPointerCancel={onPointerEnd}
      >
        <button
          ref={handleRef}
          type="button"
          aria-label={label}
          aria-expanded={snap !== 'collapsed'}
          aria-controls={bodyId}
          aria-describedby={hintId}
          title="Drag, or use the arrow keys"
          onClick={(event) => {
            // Keyboard activation only; pointer taps are handled on pointerup.
            if (event.detail === 0) {
              toggle();
            }
          }}
          onKeyDown={onHandleKeyDown}
          className="group grid h-full w-20 cursor-grab place-items-center rounded-full active:cursor-grabbing"
        >
          <span aria-hidden className="h-1.5 w-10 rounded-full bg-border-strong transition-colors duration-100 ease-out group-hover:bg-fg-muted" />
        </button>
      </div>
      <span id={hintId} className="sr-only">
        Drag the handle, or use the up and down arrow keys, to resize.
      </span>
      <Button
        variant="ghost"
        size="icon-sm"
        icon={snap === 'full' ? Minimize2 : Maximize2}
        aria-label={snap === 'full' ? 'Half height' : 'Full height'}
        title={snap === 'full' ? 'Half height' : 'Full height'}
        onClick={() => onSnapChange(snap === 'full' ? 'half' : 'full')}
        className="absolute top-0.5 right-2 z-10"
      />
      <div id={bodyId} className="relative min-h-0 flex-1">
        {children}
      </div>
    </section>
  );
}
