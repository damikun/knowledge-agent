import { PAGE_SIZES, SOURCES_PAGE_SIZE } from './sourceFilters';

const PAGE_SIZE_KEY = 'ka.sources.pageSize';
const FOLDERS_HIDDEN_KEY = 'ka.sources.foldersHidden';

export function readFoldersHidden(): boolean {
  try {
    return localStorage.getItem(FOLDERS_HIDDEN_KEY) === 'true';
  } catch {
    return false;
  }
}

export function saveFoldersHidden(hidden: boolean): void {
  try {
    localStorage.setItem(FOLDERS_HIDDEN_KEY, String(hidden));
  } catch {
  }
}

export function readPageSize(): number {
  try {
    const stored = Number(localStorage.getItem(PAGE_SIZE_KEY));
    return (PAGE_SIZES as readonly number[]).includes(stored) ? stored : SOURCES_PAGE_SIZE;
  } catch {
    return SOURCES_PAGE_SIZE;
  }
}

export function savePageSize(size: number): void {
  try {
    localStorage.setItem(PAGE_SIZE_KEY, String(size));
  } catch {
  }
}
