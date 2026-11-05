import { useQueries, type UseQueryResult } from '@tanstack/react-query';
import { RotateCcw } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Field } from '../../components/ui/Field';
import { ConfirmDialog } from '../../components/ui/Modal';
import { toast } from '../../components/ui/Toast';
import { ErrorMessage, Spinner } from '../../components/ui/feedback';
import { ApiError } from '../../lib/api';
import { useDebouncedValue } from '../../lib/hooks/useDebouncedValue';
import { renderedTemplateQuery, useSavePrompt } from './api';
import { HandlebarsCheatSheet } from './HandlebarsCheatSheet';
import { formatJson, parseVariables } from './json';
import { JsonEditor } from './JsonEditor';
import { RenderedOutput, roleLabel } from './RenderedOutput';
import { sampleVariables } from './samples';
import type { PromptMessageDto, PromptType, RenderTemplateResponse } from './types';

export interface EditSource {
  version: number;
  type: PromptType;
  text: string | null;
  messages: PromptMessageDto[];
}

type EditOutcome = 'draft' | 'published';

export interface PromptEditPanelProps {
  name: string;
  source: EditSource;
  /** Publishing fails if the live version changed since the edit started. */
  liveVersion: number | null;
  /** The label the application serves, production by default. */
  label: string;
  /** Of the live version, for the sample values. */
  variables: string[];
  onDone: (outcome: EditOutcome) => void;
  onCancel: () => void;
}

const NOTE_MAX = 300;

function fieldErrors(error: unknown, field: string): string[] | undefined {
  if (error instanceof ApiError) {
    const messages = error.errorsFor(field);
    return messages.length > 0 ? messages : undefined;
  }
  return undefined;
}

function TemplateInput({ id, label, value, onChange, errors, rows }: { id: string; label: string; value: string; onChange: (value: string) => void; errors?: string[] | undefined; rows: number }) {
  return (
    <Field label={label} htmlFor={id} errors={errors}>
      <textarea
        {...fieldAria(id, errors)}
        value={value}
        rows={rows}
        spellCheck={false}
        autoCapitalize="off"
        autoComplete="off"
        onChange={(event) => onChange(event.target.value)}
        className="field resize-y font-mono text-xs leading-relaxed"
      />
    </Field>
  );
}

// Joins message templates for a single debounce; no template contains this character.
const TEMPLATE_SEPARATOR = '\u0000';

/** Rendered by the API once typing pauses. */
function useTemplatePreviews(templates: readonly string[], variablesText: string) {
  const settledTemplates = useDebouncedValue(templates.join(TEMPLATE_SEPARATOR));
  const settledVariables = useDebouncedValue(variablesText);
  const parsed = parseVariables(settledVariables);
  const variables = parsed.ok ? parsed.value : null;
  const queries = settledTemplates.split(TEMPLATE_SEPARATOR).map((template) => renderedTemplateQuery(template, settledVariables, variables));
  return useQueries({ queries });
}

type TemplatePreview = UseQueryResult<RenderTemplateResponse>;

function TextPreview({ template, render }: { template: string; render: TemplatePreview | undefined }) {
  return (
    <PreviewFrame fetching={render?.isFetching ?? false} error={render?.isError ? render.error : null}>
      {!template.trim() ? <p className="text-small text-fg-muted">Write a template to see the result.</p> : render?.data ? <RenderedOutput text={render.data.output} /> : null}
    </PreviewFrame>
  );
}

function MessagePreview({ message, render }: { message: PromptMessageDto; render: TemplatePreview | undefined }) {
  return (
    <div className="space-y-1">
      <p className="text-xs font-medium text-fg-muted">{roleLabel(message.role)}</p>
      {render?.isError ? <ErrorMessage error={render.error} title="Rendering failed" /> : null}
      {render?.data && message.content.trim() ? <RenderedOutput text={render.data.output} /> : null}
    </div>
  );
}

function PreviewFrame({ fetching, error, children }: { fetching: boolean; error: unknown; children: ReactNode }) {
  return (
    <div className="space-y-2">
      {fetching ? <Spinner label="Rendering" className="size-3.5" /> : null}
      {error ? <ErrorMessage error={error} title="Rendering failed" /> : null}
      {children}
    </div>
  );
}

function VariablesPanel({ used, sampleKeys }: { used: string[]; sampleKeys: Set<string> }) {
  if (used.length === 0) {
    return <p className="text-small text-fg-muted">The template uses no variables.</p>;
  }
  return (
    <ul className="flex flex-wrap gap-1.5" aria-label="Variables in the edited template">
      {used.map((variable) => {
        const missing = !sampleKeys.has(variable);
        return (
          <li key={variable}>
            <code
              className={missing ? 'rounded-sm bg-warning-subtle px-1.5 py-0.5 font-mono text-xs text-warning-text' : 'rounded-sm bg-accent-subtle px-1.5 py-0.5 font-mono text-xs text-accent-text'}
              title={missing ? 'No sample value yet. Add it to the sample variables to see it in the preview.' : undefined}
            >
              {variable}
              {missing ? <span className="sr-only"> (no sample value)</span> : null}
            </code>
          </li>
        );
      })}
    </ul>
  );
}

/** "Save as draft" adds a Langfuse version without the live label; "Publish" also moves the label to it. */
export function PromptEditPanel({ name, source, liveVersion, label, variables, onDone, onCancel }: PromptEditPanelProps) {
  const chat = source.type === 'chat';
  const [text, setText] = useState(source.text ?? '');
  const [messages, setMessages] = useState<PromptMessageDto[]>(source.messages);
  const [note, setNote] = useState('');
  const sample = useMemo(() => formatJson(sampleVariables(name, variables)), [name, variables]);
  const [variablesText, setVariablesText] = useState(sample);
  const parsedVariables = parseVariables(variablesText);
  const [confirm, setConfirm] = useState<'publish' | 'discard' | null>(null);
  const save = useSavePrompt(name);

  const dirty = chat ? messages.some((message, index) => message.content !== source.messages[index]?.content) : text !== (source.text ?? '');
  const previews = useTemplatePreviews(chat ? messages.map((message) => message.content) : [text], variablesText);
  const used = [...new Set(previews.flatMap((preview) => preview.data?.variables ?? []))].sort();
  const sampleKeys = useMemo(() => new Set(parsedVariables.ok ? Object.keys(parsedVariables.value) : []), [parsedVariables]);

  const submit = (publish: boolean) => {
    save.mutate(
      {
        draft: {
          ...(chat ? { messages } : { text }),
          commitMessage: note.trim() || null,
          baseVersion: source.version,
        },
        ...(publish ? { publish: { expectedLiveVersion: liveVersion } } : {}),
      },
      {
        onSuccess: ({ saved, published, publishError }) => {
          setConfirm(null);
          if (published) {
            toast({ title: `Version ${saved.version} is live`, description: `The ${label} label moved to it. New answers use it now.`, tone: 'success' });
            onDone('published');
          } else if (publishError) {
            toast({
              title: `Saved version ${saved.version} as a draft, but it is not live`,
              description: publishError instanceof ApiError && publishError.detail ? publishError.detail : 'Publishing failed. Try again from Versions.',
              tone: 'warning',
            });
            onDone('draft');
          } else {
            toast({ title: `Saved version ${saved.version} as a draft`, description: `Version ${liveVersion ?? source.version} stays live until you publish it.`, tone: 'success' });
            onDone('draft');
          }
        },
        // Field errors show beside the template, so the dialog closes; other errors stay in it.
        onError: (failure) => {
          if (failure instanceof ApiError && failure.isValidation) {
            setConfirm(null);
          }
        },
      },
    );
  };

  const error = save.error;
  const formFields = ['text', 'messages', 'commitMessage', ...messages.flatMap((_, index) => [`messages[${index}].content`, `messages[${index}].role`])];
  const otherError = error instanceof ApiError && error.isValidation && error.errorsExcept(formFields).length === 0 ? null : error;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="grid min-h-0 flex-1 overflow-y-auto md:grid-cols-2 md:overflow-hidden">
        <section aria-label="Edit the template" className="min-h-0 space-y-4 border-border-subtle p-4 md:overflow-y-auto md:border-r lg:p-5">
          <p className="text-small text-fg-secondary">
            Editing from version {source.version}
            {liveVersion === source.version ? ` (live)` : ''}. Values in {'{{ }}'} are filled in when the prompt is used.
          </p>
          {chat ? (
            messages.map((message, index) => (
              <TemplateInput
                key={index}
                id={`prompt-edit-message-${index}`}
                label={`${roleLabel(message.role)} message`}
                value={message.content}
                rows={index === 0 ? 14 : 5}
                errors={fieldErrors(error, `messages[${index}].content`) ?? fieldErrors(error, `messages[${index}].role`)}
                onChange={(content) => setMessages((current) => current.map((item, at) => (at === index ? { ...item, content } : item)))}
              />
            ))
          ) : (
            <TemplateInput id="prompt-edit-text" label="Template" value={text} rows={22} errors={fieldErrors(error, 'text') ?? fieldErrors(error, 'messages')} onChange={setText} />
          )}
          <Field label="What changed" htmlFor="prompt-edit-note" optional hint="Saved as the commit message in Langfuse, with your name." errors={fieldErrors(error, 'commitMessage')}>
            <input
              {...fieldAria('prompt-edit-note', fieldErrors(error, 'commitMessage'), true)}
              value={note}
              maxLength={NOTE_MAX}
              onChange={(event) => setNote(event.target.value)}
              placeholder="For example: shorter answers about returns"
              className="field"
            />
          </Field>
        </section>

        <section aria-label="Preview and variables" className="min-h-0 space-y-5 p-4 md:overflow-y-auto lg:p-5">
          <div className="space-y-2">
            <h3 className="text-small font-medium text-fg">Live preview</h3>
            {chat ? (
              <div className="space-y-3" data-testid="edit-preview">
                {messages.map((message, index) => (
                  <MessagePreview key={index} message={message} render={previews[index]} />
                ))}
              </div>
            ) : (
              <div data-testid="edit-preview">
                <TextPreview template={text} render={previews[0]} />
              </div>
            )}
          </div>
          <div className="space-y-2">
            <h3 className="text-small font-medium text-fg">Variables</h3>
            <VariablesPanel used={used} sampleKeys={sampleKeys} />
            <div className="flex justify-end">
              <Button variant="ghost" size="sm" icon={RotateCcw} onClick={() => setVariablesText(sample)} disabled={variablesText === sample}>
                Reset sample
              </Button>
            </div>
            <JsonEditor id="prompt-edit-variables" label="Sample variables" value={variablesText} onChange={setVariablesText} error={parsedVariables.ok ? null : parsedVariables.error} rows={10} />
          </div>
          <HandlebarsCheatSheet />
        </section>
      </div>

      <div className="shrink-0 space-y-2 border-t border-border-subtle px-4 py-3 lg:px-5">
        {otherError && !confirm ? <ErrorMessage error={otherError} title="Could not save the prompt" /> : null}
        <div className="flex flex-wrap items-center justify-end gap-2">
          <p className="mr-auto text-xs text-fg-muted">A draft is a new version in Langfuse. The application keeps using the live one until you publish.</p>
          <Button variant="ghost" onClick={() => (dirty ? setConfirm('discard') : onCancel())} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => submit(false)} loading={save.isPending && !confirm} disabled={!dirty || save.isPending}>
            Save as draft
          </Button>
          <Button variant="primary" onClick={() => setConfirm('publish')} disabled={!dirty || save.isPending}>
            Publish
          </Button>
        </div>
      </div>

      <ConfirmDialog
        open={confirm === 'publish'}
        title="Publish this version?"
        tone="primary"
        confirmLabel="Publish"
        busy={save.isPending}
        error={save.isError ? <ErrorMessage error={save.error} title="Could not publish" /> : null}
        message={
          <p>
            Your changes are saved as a new version and the <strong className="font-medium text-fg">{label}</strong> label moves to it. New answers use it at once.
            {liveVersion !== null ? ` You can roll back to version ${liveVersion} from Versions.` : ''}
          </p>
        }
        onConfirm={() => submit(true)}
        onCancel={() => setConfirm(null)}
      />
      <ConfirmDialog
        open={confirm === 'discard'}
        title="Discard your changes?"
        confirmLabel="Discard"
        message="Nothing is saved, and the prompt stays as it is."
        onConfirm={() => {
          setConfirm(null);
          onCancel();
        }}
        onCancel={() => setConfirm(null)}
      />
    </div>
  );
}
