import { useMutation, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Play } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Field } from '../../components/ui/Field';
import { Modal } from '../../components/ui/Modal';
import { Switch } from '../../components/ui/Switch';
import { ApiError } from '../../lib/api';
import { pluralize } from '../../lib/format';
import { parsePositiveInt } from '../../lib/forms';
import { useProfiles } from '../models/api';
import { PROVIDERS_PATH } from '../models/providerSettings';
import { evalKeys, evalsApi } from './api';
import { isBuiltinDataset, runPath } from './runs';
import type { EvalDatasetDto, StartEvalRunRequest } from './types';
import { useReturnState } from '../../lib/returnTo';

/** Fields with a control; errors for the rest are listed together. */
const FORM_FIELDS = ['judge', 'maxItems', 'tags', 'profile', 'name'] as const;
const ADVANCED_FIELDS = ['profile', 'name'] as const;

export interface StartRunModalProps {
  dataset: EvalDatasetDto;
  judgeAvailable: boolean;
  judgeModel: string | null;
  onClose: () => void;
}

export function StartRunModal({ dataset, judgeAvailable, judgeModel, onClose }: StartRunModalProps) {
  const returnState = useReturnState();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const profiles = useProfiles();
  const idPrefix = useId();
  const id = (field: string) => `${idPrefix}-${field}`;

  const [judge, setJudge] = useState(false);
  const [maxItems, setMaxItems] = useState('');
  const [tags, setTags] = useState<ReadonlySet<string>>(new Set());
  const [profile, setProfile] = useState('');
  const [name, setName] = useState('');
  const [maxItemsError, setMaxItemsError] = useState<string | null>(null);
  const [advancedOpen, setAdvancedOpen] = useState(false);

  const start = useMutation({
    mutationFn: (body: StartEvalRunRequest) => evalsApi.startEvalRun(body),
    onSuccess: (run) => {
      void queryClient.invalidateQueries({ queryKey: evalKeys.runs });
      onClose();
      if (run?.id) {
        navigate(runPath(run.id));
      }
    },
    onError: (error) => {
      if (error instanceof ApiError && ADVANCED_FIELDS.some((field) => error.errorsFor(field).length > 0)) {
        setAdvancedOpen(true);
      }
    },
  });

  const validation = start.error instanceof ApiError && start.error.isValidation ? start.error : null;
  const serverErrors = (field: string): string[] | undefined => {
    const messages = validation?.errorsFor(field) ?? [];
    return messages.length > 0 ? messages : undefined;
  };
  // Without tags there's no tag control, so tag errors go to the shared list.
  const shownFields = dataset.tags.length > 0 ? FORM_FIELDS : FORM_FIELDS.filter((field) => field !== 'tags');
  const otherErrors = validation ? validation.errorsExcept(shownFields) : [];
  const maxItemsErrors = maxItemsError ? [maxItemsError] : serverErrors('maxItems');

  const toggleTag = (tag: string) =>
    setTags((current) => {
      const next = new Set(current);
      if (!next.delete(tag)) {
        next.add(tag);
      }
      return next;
    });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const parsedMaxItems = parsePositiveInt(maxItems);
    if (parsedMaxItems === 'invalid') {
      setMaxItemsError('Enter a whole number greater than 0.');
      return;
    }
    setMaxItemsError(null);
    const selectedTags = dataset.tags.filter((tag) => tags.has(tag));
    start.mutate({
      dataset: dataset.name,
      source: dataset.source,
      judge: judgeAvailable && judge,
      profile: profile.trim() || null,
      maxItems: parsedMaxItems,
      tags: selectedTags.length > 0 ? selectedTags : null,
      name: name.trim() || null,
    });
  };

  const builtin = isBuiltinDataset(dataset.source);
  const description = [
    pluralize(dataset.itemCount, 'question'),
    builtin
      ? `runs in a temporary, isolated knowledge base with the dataset’s ${pluralize(dataset.corpusDocuments, 'document')}`
      : 'runs against your live knowledge base',
  ].join(' · ');
  const chatProfiles = (profiles.data ?? []).filter((p) => p.kind?.toLowerCase() === 'chat').map((p) => p.name);
  const judgeErrors = serverErrors('judge');
  const tagErrors = serverErrors('tags');
  const formId = id('form');

  return (
    <Modal
      open
      onClose={onClose}
      title={`Run ${dataset.name}`}
      description={description}
      dismissible={!start.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={start.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="primary" icon={Play} loading={start.isPending}>
            Start run
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} noValidate className="space-y-4" aria-label={`Run options for ${dataset.name}`}>
        <div className="space-y-1">
          <Switch
            checked={judgeAvailable && judge}
            onChange={setJudge}
            disabled={!judgeAvailable}
            label="LLM judge"
            describedBy={judgeErrors ? id('judge-error') : id('judge-hint')}
          />
          {judgeErrors ? (
            <p id={id('judge-error')} className="text-xs text-danger-text">
              {judgeErrors.join(' ')}
            </p>
          ) : (
            <p id={id('judge-hint')} className="text-xs text-fg-muted">
              {judgeAvailable ? (
                <>
                  Also scores faithfulness, relevance and correctness with{' '}
                  {judgeModel ? <code className="font-mono text-fg-secondary">{judgeModel}</code> : 'the judge model'}. Costs
                  tokens.
                </>
              ) : (
                <>
                  The judge needs an LLM. Add an API key under{' '}
                  <Link to={PROVIDERS_PATH} state={returnState} className="font-medium text-accent-text underline-offset-2 hover:underline">
                    Models → Providers
                  </Link>
                  .
                </>
              )}
            </p>
          )}
        </div>

        <Field
          label="Max questions"
          htmlFor={id('max-items')}
          optional
          errors={maxItemsErrors}
          hint={`Asks only the first N of the ${pluralize(dataset.itemCount, 'question')}, for a quick check.`}
          className="max-w-xs"
        >
          <input
            {...fieldAria(id('max-items'), maxItemsErrors, true)}
            inputMode="numeric"
            autoComplete="off"
            value={maxItems}
            onChange={(event) => {
              setMaxItems(event.target.value);
              setMaxItemsError(null);
            }}
            placeholder="All"
            className="field"
          />
        </Field>

        {dataset.tags.length > 0 ? (
          <fieldset aria-describedby={tagErrors ? id('tags-error') : id('tags-hint')}>
            <legend className="flex items-baseline gap-1.5 text-small font-medium text-fg">
              Tags <span className="font-normal text-fg-muted">optional</span>
            </legend>
            <div className="mt-1.5 flex flex-wrap gap-1.5">
              {dataset.tags.map((tag) => {
                const checked = tags.has(tag);
                return (
                  <label
                    key={tag}
                    className={clsx(
                      'inline-flex h-7 cursor-pointer items-center gap-1.5 rounded-full border px-2.5 text-small font-medium has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-focus',
                      checked ? 'border-transparent bg-accent-subtle text-accent-text' : 'border-border text-fg-secondary hover:bg-fill-hover hover:text-fg',
                    )}
                  >
                    <input type="checkbox" className="size-3.5" checked={checked} onChange={() => toggleTag(tag)} />
                    {tag}
                  </label>
                );
              })}
            </div>
            {tagErrors ? (
              <p id={id('tags-error')} className="mt-1.5 text-xs text-danger-text">
                {tagErrors.join(' ')}
              </p>
            ) : (
              <p id={id('tags-hint')} className="mt-1.5 text-xs text-fg-muted">
                {tags.size === 0 ? 'All questions are asked.' : 'Only questions with at least one of the selected tags are asked.'}
              </p>
            )}
          </fieldset>
        ) : null}

        <CollapsibleSection
          title="Advanced"
          description="The profile it answers with, and a name for the run."
          open={advancedOpen}
          onOpenChange={setAdvancedOpen}
        >
          <div className="grid gap-4 sm:grid-cols-2">
            <Field
              label="Profile"
              htmlFor={id('profile')}
              optional
              errors={serverErrors('profile')}
              hint="Chat model profile the agent answers with. Empty uses the orchestrator’s."
            >
              <input
                {...fieldAria(id('profile'), serverErrors('profile'), true)}
                list={chatProfiles.length > 0 ? id('profiles') : undefined}
                autoComplete="off"
                spellCheck={false}
                value={profile}
                onChange={(event) => setProfile(event.target.value)}
                placeholder="orchestrator"
                className="field font-mono text-xs"
              />
              {chatProfiles.length > 0 ? (
                <datalist id={id('profiles')}>
                  {chatProfiles.map((option) => (
                    <option key={option} value={option} />
                  ))}
                </datalist>
              ) : null}
            </Field>
            <Field label="Run name" htmlFor={id('name')} optional errors={serverErrors('name')} hint="Also the experiment name in Langfuse.">
              <input
                {...fieldAria(id('name'), serverErrors('name'), true)}
                autoComplete="off"
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="Generated from dataset and time"
                className="field"
              />
            </Field>
          </div>
        </CollapsibleSection>

        {start.isError && !validation ? <ErrorMessage error={start.error} title="The run was not started" /> : null}
        {otherErrors.length > 0 ? (
          <Alert tone="danger" title="The run was not started" live>
            {otherErrors.join(' ')}
          </Alert>
        ) : null}
      </form>
    </Modal>
  );
}
