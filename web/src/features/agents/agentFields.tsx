import { clsx } from 'clsx';
import { Check, Plus } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Field, inputProps } from '../../components/ui/Field';
import { Select } from '../../components/ui/Select';
import { Switch } from '../../components/ui/Switch';
import { pluralize } from '../../lib/format';
import { useSourceFolders, useSourceStats } from '../sources/api';
import { buildFolderTree, findNode, internalSourceCount, withoutInternal } from '../sources/folders';
import type { WidgetDto } from '../widgets/types';
import { HANDOVER_CHANNEL_LABELS } from '../widgets/widgetTexts';
import {
  HANDOVER_POLICIES,
  MAX_DESCRIPTION_LENGTH,
  MAX_INSTRUCTIONS_LENGTH,
  MAX_KEY_LENGTH,
  MAX_NAME_LENGTH,
  toggleFolder,
  type AgentFormField,
  type AgentFormValues,
  type KnowledgeScope,
} from './agentForm';
import { INSTRUCTION_SNIPPETS, instructionsGuidance, TONES, toneOf, withSnippet, withTone } from './agentTemplates';
import { FolderChecklist } from './FolderChecklist';
import { languageOptions, matchLanguage } from './languages';
import { ToolPicker } from './ToolPicker';
import type { HandoverPolicy } from './types';
import { useReturnState } from '../../lib/returnTo';

export interface FieldKit {
  values: AgentFormValues;
  set: <K extends keyof AgentFormValues>(field: K, value: AgentFormValues[K], clears?: readonly AgentFormField[]) => void;
  errorsFor: (field: AgentFormField) => string[] | undefined;
  id: (name: string) => string;
}

function Hint({ id, error, children }: { id: string; error?: string[]; children: ReactNode }) {
  return error ? (
    <p id={id} className="text-xs text-danger-text">
      {error.join(' ')}
    </p>
  ) : (
    <p id={id} className="text-xs text-fg-muted">
      {children}
    </p>
  );
}

export function FieldGroup({ id, title, why, children }: { id: string; title: string; why?: ReactNode; children: ReactNode }) {
  return (
    <section aria-labelledby={`${id}-heading`} className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-card">
      <div>
        <h2 id={`${id}-heading`} className="text-section text-fg">
          {title}
        </h2>
        {why ? <p className="mt-0.5 text-sm text-fg-secondary">{why}</p> : null}
      </div>
      {children}
    </section>
  );
}

interface ChoiceCardsProps<T extends string> {
  name: string;
  label?: string;
  labelledBy?: string;
  describedBy?: string;
  options: ReadonlyArray<{ value: T; label: string; description: ReactNode; aside?: ReactNode }>;
  value: T | null;
  onChange: (value: T) => void;
  columns?: string;
  invalid?: boolean;
}

function ChoiceCards<T extends string>({ name, label, labelledBy, describedBy, options, value, onChange, columns, invalid }: ChoiceCardsProps<T>) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      aria-labelledby={labelledBy}
      aria-describedby={describedBy}
      aria-invalid={invalid || undefined}
      className={clsx('grid gap-2', columns)}
    >
      {options.map((option) => {
        const selected = value === option.value;
        const optionId = `${name}-${option.value}`;
        return (
          <label
            key={option.value}
            className={clsx(
              'flex cursor-pointer items-start gap-3 rounded-lg border p-3',
              selected ? 'border-accent-edge bg-accent-subtle' : 'border-border hover:bg-fill-hover',
            )}
          >
            <input
              type="radio"
              name={name}
              checked={selected}
              onChange={() => onChange(option.value)}
              aria-labelledby={optionId}
              aria-describedby={`${optionId}-text`}
              className="mt-0.5 size-4 shrink-0"
            />
            <span className="min-w-0 flex-1">
              <span id={optionId} className="block text-sm font-medium text-fg">
                {option.label}
              </span>
              <span id={`${optionId}-text`} className="block text-small text-fg-secondary">
                {option.description}
              </span>
            </span>
            {option.aside ? <span className="shrink-0 text-xs text-fg-muted tabular-nums">{option.aside}</span> : null}
          </label>
        );
      })}
    </div>
  );
}

export interface NameFieldsProps extends FieldKit {
  /** A new agent also gets its key, which follows the name until it is typed. */
  isNew: boolean;
  autoFocus?: boolean;
}

export function NameFields({ values, set, errorsFor, id, isNew, autoFocus }: NameFieldsProps) {
  const [keyOpen, setKeyOpen] = useState(false);
  const showKey = keyOpen || Boolean(errorsFor('key'));
  return (
    <>
      <Field
        label="Name"
        htmlFor={id('name')}
        errors={errorsFor('name')}
        hint={
          isNew && !showKey ? (
            <>
              Its key is <span className="font-mono">{values.key || 'set from the name'}</span>.{' '}
              <button type="button" onClick={() => setKeyOpen(true)} className="font-medium text-accent-text hover:underline">
                Change key
              </button>
            </>
          ) : undefined
        }
      >
        <input
          {...fieldAria(id('name'), errorsFor('name'), isNew && !showKey)}
          required
          maxLength={MAX_NAME_LENGTH}
          autoComplete="off"
          autoFocus={autoFocus}
          placeholder="Shop assistant"
          value={values.name}
          onChange={(event) => set('name', event.target.value, ['name', 'key'])}
          className="field"
        />
      </Field>
      {isNew && showKey ? (
        <Field label="Key" htmlFor={id('key')} errors={errorsFor('key')} hint="Lowercase letters, digits and dashes. It cannot change later.">
          <input
            {...fieldAria(id('key'), errorsFor('key'), true)}
            {...inputProps}
            required
            maxLength={MAX_KEY_LENGTH}
            placeholder="shop-assistant"
            value={values.key}
            onChange={(event) => set('key', event.target.value, ['key'])}
            className="field max-w-80 font-mono text-xs"
          />
        </Field>
      ) : null}
      <Field
        label="Purpose"
        htmlFor={id('description')}
        optional
        errors={errorsFor('description')}
        hint="One sentence on who it helps with what. It shows on the agent's card."
      >
        <input
          {...fieldAria(id('description'), errorsFor('description'), true)}
          maxLength={MAX_DESCRIPTION_LENGTH}
          autoComplete="off"
          placeholder="Answers shoppers about orders, delivery and returns."
          value={values.description}
          onChange={(event) => set('description', event.target.value, ['description'])}
          className="field"
        />
      </Field>
    </>
  );
}

/** A saved language missing from the list stays an option. */
export function LanguageField({ values, set, errorsFor, id }: FieldKit) {
  const options = languageOptions(values.language);
  const value = matchLanguage(values.language) ?? values.language.trim();
  return (
    <Field label="Language" htmlFor={id('language')} errors={errorsFor('language')} hint="Type a few letters to jump to a language.">
      <Select
        {...fieldAria(id('language'), errorsFor('language'), true)}
        className="max-w-md"
        value={value}
        onChange={(next) => set('language', next, ['language'])}
        options={options}
      />
    </Field>
  );
}

/** The tone becomes the first line of the instructions. */
export function ToneField({ values, set, id, ownHint }: FieldKit & { ownHint: string }) {
  const options = TONES.map((tone) => ({ value: tone.value, label: tone.label, description: tone.value === 'own' ? ownHint : tone.example }));
  return (
    <div className="space-y-1.5">
      <p id={id('tone-label')} className="text-small font-medium text-fg">
        Tone
      </p>
      <ChoiceCards
        name={id('tone')}
        labelledBy={id('tone-label')}
        options={options}
        value={toneOf(values.instructions)}
        onChange={(tone) => set('instructions', withTone(values.instructions, tone), ['instructions'])}
        columns="@min-[34rem]:grid-cols-2"
      />
    </div>
  );
}

export function InstructionsField({ values, set, errorsFor, id }: FieldKit) {
  const guidance = instructionsGuidance(values.instructions.length, MAX_INSTRUCTIONS_LENGTH);
  const errors = errorsFor('instructions');
  const fieldId = id('instructions');
  return (
    <div className="space-y-1.5">
      <label htmlFor={fieldId} className="flex items-baseline gap-1.5 text-small font-medium text-fg">
        Instructions
        <span className="font-normal text-fg-muted">optional</span>
      </label>
      <textarea
        id={fieldId}
        aria-invalid={errors ? true : undefined}
        aria-describedby={`${fieldId}-guidance ${fieldId}-snippets`}
        rows={9}
        maxLength={MAX_INSTRUCTIONS_LENGTH}
        placeholder={'For example:\nYou help visitors of our online shop with orders, delivery and returns.\nKeep answers short and link to the page you used.'}
        value={values.instructions}
        onChange={(event) => set('instructions', event.target.value, ['instructions'])}
        className="field resize-y leading-6"
      />
      {errors ? (
        <p id={`${fieldId}-guidance`} className="text-xs text-danger-text">
          {errors.join(' ')}
        </p>
      ) : (
        <p id={`${fieldId}-guidance`} className={clsx('text-xs', guidance.long ? 'text-warning-text' : 'text-fg-muted')}>
          {guidance.text}
        </p>
      )}
      <div className="flex flex-wrap items-center gap-2 pt-1">
        <span id={`${fieldId}-snippets`} className="text-xs text-fg-muted">
          Add an example:
        </span>
        {INSTRUCTION_SNIPPETS.map((snippet) => {
          const added = values.instructions.includes(snippet.text);
          return (
            <Button
              key={snippet.label}
              size="sm"
              variant="secondary"
              icon={added ? Check : Plus}
              disabled={added}
              title={snippet.text}
              onClick={() => set('instructions', withSnippet(values.instructions, snippet), ['instructions'])}
            >
              {snippet.label}
            </Button>
          );
        })}
      </div>
    </div>
  );
}

export function SourcesNotice() {
  const returnState = useReturnState();
  const stats = useSourceStats();
  if (!stats.data) {
    return null;
  }
  const { total, waiting, pending, processing, failed } = stats.data;
  const sourcesLink = (
    <Link to="/sources" state={returnState} className="font-medium text-accent-text hover:underline">
      Sources
    </Link>
  );
  if (total === 0) {
    return (
      <Alert tone="warning" title="There are no sources yet">
        Add a website, files or text on the {sourcesLink} page, or the agent has nothing to answer from.
      </Alert>
    );
  }
  const notes = [
    waiting > 0 ? `${pluralize(waiting, 'source')} ${waiting === 1 ? 'waits' : 'wait'} for an embedding model.` : null,
    pending + processing > 0 ? `${pluralize(pending + processing, 'source')} ${pending + processing === 1 ? 'is' : 'are'} still being read.` : null,
    failed > 0 ? `${pluralize(failed, 'source')} could not be read.` : null,
  ].filter(Boolean);
  if (notes.length === 0) {
    return null;
  }
  return (
    <Alert tone="warning" title="Some sources are not ready">
      {notes.join(' ')} The agent cannot answer from them until they are. See {sourcesLink}.
    </Alert>
  );
}

const SCOPES: ReadonlyArray<{ value: KnowledgeScope; label: string; description: string }> = [
  { value: 'all', label: 'All sources', description: 'Everything in your knowledge base, including what you add later.' },
  { value: 'folders', label: 'Chosen folders', description: 'Only the folders you pick, with their subfolders.' },
];

/** Only the help assistant searches Internal, so it isn't offered and All sources leaves it out. */
export function KnowledgeFields({ values, set, errorsFor, id }: FieldKit) {
  const folders = useSourceFolders();
  const fullTree = useMemo(() => (folders.data ? buildFolderTree(folders.data.folders) : undefined), [folders.data]);
  const internalCount = fullTree ? internalSourceCount(fullTree) : 0;
  const tree = fullTree ? withoutInternal(fullTree) : undefined;
  const missing = tree && values.scope === 'folders' ? values.folderIds.filter((folderId) => !findNode(tree, folderId)).length : 0;
  const chosenCount = tree ? values.folderIds.reduce((sum, folderId) => sum + (findNode(tree, folderId)?.totalCount ?? 0), 0) : null;
  const options = SCOPES.map((scope) => ({
    ...scope,
    aside: scope.value === 'all' && folders.data ? pluralize(folders.data.totalCount - internalCount, 'source') : undefined,
  }));
  const internalNote = internalCount > 0 ? 'Sources in the Internal folder are only for the help assistant.' : null;

  return (
    <div className="space-y-4">
      <ChoiceCards
        name={id('scope')}
        label="Knowledge"
        describedBy={id('scope-hint')}
        options={options}
        value={values.scope}
        onChange={(scope) => set('scope', scope, ['folderIds'])}
        columns="@min-[32rem]:grid-cols-2"
      />
      {values.scope === 'folders' ? (
        folders.isPending ? (
          <Spinner label="Loading folders" />
        ) : folders.isError ? (
          <ErrorMessage error={folders.error} title="Could not load the folders" />
        ) : (
          <FolderChecklist
            tree={tree ?? []}
            selected={values.folderIds}
            onToggle={(node) => set('folderIds', toggleFolder(values.folderIds, node), ['folderIds'])}
            describedBy={id('scope-hint')}
          />
        )
      ) : null}
      <Hint id={id('scope-hint')} error={errorsFor('folderIds')}>
        {[
          ...(values.scope === 'all'
            ? ['Good for one assistant that knows everything. Use folders when, say, the shop agent must not answer from HR documents.']
            : [
                chosenCount !== null && values.folderIds.length > 0 ? `${pluralize(chosenCount, 'source')} in the chosen folders.` : null,
                'Subfolders are included.',
                missing > 0
                  ? `${pluralize(missing, 'chosen folder')} no longer ${missing === 1 ? 'exists and is' : 'exist and are'} removed when you save.`
                  : null,
              ]),
          internalNote,
        ]
          .filter(Boolean)
          .join(' ')}
      </Hint>
      {values.scope === 'folders' && chosenCount === 0 && values.folderIds.length > 0 ? (
        <Alert tone="warning">The chosen folders hold no sources yet, so the agent has nothing to answer from.</Alert>
      ) : null}
    </div>
  );
}

export function GeneralKnowledgeField({ values, set, id }: FieldKit) {
  return (
    <div className="space-y-1.5">
      <Switch
        checked={values.allowGeneralKnowledge}
        onChange={(checked) => set('allowGeneralKnowledge', checked)}
        label="May answer from general knowledge"
        describedBy={id('general-hint')}
      />
      <p id={id('general-hint')} className="text-xs text-fg-muted">
        When your sources do not cover a question, it says so, then answers from general knowledge and marks that part. Leave it off to keep answers
        strictly to your facts.
      </p>
    </div>
  );
}

export function HandoverField({ values, set, id }: FieldKit) {
  return (
    <div className="space-y-1.5">
      <ChoiceCards
        name={id('policy')}
        label="When to offer a person"
        describedBy={id('person-hint')}
        options={HANDOVER_POLICIES}
        value={values.handoverPolicy}
        onChange={(policy) => set('handoverPolicy', policy)}
        columns="@min-[40rem]:grid-cols-3"
      />
      <p id={id('person-hint')} className="text-xs text-fg-muted">
        Only in widgets that offer a person. Each widget sets where requests go, such as your Inbox, Slack or email.
      </p>
    </div>
  );
}

export function ToolsField({ values, set, errorsFor, id, available }: FieldKit & { available: readonly string[] }) {
  const on = available.filter((tool) => values.tools.includes(tool)).length;
  const toggle = (tool: string, checked: boolean) =>
    set('tools', checked ? [...values.tools.filter((item) => item !== tool), tool] : values.tools.filter((item) => item !== tool), ['tools']);
  return (
    <div className="space-y-2">
      <ToolPicker idPrefix={id('tools')} available={available} selected={values.tools} onToggle={toggle} describedBy={id('tools-hint')} />
      <Hint id={id('tools-hint')} error={errorsFor('tools')}>
        {on > 0 ? `${on} of ${available.length} on.` : 'Without tools it cannot search your sources.'}
      </Hint>
    </div>
  );
}

function personChannels(widget: WidgetDto): string | null {
  if (!widget.settings.behavior.handoverEnabled) {
    return null;
  }
  const channel = widget.settings.handover.channel;
  // The inbox always gets the request, whatever else the widget sends it to.
  return channel === 'portal' ? 'Inbox' : `Inbox, ${HANDOVER_CHANNEL_LABELS[channel] ?? channel}`;
}

export function WidgetHandover({ using, policy, headingId }: { using: readonly WidgetDto[] | undefined; policy: HandoverPolicy; headingId: string }) {
  const returnState = useReturnState();
  if (!using) {
    return null;
  }
  if (using.length === 0) {
    return <p className="text-small text-fg-secondary">No widget uses this agent yet, so nobody can ask it for a person.</p>;
  }
  const nobodyOffered = using.every((widget) => personChannels(widget) === null);
  return (
    <div className="space-y-1.5 border-t border-border-subtle pt-4">
      <p id={headingId} className="text-small font-medium text-fg">
        Used by
      </p>
      <ul aria-labelledby={headingId} className="space-y-1">
        {using.map((widget) => (
          <li key={widget.id} className="text-small text-fg-secondary">
            <Link to={`/widgets/${widget.id}`} state={returnState} className="font-medium text-accent-text hover:underline">
              {widget.name}
            </Link>
            : {personChannels(widget) ?? 'no person offered'}
          </li>
        ))}
      </ul>
      {nobodyOffered && policy !== 'never' ? (
        <p className="text-xs text-fg-muted">No widget offers a person, so the agent cannot bring one in. Turn on Show Talk to a person in a widget to change that.</p>
      ) : null}
    </div>
  );
}
