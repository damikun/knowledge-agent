import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { ImagePlus, Trash2 } from 'lucide-react';
import { useRef, useState } from 'react';
import { Button } from '../../components/ui/Button';
import { ErrorMessage } from '../../components/ui/feedback';
import type { Guid } from '../../lib/types';
import { mediaUrl, widgetsApi } from './api';
import { IMAGE_TYPES, MAX_IMAGE_BYTES } from './widgetForm';

export interface ImageUploadProps {
  id: string;
  label: string;
  /** Null until the widget is saved; uploads belong to a widget. */
  widgetId: Guid | null;
  assetId: Guid | null;
  onChange: (assetId: Guid | null) => void;
  hint: string;
  round?: boolean;
}

function checkFile(file: File): string | null {
  if (!IMAGE_TYPES.includes(file.type)) {
    return 'Choose a PNG, JPEG or WebP image.';
  }
  return file.size > MAX_IMAGE_BYTES ? 'The image must be at most 512 KB.' : null;
}

export function ImageUpload({ id, label, widgetId, assetId, onChange, hint, round = false }: ImageUploadProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const upload = useMutation({
    mutationFn: (file: File) => widgetsApi.uploadAsset(widgetId!, file),
    onSuccess: (asset) => onChange(asset.id),
  });

  const pick = (file: File | undefined) => {
    upload.reset();
    if (!file) {
      return;
    }
    const problem = checkFile(file);
    setFileError(problem);
    if (!problem) {
      upload.mutate(file);
    }
  };

  const describedBy = fileError ? `${id}-error` : `${id}-hint`;
  const noun = label.toLowerCase();
  return (
    <div role="group" aria-labelledby={`${id}-label`} aria-describedby={describedBy} className="space-y-1.5">
      <div id={`${id}-label`} className="text-small font-medium text-fg">
        {label}
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <div
          className={clsx(
            'flex size-12 shrink-0 items-center justify-center overflow-hidden border border-border bg-surface-subtle',
            round ? 'rounded-full' : 'rounded-xl',
          )}
        >
          {assetId ? (
            <img src={mediaUrl(assetId)} alt={`Current ${noun}`} className="size-full object-cover" />
          ) : (
            <ImagePlus className="size-4 text-fg-muted" aria-hidden />
          )}
        </div>
        <input
          ref={inputRef}
          id={id}
          type="file"
          accept={IMAGE_TYPES.join(',')}
          aria-label={`${label} file`}
          hidden
          onChange={(event) => {
            pick(event.target.files?.[0]);
            event.target.value = '';
          }}
        />
        <Button size="sm" icon={ImagePlus} loading={upload.isPending} disabled={!widgetId} onClick={() => inputRef.current?.click()}>
          {assetId ? `Replace ${noun}` : `Upload ${noun}`}
        </Button>
        {assetId ? (
          <Button size="sm" variant="ghost" icon={Trash2} onClick={() => onChange(null)}>
            Remove {noun}
          </Button>
        ) : null}
      </div>
      {fileError ? (
        <p id={`${id}-error`} className="text-xs text-danger-text">
          {fileError}
        </p>
      ) : (
        <p id={`${id}-hint`} className="text-xs text-fg-muted">
          {widgetId ? hint : 'Save the widget to upload images.'}
        </p>
      )}
      {upload.isError ? <ErrorMessage error={upload.error} title="The image was not uploaded" /> : null}
    </div>
  );
}
