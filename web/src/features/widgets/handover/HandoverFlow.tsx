import { lazy, Suspense } from 'react';
import { Skeleton } from '../../../components/ui/Skeleton';
import type { HandoverFlowProps } from './HandoverFlowView';

export type { HandoverFlowProps } from './HandoverFlowView';

const HandoverFlowView = lazy(() => import('./HandoverFlowView'));

export function HandoverFlow(props: HandoverFlowProps) {
  return (
    <Suspense fallback={<Skeleton className="h-72 w-full rounded-2xl" />}>
      <HandoverFlowView {...props} />
    </Suspense>
  );
}
