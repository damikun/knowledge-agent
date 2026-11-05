import { describe, expect, it } from 'vitest';
import { summarizeToolArguments, toolMeta, toolSourceIds } from './tools';
import { ToolNames } from './types';

describe('find_sources tool cards', () => {
  it('has a friendly label', () => {
    expect(toolMeta(ToolNames.FindSources).label).toBe('Looked up sources');
    expect(toolMeta(ToolNames.FindSources).activeLabel).toBe('Looking up sources');
  });

  it('shows the query and the filters that differ from the defaults', () => {
    expect(summarizeToolArguments(ToolNames.FindSources, { query: 'handbook', type: 'pdf', status: 'any', offset: 20 })).toEqual({
      primary: 'handbook',
      details: ['pdf', 'any status', 'from #21'],
    });
  });

  it('hides the default status and first page', () => {
    expect(summarizeToolArguments(ToolNames.FindSources, { status: 'ready', offset: 0, limit: 20 })).toEqual({
      primary: 'all sources',
      details: [],
    });
  });

  it('labels other statuses', () => {
    expect(summarizeToolArguments(ToolNames.FindSources, { query: 'report', status: 'failed' }).details).toEqual(['failed']);
  });
});

describe('grep_sources tool cards', () => {
  it('has a friendly label', () => {
    expect(toolMeta(ToolNames.GrepSources).label).toBe('Found exact text');
    expect(toolMeta(ToolNames.GrepSources).activeLabel).toBe('Finding exact text');
  });

  it('shows the text and the options that differ from the defaults', () => {
    expect(
      summarizeToolArguments(ToolNames.GrepSources, {
        text: 'Jan Novák',
        output: 'sources',
        match_case: true,
        whole_words: false,
        offset: 20,
        source_ids: ['s1', 's2'],
      }),
    ).toEqual({ primary: 'Jan Novák', details: ['sources', 'match case', 'inside words', 'from #21', '2 sources'] });
  });

  it('hides the defaults', () => {
    expect(
      summarizeToolArguments(ToolNames.GrepSources, { text: 'E-214', output: 'passages', match_case: false, whole_words: true, offset: 0 }),
    ).toEqual({ primary: 'E-214', details: [] });
  });

  it('shows a single scoped source by title', () => {
    expect(toolSourceIds(ToolNames.GrepSources, { text: 'E-214', source_ids: ['s1'] })).toEqual(['s1']);
  });
});

describe('toolSourceIds', () => {
  it('returns the source ids a card shows as titles', () => {
    expect(toolSourceIds(ToolNames.ReadSource, { source_id: 's1', offset: 0 })).toEqual(['s1']);
    expect(toolSourceIds(ToolNames.SearchKnowledgeBase, { query: 'q', source_ids: ['s2'] })).toEqual(['s2']);
    expect(toolSourceIds(ToolNames.DelegateResearch, { task: 't', source_ids: ['s3'] })).toEqual(['s3']);
  });

  it('skips ids that are only counted', () => {
    expect(toolSourceIds(ToolNames.SearchKnowledgeBase, { query: 'q', source_ids: ['s1', 's2'] })).toEqual([]);
    expect(toolSourceIds(ToolNames.FindSources, { query: 'handbook' })).toEqual([]);
    expect(toolSourceIds(ToolNames.ReadSource, null)).toEqual([]);
  });

  it('uses resolved titles and falls back to the id', () => {
    const title = (id: string) => (id === 's1' ? 'Employee handbook' : undefined);
    expect(summarizeToolArguments(ToolNames.ReadSource, { source_id: 's1' }, title).primary).toBe('Employee handbook');
    expect(summarizeToolArguments(ToolNames.ReadSource, { source_id: 's9' }, title).primary).toBe('s9');
    expect(summarizeToolArguments(ToolNames.SearchKnowledgeBase, { query: 'q', source_ids: ['s1'] }, title).details).toEqual(['Employee handbook']);
  });
});
