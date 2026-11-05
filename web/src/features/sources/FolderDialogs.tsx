import { useMutation, useQueryClient } from '@tanstack/react-query';
import { FolderInput, FolderPlus, Pencil, type LucideIcon } from 'lucide-react';
import { useId, useRef, useState, type FormEvent, type ReactNode, type RefObject } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { ErrorMessage } from '../../components/ui/feedback';
import { Field } from '../../components/ui/Field';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { ApiError } from '../../lib/api';
import { formatNumber, pluralize } from '../../lib/format';
import type { Guid } from '../../lib/types';
import { sourceKeys, sourcesApi } from './api';
import { FolderPicker } from './FolderPicker';
import { flattenTree, MAX_FOLDER_DEPTH, MAX_FOLDER_NAME_LENGTH, moveFolderBlocker, newFolderBlocker, type FolderNode } from './folders';
import type { SourceDto, SourceFolderDto, SourceFolderListDto, SourceListParams } from './types';

function fieldErrors(error: unknown, field: string): string[] | undefined {
  return error instanceof ApiError ? error.errorsFor(field) : undefined;
}

// Field errors shown next to an input are left out here.
function OtherErrors({ error, shown }: { error: unknown; shown: readonly string[] }) {
  if (!error) {
    return null;
  }
  const others = error instanceof ApiError && error.isValidation ? error.errorsExcept(shown) : null;
  if (others && others.length === 0) {
    return null;
  }
  return <ErrorMessage error={others ? new Error(others.join(' ')) : error} />;
}

interface FormDialogProps {
  title: string;
  description?: ReactNode;
  submitLabel: string;
  submitIcon: LucideIcon;
  busy: boolean;
  canSubmit: boolean;
  onSubmit: () => void;
  onClose: () => void;
  initialFocus?: RefObject<HTMLElement | null>;
  children: ReactNode;
}

function FormDialog({ title, description, submitLabel, submitIcon, busy, canSubmit, onSubmit, onClose, initialFocus, children }: FormDialogProps) {
  const formId = useId();
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (canSubmit && !busy) {
      onSubmit();
    }
  };
  return (
    <Modal
      open
      onClose={onClose}
      title={title}
      description={description}
      initialFocus={initialFocus}
      dismissible={!busy}
      footer={
        <>
          <Button onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="primary" icon={submitIcon} loading={busy} disabled={!canSubmit}>
            {submitLabel}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="space-y-4">
        {children}
      </form>
    </Modal>
  );
}

interface NameFieldProps {
  value: string;
  onChange: (value: string) => void;
  errors?: string[];
  inputRef: RefObject<HTMLInputElement | null>;
}

function NameField({ value, onChange, errors, inputRef }: NameFieldProps) {
  return (
    <Field label="Name" htmlFor="folder-name" errors={errors}>
      <input
        {...fieldAria('folder-name', errors)}
        ref={inputRef}
        required
        maxLength={MAX_FOLDER_NAME_LENGTH}
        autoComplete="off"
        placeholder="e.g. Contracts"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="field"
      />
    </Field>
  );
}

export function CreateFolderDialog({
  tree,
  parentId,
  onClose,
  onCreated,
}: {
  tree: readonly FolderNode[];
  parentId: Guid | null;
  onClose: () => void;
  onCreated: (folder: SourceFolderDto) => void;
}) {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState('');
  const [location, setLocation] = useState<Guid | null>(parentId);
  const create = useMutation({
    mutationFn: () => sourcesApi.createFolder({ name: name.trim(), parentId: location }),
    onSuccess: (folder) => {
      // The caller opens the new folder right away, before the list is fetched again.
      queryClient.setQueryData<SourceFolderListDto>(sourceKeys.folders, (old) =>
        old ? { ...old, folders: [...old.folders, { ...folder, sourceCount: 0 }] } : old,
      );
      void queryClient.invalidateQueries({ queryKey: sourceKeys.folders });
      onCreated(folder);
    },
  });

  return (
    <FormDialog
      title="New folder"
      description={`Folders only organize this list; search and the agent still see every source. They nest at most ${MAX_FOLDER_DEPTH} levels deep.`}
      submitLabel="Create folder"
      submitIcon={FolderPlus}
      busy={create.isPending}
      canSubmit={name.trim() !== ''}
      onSubmit={() => create.mutate()}
      onClose={onClose}
      initialFocus={inputRef}
    >
      <NameField value={name} onChange={setName} errors={fieldErrors(create.error, 'name')} inputRef={inputRef} />
      <FolderPicker
        legend="Location"
        nodes={flattenTree(tree)}
        value={location}
        onChange={setLocation}
        rootLabel="Top level"
        blocker={(node) => (node ? newFolderBlocker(node) : null)}
      />
      <OtherErrors error={create.error} shown={['name']} />
    </FormDialog>
  );
}

export function RenameFolderDialog({ folder, onClose }: { folder: SourceFolderDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState(folder.name);
  const rename = useMutation({
    mutationFn: () => sourcesApi.renameFolder(folder.id, name.trim()),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: sourceKeys.folders });
      onClose();
    },
  });

  return (
    <FormDialog
      title={`Rename “${folder.name}”`}
      submitLabel="Rename"
      submitIcon={Pencil}
      busy={rename.isPending}
      canSubmit={name.trim() !== '' && name.trim() !== folder.name}
      onSubmit={() => rename.mutate()}
      onClose={onClose}
      initialFocus={inputRef}
    >
      <NameField value={name} onChange={setName} errors={fieldErrors(rename.error, 'name')} inputRef={inputRef} />
      <OtherErrors error={rename.error} shown={['name']} />
    </FormDialog>
  );
}

export function MoveFolderDialog({ node, tree, onClose }: { node: FolderNode; tree: readonly FolderNode[]; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [target, setTarget] = useState<Guid | null | undefined>(undefined);
  const move = useMutation({
    mutationFn: (parentId: Guid | null) => sourcesApi.moveFolder(node.folder.id, parentId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: sourceKeys.folders });
      onClose();
    },
  });

  return (
    <FormDialog
      title={`Move “${node.folder.name}”`}
      description={`Its subfolders and sources move with it. Folders nest at most ${MAX_FOLDER_DEPTH} levels deep.`}
      submitLabel="Move folder"
      submitIcon={FolderInput}
      busy={move.isPending}
      canSubmit={target !== undefined}
      onSubmit={() => target !== undefined && move.mutate(target)}
      onClose={onClose}
    >
      <FolderPicker
        legend="Move to"
        nodes={flattenTree(tree)}
        value={target}
        onChange={setTarget}
        rootLabel="Top level"
        blocker={(candidate) => moveFolderBlocker(node, candidate)}
      />
      <OtherErrors error={move.error} shown={[]} />
    </FormDialog>
  );
}

// Either picked sources, or every source the list's filters match on all pages.
export type SourcesToMove =
  | { kind: 'sources'; sources: readonly SourceDto[] }
  | { kind: 'matching'; params: SourceListParams; count: number };

function alreadyIn(sources: SourcesToMove, folderId: Guid | null): boolean {
  return sources.kind === 'sources'
    ? sources.sources.every((source) => (source.folderId ?? null) === folderId)
    : (sources.params.folder ?? null) === (folderId ?? 'unfiled');
}

export function MoveSourcesDialog({
  sources,
  tree,
  onClose,
  onMoved,
}: {
  sources: SourcesToMove;
  tree: readonly FolderNode[];
  onClose: () => void;
  onMoved: (moved: number, folderId: Guid | null) => void;
}) {
  const queryClient = useQueryClient();
  const [target, setTarget] = useState<Guid | null | undefined>(undefined);
  const count = sources.kind === 'sources' ? sources.sources.length : sources.count;
  const move = useMutation({
    mutationFn: (folderId: Guid | null) =>
      sources.kind === 'sources'
        ? sourcesApi.moveSources(
            sources.sources.map((source) => source.id),
            folderId,
          )
        : sourcesApi.moveMatchingSources(sources.params, folderId),
    onSuccess: (result, folderId) => {
      void queryClient.invalidateQueries({ queryKey: sourceKeys.all });
      onMoved(result?.moved ?? count, folderId);
    },
  });
  const title = sources.kind === 'sources' && count === 1 ? `Move “${sources.sources[0]?.title}”` : `Move ${pluralize(count, 'source')}`;

  return (
    <FormDialog
      title={title}
      description="Only this list changes. Search and the agent still see every source."
      submitLabel="Move"
      submitIcon={FolderInput}
      busy={move.isPending}
      canSubmit={target !== undefined}
      onSubmit={() => target !== undefined && move.mutate(target)}
      onClose={onClose}
    >
      <FolderPicker
        legend="Move to"
        nodes={flattenTree(tree)}
        value={target}
        onChange={setTarget}
        rootLabel="Unfiled (no folder)"
        rootKind="unfiled"
        blocker={(node) => (alreadyIn(sources, node?.folder.id ?? null) ? 'Already here' : null)}
      />
      <OtherErrors error={move.error} shown={[]} />
    </FormDialog>
  );
}

function deleteFolderMessage(node: FolderNode): string {
  const subfolders = flattenTree([node]).length - 1;
  const deleted = `“${node.folder.name}”${subfolders > 0 ? ` and its ${pluralize(subfolders, 'subfolder')}` : ''} will be deleted.`;
  if (node.totalCount === 0) {
    return `${deleted} There are no sources inside.`;
  }
  const kept = node.totalCount === 1 ? 'The source inside stays' : `The ${formatNumber(node.totalCount)} sources inside stay`;
  return `${deleted} ${kept} in the library, unfiled. Search is not affected.`;
}

/** Asks before a folder goes; its sources stay in the library. Mounted per folder, so an earlier failure never shows. */
function DeleteFolderDialog({ node, onClose, onDeleted }: { node: FolderNode; onClose: () => void; onDeleted: (node: FolderNode) => void }) {
  const queryClient = useQueryClient();
  const remove = useMutation({
    mutationFn: () => sourcesApi.deleteFolder(node.folder.id),
    onSuccess: () => {
      onDeleted(node);
      void queryClient.invalidateQueries({ queryKey: sourceKeys.all });
    },
  });
  return (
    <ConfirmDialog
      open
      title="Delete folder?"
      message={deleteFolderMessage(node)}
      confirmLabel="Delete folder"
      busy={remove.isPending}
      error={remove.isError ? <ErrorMessage error={remove.error} /> : null}
      onConfirm={() => remove.mutate()}
      onCancel={onClose}
    />
  );
}

export type FolderDialogState =
  | { kind: 'create'; parentId: Guid | null }
  | { kind: 'rename'; node: FolderNode }
  | { kind: 'move'; node: FolderNode }
  | { kind: 'delete'; node: FolderNode }
  | { kind: 'move-sources'; sources: SourcesToMove };

export interface FolderDialogHostProps {
  dialog: FolderDialogState;
  tree: readonly FolderNode[];
  onClose: () => void;
  onCreated: (folder: SourceFolderDto) => void;
  onMoved: (moved: number, folderId: Guid | null) => void;
  onDeleted: (node: FolderNode) => void;
}

/** The one folder dialog open on the Sources page. */
export function FolderDialogHost({ dialog, tree, onClose, onCreated, onMoved, onDeleted }: FolderDialogHostProps) {
  switch (dialog.kind) {
    case 'create':
      return <CreateFolderDialog tree={tree} parentId={dialog.parentId} onClose={onClose} onCreated={onCreated} />;
    case 'rename':
      return <RenameFolderDialog folder={dialog.node.folder} onClose={onClose} />;
    case 'move':
      return <MoveFolderDialog node={dialog.node} tree={tree} onClose={onClose} />;
    case 'move-sources':
      return <MoveSourcesDialog sources={dialog.sources} tree={tree} onClose={onClose} onMoved={onMoved} />;
    case 'delete':
      return <DeleteFolderDialog node={dialog.node} onClose={onClose} onDeleted={onDeleted} />;
  }
}
