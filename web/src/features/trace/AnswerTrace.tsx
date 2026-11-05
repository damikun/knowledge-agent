import { lazy, Suspense } from 'react';
import { Skeleton } from '../../components/ui/Skeleton';
import type { AnswerTraceProps } from './AnswerTraceGraph';

export type { AnswerTraceProps } from './AnswerTraceGraph';

// Lazy, so React Flow and dagre stay out of the playground chunk.
const AnswerTraceGraph = lazy(() => import('./AnswerTraceGraph'));

export function AnswerTrace(props: AnswerTraceProps) {
  return (
    <Suspense
      fallback={
        <div aria-busy>
          <p role="status" className="sr-only">
            Loading the trace
          </p>
          <Skeleton className="w-full" style={{ height: props.height ?? '28rem' }} />
        </div>
      }
    >
      <AnswerTraceGraph {...props} />
    </Suspense>
  );
}
