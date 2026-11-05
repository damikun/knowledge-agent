import { describe, expect, it } from 'vitest';
import { sourceDto } from '../../test/fixtures';
import { ingestingCount, SOURCE_POLL_INTERVAL_MS, sourcesRefetchInterval } from './polling';
import { lastPageOffset, librarySummary, pageRangeLabel, toListParams } from './sourceFilters';
import type { SourceStatsDto } from './types';
import { MAX_UPLOAD_BYTES, validateUpload } from './uploads';

function stats(overrides: Partial<SourceStatsDto> = {}): SourceStatsDto {
  return { total: 0, ready: 0, pending: 0, processing: 0, failed: 0, waiting: 0, readyChunks: 0, readyByType: {}, readyByEmbeddingModel: {}, ...overrides };
}

describe('sources polling decision', () => {
  it('polls every 2 s while any source is pending or processing', () => {
    expect(SOURCE_POLL_INTERVAL_MS).toBe(2000);
    expect(sourcesRefetchInterval(stats({ total: 5, ready: 4, pending: 1 }))).toBe(2000);
    expect(sourcesRefetchInterval(stats({ total: 1, processing: 1 }))).toBe(2000);
    expect(ingestingCount(stats({ pending: 2, processing: 3 }))).toBe(5);
  });

  it('stops polling when nothing is pending or processing', () => {
    expect(sourcesRefetchInterval(stats({ total: 3, ready: 2, failed: 1 }))).toBe(false);
    // Waiting sources move on only once an embedding model is saved.
    expect(sourcesRefetchInterval(stats({ total: 2, waiting: 2 }))).toBe(false);
    expect(sourcesRefetchInterval(stats())).toBe(false);
    // No stats yet (still loading or failed to load): no polling.
    expect(sourcesRefetchInterval(undefined)).toBe(false);
  });
});

describe('library paging and filters', () => {
  it('maps the filters to GET /api/sources parameters', () => {
    expect(toListParams({ query: 'handbook', type: 'pdf', status: 'indexing' }, 25)).toEqual({
      query: 'handbook',
      type: 'pdf',
      status: ['pending', 'processing'],
      site: null,
      offset: 25,
      limit: 25,
    });
    expect(toListParams({ query: '', type: 'all', status: 'all' }, 0)).toEqual({ query: '', type: null, status: [], site: null, offset: 0, limit: 25 });
    expect(toListParams({ query: '', type: 'all', status: 'ready' }, 0).status).toEqual(['ready']);
    // Failed sources and sources waiting for an embedding model both need someone.
    expect(toListParams({ query: '', type: 'all', status: 'attention' }, 0).status).toEqual(['failed', 'waiting']);
    expect(toListParams({ query: '', type: 'all', status: 'all', site: 'docs.python.org' }, 50, 50)).toMatchObject({ site: 'docs.python.org', limit: 50 });
  });

  it('describes the visible range and sums up the library', () => {
    const items = Array.from({ length: 25 }, (_, i) => sourceDto({ id: `s${i}` }));
    expect(pageRangeLabel({ items, offset: 25, total: 1240 })).toBe('Showing 26–50 of 1,240');
    expect(pageRangeLabel({ items: [], offset: 0, total: 0 })).toBe('');
    expect(librarySummary(stats({ total: 1240, ready: 1200, pending: 4, processing: 8, failed: 3 }))).toEqual([
      { label: '1,240 sources' },
      { label: '1,200 ready', tone: 'success' },
      { label: '12 indexing', tone: 'info' },
      { label: '3 need attention', tone: 'danger' },
    ]);
    // Waiting sources need attention too; without failures they are a warning.
    expect(librarySummary(stats({ total: 1250, ready: 1200, pending: 4, processing: 8, waiting: 35, failed: 3 })).map((item) => item.label)).toEqual([
      '1,250 sources',
      '1,200 ready',
      '12 indexing',
      '38 need attention',
    ]);
    expect(librarySummary(stats({ total: 2, ready: 1, waiting: 1 })).at(-1)).toEqual({ label: '1 needs attention', tone: 'warning' });
    expect(librarySummary(stats({ total: 1, ready: 1 })).map((item) => item.label)).toEqual(['1 source', '1 ready']);
  });

  it('finds the last page that still has items', () => {
    expect(lastPageOffset(26)).toBe(25);
    expect(lastPageOffset(25)).toBe(0);
    expect(lastPageOffset(51)).toBe(50);
    expect(lastPageOffset(0)).toBe(0);
  });
});

describe('validateUpload', () => {
  it('accepts PDF, TXT and Markdown files up to 25 MB', () => {
    expect(validateUpload({ name: 'report.PDF', size: 1024 })).toBeNull();
    expect(validateUpload({ name: 'notes.md', size: MAX_UPLOAD_BYTES })).toBeNull();
    expect(validateUpload({ name: 'readme.markdown', size: 10 })).toBeNull();
    expect(validateUpload({ name: 'plain.txt', size: 10 })).toBeNull();
  });

  it('rejects other types, empty files and files over the limit', () => {
    expect(validateUpload({ name: 'slides.pptx', size: 10 })).toMatch(/unsupported/i);
    expect(validateUpload({ name: 'empty.txt', size: 0 })).toMatch(/empty/i);
    expect(validateUpload({ name: 'huge.pdf', size: MAX_UPLOAD_BYTES + 1 })).toMatch(/limit is 25 MB/);
  });
});
