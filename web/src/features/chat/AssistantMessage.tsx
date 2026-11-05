import { clsx } from 'clsx';
import { ArrowUpRight, Check, Copy, Info, Square, Waypoints, type LucideIcon } from 'lucide-react';
import { memo, useId, useMemo, useRef, useState, type ComponentProps, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Alert } from '../../components/ui/feedback';
import { SideSheet } from '../../components/ui/SideSheet';
import { AnswerTrace } from '../trace/AnswerTrace';
import { Popover } from '../../components/ui/Popover';
import { formatDuration, formatNumber } from '../../lib/format';
import { useReturnState } from '../../lib/returnTo';
import { useCopyToClipboard } from '../../lib/hooks/useCopyToClipboard';
import { useDisclosure } from '../../lib/hooks/useDisclosure';
import { AnswerSourcesContext, numberSources, type AnswerSource } from './answerSources';
import { useChatDisplay } from './chatDisplay';
import type { ToolCallView, TurnError, TurnView } from './chatReducer';
import { sourceIcon, sourceOrigin, sourcesPageLink } from './CitationChip';
import { CitationContext, createCitationLookup } from './citationContext';
import { extractCitationRefs } from './citations';
import { Markdown } from './Markdown';
import { ThinkingBlock, TypingDots } from './ThinkingBlock';
import { activityPassages, ToolActivity } from './ToolCard';
import { toolMeta } from './tools';
import type { CitationDto } from './types';

const ERROR_HINTS: Record<string, string> = {
  model_not_configured: 'No chat model is available. Connect one on the AI models page.',
  refusal: 'The model declined to answer this request.',
  provider_error: 'The model provider returned an error.',
};

const SOURCES_SHOWN = 3;

// Stopping keeps the text so far, so a stopped answer is not an error.
function StoppedNote() {
  return (
    <p className="inline-flex items-center gap-1.5 text-xs text-fg-muted">
      <Square className="size-3" aria-hidden /> Stopped
    </p>
  );
}

// Older stored turns and some provider messages carry only the text.
function errorCode(error: TurnError): string | null {
  if (error.code) {
    return error.code;
  }
  return /no configured chat provider/i.test(error.message) ? 'model_not_configured' : null;
}

function TurnErrorView({ error }: { error: TurnError }) {
  const returnState = useReturnState();
  const { developer } = useChatDisplay();
  if (error.code === 'cancelled') {
    return <StoppedNote />;
  }
  const code = errorCode(error);
  const hint = code ? ERROR_HINTS[code] : undefined;
  if (!hint || hint === error.message) {
    return (
      // Provider errors can run to a paragraph, so the message is the body rather than a semibold title.
      <Alert tone="danger" live>
        <p className="text-fg">{error.message}</p>
        {developer && code ? <p className="mt-0.5 font-mono text-xs text-fg-muted">{code}</p> : null}
      </Alert>
    );
  }
  return (
    <Alert tone="danger" live>
      <p className="text-fg">
        {hint}{' '}
        {code === 'model_not_configured' ? (
          <Link to="/models" state={returnState} className="font-medium underline underline-offset-2">
            Open AI models
          </Link>
        ) : null}
      </p>
      <details className="mt-1 text-xs text-fg-muted">
        <summary className="cursor-pointer select-none">Technical details</summary>
        <p className="mt-1 break-words">{error.message}</p>
        {developer && code ? <p className="mt-0.5 font-mono">{code}</p> : null}
      </details>
    </Alert>
  );
}

function AnswerAction({ icon: Icon, children, ...props }: ComponentProps<'button'> & { icon: LucideIcon }) {
  return (
    <button type="button" className="inline-flex h-7 items-center gap-1 rounded-md px-1.5 text-xs text-fg-muted hover:bg-fill-hover hover:text-fg" {...props}>
      <Icon className="size-3.5" aria-hidden />
      {children}
    </button>
  );
}

function CopyAnswerButton({ text }: { text: string }) {
  const { copied, supported, copy } = useCopyToClipboard(1500);
  if (!text || !supported) {
    return null;
  }
  return (
    <AnswerAction icon={copied ? Check : Copy} onClick={() => copy(text)} aria-label={copied ? 'Copied' : 'Copy answer'} title="Copy answer">
      <span className="sr-only sm:not-sr-only">{copied ? 'Copied' : 'Copy'}</span>
    </AnswerAction>
  );
}

function stepDurations(tools: readonly ToolCallView[], depth = 0): Array<{ key: string; label: string; ms: number | null; depth: number }> {
  return tools.flatMap((tool) => [
    { key: tool.callId, label: tool.subAgent ? tool.subAgent.agent : toolMeta(tool.toolName).label, ms: tool.durationMs, depth },
    ...stepDurations(tool.children, depth + 1),
  ]);
}

function DetailRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[6.5rem_1fr] gap-2 py-1">
      <dt className="text-fg-muted">{label}</dt>
      <dd className="min-w-0 break-words text-fg">{children}</dd>
    </div>
  );
}

function AnswerDetails({ turn }: { turn: TurnView }) {
  const { traceUrl, prompt } = useChatDisplay();
  const steps = stepDurations(turn.tools);
  const passages = activityPassages(turn.tools);
  const trace = turn.traceId && traceUrl ? traceUrl(turn.traceId) : null;
  const usage = turn.usage;
  return (
    <div className="space-y-2 text-xs">
      <dl className="divide-y divide-border-subtle">
        {turn.model ? (
          <DetailRow label="Model">
            <span className="font-mono">{turn.model}</span>
            {turn.provider ? <span className="text-fg-muted"> · {turn.provider}</span> : null}
          </DetailRow>
        ) : null}
        {prompt ? (
          <DetailRow label="Prompt">
            <span className="font-mono">{prompt}</span>
          </DetailRow>
        ) : null}
        {usage ? (
          <DetailRow label="Tokens">
            <span className="tabular-nums">
              {usage.inputTokens != null ? `${formatNumber(usage.inputTokens)} in` : '–'}
              {' · '}
              {usage.outputTokens != null ? `${formatNumber(usage.outputTokens)} out` : '–'}
            </span>
          </DetailRow>
        ) : null}
        {turn.durationMs != null && turn.durationMs > 0 ? (
          <DetailRow label="Time">
            <span className="tabular-nums">{formatDuration(turn.durationMs)}</span>
          </DetailRow>
        ) : null}
        {passages.length > 0 ? <DetailRow label="Passages">{formatNumber(passages.length)} found</DetailRow> : null}
        {turn.finishReason ? (
          <DetailRow label="Finished">
            <span className="font-mono">{turn.finishReason}</span>
          </DetailRow>
        ) : null}
      </dl>
      {steps.length > 0 ? (
        <div>
          <div className="mb-1 font-medium text-fg-muted">Steps</div>
          <ul className="space-y-0.5">
            {steps.map((step) => (
              <li key={step.key} className="flex gap-2" style={{ paddingLeft: `${step.depth * 0.75}rem` }}>
                <span className="min-w-0 flex-1 truncate text-fg-secondary">{step.label}</span>
                <span className="shrink-0 text-fg-muted tabular-nums">{step.ms != null ? formatDuration(step.ms) : '–'}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
      {turn.persisted ? <div className="font-mono break-all text-fg-muted">Turn {turn.id}</div> : null}
      {trace ? (
        <a href={trace} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 font-medium text-accent-text hover:underline">
          Open in Langfuse
          <ArrowUpRight className="size-3.5" aria-hidden />
          <span className="sr-only"> (new tab)</span>
        </a>
      ) : null}
    </div>
  );
}

function DetailsButton({ turn }: { turn: TurnView }) {
  const details = useDisclosure();
  const buttonRef = useRef<HTMLButtonElement>(null);
  const id = useId();
  return (
    <>
      <AnswerAction
        ref={buttonRef}
        icon={Info}
        aria-expanded={details.open}
        aria-haspopup="dialog"
        aria-controls={details.open ? id : undefined}
        onClick={details.toggle}
        title="Model, prompt, tokens and timings"
      >
        <span>Details</span>
      </AnswerAction>
      <Popover id={id} open={details.open} onClose={details.close} anchorRef={buttonRef} label="Answer details" className="w-80 p-3">
        <AnswerDetails turn={turn} />
      </Popover>
    </>
  );
}

function TraceButton({ turn }: { turn: TurnView }) {
  const trace = useDisclosure();
  return (
    <>
      <AnswerAction icon={Waypoints} aria-haspopup="dialog" onClick={trace.show} title="What the agent did, step by step">
        <span>Trace</span>
      </AnswerAction>
      <SideSheet open={trace.open} onClose={trace.close} title="How this answer was made" size="lg">
        <AnswerTrace turn={turn} />
      </SideSheet>
    </>
  );
}

function AnswerActions({ turn }: { turn: TurnView }) {
  const { developer } = useChatDisplay();
  const hasDetails = developer && (turn.model !== null || turn.usage !== null || turn.tools.length > 0 || turn.persisted);
  if (!turn.text && !hasDetails) {
    return null;
  }
  return (
    // Quiet until the answer is hovered or focused; always there on touch screens, which cannot hover.
    <div
      className="-ml-1.5 flex items-center gap-0.5 opacity-0 transition-opacity duration-100 group-focus-within/answer:opacity-100 group-hover/answer:opacity-100 max-md:opacity-100"
      data-testid="answer-actions"
    >
      <CopyAnswerButton text={turn.text} />
      {hasDetails ? <DetailsButton turn={turn} /> : null}
      {developer && turn.tools.length > 0 ? <TraceButton turn={turn} /> : null}
    </div>
  );
}

function SourceCard({ source }: { source: AnswerSource }) {
  const returnState = useReturnState();
  const Icon = sourceIcon(source);
  const passages = source.citations.length;
  const content = (
    <>
      <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-fill-muted text-[0.6875rem] font-medium text-fg-secondary tabular-nums">
        {source.number}
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex min-w-0 items-center gap-1.5">
          <Icon className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
          <span className="truncate font-medium text-fg">{source.title}</span>
        </span>
        <span className="block truncate text-fg-muted">
          {sourceOrigin(source)}
          {passages > 1 ? ` · ${passages} passages` : ''}
        </span>
      </span>
    </>
  );
  return source.url ? (
    <a
      href={source.url}
      target="_blank"
      rel="noopener noreferrer"
      className="flex min-w-0 items-start gap-2 rounded-lg border border-border bg-surface px-2.5 py-2 text-xs hover:border-border-hover hover:bg-fill-hover"
      title={`${source.title} (${source.host ?? source.url})`}
    >
      {content}
      <span className="sr-only"> (new tab)</span>
    </a>
  ) : (
    <Link
      to={sourcesPageLink(source.sourceId)}
      state={returnState}
      className="flex min-w-0 items-start gap-2 rounded-lg border border-border bg-surface px-2.5 py-2 text-xs hover:border-border-hover hover:bg-fill-hover"
      title={source.title}
    >
      {content}
    </Link>
  );
}

function CitedSources({ sources }: { sources: readonly AnswerSource[] }) {
  const [expanded, setExpanded] = useState(false);
  if (sources.length === 0) {
    return null;
  }
  const hidden = sources.length - SOURCES_SHOWN;
  const shown = expanded || hidden <= 0 ? sources : sources.slice(0, SOURCES_SHOWN);
  return (
    <section className="space-y-1.5">
      <h3 className="text-xs font-medium text-fg-muted">Sources</h3>
      <ul className="grid grid-cols-[repeat(auto-fill,minmax(13rem,1fr))] gap-1.5" aria-label="Sources">
        {shown.map((source) => (
          <li key={source.sourceId} className="min-w-0">
            <SourceCard source={source} />
          </li>
        ))}
        {hidden > 0 && !expanded ? (
          <li className="min-w-0">
            <button
              type="button"
              onClick={() => setExpanded(true)}
              className="flex h-full min-h-12 w-full items-center justify-center rounded-lg border border-dashed border-border px-2.5 py-2 text-xs font-medium text-fg-secondary hover:bg-fill-hover hover:text-fg"
            >
              +{hidden} more
            </button>
          </li>
        ) : null}
      </ul>
    </section>
  );
}

export interface AssistantMessageProps {
  turn: TurnView;
  /** An answer may cite passages retrieved in earlier turns. */
  conversationCitations?: ReadonlyMap<string, CitationDto>;
}

const EMPTY_INDEX: ReadonlyMap<string, CitationDto> = new Map();

export const AssistantMessage = memo(function AssistantMessage({ turn, conversationCitations = EMPTY_INDEX }: AssistantMessageProps) {
  const lookup = useMemo(() => createCitationLookup(turn.citations, conversationCitations), [turn.citations, conversationCitations]);
  const refs = useMemo(() => extractCitationRefs(turn.text), [turn.text]);
  const sources = useMemo(() => numberSources(refs, lookup, [...activityPassages(turn.tools), ...turn.citations]), [refs, lookup, turn.tools, turn.citations]);
  const streaming = turn.status === 'streaming';
  const toolsRunning = turn.tools.some((tool) => tool.status === 'running');

  return (
    <CitationContext.Provider value={lookup}>
      <AnswerSourcesContext.Provider value={sources}>
        <article aria-label="Assistant answer" aria-busy={streaming} className={clsx('group/answer space-y-3')}>
          {turn.reasoning ? <ThinkingBlock text={turn.reasoning} active={streaming && !turn.text && !toolsRunning} /> : null}
          {turn.tools.length > 0 ? <ToolActivity tools={turn.tools} /> : null}
          {turn.text ? (
            <Markdown text={turn.text} />
          ) : streaming && !toolsRunning ? (
            <div className="flex items-center gap-2 py-1 text-xs text-fg-muted" role="status">
              <TypingDots />
              <span>{turn.reasoning ? 'Thinking…' : 'Waiting for the answer…'}</span>
            </div>
          ) : null}
          {turn.errors.map((error, index) => (
            <TurnErrorView key={index} error={error} />
          ))}
          {turn.status === 'cancelled' && !turn.errors.some((e) => e.code === 'cancelled') ? <StoppedNote /> : null}
          {!streaming ? <CitedSources sources={sources.cited} /> : null}
          {!streaming ? <AnswerActions turn={turn} /> : null}
        </article>
      </AnswerSourcesContext.Provider>
    </CitationContext.Provider>
  );
});

export function UserMessage({ text }: { text: string }) {
  return (
    <div className="flex justify-end">
      <div className="max-w-[85%] rounded-2xl bg-fill-muted px-3.5 py-2 text-sm leading-[1.375rem] break-words whitespace-pre-wrap text-fg">
        <span className="sr-only">You: </span>
        {text}
      </div>
    </div>
  );
}

export function SystemNotice({ text }: { text: string }) {
  return (
    <div className="flex justify-center" role="note">
      <p className="inline-flex max-w-[85%] items-start gap-2 rounded-lg border border-border-subtle bg-surface-subtle px-3 py-2 text-small text-fg-secondary">
        <Info className="mt-0.5 size-3.5 shrink-0 text-fg-muted" aria-hidden />
        <span className="break-words">{text}</span>
      </p>
    </div>
  );
}
