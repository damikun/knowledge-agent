import { PAGE_SIZES, SOURCES_PAGE_SIZE } from './sourceFilters';

// What the Sources page remembers in this browser: the page size and whether the folders are hidden.
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
    // Storage can be blocked; the panel then shows again on the next visit.
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
    // Storage can be blocked, for example in private windows; the size then lasts for this visit only.
  }
}
