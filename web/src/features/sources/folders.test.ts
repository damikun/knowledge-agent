import { describe, expect, it } from 'vitest';
import { sourceFolder } from '../../test/sourcesApi';
import {
  buildFolderTree,
  findNode,
  flattenTree,
  folderPathLabel,
  internalSourceCount,
  moveFolderBlocker,
  newFolderBlocker,
  parseFolderView,
  pickableFolderIds,
  subtreeHeight,
  viewFolderId,
  withoutInternal,
} from './folders';

const withCount = (folder: ReturnType<typeof sourceFolder>, sourceCount: number) => ({ ...folder, sourceCount });

const folders = [
  withCount(sourceFolder('specs', 'Specs', 'apollo'), 4),
  withCount(sourceFolder('apollo', 'Apollo', 'projects'), 2),
  withCount(sourceFolder('projects', 'Projects'), 1),
  sourceFolder('folder-10', 'Folder 10'),
  sourceFolder('folder-2', 'folder 2'),
  withCount(sourceFolder('gemini', 'Gemini', 'projects'), 3),
];

describe('folder tree', () => {
  it('nests folders under their parents, sorted naturally and ignoring case', () => {
    const tree = buildFolderTree(folders);

    expect(tree.map((node) => node.folder.name)).toEqual(['folder 2', 'Folder 10', 'Projects']);
    expect(flattenTree(tree).map((node) => `${'  '.repeat(node.depth - 1)}${node.folder.name}`)).toEqual([
      'folder 2',
      'Folder 10',
      'Projects',
      '  Apollo',
      '    Specs',
      '  Gemini',
    ]);
  });

  it('adds up the sources below each folder', () => {
    const tree = buildFolderTree(folders);

    expect(findNode(tree, 'projects')?.totalCount).toBe(10);
    expect(findNode(tree, 'apollo')?.totalCount).toBe(6);
    expect(findNode(tree, 'specs')?.totalCount).toBe(4);
    expect(subtreeHeight(findNode(tree, 'projects')!)).toBe(3);
    expect(folderPathLabel(tree, 'specs')).toBe('Projects / Apollo / Specs');
    expect(findNode(tree, 'missing')).toBeUndefined();
  });

  it('keeps folders whose parent is missing or part of a cycle reachable', () => {
    const tree = buildFolderTree([sourceFolder('orphan', 'Orphan', 'deleted'), sourceFolder('a', 'A', 'b'), sourceFolder('b', 'B', 'a')]);

    expect(flattenTree(tree).map((node) => node.folder.id).sort()).toEqual(['a', 'b', 'orphan']);
    expect(findNode(tree, 'orphan')?.depth).toBe(1);
  });

  it('allows subfolders and moves only within three levels and never into the folder itself', () => {
    const tree = buildFolderTree(folders);
    const projects = findNode(tree, 'projects')!;
    const apollo = findNode(tree, 'apollo')!;
    const specs = findNode(tree, 'specs')!;
    const gemini = findNode(tree, 'gemini')!;
    const loose = findNode(tree, 'folder-2')!;

    expect(newFolderBlocker(apollo)).toBeNull();
    expect(newFolderBlocker(specs)).toBe('Too deep');
    expect(moveFolderBlocker(apollo, projects)).toBe('Already here');
    expect(moveFolderBlocker(projects, specs)).toBe('Inside this folder');
    expect(moveFolderBlocker(apollo, apollo)).toBe('This folder');
    expect(moveFolderBlocker(apollo, gemini)).toBe('Too deep');
    expect(moveFolderBlocker(apollo, loose)).toBeNull();
    expect(moveFolderBlocker(specs, null)).toBeNull();
    expect(moveFolderBlocker(projects, null)).toBe('Already here');
  });

  it('reads the folder view from the URL', () => {
    expect(parseFolderView(null)).toBe('all');
    expect(parseFolderView('  ')).toBe('all');
    expect(parseFolderView('UNFILED')).toBe('unfiled');
    expect(parseFolderView(' f-1 ')).toBe('f-1');
    expect(viewFolderId('all')).toBeNull();
    expect(viewFolderId('unfiled')).toBeNull();
    expect(viewFolderId('f-1')).toBe('f-1');
  });

  it('keeps the Internal folder out of what agents can pick, and in place', () => {
    const internal = { ...withCount(sourceFolder('internal', 'Internal'), 2), isInternal: true };
    const tree = buildFolderTree([...folders, internal, withCount(sourceFolder('hr', 'HR', 'internal'), 3)]);
    const internalNode = findNode(tree, 'internal')!;

    expect(withoutInternal(tree).map((node) => node.folder.id)).not.toContain('internal');
    expect(internalSourceCount(tree)).toBe(5);
    const pickable = pickableFolderIds([...folders, internal, sourceFolder('hr', 'HR', 'internal')]);
    expect([pickable.has('internal'), pickable.has('hr'), pickable.has('apollo')]).toEqual([false, false, true]);
    expect(moveFolderBlocker(internalNode, findNode(tree, 'projects')!)).toBe('Built in');
    expect(moveFolderBlocker(findNode(tree, 'gemini')!, internalNode)).toBeNull();
  });
});
