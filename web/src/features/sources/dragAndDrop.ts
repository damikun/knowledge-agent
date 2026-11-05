import { useState, type DragEvent } from 'react';
import type { Guid } from '../../lib/types';
import type { SourceListParams } from './types';

export type DragPayload =
  | { kind: 'sources'; ids: Guid[] }
  | { kind: 'matching'; params: SourceListParams; count: number }
  | { kind: 'folder'; id: Guid };

export interface FolderDrop {
  // A null folder is Unfiled for sources and the top level for folders.
  canDrop: (payload: DragPayload, folderId: Guid | null) => boolean;
  drop: (payload: DragPayload, folderId: Guid | null) => void;
}

const PAYLOAD_TYPE = 'application/x-knowledge-agent';

// Browsers reveal dragged data only on drop, so targets check the payload kept here while the drag is under way.
let current: DragPayload | null = null;

export function startDrag(event: DragEvent, payload: DragPayload, label: string): void {
  current = payload;
  event.dataTransfer.setData(PAYLOAD_TYPE, JSON.stringify(payload));
  event.dataTransfer.setData('text/plain', label);
  event.dataTransfer.effectAllowed = 'move';
  showDragLabel(event.dataTransfer, label);
}

export function endDrag(): void {
  current = null;
}

function draggedPayload(dataTransfer: DataTransfer | null): DragPayload | null {
  if (!Array.from(dataTransfer?.types ?? []).includes(PAYLOAD_TYPE)) {
    return null;
  }
  if (current) {
    return current;
  }
  try {
    return JSON.parse(dataTransfer?.getData(PAYLOAD_TYPE) || 'null') as DragPayload | null;
  } catch {
    return null;
  }
}

// A small pill instead of the browser's snapshot of a whole table row.
function showDragLabel(dataTransfer: DataTransfer, label: string): void {
  if (typeof dataTransfer.setDragImage !== 'function') {
    return;
  }
  const pill = document.createElement('div');
  pill.textContent = label;
  pill.className =
    'fixed -top-96 left-0 max-w-64 truncate rounded-full border border-border bg-raised px-3 py-1 text-xs font-medium text-fg shadow-menu';
  document.body.append(pill);
  dataTransfer.setDragImage(pill, 14, 14);
  setTimeout(() => pill.remove());
}

export function useDropTarget(folderId: Guid | null, target: FolderDrop, accepts: (payload: DragPayload) => boolean = () => true) {
  const [over, setOver] = useState(false);
  const allowed = (payload: DragPayload | null): payload is DragPayload =>
    payload !== null && accepts(payload) && target.canDrop(payload, folderId);

  return {
    dropping: over,
    dropHandlers: {
      onDragOver: (event: DragEvent<HTMLElement>) => {
        if (allowed(draggedPayload(event.dataTransfer))) {
          event.preventDefault();
          event.dataTransfer.dropEffect = 'move';
          setOver(true);
        }
      },
      onDragLeave: (event: DragEvent<HTMLElement>) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setOver(false);
        }
      },
      onDrop: (event: DragEvent<HTMLElement>) => {
        setOver(false);
        const payload = draggedPayload(event.dataTransfer);
        if (allowed(payload)) {
          event.preventDefault();
          endDrag();
          target.drop(payload, folderId);
        }
      },
    },
  };
}
