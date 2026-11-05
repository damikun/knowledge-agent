import { clsx } from 'clsx';
import { X } from 'lucide-react';
import { useEffect, useSyncExternalStore, type FocusEvent } from 'react';
import { Button } from './Button';
import { TONES, type Tone } from './tone';

export interface ToastOptions {
  title: string;
  description?: string;
  tone?: Tone;
  /** One follow-up, such as Undo or Open; choosing it closes the toast. */
  action?: { label: string; onClick: () => void };
}

interface ToastEntry extends ToastOptions {
  id: number;
}

// Enough to read two lines. The timers pause while the pointer or focus is on a toast (WCAG 2.2.1).
const DURATION = 5000;
const MAX_TOASTS = 3;

let entries: ToastEntry[] = [];
let nextId = 1;
let paused = false;
const timers = new Map<number, { timeout?: ReturnType<typeof setTimeout>; remaining: number; started: number }>();
const listeners = new Set<() => void>();

function emit(): void {
  listeners.forEach((listener) => listener());
}

function startTimer(id: number): void {
  const timer = timers.get(id);
  if (timer) {
    timer.started = Date.now();
    timer.timeout = setTimeout(() => dismissToast(id), timer.remaining);
  }
}

function stopTimer(id: number): void {
  clearTimeout(timers.get(id)?.timeout);
  timers.delete(id);
}

/** Returns an id for dismissToast. */
export function toast(options: ToastOptions | string): number {
  const entry: ToastEntry = { ...(typeof options === 'string' ? { title: options } : options), id: nextId++ };
  timers.set(entry.id, { remaining: DURATION, started: 0 });
  if (!paused) {
    startTimer(entry.id);
  }
  const all = [...entries, entry];
  all.slice(0, -MAX_TOASTS).forEach((old) => stopTimer(old.id));
  entries = all.slice(-MAX_TOASTS);
  emit();
  return entry.id;
}

export function dismissToast(id: number): void {
  stopTimer(id);
  entries = entries.filter((entry) => entry.id !== id);
  emit();
}

/** The list lives outside React, so tests clear it between runs. */
export function dismissAllToasts(): void {
  entries.forEach((entry) => stopTimer(entry.id));
  entries = [];
  emit();
}

function setPaused(next: boolean): void {
  if (paused === next) {
    return;
  }
  paused = next;
  timers.forEach((timer, id) => {
    if (next) {
      clearTimeout(timer.timeout);
      timer.remaining -= Date.now() - timer.started;
    } else {
      startTimer(id);
    }
  });
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function ToastItem({ id, title, description, tone = 'neutral', action }: ToastEntry) {
  const Icon = tone === 'neutral' ? null : TONES[tone].Icon;
  return (
    <li data-tone={tone} className="flex animate-dialog-in items-start gap-2.5 rounded-xl border border-border bg-raised p-3 shadow-menu">
      {Icon ? <Icon className={clsx('mt-0.5 size-4 shrink-0', TONES[tone].icon)} aria-hidden /> : null}
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-fg">{title}</p>
        {description ? <p className="mt-0.5 text-small text-fg-secondary">{description}</p> : null}
      </div>
      {action ? (
        <Button
          size="sm"
          variant="ghost"
          className="-my-1"
          onClick={() => {
            dismissToast(id);
            action.onClick();
          }}
        >
          {action.label}
        </Button>
      ) : null}
      <Button variant="ghost" size="icon-sm" icon={X} aria-label="Dismiss" className="-my-1 -mr-1" onClick={() => dismissToast(id)} />
    </li>
  );
}

/** Render once. The list stays mounted so screen readers announce each toast. */
export function Toaster() {
  const items = useSyncExternalStore(subscribe, () => entries, () => entries);
  // Leaving while hovered sends no pointerleave, so the timers would stay paused.
  useEffect(() => () => setPaused(false), []);
  const onBlur = (event: FocusEvent<HTMLOListElement>) => {
    if (!event.currentTarget.contains(event.relatedTarget)) {
      setPaused(false);
    }
  };
  return (
    <section aria-label="Notifications" className="pointer-events-none fixed inset-x-4 bottom-4 z-60 sm:left-auto sm:w-90">
      <ol
        aria-live="polite"
        className="pointer-events-auto flex flex-col gap-2"
        onPointerEnter={() => setPaused(true)}
        onPointerLeave={() => setPaused(false)}
        onFocus={() => setPaused(true)}
        onBlur={onBlur}
      >
        {items.map((item) => (
          <ToastItem key={item.id} {...item} />
        ))}
      </ol>
    </section>
  );
}
