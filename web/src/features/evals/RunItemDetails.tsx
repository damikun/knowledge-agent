import { clsx } from 'clsx';
import { Check, CircleCheck, CircleX, ExternalLink, type LucideIcon } from 'lucide-react';
import { useMemo, type ReactNode } from 'react';
import { Badge, type BadgeTone } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/feedback';
import { formatDuration, formatNumber, pluralize, safeExternalUrl } from '../../lib/format';
import { Markdown } from '../chat/Markdown';
import { formatPercent } from './metrics';
import type { EvalCitationResultDto, EvalCriterionDto, EvalItemResultDto, EvalJudgementDto } from './types';

type CitationState = 'expected' | 'valid' | 'invalid' | 'unknown';

function citationState(citation: EvalCitationResultDto | undefined): CitationState {
  if (!citation) {
    return 'unknown';
  }
  if (!citation.valid) {
    return 'invalid';
  }
  return citation.expected ? 'expected' : 'valid';
}

const CITATION_STATES: Record<CitationState, { note: string; tone: BadgeTone; icon: LucideIcon; chip: string }> = {
  expected: { note: 'valid · expected', tone: 'success', icon: CircleCheck, chip: 'bg-success-subtle text-success-text' },
  valid: { note: 'valid · not expected', tone: 'neutral', icon: Check, chip: 'bg-fill-muted text-fg-secondary' },
  invalid: { note: 'invalid', tone: 'danger', icon: CircleX, chip: 'bg-danger-subtle text-danger-text' },
  unknown: { note: 'not checked', tone: 'neutral', icon: Check, chip: 'bg-fill-muted text-fg-muted' },
};

function citationTooltip(ref: string, citation: EvalCitationResultDto | undefined): string {
  if (!citation) {
    return `${ref} was not checked`;
  }
  if (!citation.valid) {
    return `${ref} does not refer to a passage the agent received`;
  }
  return `${ref} · ${citation.title || 'Untitled source'} · ${citation.expected ? 'an expected source' : 'not an expected source'}`;
}

/** No popover: eval sources are not in the library. */
function AnswerCitation({ citationRef, citation }: { citationRef: string; citation: EvalCitationResultDto | undefined }) {
  const state = citationState(citation);
  return (
    <span
      data-citation-state={state}
      title={citationTooltip(citationRef, citation)}
      className={clsx('not-prose mx-0.5 inline-flex h-4.5 items-center rounded-sm px-1 align-baseline text-[11px] font-semibold tabular-nums', CITATION_STATES[state].chip)}
    >
      {citationRef}
    </span>
  );
}

function EvalAnswer({ answer, citations }: { answer: string; citations: EvalCitationResultDto[] }) {
  const renderCitation = useMemo(() => {
    const byRef = new Map(citations.map((citation) => [citation.ref.toUpperCase(), citation] as const));
    return (ref: string) => <AnswerCitation citationRef={ref} citation={byRef.get(ref.toUpperCase())} />;
  }, [citations]);
  return <Markdown text={answer} renderCitation={renderCitation} />;
}

function CitationResultBadge({ citation }: { citation: EvalCitationResultDto }) {
  const state = citationState(citation);
  const { tone, icon: Icon, note } = CITATION_STATES[state];
  return (
    <Badge tone={tone} data-citation-state={state} title={citationTooltip(citation.ref, citation)} className="max-w-full">
      <Icon className="size-3 shrink-0" aria-hidden />
      <span className="font-semibold">{citation.ref}</span>
      {citation.title ? <span className="min-w-0 truncate font-normal">{citation.title}</span> : null}
      <span className="font-normal opacity-75">· {note}</span>
    </Badge>
  );
}

const VERDICTS: Record<string, { label: string; tone: BadgeTone }> = {
  yes: { label: 'Yes', tone: 'success' },
  partially: { label: 'Partially', tone: 'warning' },
  no: { label: 'No', tone: 'danger' },
};

const CRITERIA: Record<string, string> = { faithfulness: 'Faithfulness', relevance: 'Relevance', correctness: 'Correctness' };

function Criterion({ criterion }: { criterion: EvalCriterionDto }) {
  const verdict = VERDICTS[criterion.verdict?.toLowerCase()];
  return (
    <li className="space-y-0.5">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium text-fg">{CRITERIA[criterion.name?.toLowerCase()] ?? criterion.name}</span>
        <Badge tone={verdict?.tone ?? 'neutral'}>{verdict?.label ?? criterion.verdict}</Badge>
        <span className="text-xs text-fg-muted tabular-nums">{formatPercent(criterion.score)}</span>
      </div>
      {criterion.reasoning ? <p className="break-words text-fg-secondary">{criterion.reasoning}</p> : null}
      {criterion.issues.length > 0 ? (
        <ul className="list-disc space-y-0.5 pl-4 text-xs text-danger-text" aria-label={`${CRITERIA[criterion.name?.toLowerCase()] ?? criterion.name} issues`}>
          {criterion.issues.map((issue, index) => (
            <li key={index} className="break-words">
              {issue}
            </li>
          ))}
        </ul>
      ) : null}
    </li>
  );
}

function Judgement({ judgement }: { judgement: EvalJudgementDto }) {
  return (
    <div className="space-y-2">
      {judgement.error ? (
        <Alert tone="warning" title="The judge could not score this answer">
          {judgement.error}
        </Alert>
      ) : null}
      {judgement.criteria.length > 0 ? (
        <ul className="space-y-2.5" aria-label="Judge criteria">
          {judgement.criteria.map((criterion) => (
            <Criterion key={criterion.name} criterion={criterion} />
          ))}
        </ul>
      ) : null}
      {judgement.model ? <p className="text-xs text-fg-muted">Judged by {judgement.model}</p> : null}
    </div>
  );
}

function Block({ title, children, aside }: { title: string; children: ReactNode; aside?: ReactNode }) {
  return (
    <section className="min-w-0 space-y-1.5">
      <div className="flex items-baseline justify-between gap-2">
        <h3 className="text-xs font-medium text-fg-muted">{title}</h3>
        {aside ? <span className="text-xs text-fg-muted">{aside}</span> : null}
      </div>
      {children}
    </section>
  );
}

function Muted({ children }: { children: ReactNode }) {
  return <p className="text-sm text-fg-muted">{children}</p>;
}

export function ItemDetails({ item }: { item: EvalItemResultDto }) {
  const missing = new Set(item.missingFacts);
  const expectedSet = new Set(item.expectedFacts);
  // Missing facts should be a subset of the expected ones, but list any extras too.
  const facts = [...item.expectedFacts, ...item.missingFacts.filter((fact) => !expectedSet.has(fact))];
  const found = facts.filter((fact) => !missing.has(fact)).length;
  const traceUrl = safeExternalUrl(item.langfuseTraceUrl);
  const tokens = [
    item.inputTokens != null ? `${formatNumber(item.inputTokens)} in` : null,
    item.outputTokens != null ? `${formatNumber(item.outputTokens)} out` : null,
  ].filter(Boolean);

  return (
    <div className="space-y-4 text-sm" data-testid="eval-item-details">
      {item.error ? (
        <Alert tone="danger" title="The agent turn failed">
          {item.error}
        </Alert>
      ) : null}
      <div className="grid gap-5 lg:grid-cols-2">
        <div className="min-w-0 space-y-4">
          <Block title="Question">
            <p className="break-words whitespace-pre-wrap text-fg">{item.question}</p>
          </Block>
          <Block title="Answer">
            {item.answer ? (
              <div className="rounded-lg border border-border bg-surface px-3.5 py-1">
                <EvalAnswer answer={item.answer} citations={item.citations} />
              </div>
            ) : (
              <Muted>No answer.</Muted>
            )}
          </Block>
          <Block title="Citations" aside={item.citations.length > 0 ? pluralize(item.citations.length, 'tag') : undefined}>
            {item.citations.length > 0 ? (
              <ul className="flex flex-wrap gap-1.5" aria-label="Citations">
                {item.citations.map((citation, index) => (
                  <li key={`${citation.ref}-${index}`} className="max-w-full">
                    <CitationResultBadge citation={citation} />
                  </li>
                ))}
              </ul>
            ) : (
              <Muted>{item.answerable ? 'The answer cites nothing.' : 'The answer cites nothing, as expected for an unanswerable question.'}</Muted>
            )}
          </Block>
          {item.referenceAnswer ? (
            <Block title="Reference answer">
              <p className="break-words whitespace-pre-wrap text-fg-secondary">{item.referenceAnswer}</p>
            </Block>
          ) : null}
        </div>

        <div className="min-w-0 space-y-4">
          <Block title="Retrieved sources" aside="Top 10 search hits, by source">
            {item.retrieved.length > 0 ? (
              <ol className="space-y-0.5" aria-label="Retrieved sources">
                {item.retrieved.map((source) => (
                  <li
                    key={`${source.rank}-${source.title}`}
                    data-expected={source.expected || undefined}
                    className={clsx('flex items-center gap-2 rounded-md px-2 py-1', source.expected && 'bg-accent-subtle')}
                  >
                    <span className="w-5 shrink-0 text-right text-xs text-fg-muted tabular-nums">{source.rank}.</span>
                    <span className="min-w-0 flex-1 truncate text-fg" title={source.title}>
                      {source.title || 'Untitled source'}
                    </span>
                    {source.expected ? <Badge tone="accent">Expected</Badge> : null}
                    <span className="shrink-0 text-xs text-fg-muted tabular-nums" title="Best hit score">
                      {Number.isFinite(source.score) ? source.score.toFixed(2) : '–'}
                    </span>
                  </li>
                ))}
              </ol>
            ) : (
              <Muted>Search returned no passages.</Muted>
            )}
            {item.expectedSources.length > 0 ? (
              <p className="text-xs text-fg-muted">
                Expected: <span className="text-fg-secondary">{item.expectedSources.join(', ')}</span>
              </p>
            ) : null}
          </Block>
          <Block title="Expected facts" aside={facts.length > 0 ? `${formatNumber(found)} of ${formatNumber(facts.length)} found` : undefined}>
            {facts.length > 0 ? (
              <ul className="space-y-1" aria-label="Expected facts">
                {facts.map((fact, index) => {
                  const isMissing = missing.has(fact);
                  return (
                    <li key={`${index}-${fact}`} data-missing={isMissing || undefined} className="flex items-start gap-2">
                      {isMissing ? (
                        <CircleX className="mt-0.5 size-3.5 shrink-0 text-danger" aria-hidden />
                      ) : (
                        <CircleCheck className="mt-0.5 size-3.5 shrink-0 text-success" aria-hidden />
                      )}
                      <span className="min-w-0 break-words text-fg-secondary">{fact}</span>
                      {isMissing ? <Badge tone="danger">Missing</Badge> : <span className="sr-only">(found)</span>}
                    </li>
                  );
                })}
              </ul>
            ) : (
              <Muted>{item.answerable ? 'No expected facts.' : 'Unanswerable: the agent should decline.'}</Muted>
            )}
          </Block>
          {item.judgement ? (
            <Block title="Judge">
              <Judgement judgement={item.judgement} />
            </Block>
          ) : null}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-border-subtle pt-3 text-xs text-fg-muted tabular-nums">
        <span>Duration {formatDuration(item.durationMs)}</span>
        {tokens.length > 0 ? <span>Tokens {tokens.join(' · ')}</span> : null}
        <span>{pluralize(item.toolCalls, 'tool call')}</span>
        {item.finishReason ? <span>Finish reason {item.finishReason}</span> : null}
        {traceUrl ? (
          <a href={traceUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 font-medium text-accent-text hover:underline">
            Open trace in Langfuse
            <ExternalLink className="size-3" aria-hidden />
          </a>
        ) : item.traceId ? (
          <span className="font-mono">Trace {item.traceId}</span>
        ) : null}
      </div>
    </div>
  );
}
