import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Card } from '../../components/ui/feedback';
import { formatNumber } from '../../lib/format';
import { useReturnState } from '../../lib/returnTo';
import { SEARCH_MODE_LABELS, TARGET_LABELS } from './runs';
import type { EvalRunDto } from './types';

export function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-fg-muted">{label}</dt>
      <dd className="mt-0.5 text-sm break-words text-fg">{children}</dd>
    </div>
  );
}

export function Missing({ children = '–' }: { children?: ReactNode }) {
  return <span className="text-fg-muted">{children}</span>;
}

function Mono({ children }: { children: ReactNode }) {
  return <span className="font-mono text-xs">{children}</span>;
}

export function SectionTitle({ id, title, aside }: { id: string; title: string; aside?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
      <h2 id={id} className="text-section text-fg">
        {title}
      </h2>
      {aside ? <div className="text-small text-fg-muted">{aside}</div> : null}
    </div>
  );
}

export function RunConfiguration({ run }: { run: EvalRunDto }) {
  const returnState = useReturnState();
  const { config } = run;
  const prompt = config.promptName ? (
    <>
      <Link to={`/prompts/${encodeURIComponent(config.promptName)}`} state={returnState} className="font-mono text-xs text-accent-text hover:underline">
        {config.promptName}
      </Link>
      {config.promptVersion != null ? <span className="text-fg-secondary"> v{config.promptVersion}</span> : null}
      {config.promptOrigin ? <span className="text-fg-muted"> · {config.promptOrigin}</span> : null}
    </>
  ) : (
    <Missing />
  );

  return (
      <section aria-labelledby="run-config-heading" className="space-y-3">
        <SectionTitle id="run-config-heading" title="Configuration" />
        <Card>
          <dl className="grid gap-x-6 gap-y-4 px-5 py-4 sm:grid-cols-2 lg:grid-cols-3">
            <Fact label="Knowledge base">{TARGET_LABELS[config.target?.toLowerCase()] ?? config.target}</Fact>
            <Fact label="Profile">{config.profile ? <Mono>{config.profile}</Mono> : <Missing />}</Fact>
            <Fact label="Chat model">{config.chatModel ? <Mono>{config.chatModel}</Mono> : <Missing />}</Fact>
            <Fact label="Embedding model">{config.embeddingModel ? <Mono>{config.embeddingModel}</Mono> : <Missing />}</Fact>
            <Fact label="Judge model">{config.judgeModel ? <Mono>{config.judgeModel}</Mono> : <Missing>Judge off</Missing>}</Fact>
            <Fact label="Prompt">{prompt}</Fact>
            <Fact label="Search">
              {config.searchMode ? (
                <>
                  {SEARCH_MODE_LABELS[config.searchMode.toLowerCase()] ?? config.searchMode}
                  {config.rerankModel ? (
                    <>
                      {' · reranked by '}
                      <Mono>{config.rerankModel}</Mono>
                    </>
                  ) : (
                    ' · no reranking'
                  )}
                </>
              ) : (
                <Missing>Not recorded</Missing>
              )}
            </Fact>
            <Fact label="Retrieval">
              Top {formatNumber(config.topK)} · {formatNumber(config.parallelism)} in parallel
            </Fact>
            <Fact label="Questions asked">
              {config.maxItems != null ? `First ${formatNumber(config.maxItems)}` : 'All'}
              {config.tags.length > 0 ? ` · tagged ${config.tags.join(', ')}` : ''}
            </Fact>
          </dl>
        </Card>
      </section>
  );
}
