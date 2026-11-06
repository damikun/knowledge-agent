import { clsx } from 'clsx';
import { ArrowLeft, ExternalLink, Pencil, RotateCcw } from 'lucide-react';
import { useId, useMemo, useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Button, buttonClass } from '../../components/ui/Button';
import { EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { TabPanel, Tabs, type TabItem } from '../../components/ui/Tabs';
import { ApiError } from '../../lib/api';
import { safeExternalUrl } from '../../lib/format';
import { useDebouncedValue } from '../../lib/hooks/useDebouncedValue';
import { usePrompt, useRenderedPrompt } from './api';
import { promptInfo } from './catalog';
import { formatJson, parseVariables } from './json';
import { JsonEditor } from './JsonEditor';
import { PromptEditPanel, type EditSource } from './PromptEditPanel';
import { PromptVersions } from './PromptVersions';
import { RenderedOutput, roleLabel } from './RenderedOutput';
import { sampleVariables } from './samples';
import { TemplateView } from './TemplateView';
import type { PromptDetailDto, RenderedPromptDto } from './types';
import { ScrollBox } from '../../components/ui/ScrollBox';

function OriginBadge({ origin }: { origin: string }) {
  const langfuse = origin?.toLowerCase() === 'langfuse';
  return (
    <Badge
      tone={langfuse ? 'accent' : 'neutral'}
      title={langfuse ? 'Stored in Langfuse' : 'Built-in default (Langfuse unavailable or not configured)'}
    >
      {langfuse ? 'Langfuse' : 'Built-in'}
    </Badge>
  );
}

function ModelSettingsSummary({ result }: { result: RenderedPromptDto }) {
  const override = result.modelOverride;
  const parts = override
    ? [
        override.provider && override.provider !== 'auto' ? `provider ${override.provider}` : null,
        override.model ? `model ${override.model}` : null,
        override.reasoningEffort ? `effort ${override.reasoningEffort}` : null,
        override.maxOutputTokens != null ? `max ${override.maxOutputTokens} tokens` : null,
        override.temperature != null ? `temperature ${override.temperature}` : null,
      ].filter(Boolean)
    : [];
  if (!result.profile && parts.length === 0) {
    return null;
  }
  return (
    <p className="text-xs text-fg-muted">
      Prompt config:{result.profile ? ` profile ${result.profile}` : ''}
      {result.profile && parts.length > 0 ? ' ·' : ''}
      {parts.length > 0 ? ` ${parts.join(', ')}` : ''}
    </p>
  );
}

export type DetailTab = 'template' | 'preview' | 'variables' | 'versions';

const DETAIL_TABS: TabItem<DetailTab>[] = [
  { id: 'template', label: 'Template' },
  { id: 'preview', label: 'Live preview' },
  { id: 'variables', label: 'Variables and config' },
];

// Versions exist only in Langfuse; built-in defaults have just one.
const LANGFUSE_TABS: TabItem<DetailTab>[] = [...DETAIL_TABS, { id: 'versions', label: 'Versions' }];

interface EditState {
  name: string;
  source: EditSource;
  liveVersion: number | null;
  label: string;
}

/** Re-rendered whenever typing pauses. */
function PromptPreview({ detail }: { detail: PromptDetailDto }) {
  const sample = useMemo(() => formatJson(sampleVariables(detail.name, detail.variables ?? [])), [detail.name, detail.variables]);
  const [text, setText] = useState(sample);
  const parsed = parseVariables(text);
  const settled = useDebouncedValue(text);
  const settledVariables = parseVariables(settled);
  const render = useRenderedPrompt(detail.name, detail.version, settled, settledVariables.ok ? settledVariables.value : null);

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
        <p className="min-w-0 flex-1 basis-64 text-small text-fg-secondary">
          What the model receives, rendered on the API with sample variables. Change them to see the result follow.
        </p>
        <Button variant="ghost" size="sm" icon={RotateCcw} onClick={() => setText(sample)} disabled={text === sample}>
          Reset sample
        </Button>
      </div>
      <div className="@container">
        <div className="grid gap-5 @xl:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
          <JsonEditor
            id="prompt-variables"
            label="Sample variables"
            value={text}
            onChange={setText}
            error={parsed.ok ? null : parsed.error}
            rows={16}
          />
          <div className="min-w-0 space-y-1.5">
            <div className="flex h-5 items-center justify-between gap-2">
              <span className="text-small font-medium text-fg">Result</span>
              {render.isFetching ? <Spinner label="Rendering" className="size-3.5" /> : null}
            </div>
            {render.isError ? <ErrorMessage error={render.error} title="Rendering failed" /> : null}
            {render.data ? (
              <div className="space-y-1.5">
                <RenderedOutput text={render.data.text} messages={render.data.messages} />
                <ModelSettingsSummary result={render.data} />
              </div>
            ) : null}
          </div>
        </div>
      </div>
    </div>
  );
}

function isChat(detail: PromptDetailDto): boolean {
  return detail.type?.toLowerCase() === 'chat' || (detail.text === null && detail.messages.length > 0);
}

function PromptTemplate({ detail }: { detail: PromptDetailDto }) {
  const chat = isChat(detail);
  return (
    <div className="flex h-full min-h-0 flex-col gap-2">
      <p className="shrink-0 text-xs text-fg-muted">
        {chat
          ? 'A chat prompt: one template for each message. Values in {{ }} are filled in when it is used.'
          : 'Values in {{ }} are filled in when the prompt is used.'}
      </p>
      {chat ? (
        <ol className="min-h-0 flex-1 space-y-3 overflow-y-auto">
          {detail.messages.map((message, index) => (
            <li key={index}>
              <p className="mb-1 text-xs font-medium text-fg-muted">{roleLabel(message.role)}</p>
              <TemplateView template={message.content} label={`${message.role} message template`} />
            </li>
          ))}
        </ol>
      ) : (
        <TemplateView template={detail.text ?? ''} label="Prompt template" className="min-h-0" />
      )}
    </div>
  );
}

function PromptVariables({ detail }: { detail: PromptDetailDto }) {
  const configText = detail.config && Object.keys(detail.config).length > 0 ? formatJson(detail.config) : null;
  return (
    <div className="grid max-w-4xl gap-6 @container sm:grid-cols-2">
      <div className="space-y-1.5">
        <h3 className="text-small font-medium text-fg">Variables used</h3>
        {detail.variables.length > 0 ? (
          <ul className="flex flex-wrap gap-1.5" aria-label="Detected variables">
            {detail.variables.map((variable) => (
              <li key={variable}>
                <code className="rounded-sm bg-accent-subtle px-1.5 py-0.5 font-mono text-xs text-accent-text">{variable}</code>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-small text-fg-muted">No variables detected.</p>
        )}
      </div>
      <div className="min-w-0 space-y-1.5">
        <h3 className="text-small font-medium text-fg">Config</h3>
        {configText ? (
          <ScrollBox
            as="pre"
            className="overflow-x-auto rounded-lg border border-border-subtle bg-surface-subtle px-3 py-2 font-mono text-xs leading-relaxed text-fg-secondary"
          >
            {configText}
          </ScrollBox>
        ) : (
          <p className="text-small text-fg-muted">No config. Model settings come from the profile.</p>
        )}
      </div>
    </div>
  );
}

function BackLink({ to }: { to: string }) {
  return (
    <Link
      to={to}
      aria-label="All prompts"
      title="All prompts"
      className={clsx(buttonClass({ variant: 'ghost', size: 'icon-sm' }), '-ml-1.5 shrink-0')}
    >
      <ArrowLeft className="size-4" aria-hidden />
    </Link>
  );
}

// Mirrors the loaded layout to avoid a jump.
function LoadingPrompt({ backTo }: { backTo?: string | undefined }) {
  return (
    <div role="status" className="flex h-full flex-col">
      <span className="sr-only">Loading prompt</span>
      <div className="shrink-0 border-b border-border-subtle px-4 pt-3 lg:px-5">
        <div className="flex items-center gap-2">
          {backTo ? <BackLink to={backTo} /> : null}
          <Skeleton className="h-4 w-44" />
          <Skeleton className="h-5 w-16" />
          <Skeleton className="h-5 w-10" />
        </div>
        <Skeleton className="mt-2.5 h-3 w-96 max-w-full" />
        <div className="flex h-10 items-center gap-5">
          <Skeleton className="h-3.5 w-16" />
          <Skeleton className="h-3.5 w-20" />
          <Skeleton className="h-3.5 w-28" />
        </div>
      </div>
      <div className="min-h-0 flex-1 p-4 lg:p-5">
        <Skeleton shape="card" className="h-full" />
      </div>
    </div>
  );
}

export interface PromptDetailProps {
  name: string;
  tab: DetailTab;
  onTabChange: (tab: DetailTab) => void;
  /** Only on narrow windows, where the list isn't beside the detail. */
  backTo?: string | undefined;
}

export function PromptDetail({ name, tab: requestedTab, onTabChange, backTo }: PromptDetailProps) {
  const titleId = useId();
  const prompt = usePrompt(name);
  const [editState, setEditState] = useState<EditState | null>(null);
  // Choosing another prompt leaves edit mode.
  const editing = editState?.name === name ? editState : null;

  if (prompt.isPending) {
    return <LoadingPrompt backTo={backTo} />;
  }
  if (prompt.isError) {
    return (
      <div className="p-4 lg:p-5">
        {backTo ? <BackLink to={backTo} /> : null}
        {prompt.error instanceof ApiError && prompt.error.status === 404 ? (
          <EmptyState title="Prompt not found" headingLevel={2} description={`There is no prompt named “${name}”.`} />
        ) : (
          <ErrorMessage error={prompt.error} title="Could not load the prompt" />
        )}
      </div>
    );
  }

  const detail = prompt.data;
  const info = promptInfo(detail.name);
  const externalUrl = safeExternalUrl(detail.externalUrl);
  const chat = detail.type?.toLowerCase() === 'chat';
  const inLangfuse = detail.origin?.toLowerCase() === 'langfuse';
  const tabs = inLangfuse ? LANGFUSE_TABS : DETAIL_TABS;
  const tab = tabs.some((item) => item.id === requestedTab) ? requestedTab : 'template';
  const startEdit = () =>
    setEditState({
      name,
      source: { version: detail.version, type: chat ? 'chat' : 'text', text: detail.text, messages: detail.messages },
      liveVersion: detail.version,
      label: detail.labels.includes('production') ? 'production' : (detail.labels.find((label) => label !== 'latest') ?? 'production'),
    });

  // Panels stay mounted while hidden, so sample variables survive a tab switch.
  const panel = (id: DetailTab, children: ReactNode, className = 'h-full overflow-y-auto p-4 lg:p-5') => (
    <div hidden={tab !== id} className="h-full">
      <TabPanel idPrefix="prompt-detail" id={id} className={clsx('rounded-none', className)}>
        {children}
      </TabPanel>
    </div>
  );

  return (
    <section aria-labelledby={titleId} className="flex h-full min-h-0 flex-col">
      <div className="shrink-0 border-b border-border-subtle px-4 pt-3 lg:px-5">
        <div className="flex items-start gap-3">
          {backTo ? <BackLink to={backTo} /> : null}
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <h2 id={titleId} className="min-w-0 truncate text-section text-fg">
                {info.title}
              </h2>
              <OriginBadge origin={detail.origin} />
              <Badge>v{detail.version}</Badge>
              <Badge>{chat ? 'Chat prompt' : 'Text prompt'}</Badge>
              {detail.labels.map((label) => (
                <Badge key={label}>{label}</Badge>
              ))}
              {editing ? <Badge tone="warning">Editing</Badge> : null}
            </div>
            <p className="mt-0.5 truncate text-small text-fg-secondary" title={info.description}>
              <code className="font-mono text-xs text-fg-muted">{detail.name}</code>
              <span aria-hidden className="mx-1.5 text-fg-muted">
                ·
              </span>
              {info.description}
            </p>
          </div>
          <div className="flex shrink-0 flex-wrap items-center justify-end gap-1">
            {externalUrl ? (
              <a href={externalUrl} target="_blank" rel="noopener noreferrer" className={buttonClass({ variant: 'ghost', size: 'sm' })}>
                Edit in Langfuse
                <ExternalLink className="size-3.5" aria-hidden />
              </a>
            ) : null}
            {inLangfuse && !editing ? (
              <Button size="sm" icon={Pencil} onClick={startEdit}>
                Edit
              </Button>
            ) : null}
          </div>
        </div>
        {editing ? (
          <div className="h-3" />
        ) : (
          <Tabs idPrefix="prompt-detail" items={tabs} value={tab} onChange={onTabChange} label="Prompt details" className="mt-1.5 -mb-px" />
        )}
      </div>
      <div className="min-h-0 flex-1">
        {editing ? (
          <PromptEditPanel
            key={`${editing.source.version}`}
            name={name}
            source={editing.source}
            liveVersion={editing.liveVersion}
            label={editing.label}
            variables={detail.variables ?? []}
            onCancel={() => setEditState(null)}
            onDone={(outcome) => {
              setEditState(null);
              onTabChange(outcome === 'draft' ? 'versions' : 'template');
            }}
          />
        ) : (
          <>
            {panel('template', <PromptTemplate detail={detail} />, 'h-full p-4 lg:p-5')}
            {panel('preview', <PromptPreview key={`${detail.name}:${detail.version}`} detail={detail} />)}
            {panel('variables', <PromptVariables detail={detail} />)}
            {inLangfuse && tab === 'versions'
              ? panel(
                  'versions',
                  <PromptVersions
                    name={name}
                    onEdit={(version, liveVersion, label) =>
                      setEditState({
                        name,
                        source: { version: version.version, type: version.type, text: version.text, messages: version.messages },
                        liveVersion,
                        label,
                      })
                    }
                  />,
                )
              : null}
          </>
        )}
      </div>
    </section>
  );
}
