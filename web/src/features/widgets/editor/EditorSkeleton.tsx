import { ArrowLeft } from 'lucide-react';
import { Link } from 'react-router';
import { buttonClass } from '../../../components/ui/Button';
import { Skeleton } from '../../../components/ui/Skeleton';
import { useBreadcrumb } from '../../../lib/breadcrumb';

/** Same layout and breakpoint as WidgetEditor, so the editor fills in without a jump. */
export function EditorSkeleton({ title }: { title?: string | undefined }) {
  useBreadcrumb(title);
  return (
    <div className="@container flex h-full flex-col">
      <p role="status" className="sr-only">
        Loading widget
      </p>
      <header className="flex min-h-12 shrink-0 items-center gap-x-3 px-4 py-2 lg:px-6">
        <Link to="/widgets" aria-label="All widgets" title="All widgets" className={buttonClass({ variant: 'ghost', size: 'icon' })}>
          <ArrowLeft className="size-4" aria-hidden />
        </Link>
        <div className="flex h-8 min-w-0 flex-1 items-center gap-2.5" aria-hidden>
          {title ? <span className="truncate text-section text-fg">{title}</span> : <Skeleton className="h-4 w-40" />}
          <Skeleton className="h-5 w-16" />
        </div>
        <div className="flex items-center gap-1.5" aria-hidden>
          <Skeleton shape="tile" className="size-7" />
          <Skeleton shape="tile" className="size-7" />
          <Skeleton shape="tile" className="h-7 w-20" />
          <Skeleton shape="tile" className="h-7 w-16" />
        </div>
      </header>
      <div
        aria-hidden
        className="grid min-h-0 flex-1 grid-rows-[minmax(0,1fr)_7.75rem] gap-2 px-4 lg:px-6 @min-[56.25rem]:grid-cols-[minmax(0,1fr)_22.5rem] @min-[56.25rem]:grid-rows-[minmax(0,1fr)] @min-[56.25rem]:gap-3 @min-[56.25rem]:pb-4"
      >
        <div className="flex min-h-0 flex-col gap-2.5">
          <div className="flex h-8 items-center gap-2">
            <Skeleton shape="tile" className="h-8 w-40" />
            <Skeleton shape="tile" className="h-8 w-36" />
            <Skeleton shape="tile" className="h-8 w-44" />
          </div>
          <div className="dot-grid min-h-0 flex-1 rounded-2xl border border-border-subtle bg-surface-subtle p-3">
            <span className="block size-full rounded-2xl bg-fill-muted" />
          </div>
        </div>
        <div className="hidden min-h-0 flex-col overflow-hidden rounded-2xl border border-border bg-surface @min-[56.25rem]:flex">
          <div className="space-y-2.5 border-b border-border-subtle p-3">
            <Skeleton shape="tile" className="h-8 w-full" />
            <div className="flex h-10 items-center gap-6 px-1">
              <Skeleton className="h-3.5 w-16" />
              <Skeleton className="h-3.5 w-12" />
              <Skeleton className="h-3.5 w-14" />
            </div>
          </div>
          <div className="space-y-4 p-4">
            {[0.4, 0.55, 0.35, 0.5, 0.45, 0.6].map((width, index) => (
              <Skeleton key={index} className="h-3.5" style={{ width: `${width * 100}%` }} />
            ))}
          </div>
        </div>
        <div className="-mx-4 flex flex-col rounded-t-2xl border border-b-0 border-border bg-raised px-3 lg:-mx-6 @min-[56.25rem]:hidden">
          <span className="mx-auto mt-3 mb-2.5 block h-1.5 w-10 rounded-full bg-border-strong" />
          <Skeleton shape="tile" className="h-8 w-full" />
          <div className="flex h-10 items-center justify-around">
            <Skeleton className="h-3.5 w-16" />
            <Skeleton className="h-3.5 w-12" />
            <Skeleton className="h-3.5 w-14" />
          </div>
        </div>
      </div>
    </div>
  );
}
