import { formatBytes } from '../../lib/format';

/** Mirrors Ingestion:MaxUploadBytes on the API. */
export const MAX_UPLOAD_BYTES = 25 * 1024 * 1024;

const ACCEPTED_EXTENSIONS = ['.pdf', '.txt', '.md', '.markdown'] as const;

export const ACCEPT_ATTRIBUTE = [...ACCEPTED_EXTENSIONS, 'application/pdf', 'text/plain', 'text/markdown'].join(',');

function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot >= 0 ? name.slice(dot).toLowerCase() : '';
}

export function validateUpload(file: Pick<File, 'name' | 'size'>): string | null {
  if (!(ACCEPTED_EXTENSIONS as readonly string[]).includes(extensionOf(file.name))) {
    return 'Unsupported file type. Upload PDF, TXT or Markdown files.';
  }
  if (file.size === 0) {
    return 'The file is empty.';
  }
  if (file.size > MAX_UPLOAD_BYTES) {
    return `The file is ${formatBytes(file.size)}; the limit is ${formatBytes(MAX_UPLOAD_BYTES)}.`;
  }
  return null;
}
