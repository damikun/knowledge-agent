import type { ReactNode } from 'react';
import { formatDuration, formatNumber } from '../../lib/format';
import type { TurnView } from '../chat/chatReducer';
import { extractCitationRefs } from '../chat/citations';
import type { CitationDto } from '../chat/types';
import type { TraceNode } from './traceGraph';

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[5.5rem_1fr] gap-2 py-1">
      <dt className="text-fg-muted">{label}</dt>
      <dd className="min-w-0 break-words text-fg">{children}</dd>
    </div>
  );
}

function Heading({ children }: { children: ReactNode }) {
  return <h4 className="mb-1 text-xs font-medium text-fg-muted">{children}</h4>;
}

function Passages({ citations }: { citations: readonly CitationDto[] }) {
  if (citations.length === 0) {
    return null;
  }
  const sorted = [...citations].sort((a, b) => b.score - a.score);
  return (
    <section>
      <Heading>Passages</Heading>
      <ol className="space-y-2">
        {sorted.map((citation) => (
          <li key={citation.ref} className="rounded-lg border border-border-subtle bg-surface p-2">
            <div className="flex items-baseline gap-2">
              <span className="font-mono text-fg-muted">{citation.ref}</span>
              <span className="min-w-0 flex-1 truncate font-medium text-fg" title={citation.sourceTitle}>
                {citation.sourceTitle}
              </span>
              <span className="shrink-0 font-mono text-fg-muted tabular-nums" title="Score">
                {citation.score.toFixed(3)}
              </span>
            </div>
            {citation.page != null ? <p className="text-fg-muted">Page {citation.page}</p> : null}
            <p className="mt-1 line-clamp-4 text-fg-secondary">{citation.snippet}</p>
          </li>
        ))}
      </ol>
    </section>
  );
}

function Json({ value }: { value: unknown }) {
  return <pre className="max-h-48 overflow-auto rounded-lg bg-surface p-2 font-mono text-xs break-words whitespace-pre-wrap text-fg">{JSON.stringify(value, null, 2)}</pre>;
}

export function TraceDetails({ node, turn }: { node: TraceNode | null; turn: TurnView }) {
  if (!node) {
    return <p className="text-small text-fg-muted">Select a step to see what it did.</p>;
  }

  if (node.kind === 'question') {
    return (
      <div className="space-y-2 text-xs">
        <Heading>Question</Heading>
        <p className="text-small break-words whitespace-pre-wrap text-fg">{turn.userMessage}</p>
      </div>
    );
  }

  if (node.kind === 'answer') {
    const cited = new Set(extractCitationRefs(turn.text).map((ref) => ref.toUpperCase()));
    const citations = turn.citations.filter((citation) => cited.has(citation.ref.toUpperCase()));
    return (
      <div className="space-y-3 text-xs">
        <dl className="divide-y divide-border-subtle">
          {turn.model ? (
            <Row label="Model">
              <span className="font-mono">{turn.model}</span>
              {turn.provider ? <span className="text-fg-muted"> · {turn.provider}</span> : null}
            </Row>
          ) : null}
          {turn.usage ? (
            <Row label="Tokens">
              <span className="tabular-nums">
                {turn.usage.inputTokens != null ? `${formatNumber(turn.usage.inputTokens)} in` : '–'} ·{' '}
                {turn.usage.outputTokens != null ? `${formatNumber(turn.usage.outputTokens)} out` : '–'}
              </span>
            </Row>
          ) : null}
          {turn.durationMs ? <Row label="Time">{formatDuration(turn.durationMs)}</Row> : null}
          {turn.finishReason ? (
            <Row label="Finished">
              <span className="font-mono">{turn.finishReason}</span>
            </Row>
          ) : null}
        </dl>
        {turn.errors.map((error, index) => (
          <p key={index} className="font-medium text-danger-text">
            {error.message}
          </p>
        ))}
        <Passages citations={citations} />
      </div>
    );
  }

  const call = node.call;
  if (!call) {
    return null;
  }
  const sub = call.subAgent;
  return (
    <div className="space-y-3 text-xs">
      <dl className="divide-y divide-border-subtle">
        <Row label="Tool">
          <span className="font-mono">{call.toolName}</span>
        </Row>
        <Row label="Time">{call.durationMs != null ? formatDuration(call.durationMs) : '–'}</Row>
        {sub?.model ? (
          <Row label="Model">
            <span className="font-mono">{sub.model}</span>
            {sub.provider ? <span className="text-fg-muted"> · {sub.provider}</span> : null}
          </Row>
        ) : null}
        {sub?.iterations != null ? <Row label="Rounds">{formatNumber(sub.iterations)}</Row> : null}
        {sub?.usage ? (
          <Row label="Tokens">
            <span className="tabular-nums">
              {sub.usage.inputTokens != null ? `${formatNumber(sub.usage.inputTokens)} in` : '–'} ·{' '}
              {sub.usage.outputTokens != null ? `${formatNumber(sub.usage.outputTokens)} out` : '–'}
            </span>
          </Row>
        ) : null}
      </dl>
      {call.arguments != null ? (
        <section>
          <Heading>Arguments</Heading>
          <Json value={call.arguments} />
        </section>
      ) : null}
      {call.summary ? (
        <section>
          <Heading>Result</Heading>
          <p className={call.status === 'error' ? 'text-danger-text' : 'text-fg-secondary'}>{call.summary}</p>
        </section>
      ) : null}
      {sub?.text ? (
        <section>
          <Heading>Findings</Heading>
          <p className="max-h-48 overflow-auto break-words whitespace-pre-wrap text-fg-secondary">{sub.text}</p>
        </section>
      ) : null}
      <Passages citations={call.citations} />
    </div>
  );
}
