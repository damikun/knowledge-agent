import { useQuery } from '@tanstack/react-query';
import { MessagesSquare, Plus, Search, X } from 'lucide-react';
import { useState } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { Field } from '../../components/ui/Field';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { Switch } from '../../components/ui/Switch';
import { ApiError } from '../../lib/api';
import { sourcesApi } from '../sources/api';
import { evalKeys } from './api';
import type { EvalDatasetItemDto, EvalTurn, SaveEvalItemRequest } from './types';

/** Facts are one per line, tags comma-separated. */
export interface QuestionDraft {
  question: string;
  referenceAnswer: string;
  facts: string;
  /** Source titles, which runs match on. */
  sources: string[];
  tags: string;
  answerable: boolean;
  /** Oldest first. */
  history: EvalTurn[];
}

export const EMPTY_QUESTION: QuestionDraft = { question: '', referenceAnswer: '', facts: '', sources: [], tags: '', answerable: true, history: [] };

export const QUESTION_FIELDS = ['question', 'referenceAnswer', 'expectedFacts', 'expectedSources', 'tags', 'history'] as const;

function lines(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean);
}

export function draftOf(item: Pick<EvalDatasetItemDto, 'question' | 'referenceAnswer' | 'expectedFacts' | 'expectedSources' | 'tags' | 'answerable' | 'history'>): QuestionDraft {
  return {
    question: item.question,
    referenceAnswer: item.referenceAnswer ?? '',
    facts: item.expectedFacts.join('\n'),
    sources: [...item.expectedSources],
    tags: item.tags.join(', '),
    answerable: item.answerable,
    history: [...(item.history ?? [])],
  };
}

export function toRequest(
  draft: QuestionDraft,
  origin: Pick<SaveEvalItemRequest, 'conversationId' | 'messageId' | 'source'> = {},
): SaveEvalItemRequest {
  const history = draft.history.map((turn) => ({ role: turn.role, content: turn.content.trim() })).filter((turn) => turn.content);
  return {
    question: draft.question.trim(),
    referenceAnswer: draft.answerable ? draft.referenceAnswer.trim() || null : null,
    expectedFacts: draft.answerable ? lines(draft.facts) : [],
    expectedSources: draft.answerable ? draft.sources : [],
    answerable: draft.answerable,
    tags: draft.tags
      .split(',')
      .map((tag) => tag.trim())
      .filter(Boolean),
    // Omitted for single-turn questions to keep the request shape older APIs accept.
    ...(history.length > 0 ? { history } : {}),
    ...origin,
  };
}

/** Titles can also be typed in, for sources not added yet. */
function SourcePicker({ id, value, onChange, errors }: { id: string; value: string[]; onChange: (value: string[]) => void; errors: string[] }) {
  const [query, setQuery] = useState('');
  const words = query.trim();
  // Only while typing, so the source list isn't loaded unless needed.
  const results = useQuery({
    queryKey: evalKeys.sourcePicker(words),
    queryFn: ({ signal }) => sourcesApi.listSources({ query: words, status: ['ready'], limit: 6 }, signal),
    enabled: words.length >= 2,
    staleTime: 30_000,
  });
  const chosen = new Set(value.map((title) => title.toLowerCase()));
  const add = (title: string) => {
    const trimmed = title.trim();
    if (trimmed && !chosen.has(trimmed.toLowerCase())) {
      onChange([...value, trimmed]);
    }
    setQuery('');
  };
  const options = (results.data?.items ?? []).filter((source) => !chosen.has(source.title.toLowerCase()));
  const exact = options.some((source) => source.title.toLowerCase() === words.toLowerCase());

  return (
    <div className="space-y-2">
      {value.length > 0 ? (
        <ul className="flex flex-wrap gap-1.5" aria-label="Sources it should cite">
          {value.map((title) => (
            <li key={title}>
              <Badge className="max-w-full gap-1 pr-1">
                <span className="truncate">{title}</span>
                <button
                  type="button"
                  onClick={() => onChange(value.filter((other) => other !== title))}
                  aria-label={`Remove ${title}`}
                  className="rounded-full p-0.5 hover:bg-fill-hover hover:text-fg"
                >
                  <X className="size-3" aria-hidden />
                </button>
              </Badge>
            </li>
          ))}
        </ul>
      ) : null}
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
        <input
          {...fieldAria(id, errors, true)}
          type="search"
          autoComplete="off"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              add(options[0] && !exact ? options[0].title : words);
            }
          }}
          placeholder="Search your sources"
          className="field pl-8"
        />
      </div>
      {words.length >= 2 ? (
        <ul className="divide-y divide-border-subtle rounded-lg border border-border" aria-label="Matching sources">
          {options.map((source) => (
            <li key={source.id}>
              <button type="button" onClick={() => add(source.title)} className="flex w-full items-center gap-2 px-3 py-2 text-left text-small hover:bg-fill-hover">
                <Plus className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
                <span className="truncate text-fg">{source.title}</span>
              </button>
            </li>
          ))}
          {!exact && !chosen.has(words.toLowerCase()) ? (
            <li>
              <button type="button" onClick={() => add(words)} className="flex w-full items-center gap-2 px-3 py-2 text-left text-small hover:bg-fill-hover">
                <Plus className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
                <span className="truncate text-fg-secondary">Add “{words}” as typed</span>
              </button>
            </li>
          ) : null}
        </ul>
      ) : null}
    </div>
  );
}

const ROLE_OPTIONS = [
  { value: 'user', label: 'Visitor' },
  { value: 'assistant', label: 'Agent' },
] as const;

function HistoryEditor({
  idPrefix,
  turns,
  onChange,
  errors,
}: {
  idPrefix: string;
  turns: EvalTurn[];
  onChange: (turns: EvalTurn[]) => void;
  errors: string[];
}) {
  const set = (index: number, patch: Partial<EvalTurn>) => onChange(turns.map((turn, at) => (at === index ? { ...turn, ...patch } : turn)));
  const next: EvalTurn['role'] = turns.at(-1)?.role === 'user' ? 'assistant' : 'user';
  return (
    <fieldset className="space-y-3 rounded-xl border border-border p-3">
      <legend className="px-1 text-small font-medium text-fg">Earlier in the conversation</legend>
      <p className="text-xs text-fg-muted">A run asks the visitor’s turns first, in one conversation, then the question above. The agent’s turns are for reference.</p>
      <ol className="space-y-3">
        {turns.map((turn, index) => (
          // Turns have no ids; the index is stable since only the last turn is appended.
          <li key={index} className="space-y-1.5">
            <div className="flex items-center justify-between gap-2">
              <SegmentedControl
                label={`Turn ${index + 1} is from`}
                options={ROLE_OPTIONS}
                value={turn.role}
                onChange={(role) => set(index, { role })}
              />
              <Button size="icon-sm" variant="ghost" icon={X} aria-label={`Remove turn ${index + 1}`} onClick={() => onChange(turns.filter((_, at) => at !== index))} />
            </div>
            <textarea
              id={`${idPrefix}-turn-${index}`}
              aria-label={`Turn ${index + 1}`}
              rows={2}
              value={turn.content}
              onChange={(event) => set(index, { content: event.target.value })}
              className="field min-h-14 py-1.5"
            />
          </li>
        ))}
      </ol>
      {errors.length > 0 ? <p className="text-xs text-danger-text">{errors.join(' ')}</p> : null}
      <Button size="sm" variant="ghost" icon={Plus} onClick={() => onChange([...turns, { role: next, content: '' }])}>
        Add a turn
      </Button>
    </fieldset>
  );
}

export interface QuestionFieldsProps {
  idPrefix: string;
  draft: QuestionDraft;
  onChange: (draft: QuestionDraft) => void;
  error: unknown;
}

export function QuestionFields({ idPrefix, draft, onChange, error }: QuestionFieldsProps) {
  const id = (field: string) => `${idPrefix}-${field}`;
  const errors = (field: string) => (error instanceof ApiError ? error.errorsFor(field) : []);
  const set = (patch: Partial<QuestionDraft>) => onChange({ ...draft, ...patch });
  const multiTurn = draft.history.length > 0;
  return (
    <div className="space-y-4">
      {multiTurn ? <HistoryEditor idPrefix={idPrefix} turns={draft.history} onChange={(history) => set({ history })} errors={errors('history')} /> : null}
      <Field
        label={multiTurn ? 'Follow-up question' : 'Question'}
        htmlFor={id('question')}
        errors={errors('question')}
        hint={multiTurn ? 'The visitor’s last turn, which the run scores.' : 'As a visitor would ask it.'}
      >
        <textarea
          {...fieldAria(id('question'), errors('question'), true)}
          rows={2}
          value={draft.question}
          onChange={(event) => set({ question: event.target.value })}
          className="field min-h-16 py-1.5"
        />
      </Field>
      {multiTurn ? null : (
        <Button size="sm" variant="ghost" icon={MessagesSquare} onClick={() => set({ history: [{ role: 'user', content: '' }, { role: 'assistant', content: '' }] })}>
          Make it a follow-up question
        </Button>
      )}
      <div className="space-y-1">
        <Switch
          checked={draft.answerable}
          onChange={(answerable) => set({ answerable })}
          label="The agent should answer this"
          describedBy={id('answerable-hint')}
        />
        <p id={id('answerable-hint')} className="text-xs text-fg-muted">
          {draft.answerable
            ? 'Turn off for a question your sources do not cover, where a good reply says so instead of guessing.'
            : 'A good reply says it does not know. Expected answer, facts and sources are not used.'}
        </p>
      </div>
      {draft.answerable ? (
        <>
          <Field
            label="Expected answer"
            htmlFor={id('answer')}
            optional
            errors={errors('referenceAnswer')}
            hint="What a correct reply says. Correct the agent's answer here if it was wrong."
          >
            <textarea
              {...fieldAria(id('answer'), errors('referenceAnswer'), true)}
              rows={4}
              value={draft.referenceAnswer}
              onChange={(event) => set({ referenceAnswer: event.target.value })}
              className="field min-h-24 py-1.5"
            />
          </Field>
          <Field
            label="Facts a good answer must contain"
            htmlFor={id('facts')}
            optional
            errors={errors('expectedFacts')}
            hint="One per line, short, such as “30 days” or “free over 50 EUR”. Each run checks that the answer contains them."
          >
            <textarea
              {...fieldAria(id('facts'), errors('expectedFacts'), true)}
              rows={3}
              value={draft.facts}
              onChange={(event) => set({ facts: event.target.value })}
              className="field min-h-20 py-1.5"
            />
          </Field>
          <Field
            label="Sources it should cite"
            htmlFor={id('sources')}
            optional
            errors={errors('expectedSources')}
            hint="Pick the sources that hold the answer. Leave empty if you are not sure."
          >
            <SourcePicker id={id('sources')} value={draft.sources} onChange={(sources) => set({ sources })} errors={errors('expectedSources')} />
          </Field>
        </>
      ) : null}
      <Field label="Tags" htmlFor={id('tags')} optional errors={errors('tags')} hint="Separated by commas, to run a part of the dataset.">
        <input
          {...fieldAria(id('tags'), errors('tags'), true)}
          autoComplete="off"
          value={draft.tags}
          onChange={(event) => set({ tags: event.target.value })}
          className="field"
        />
      </Field>
    </div>
  );
}
