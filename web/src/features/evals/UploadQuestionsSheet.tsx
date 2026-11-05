import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { CircleAlert, CircleCheck, Download, FileUp, RotateCcw } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type DragEvent } from 'react';
import { Badge } from '../../components/ui/Badge';
import { buttonClass, Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Select } from '../../components/ui/Select';
import { SideSheet } from '../../components/ui/SideSheet';
import { Table, TableCell, TableHead, TableHeader, TableRow } from '../../components/ui/Table';
import { ApiError } from '../../lib/api';
import { formatBytes, formatNumber, pluralize } from '../../lib/format';
import { evalsApi } from './api';
import { expectedPreview } from './datasetItems';
import {
  CSV_TEMPLATE,
  formatOf,
  guessMapping,
  JSONL_TEMPLATE,
  MAX_UPLOAD_BYTES,
  MAX_UPLOAD_ROWS,
  parseFile,
  readRows,
  UPLOAD_FIELD_LABELS,
  type ParsedFile,
  type UploadField,
  type UploadRow,
} from './questionUpload';
import type { ImportEvalItemsResultDto } from './types';

const PREVIEW_ROWS = 50;
const FIELD_OPTIONS = (Object.keys(UPLOAD_FIELD_LABELS) as UploadField[]).map((value) => ({ value, label: UPLOAD_FIELD_LABELS[value] }));

function TemplateLink({ name, content, type }: { name: string; content: string; type: string }) {
  const [href, setHref] = useState<string | null>(null);
  useEffect(() => {
    // jsdom has no URL.createObjectURL.
    if (typeof URL.createObjectURL !== 'function') {
      return undefined;
    }
    const url = URL.createObjectURL(new Blob([content], { type }));
    setHref(url);
    return () => URL.revokeObjectURL(url);
  }, [content, type]);
  return (
    <a href={href ?? undefined} download={name} className={buttonClass({ variant: 'ghost', size: 'sm' })}>
      <Download className="size-3.5" aria-hidden />
      {name.endsWith('.csv') ? 'CSV template' : 'JSONL template'}
    </a>
  );
}

/** Server errors are keyed items[i].field, where i indexes the rows sent, not the file's rows. */
function serverRowErrors(error: unknown, sent: readonly UploadRow[]): Map<number, string[]> {
  const byLine = new Map<number, string[]>();
  if (!(error instanceof ApiError)) {
    return byLine;
  }
  for (const [key, messages] of Object.entries(error.fieldErrors)) {
    const match = /^items\[(\d+)\]/i.exec(key.replace(/^\$\.?/, ''));
    const row = match ? sent[Number(match[1])] : undefined;
    if (row) {
      byLine.set(row.line, [...(byLine.get(row.line) ?? []), ...messages]);
    }
  }
  return byLine;
}

export interface UploadQuestionsSheetProps {
  datasetId: string;
  /** duplicateKey values of the dataset's questions. */
  existing: ReadonlySet<string>;
  onClose: () => void;
  onAdded: (result: ImportEvalItemsResultDto) => void;
}

export function UploadQuestionsSheet({ datasetId, existing, onClose, onAdded }: UploadQuestionsSheetProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [file, setFile] = useState<{ name: string; size: number; parsed: ParsedFile } | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [mapping, setMapping] = useState<UploadField[]>([]);

  const rows = useMemo(() => (file && !file.parsed.error ? readRows(file.parsed, mapping, existing) : []), [file, mapping, existing]);
  const ready = rows.filter((row) => row.request && !row.duplicate);
  const duplicates = rows.filter((row) => row.duplicate).length;
  const broken = rows.filter((row) => !row.request).length;
  const mapsQuestion = file?.parsed.format !== 'csv' || mapping.includes('question');

  const upload = useMutation({
    mutationFn: () => evalsApi.importItems(datasetId, ready.map((row) => row.request!)),
    onSuccess: (result) => {
      onAdded(result);
      onClose();
    },
  });
  const serverErrors = serverRowErrors(upload.error, ready);
  const otherError = upload.isError && serverErrors.size === 0;
  // Problem rows first, so a truncated preview still shows them.
  const previewRows = [
    ...rows.filter((row) => !row.request || serverErrors.has(row.line)),
    ...rows.filter((row) => row.request && !serverErrors.has(row.line)),
  ].slice(0, PREVIEW_ROWS);

  const read = async (chosen: File) => {
    upload.reset();
    setFile(null);
    const format = formatOf(chosen.name);
    if (!format) {
      setFileError('Choose a .csv or .jsonl file.');
      return;
    }
    if (chosen.size > MAX_UPLOAD_BYTES) {
      setFileError(`The file is ${formatBytes(chosen.size)}; at most ${formatBytes(MAX_UPLOAD_BYTES)} fit in one upload.`);
      return;
    }
    const parsed = parseFile(await chosen.text(), format);
    setFileError(parsed.error);
    setMapping(guessMapping(parsed.columns));
    setFile({ name: chosen.name, size: chosen.size, parsed });
  };

  const onDrop = (event: DragEvent) => {
    event.preventDefault();
    setDragging(false);
    const dropped = event.dataTransfer?.files?.[0];
    if (dropped) {
      void read(dropped);
    }
  };

  return (
    <SideSheet
      open
      onClose={onClose}
      size="lg"
      title="Upload questions"
      description={`A CSV or JSONL file with up to ${formatNumber(MAX_UPLOAD_ROWS)} questions. Questions the dataset already has are left out.`}
      footer={
        <>
          <Button onClick={onClose} disabled={upload.isPending}>
            Cancel
          </Button>
          <Button variant="primary" icon={FileUp} loading={upload.isPending} disabled={ready.length === 0 || !mapsQuestion} onClick={() => upload.mutate()}>
            {ready.length > 1 ? `Add ${formatNumber(ready.length)} questions` : 'Add question'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-1">
          <span className="mr-1 text-small text-fg-secondary">Start from a template:</span>
          <TemplateLink name="questions-template.csv" content={CSV_TEMPLATE} type="text/csv" />
          <TemplateLink name="questions-template.jsonl" content={JSONL_TEMPLATE} type="application/x-ndjson" />
        </div>

        {file ? (
          <div className="flex items-center justify-between gap-3 rounded-xl border border-border px-3 py-2">
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-fg">{file.name}</p>
              <p className="text-xs text-fg-muted">
                {formatBytes(file.size)} · {file.parsed.format === 'csv' ? 'CSV' : 'JSONL'}
              </p>
            </div>
            <Button size="sm" variant="ghost" icon={RotateCcw} onClick={() => inputRef.current?.click()}>
              Choose another file
            </Button>
          </div>
        ) : (
          <div
            role="button"
            tabIndex={0}
            aria-label="Choose a file: drop a CSV or JSONL file here or press Enter to browse"
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
              setDragging(true);
            }}
            onDragLeave={(event) => {
              if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
                setDragging(false);
              }
            }}
            onDrop={onDrop}
            className={clsx(
              'flex cursor-pointer flex-col items-center justify-center gap-1.5 rounded-xl border border-dashed px-4 py-8 text-center transition-colors duration-100 ease-out',
              dragging ? 'border-accent bg-accent-subtle' : 'border-border-strong hover:bg-fill-hover',
            )}
          >
            <FileUp className="size-5 text-fg-muted" aria-hidden />
            <p className="text-sm text-fg">
              Drop a file here or <span className="font-medium underline underline-offset-2">browse</span>
            </p>
            <p className="text-xs text-fg-muted">CSV with a header row, or JSONL with one question per line</p>
          </div>
        )}
        <input
          ref={inputRef}
          type="file"
          accept=".csv,.tsv,.txt,.jsonl,.ndjson,.json,text/csv,application/json"
          className="sr-only"
          tabIndex={-1}
          aria-label="Choose a file of questions"
          data-testid="questions-file"
          onChange={(event) => {
            const chosen = event.target.files?.[0];
            if (chosen) {
              void read(chosen);
            }
            event.target.value = '';
          }}
        />

        {fileError ? (
          <Alert tone="danger" title="The file cannot be used" live>
            {fileError}
          </Alert>
        ) : null}

        {file && file.parsed.format === 'csv' && !file.parsed.error ? (
          <fieldset className="space-y-2">
            <legend className="text-sm font-semibold text-fg">Columns</legend>
            <p className="text-xs text-fg-muted">Say what each column holds. Separate several facts, sources or tags in a cell with a semicolon.</p>
            <ul className="grid gap-2 sm:grid-cols-2">
              {file.parsed.columns.map((column, index) => (
                <li key={`${column}-${index}`} className="flex min-w-0 items-center gap-2">
                  <span className="w-28 shrink-0 truncate text-small text-fg-secondary" title={column}>
                    {column || `Column ${index + 1}`}
                  </span>
                  <Select
                    size="sm"
                    aria-label={`What the column “${column || index + 1}” holds`}
                    value={mapping[index] ?? 'ignore'}
                    options={FIELD_OPTIONS}
                    onChange={(field) =>
                      // A field maps to one column, so choosing it here clears it elsewhere.
                      setMapping((current) =>
                        current.map((value, at) => (at === index ? field : field !== 'ignore' && value === field ? 'ignore' : value)),
                      )
                    }
                    className="min-w-0 flex-1"
                  />
                </li>
              ))}
            </ul>
            {!mapsQuestion ? <p className="text-xs text-danger-text">Choose the column that holds the question.</p> : null}
          </fieldset>
        ) : null}

        {rows.length > 0 && mapsQuestion ? (
          <section aria-labelledby="upload-preview-heading" className="space-y-2">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <h3 id="upload-preview-heading" className="text-sm font-semibold text-fg">
                Preview
              </h3>
              <p className="text-small text-fg-secondary" aria-live="polite">
                {formatNumber(ready.length)} ready
                {duplicates > 0 ? ` · ${formatNumber(duplicates)} already in the dataset` : ''}
                {broken > 0 ? ` · ${pluralize(broken, 'row')} with problems` : ''}
              </p>
            </div>
            {broken > 0 ? <p className="text-xs text-fg-muted">Rows with problems are left out. Fix them in the file and choose it again to add them.</p> : null}
            {serverErrors.size > 0 ? (
              <Alert tone="danger" title="Nothing was added" live>
                Some rows were refused. They are marked below.
              </Alert>
            ) : null}
            <div className="overflow-hidden rounded-xl border border-border">
              <Table aria-label="Questions in the file">
                <TableHead>
                  <TableHeader numeric>Row</TableHeader>
                  <TableHeader>Question</TableHeader>
                  <TableHeader>Status</TableHeader>
                </TableHead>
                <tbody>
                  {previewRows.map((row) => {
                    const problems = [...row.errors, ...(serverErrors.get(row.line) ?? [])];
                    return (
                      <TableRow key={row.line} data-row={row.line}>
                        <TableCell numeric className="align-top">
                          {row.line}
                        </TableCell>
                        <TableCell className="max-w-0 align-top whitespace-normal">
                          <p className="line-clamp-2 break-words text-fg">{row.request?.question || <span className="text-fg-muted">No question</span>}</p>
                          {row.request ? (
                            <p className="line-clamp-1 text-xs text-fg-muted">
                              {expectedPreview({
                                answerable: row.request.answerable ?? true,
                                referenceAnswer: row.request.referenceAnswer ?? null,
                                expectedFacts: row.request.expectedFacts ?? [],
                              })}
                            </p>
                          ) : null}
                        </TableCell>
                        <TableCell className="align-top whitespace-normal">
                          {problems.length > 0 ? (
                            <span className="flex items-start gap-1 text-xs text-danger-text">
                              <CircleAlert className="mt-px size-3.5 shrink-0" aria-hidden />
                              {problems.join(' ')}
                            </span>
                          ) : row.duplicate ? (
                            <Badge>Already in the dataset</Badge>
                          ) : (
                            <span className="inline-flex items-center gap-1 text-xs text-success-text">
                              <CircleCheck className="size-3.5" aria-hidden />
                              Ready
                            </span>
                          )}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </tbody>
              </Table>
              {rows.length > PREVIEW_ROWS ? (
                <p className="border-t border-border-subtle px-3 py-2 text-xs text-fg-muted">
                  Showing {PREVIEW_ROWS} of {formatNumber(rows.length)} rows, those with problems first. Every row is checked.
                </p>
              ) : null}
            </div>
          </section>
        ) : null}
        {otherError ? <ErrorMessage error={upload.error} title="The questions were not added" /> : null}
      </div>
    </SideSheet>
  );
}
