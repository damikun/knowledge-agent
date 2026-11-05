import { useMutation, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { CircleCheck, CircleX, FileText, FileUp, Globe, LoaderCircle, PenLine, Upload, X, type LucideIcon } from 'lucide-react';
import { useId, useRef, useState, type DragEvent, type RefObject } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Field } from '../../components/ui/Field';
import { SideSheet } from '../../components/ui/SideSheet';
import { Switch } from '../../components/ui/Switch';
import { ApiError, errorMessage } from '../../lib/api';
import { formatBytes } from '../../lib/format';
import { createClientId } from '../../lib/ids';
import type { Guid } from '../../lib/types';
import { sourceKeys, sourcesApi } from './api';
import type { AddTextSourceRequest, SourceDto } from './types';
import { ACCEPT_ATTRIBUTE, MAX_UPLOAD_BYTES, validateUpload } from './uploads';
import { WebsiteForm } from './WebsiteForm';

export type AddSourceKind = 'url' | 'upload' | 'text';

/** The three ways to add knowledge, for the sheet's choice cards and the empty library. */
export const SOURCE_KINDS: ReadonlyArray<{ value: AddSourceKind; label: string; description: string; icon: LucideIcon }> = [
  { value: 'url', label: 'Website', description: 'A page or a whole site', icon: Globe },
  { value: 'upload', label: 'Files', description: 'PDF, TXT or Markdown', icon: FileUp },
  { value: 'text', label: 'Text', description: 'Paste or type it', icon: PenLine },
];

function fieldErrors(error: unknown, field: string): string[] | undefined {
  return error instanceof ApiError ? error.errorsFor(field) : undefined;
}

function Added({ source, fallback, onDismiss }: { source: SourceDto | undefined; fallback: string; onDismiss: () => void }) {
  return (
    <Alert tone="success" action={<Button variant="ghost" size="icon-sm" icon={X} aria-label="Dismiss" onClick={onDismiss} />}>
      Added “{source?.title || fallback}”. It is indexed in the background and shows up in the list right away.
    </Alert>
  );
}

interface UploadItem {
  id: string;
  file: File;
  status: 'queued' | 'uploading' | 'done' | 'error';
  error?: string;
}

function UploadStatusIcon({ status }: { status: UploadItem['status'] }) {
  if (status === 'done') {
    return <CircleCheck className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />;
  }
  if (status === 'error') {
    return <CircleX className="mt-0.5 size-4 shrink-0 text-danger" aria-hidden />;
  }
  return <LoaderCircle className={clsx('mt-0.5 size-4 shrink-0 text-fg-muted', status === 'uploading' && 'animate-spin')} aria-hidden />;
}

function UploadForm({ folderId }: { folderId: Guid | null }) {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [items, setItems] = useState<UploadItem[]>([]);

  const update = (id: string, patch: Partial<UploadItem>) => {
    setItems((current) => current.map((item) => (item.id === id ? { ...item, ...patch } : item)));
  };

  const uploadAll = async (queue: UploadItem[], targetFolderId: Guid | null) => {
    for (const item of queue) {
      update(item.id, { status: 'uploading' });
      try {
        await sourcesApi.uploadSource(item.file, { folderId: targetFolderId });
        update(item.id, { status: 'done' });
        void queryClient.invalidateQueries({ queryKey: sourceKeys.all });
      } catch (error) {
        update(item.id, { status: 'error', error: errorMessage(error) });
      }
    }
  };

  const addFiles = (files: FileList | File[]) => {
    const next: UploadItem[] = Array.from(files).map((file) => {
      const error = validateUpload(file);
      return error ? { id: createClientId(), file, status: 'error', error } : { id: createClientId(), file, status: 'queued' };
    });
    if (next.length === 0) {
      return;
    }
    setItems((current) => [...next, ...current]);
    void uploadAll(
      next.filter((item) => item.status === 'queued'),
      folderId,
    );
  };

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragging(false);
    if (event.dataTransfer?.files?.length) {
      addFiles(event.dataTransfer.files);
    }
  };

  const finished = items.filter((item) => item.status === 'done' || item.status === 'error');

  return (
    <div className="space-y-3">
      <div
        role="button"
        tabIndex={0}
        aria-label="Upload files: drop PDF, TXT or Markdown files here or press Enter to browse"
        onClick={() => inputRef.current?.click()}
        onKeyDown={(event) => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            inputRef.current?.click();
          }
        }}
        onDragEnter={(event) => {
          event.preventDefault();
          setDragging(true);
        }}
        onDragOver={(event) => {
          event.preventDefault();
          if (event.dataTransfer) {
            event.dataTransfer.dropEffect = 'copy';
          }
          setDragging(true);
        }}
        onDragLeave={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
            setDragging(false);
          }
        }}
        onDrop={onDrop}
        data-dragging={dragging || undefined}
        className={clsx(
          'flex cursor-pointer flex-col items-center justify-center gap-1.5 rounded-xl border border-dashed px-4 py-8 text-center transition-colors duration-100 ease-out',
          dragging ? 'border-accent bg-accent-subtle' : 'border-border-strong hover:bg-fill-hover',
        )}
      >
        <Upload className="size-5 text-fg-muted" aria-hidden />
        <p className="text-sm text-fg">
          Drop files here or <span className="font-medium underline underline-offset-2">browse</span>
        </p>
        <p className="text-xs text-fg-muted">PDF, TXT or Markdown, up to {formatBytes(MAX_UPLOAD_BYTES)} each</p>
      </div>
      <input
        ref={inputRef}
        type="file"
        multiple
        accept={ACCEPT_ATTRIBUTE}
        className="sr-only"
        tabIndex={-1}
        aria-label="Choose files to upload"
        data-testid="upload-input"
        onChange={(event) => {
          if (event.target.files?.length) {
            addFiles(event.target.files);
          }
          event.target.value = '';
        }}
      />
      {items.length > 0 ? (
        <div>
          <ul className="divide-y divide-border-subtle rounded-xl border border-border" aria-label="Uploads">
            {items.map((item) => (
              <li key={item.id} className="flex items-start gap-2 px-3 py-2 text-sm">
                <UploadStatusIcon status={item.status} />
                <div className="min-w-0 flex-1">
                  <div className="flex items-baseline justify-between gap-2">
                    <span className="truncate font-medium text-fg">{item.file.name}</span>
                    <span className="shrink-0 text-xs text-fg-muted tabular-nums">{formatBytes(item.file.size)}</span>
                  </div>
                  <div className={clsx('text-xs', item.status === 'error' ? 'text-danger-text' : 'text-fg-muted')}>
                    {item.status === 'queued'
                      ? 'Waiting…'
                      : item.status === 'uploading'
                        ? 'Uploading…'
                        : item.status === 'done'
                          ? 'Uploaded, ingestion started'
                          : item.error}
                  </div>
                </div>
              </li>
            ))}
          </ul>
          {finished.length > 0 ? (
            <Button
              size="sm"
              variant="ghost"
              className="mt-1.5"
              onClick={() => setItems((current) => current.filter((item) => item.status === 'queued' || item.status === 'uploading'))}
            >
              Clear finished
            </Button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function TextForm({ folderId }: { folderId: Guid | null }) {
  const queryClient = useQueryClient();
  const [title, setTitle] = useState('');
  const [content, setContent] = useState('');
  const [markdown, setMarkdown] = useState(false);
  const mutation = useMutation({
    mutationFn: (body: AddTextSourceRequest) => sourcesApi.addTextSource(body),
    onSuccess: () => {
      setTitle('');
      setContent('');
      void queryClient.invalidateQueries({ queryKey: sourceKeys.all });
    },
  });
  const titleErrors = fieldErrors(mutation.error, 'title');
  const contentErrors = fieldErrors(mutation.error, 'content');

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (title.trim() && content.trim()) {
          mutation.mutate({ title: title.trim(), content, format: markdown ? 'markdown' : 'text', folderId });
        }
      }}
    >
      <Field label="Title" htmlFor="source-text-title" errors={titleErrors}>
        <input
          {...fieldAria('source-text-title', titleErrors)}
          required
          placeholder="e.g. Meeting notes, 12 October"
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          className="field"
        />
      </Field>
      <Field label="Content" htmlFor="source-text-content" errors={contentErrors}>
        <textarea
          {...fieldAria('source-text-content', contentErrors)}
          required
          rows={10}
          placeholder={markdown ? '# Heading\n\nMarkdown content…' : 'Paste or type the text…'}
          value={content}
          onChange={(event) => setContent(event.target.value)}
          className={clsx('field resize-y', markdown && 'font-mono text-xs leading-relaxed')}
        />
      </Field>
      <Switch checked={markdown} onChange={setMarkdown} label="Content is Markdown" />
      {mutation.isError && !titleErrors?.length && !contentErrors?.length ? <ErrorMessage error={mutation.error} /> : null}
      {mutation.isSuccess ? <Added source={mutation.data} fallback={mutation.variables?.title || 'text'} onDismiss={() => mutation.reset()} /> : null}
      <Button type="submit" variant="primary" icon={FileText} loading={mutation.isPending} disabled={!title.trim() || !content.trim()}>
        Add text
      </Button>
    </form>
  );
}

function KindChoices({
  value,
  onChange,
  selectedRef,
}: {
  value: AddSourceKind;
  onChange: (kind: AddSourceKind) => void;
  selectedRef: RefObject<HTMLInputElement | null>;
}) {
  const name = useId();
  return (
    <fieldset>
      <legend className="sr-only">Source type</legend>
      <div className="grid grid-cols-3 gap-2">
        {SOURCE_KINDS.map(({ value: kind, label, description, icon: Icon }) => {
          const labelId = `${name}-${kind}-label`;
          const descriptionId = `${name}-${kind}-description`;
          return (
            <label key={kind} className="group relative cursor-pointer">
              <input
                ref={kind === value ? selectedRef : undefined}
                type="radio"
                name={name}
                value={kind}
                checked={kind === value}
                onChange={() => onChange(kind)}
                aria-labelledby={labelId}
                aria-describedby={descriptionId}
                className="peer sr-only"
              />
              <span className="flex h-full flex-col gap-2 rounded-xl border border-border bg-surface p-3 peer-checked:border-fg peer-checked:bg-fill-active peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-focus hover:border-border-hover forced-colors:peer-checked:outline-2 forced-colors:peer-checked:outline-[Highlight]">
                <Icon className="size-5 text-fg-muted group-has-checked:text-fg" aria-hidden />
                <span className="min-w-0">
                  <span id={labelId} className="block text-sm font-medium text-fg">
                    {label}
                  </span>
                  <span id={descriptionId} className="mt-0.5 block text-xs text-fg-secondary">
                    {description}
                  </span>
                </span>
              </span>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}

export interface AddSourceSheetProps {
  open: boolean;
  /** The choice the sheet opens with. */
  kind: AddSourceKind;
  onKindChange: (kind: AddSourceKind) => void;
  onClose: () => void;
  // New sources go into this folder; null leaves them unfiled.
  folder?: { id: Guid; label: string } | null;
}

export function AddSourceSheet({ open, kind, onKindChange, onClose, folder = null }: AddSourceSheetProps) {
  const selectedRef = useRef<HTMLInputElement>(null);
  const folderId = folder?.id ?? null;
  return (
    <SideSheet
      open={open}
      onClose={onClose}
      title="Add a source"
      initialFocus={selectedRef}
      description={
        folder ? (
          <>
            New sources go into <span className="font-medium text-fg">{folder.label}</span>.
          </>
        ) : (
          'Your agents can search it as soon as it is indexed.'
        )
      }
    >
      <div className="space-y-6">
        <KindChoices value={kind} onChange={onKindChange} selectedRef={selectedRef} />
        {kind === 'url' ? (
          <WebsiteForm folderId={folderId} />
        ) : kind === 'upload' ? (
          <UploadForm folderId={folderId} />
        ) : (
          <TextForm folderId={folderId} />
        )}
      </div>
    </SideSheet>
  );
}
