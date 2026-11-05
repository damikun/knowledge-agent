import { clsx } from 'clsx';
import { ChevronDown, ChevronRight, Maximize2 } from 'lucide-react';
import { useCallback, useId, useState, type ReactNode } from 'react';
import { SideSheet } from '../ui/SideSheet';

export interface GraphPanelProps {
  title: string;
  description?: string;
  /** Height of the small graph, in pixels. */
  height?: number;
  /** The small view sizes to fit this, never zoomed past 100%, up to `maxHeight`. */
  naturalSize?: { width: number; height: number };
  maxHeight?: number;
  /** A header that opens and closes the graph, for narrow screens where it sits in the form's flow. */
  collapsible?: boolean;
  defaultOpen?: boolean;
  /** The graph; `large` is true in the side sheet, where it can pan and zoom. */
  children: (large: boolean) => ReactNode;
  className?: string;
}

// The fit view leaves about 8% around the graph.
const PADDING = 1.16;

function fittedHeight(size: { width: number; height: number }, width: number, maxHeight: number): number {
  const zoom = width > 0 ? Math.min(1, width / (size.width * PADDING)) : 1;
  return Math.round(Math.min(maxHeight, size.height * PADDING * zoom));
}

export function GraphPanel({
  title,
  description,
  height = 320,
  naturalSize,
  maxHeight = 560,
  collapsible = false,
  defaultOpen = true,
  children,
  className,
}: GraphPanelProps) {
  const [open, setOpen] = useState(defaultOpen);
  const [width, setWidth] = useState(0);
  const measure = useCallback((element: HTMLDivElement | null) => {
    if (!element || typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver(([entry]) => setWidth(entry?.contentRect.width ?? 0));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  const bodyHeight = naturalSize ? fittedHeight(naturalSize, width, maxHeight) : height;
  const [large, setLarge] = useState(false);
  const bodyId = useId();
  const shown = !collapsible || open;
  return (
    <div className={clsx('rounded-2xl border border-border bg-surface', className)}>
      <div className="flex h-10 items-center gap-2 pr-1.5 pl-3">
        {collapsible ? (
          <button
            type="button"
            aria-expanded={open}
            aria-controls={bodyId}
            onClick={() => setOpen((value) => !value)}
            className="flex min-w-0 flex-1 items-center gap-1.5 text-left text-sm font-medium text-fg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          >
            {open ? <ChevronDown className="size-4 shrink-0 text-fg-muted" aria-hidden /> : <ChevronRight className="size-4 shrink-0 text-fg-muted" aria-hidden />}
            <span className="truncate">{title}</span>
          </button>
        ) : (
          <h3 className="min-w-0 flex-1 truncate text-sm font-medium text-fg">{title}</h3>
        )}
        <button
          type="button"
          className="grid size-7 shrink-0 place-items-center rounded-md text-fg-muted hover:bg-fill-hover hover:text-fg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          aria-label={`Open larger: ${title}`}
          title="Open larger"
          onClick={() => setLarge(true)}
        >
          <Maximize2 className="size-3.5" aria-hidden />
        </button>
      </div>
      {shown ? (
        <div id={bodyId} ref={measure} className="border-t border-border-subtle" style={{ height: bodyHeight }}>
          {large ? null : children(false)}
        </div>
      ) : null}
      <SideSheet open={large} onClose={() => setLarge(false)} title={title} description={description} size="lg">
        <div className="h-[calc(100dvh-10rem)] min-h-80 overflow-hidden rounded-2xl border border-border bg-surface-subtle">{large ? children(true) : null}</div>
      </SideSheet>
    </div>
  );
}
