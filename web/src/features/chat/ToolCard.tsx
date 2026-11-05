import { clsx } from 'clsx';
import { Bot, ChevronRight, CircleSlash, CircleX, LoaderCircle } from 'lucide-react';
import { useId, useState } from 'react';
import { capitalize, formatDuration, formatNumber } from '../../lib/format';
import { useSourceTitles } from '../sources/useSourceTitles';
import { useChatDisplay } from './chatDisplay';
import type { AgentActivity, ToolCallView } from './chatReducer';
import { useSourceTitleLookup } from './citationContext';
import { Markdown } from './Markdown';
import { ThinkingBlock } from './ThinkingBlock';
import { isDelegateTool, summarizeToolArguments, toolMeta, toolSourceIds } from './tools';
import { ToolNames, type CitationDto } from './types';

function flatten(tools: readonly ToolCallView[]): ToolCallView[] {
  return tools.flatMap((tool) => [tool, ...flatten(tool.children)]);
}

function isDelegate(tool: ToolCallView): boolean {
  return isDelegateTool(tool.toolName) || tool.subAgent !== null || tool.children.length > 0;
}

export function activityPassages(tools: readonly ToolCallView[]): CitationDto[] {
  const byRef = new Map<string, CitationDto>();
  for (const tool of flatten(tools)) {
    for (const citation of tool.citations) {
      byRef.set(citation.ref.toUpperCase(), citation);
    }
  }
  return [...byRef.values()];
}

function times(phrase: string, count: number): string {
  return count === 1 ? phrase : `${phrase} ${count} times`;
}

/** "Searched the knowledge base 2 times, read a source" for finished steps. */
function describeActivity(tools: readonly ToolCallView[]): string {
  const counts = new Map<string, number>();
  for (const tool of tools) {
    const key = isDelegate(tool) ? ToolNames.DelegateResearch : tool.toolName;
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  const phrases = [...counts].map(([name, count]) => {
    switch (name) {
      case ToolNames.SearchKnowledgeBase:
        return times('searched the knowledge base', count);
      case ToolNames.GrepSources:
        return times('found exact text', count);
      case ToolNames.FindSources:
        return times('looked up sources', count);
      case ToolNames.ReadSource:
        return count === 1 ? 'read a source' : `read ${count} sources`;
      case ToolNames.DelegateResearch:
        return times('asked the researcher', count);
      default:
        return times(toolMeta(name).label.toLowerCase(), count);
    }
  });
  return capitalize(phrases.join(', '));
}

function StatusIcon({ tool }: { tool: ToolCallView }) {
  if (tool.status === 'running') {
    return <LoaderCircle className="size-3.5 shrink-0 animate-spin text-accent-icon" aria-hidden />;
  }
  if (tool.status === 'error') {
    return <CircleX className="size-3.5 shrink-0 text-danger" aria-hidden />;
  }
  if (tool.status === 'cancelled') {
    return <CircleSlash className="size-3.5 shrink-0 text-fg-muted" aria-hidden />;
  }
  const Icon = toolMeta(tool.toolName).icon;
  return <Icon className="size-3.5 shrink-0 text-fg-muted" aria-hidden />;
}

const statusText: Record<ToolCallView['status'], string> = {
  running: 'running',
  success: 'completed',
  error: 'failed',
  cancelled: 'cancelled',
};

function Passages({ citations }: { citations: readonly CitationDto[] }) {
  const { developer } = useChatDisplay();
  return (
    <ul className="space-y-1.5" aria-label="Passages">
      {citations.map((citation) => (
        <li key={citation.ref} className="min-w-0">
          <div className="flex min-w-0 items-baseline gap-1.5">
            <span className="min-w-0 truncate font-medium text-fg-secondary" title={citation.sourceTitle}>
              {citation.sourceTitle || 'Untitled source'}
            </span>
            {citation.page != null ? <span className="shrink-0 text-fg-muted">p. {citation.page}</span> : null}
            {developer ? (
              <span className="ml-auto shrink-0 font-mono text-fg-muted tabular-nums">
                {citation.ref}
                {Number.isFinite(citation.score) ? ` · ${citation.score.toFixed(2)}` : ''}
              </span>
            ) : null}
          </div>
          {citation.snippet ? <p className="line-clamp-2 break-words text-fg-muted">{citation.snippet}</p> : null}
        </li>
      ))}
    </ul>
  );
}

function SubAgentActivity({ activity, tool }: { activity: AgentActivity | null; tool: ToolCallView }) {
  const { developer } = useChatDisplay();
  const agent = activity?.agent ?? tool.children[0]?.agent ?? 'researcher';
  const running = activity?.status === 'running';
  const model = activity?.model ? [activity.model, activity.provider].filter(Boolean).join(' · ') : null;
  const tokens = activity?.usage
    ? [
        activity.usage.inputTokens != null ? `${formatNumber(activity.usage.inputTokens)} in` : null,
        activity.usage.outputTokens != null ? `${formatNumber(activity.usage.outputTokens)} out` : null,
      ].filter(Boolean)
    : [];
  return (
    <div className="space-y-2" data-testid="subagent-activity">
      {developer && (model || activity?.iterations != null || tokens.length > 0) ? (
        <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-fg-muted">
          <Bot className="size-3.5" aria-hidden />
          {model ? <span className="font-mono">{model}</span> : null}
          {activity?.iterations != null ? <span>· {activity.iterations} steps</span> : null}
          {tokens.length > 0 ? <span>· {tokens.join(' / ')}</span> : null}
        </div>
      ) : null}
      {activity?.reasoning ? <ThinkingBlock text={activity.reasoning} active={running && !activity.text} /> : null}
      {tool.children.length > 0 ? (
        <ul className="space-y-2 border-l border-border pl-3" aria-label={`${capitalize(agent)} steps`}>
          {tool.children.map((child) => (
            <li key={child.callId}>
              <ToolStep tool={child} />
            </li>
          ))}
        </ul>
      ) : running ? (
        <p className="text-fg-muted">Starting…</p>
      ) : null}
      {activity?.text ? (
        <div className="rounded-md border border-border-subtle bg-surface px-2.5 py-1.5">
          <Markdown text={activity.text} variant="muted" />
        </div>
      ) : null}
      {activity?.errors.map((error, index) => (
        <p key={index} className="text-danger-text">
          {error.message}
        </p>
      ))}
    </div>
  );
}

export function ToolStep({ tool }: { tool: ToolCallView }) {
  const { developer } = useChatDisplay();
  const knownTitle = useSourceTitleLookup();
  const fetchedTitle = useSourceTitles(toolSourceIds(tool.toolName, tool.arguments).filter((id) => knownTitle(id) === undefined));
  const sourceTitle = (id: string) => knownTitle(id) ?? fetchedTitle(id);
  const meta = toolMeta(tool.toolName);
  const args = summarizeToolArguments(tool.toolName, tool.arguments, sourceTitle);
  const delegate = isDelegate(tool);
  const running = tool.status === 'running';
  const failed = tool.status === 'error';
  const label = delegate && tool.subAgent ? (running ? `${capitalize(tool.subAgent.agent)} working` : capitalize(tool.subAgent.agent)) : running ? meta.activeLabel : meta.label;
  const hasArguments = tool.arguments !== null && !(typeof tool.arguments === 'object' && !Array.isArray(tool.arguments) && Object.keys(tool.arguments).length === 0);
  const quoted = delegate || tool.toolName === ToolNames.SearchKnowledgeBase;

  return (
    <div data-testid="tool-card" data-status={tool.status} className="space-y-1.5 text-xs">
      <div className="flex items-start gap-2">
        <span className="mt-0.5">
          <StatusIcon tool={tool} />
        </span>
        <span className="min-w-0 flex-1 leading-5">
          <span className="font-medium text-fg">{label}</span>
          <span className="sr-only">, {statusText[tool.status]}</span>
          {args.primary ? <span className="ml-1.5 break-words text-fg-secondary">{quoted ? `“${args.primary}”` : args.primary}</span> : null}
          {args.details.map((detail) => (
            <span key={detail} className="ml-1.5 text-fg-muted">
              · {detail}
            </span>
          ))}
        </span>
        {developer && tool.durationMs != null && !running ? (
          <span className="mt-0.5 shrink-0 text-fg-muted tabular-nums">{formatDuration(tool.durationMs)}</span>
        ) : null}
      </div>
      <div className="space-y-2 pl-5.5">
        {failed && tool.summary ? (
          <p data-tone="danger" className="break-words text-danger-text">
            {tool.summary}
          </p>
        ) : null}
        {delegate ? <SubAgentActivity activity={tool.subAgent} tool={tool} /> : null}
        {tool.citations.length > 0 ? (
          <Passages citations={tool.citations} />
        ) : !failed && !running && tool.summary && !(delegate && tool.subAgent?.text) ? (
          <p className="break-words text-fg-muted">{tool.summary}</p>
        ) : null}
        {developer && hasArguments ? (
          <details>
            <summary className="w-fit cursor-pointer rounded-sm text-fg-muted select-none hover:text-fg">Arguments</summary>
            <pre className="mt-1 overflow-x-auto rounded-md border border-border-subtle bg-surface p-2 font-mono text-xs leading-relaxed text-fg-secondary">
              {JSON.stringify(tool.arguments, null, 2)}
            </pre>
            <div className="mt-1 font-mono break-all text-fg-muted">
              {tool.toolName} · {tool.callId}
            </div>
          </details>
        ) : null}
      </div>
    </div>
  );
}

function runningStep(tools: readonly ToolCallView[]): ToolCallView | undefined {
  // The innermost running step says best what is happening now, such as the researcher's search.
  return flatten(tools)
    .filter((tool) => tool.status === 'running')
    .pop();
}

export function ToolActivity({ tools }: { tools: readonly ToolCallView[] }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  const running = runningStep(tools);
  const passages = activityPassages(tools);
  const failures = flatten(tools).filter((tool) => tool.status === 'error').length;

  let text: string;
  let query: string | null = null;
  if (running) {
    const meta = toolMeta(running.toolName);
    text = `${running.subAgent ? `${capitalize(running.subAgent.agent)} working` : meta.activeLabel}…`;
    query = summarizeToolArguments(running.toolName, running.arguments).primary;
  } else {
    text = describeActivity(tools);
  }

  return (
    <div aria-label="Agent activity" role="group">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen((value) => !value)}
        className="-ml-1.5 inline-flex min-h-7 max-w-full items-center gap-1.5 rounded-md px-1.5 py-1 text-left text-xs text-fg-muted hover:bg-fill-hover hover:text-fg"
      >
        {running ? (
          <LoaderCircle className="size-3.5 shrink-0 animate-spin text-accent-icon" aria-hidden />
        ) : (
          (() => {
            const Icon = toolMeta(tools[0]?.toolName ?? '').icon;
            return <Icon className="size-3.5 shrink-0" aria-hidden />;
          })()
        )}
        <span className="min-w-0">
          <span className="font-medium">{text}</span>
          {query ? <span className="ml-1 break-words">“{query}”</span> : null}
          {!running && passages.length > 0 ? ` · ${passages.length === 1 ? '1 passage' : `${formatNumber(passages.length)} passages`}` : null}
          {failures > 0 ? (
            <>
              {' · '}
              <span className="text-danger-text">{failures === 1 ? '1 step failed' : `${failures} steps failed`}</span>
            </>
          ) : null}
        </span>
        <ChevronRight className={clsx('size-3.5 shrink-0 transition-transform duration-100 ease-out', open && 'rotate-90')} aria-hidden />
      </button>
      {open ? (
        <ol id={id} className="mt-1.5 ml-1 space-y-3 border-l border-border pl-3">
          {tools.map((tool) => (
            <li key={tool.callId}>
              <ToolStep tool={tool} />
            </li>
          ))}
        </ol>
      ) : null}
    </div>
  );
}
