import { ArrowUpRight, Library, MessageSquareText } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Alert, type AlertTone } from '../../components/ui/feedback';
import { pluralize } from '../../lib/format';
import { useReturnState } from '../../lib/returnTo';
import { ingestingCount } from '../sources/polling';
import type { SourceStatsDto } from '../sources/types';
import { WaitingSourcesMessage } from '../sources/WaitingSourcesMessage';


export interface EmptyChatProps {
  onPick: (question: string) => void;
  sourceStats: SourceStatsDto | undefined;
  title?: string;
  description?: ReactNode;
  summary?: ReactNode;
  questions?: readonly string[];
  greeting?: string | null;
}

function SourcesLink() {
  return (
    <Link to="/sources" state={useReturnState()} className="font-medium underline underline-offset-2">
      Sources
    </Link>
  );
}

const sourcesLink = <SourcesLink />;

function sourcesHint(stats: SourceStatsDto | undefined): { tone: AlertTone; title: string; body: ReactNode } | null {
  if (!stats || stats.ready > 0) {
    return null;
  }
  if (stats.total === 0) {
    return {
      tone: 'accent',
      title: 'No sources yet',
      body: <>Add a web page, a PDF or some text on the {sourcesLink} page, then come back to ask about it.</>,
    };
  }
  const ingesting = ingestingCount(stats);
  if (ingesting > 0) {
    return {
      tone: 'info',
      title: 'Your sources are being processed',
      body: <>{ingesting === 1 ? '1 source is' : `${pluralize(ingesting, 'source')} are`} being ingested. You can ask questions once they are ready.</>,
    };
  }
  if (stats.waiting > 0) {
    return { tone: 'warning', title: 'No sources are ready', body: <WaitingSourcesMessage count={stats.waiting} /> };
  }
  return {
    tone: 'warning',
    title: 'No sources are ready',
    body: <>Ingestion failed for {pluralize(stats.failed, 'source')}. Check the errors on the {sourcesLink} page.</>,
  };
}

export function EmptyChat({
  onPick,
  sourceStats,
  title = 'Ask your knowledge base',
  description = 'The agent searches your sources and cites the passages it used. Select a citation to see the source.',
  summary,
  greeting,
  questions,
}: EmptyChatProps) {
  const hint = sourcesHint(sourceStats);
  const examples = questions ?? [];
  return (
    <div className="flex h-full overflow-y-auto px-4 py-10">
      {/* Auto margins center it while it fits and let it scroll from the top when it does not. */}
      <div className="m-auto w-full max-w-2xl">
        <div className="flex flex-col items-center text-center">
          <div className="flex size-10 items-center justify-center rounded-xl border border-border bg-surface text-fg-muted">
            <MessageSquareText className="size-5" aria-hidden />
          </div>
          <h2 className="mt-4 text-title text-fg">{title}</h2>
          <p className="mt-1 max-w-md text-sm text-fg-secondary">{description}</p>
        </div>
        {summary ? <div className="mt-6">{summary}</div> : null}
        {greeting ? (
          <div className="mt-6 text-left">
            <p className="text-xs font-medium text-fg-muted">Visitors see first</p>
            <p className="mt-1.5 inline-block max-w-full rounded-2xl rounded-tl-md bg-surface-subtle px-3.5 py-2 text-sm text-fg">{greeting}</p>
          </div>
        ) : null}

        {hint ? (
          <Alert tone={hint.tone} icon={Library} className="mt-6" title={hint.title}>
            {hint.body}
          </Alert>
        ) : null}

        {examples.length > 0 ? (
          <>
        <div className="mt-8 mb-2 text-xs font-medium text-fg-muted">Suggested questions of this widget</div>
        <ul className="grid gap-2 sm:grid-cols-2">
          {examples.map((question) => (
            <li key={question}>
              <button
                type="button"
                onClick={() => onPick(question)}
                className="group flex h-full w-full items-start justify-between gap-3 rounded-2xl border border-border bg-surface px-4 py-3 text-left text-sm text-fg-secondary hover:border-border-hover hover:text-fg"
              >
                <span>{question}</span>
                <ArrowUpRight className="mt-0.5 size-4 shrink-0 text-fg-muted group-hover:text-accent-text" aria-hidden />
              </button>
            </li>
          ))}
        </ul>
          </>
        ) : null}
      </div>
    </div>
  );
}
