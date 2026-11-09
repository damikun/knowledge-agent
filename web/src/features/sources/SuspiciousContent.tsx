import { ShieldAlert } from 'lucide-react';
import { Badge } from '../../components/ui/Badge';
import { pluralize } from '../../lib/format';
import type { SourceDto } from './types';

/** How many passages look written to the assistant rather than the reader; set at ingestion. */
export function suspiciousChunks(source: Pick<SourceDto, 'metadata'>): number {
  const count = Number.parseInt(source.metadata?.suspiciousChunks ?? '', 10);
  return Number.isFinite(count) && count > 0 ? count : 0;
}

export function suspiciousText(count: number): string {
  return `${pluralize(count, 'passage')} ${count === 1 ? 'looks' : 'look'} written to the assistant. Read ${count === 1 ? 'it' : 'them'} before you rely on this source.`;
}

export function SuspiciousBadge({ count }: { count: number }) {
  return (
    <Badge tone="warning" title={suspiciousText(count)}>
      <ShieldAlert className="size-3" aria-hidden />
      Suspicious content
    </Badge>
  );
}
