import type { Guid } from '../../lib/types';
import type { SourceFolderDto } from './types';

export const UNFILED = 'unfiled';
export const MAX_FOLDER_DEPTH = 3;
export const MAX_FOLDER_NAME_LENGTH = 100;

export type FolderView = 'all' | typeof UNFILED | Guid;

export interface FolderNode {
  folder: SourceFolderDto;
  // 1 for a top-level folder.
  depth: number;
  children: FolderNode[];
  // Includes the sources of every subfolder, unlike folder.sourceCount.
  totalCount: number;
}

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

function compareFolderNames(a: SourceFolderDto, b: SourceFolderDto): number {
  return collator.compare(a.name, b.name) || a.id.localeCompare(b.id);
}

export function buildFolderTree(folders: readonly SourceFolderDto[]): FolderNode[] {
  const byId = new Map(folders.map((folder) => [folder.id, folder]));
  const children = new Map<Guid | null, SourceFolderDto[]>();
  for (const folder of folders) {
    const parentId = folder.parentId && byId.has(folder.parentId) ? folder.parentId : null;
    children.set(parentId, [...(children.get(parentId) ?? []), folder]);
  }

  const placed = new Set<Guid>();
  const build = (folder: SourceFolderDto, depth: number): FolderNode => {
    placed.add(folder.id);
    const nodes = (children.get(folder.id) ?? [])
      .filter((child) => !placed.has(child.id))
      .sort(compareFolderNames)
      .map((child) => build(child, depth + 1));
    return { folder, depth, children: nodes, totalCount: folder.sourceCount + nodes.reduce((sum, node) => sum + node.totalCount, 0) };
  };

  const roots = (children.get(null) ?? []).sort(compareFolderNames).map((folder) => build(folder, 1));
  // Folders caught in a parent cycle are not below any root; listing them at the top keeps them reachable.
  for (const folder of [...folders].sort(compareFolderNames)) {
    if (!placed.has(folder.id)) {
      roots.push(build(folder, 1));
    }
  }
  return roots;
}

export function flattenTree(nodes: readonly FolderNode[]): FolderNode[] {
  return nodes.flatMap((node) => [node, ...flattenTree(node.children)]);
}

export function findNode(nodes: readonly FolderNode[], id: Guid | null | undefined): FolderNode | undefined {
  return id ? flattenTree(nodes).find((node) => node.folder.id === id) : undefined;
}

export function folderPath(nodes: readonly FolderNode[], id: Guid): FolderNode[] {
  for (const node of nodes) {
    if (node.folder.id === id) {
      return [node];
    }
    const below = folderPath(node.children, id);
    if (below.length > 0) {
      return [node, ...below];
    }
  }
  return [];
}

export function folderPathLabel(nodes: readonly FolderNode[], id: Guid): string {
  return folderPath(nodes, id)
    .map((node) => node.folder.name)
    .join(' / ');
}

// 1 for a folder without subfolders.
export function subtreeHeight(node: FolderNode): number {
  return 1 + Math.max(0, ...node.children.map(subtreeHeight));
}

export function subtreeIds(node: FolderNode): Set<Guid> {
  return new Set(flattenTree([node]).map((child) => child.folder.id));
}

/** The Internal folder, which only the help assistant searches, with everything filed below it. */
export function isInternalNode(node: FolderNode): boolean {
  return node.folder.isInternal === true;
}

/** The tree without the Internal folder, which no agent searches. Internal is always a top-level folder. */
export function withoutInternal(nodes: readonly FolderNode[]): FolderNode[] {
  return nodes.filter((node) => !isInternalNode(node));
}

/** Sources in the Internal folder and its subfolders. */
export function internalSourceCount(nodes: readonly FolderNode[]): number {
  return nodes.filter(isInternalNode).reduce((sum, node) => sum + node.totalCount, 0);
}

/** Folders an agent can be limited to: every folder but Internal and its subfolders. */
export function pickableFolderIds(folders: readonly SourceFolderDto[]): Set<Guid> {
  return new Set(flattenTree(withoutInternal(buildFolderTree(folders))).map((node) => node.folder.id));
}

export const INTERNAL_FOLDER_NOTE = 'Only the help assistant searches these, for your team.';

// The reasons label disabled destinations in a list, so they stay short.
export function newFolderBlocker(parent: FolderNode): string | null {
  return parent.depth >= MAX_FOLDER_DEPTH ? 'Too deep' : null;
}

// A null target is the top level.
export function moveFolderBlocker(folder: FolderNode, target: FolderNode | null): string | null {
  if (isInternalNode(folder)) {
    return 'Built in';
  }
  if ((target?.folder.id ?? null) === folder.folder.parentId) {
    return 'Already here';
  }
  if (target?.folder.id === folder.folder.id) {
    return 'This folder';
  }
  if (target && subtreeIds(folder).has(target.folder.id)) {
    return 'Inside this folder';
  }
  if (target && target.depth + subtreeHeight(folder) > MAX_FOLDER_DEPTH) {
    return 'Too deep';
  }
  return null;
}

export function viewFolderId(view: FolderView): Guid | null {
  return view === 'all' || view === UNFILED ? null : view;
}

export function parseFolderView(value: string | null | undefined): FolderView {
  const trimmed = value?.trim();
  if (!trimmed) {
    return 'all';
  }
  return trimmed.toLowerCase() === UNFILED ? UNFILED : trimmed;
}
