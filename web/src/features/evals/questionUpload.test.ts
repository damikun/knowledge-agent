import { describe, expect, it } from 'vitest';
import {
  CSV_TEMPLATE,
  detectDelimiter,
  duplicateKey,
  formatOf,
  guessMapping,
  JSONL_TEMPLATE,
  MAX_UPLOAD_ROWS,
  parseCsv,
  parseFile,
  readRows,
} from './questionUpload';

describe('parseCsv', () => {
  it('reads quoted cells with commas, quotes and line breaks, and drops blank lines', () => {
    const text = '﻿question,answer\r\n"Do you ship, to Norway?","Yes, ""fast""\nin 5 days"\r\n\r\nPlain,row\n';
    expect(parseCsv(text)).toEqual([
      ['question', 'answer'],
      ['Do you ship, to Norway?', 'Yes, "fast"\nin 5 days'],
      ['Plain', 'row'],
    ]);
  });

  it('detects semicolons and tabs as the delimiter from the header', () => {
    expect(detectDelimiter('question;answer;tags\nA;B;C')).toBe(';');
    expect(detectDelimiter('question\tanswer')).toBe('\t');
    expect(detectDelimiter('"a;b",c')).toBe(',');
    expect(parseCsv('question;answer\nA;B')).toEqual([
      ['question', 'answer'],
      ['A', 'B'],
    ]);
  });
});

describe('formatOf', () => {
  it('knows CSV and JSONL by the file name', () => {
    expect(formatOf('Questions.CSV')).toBe('csv');
    expect(formatOf('q.jsonl')).toBe('jsonl');
    expect(formatOf('q.ndjson')).toBe('jsonl');
    expect(formatOf('q.xlsx')).toBeNull();
  });
});

describe('guessMapping', () => {
  it('maps the usual header names and leaves unknown or repeated ones out', () => {
    expect(guessMapping(['Question', 'Expected answer', 'Key facts', 'Sources', 'Tags', 'Answerable', 'Notes', 'question'])).toEqual([
      'question',
      'referenceAnswer',
      'expectedFacts',
      'expectedSources',
      'tags',
      'answerable',
      'ignore',
      'ignore',
    ]);
  });
});

describe('readRows', () => {
  it('turns CSV rows into requests, splits lists on semicolons and keeps fact alternatives', () => {
    const file = parseFile('question,answer,facts,tags,answerable\n"Refunds?","Within 30 days.","30 days|thirty days; receipt","returns, money",yes\nSecret?,,,,no\n', 'csv');
    const rows = readRows(file, guessMapping(file.columns), new Set());
    expect(rows[0]).toEqual({
      line: 1,
      duplicate: false,
      errors: [],
      request: {
        question: 'Refunds?',
        referenceAnswer: 'Within 30 days.',
        expectedFacts: ['30 days|thirty days', 'receipt'],
        expectedSources: [],
        answerable: true,
        tags: ['returns', 'money'],
        source: 'upload',
        history: [],
      },
    });
    expect(rows[1]!.request).toMatchObject({ question: 'Secret?', answerable: false, referenceAnswer: null, expectedFacts: [] });
  });

  it('reports each row’s problems and marks questions the dataset or an earlier row has', () => {
    const file = parseFile('question,answerable\n,yes\nDo you ship?,maybe\nDo   YOU ship to Norway?,\nNew one,\ndo you ship to norway?,\n', 'csv');
    const rows = readRows(file, guessMapping(file.columns), new Set([duplicateKey('Do you ship to Norway?')]));
    expect(rows.map((row) => [row.line, row.errors, row.duplicate])).toEqual([
      [1, ['The question is empty.'], false],
      [2, ['Should answer must be yes or no.'], false],
      [3, [], true],
      [4, [], false],
      [5, [], true],
    ]);
  });

  it('follows a changed mapping', () => {
    const file = parseFile('a,b\nWhat?,Because.\n', 'csv');
    expect(readRows(file, ['question', 'referenceAnswer'], new Set())[0]!.request).toMatchObject({ question: 'What?', referenceAnswer: 'Because.' });
    expect(readRows(file, ['ignore', 'question'], new Set())[0]!.request).toMatchObject({ question: 'Because.', referenceAnswer: null });
  });

  it('reads JSONL in the portal’s and the built-in files’ field names, with history', () => {
    const text = [
      JSON.stringify({ question: 'And for sale items?', referenceAnswer: '14 days.', history: [{ role: 'user', content: 'Returns?' }, { role: 'assistant', content: '30 days.' }] }),
      '',
      JSON.stringify({ question: 'Opening hours?', expected_facts: ['9 to 5'], expected_sources: ['hours.md'], answerable: true }),
      '{not json',
      JSON.stringify(['a list']),
      JSON.stringify({ question: 'Odd?', history: [{ role: 'system', content: 'Be brief.' }] }),
    ].join('\n');
    const rows = readRows(parseFile(text, 'jsonl'), [], new Set());
    expect(rows[0]!.request).toMatchObject({ question: 'And for sale items?', history: [{ role: 'user', content: 'Returns?' }, { role: 'assistant', content: '30 days.' }] });
    expect(rows[1]).toMatchObject({ line: 3, request: { expectedFacts: ['9 to 5'], expectedSources: ['hours.md'] } });
    expect(rows[2]).toMatchObject({ line: 4, request: null, errors: ['This line is not valid JSON.'] });
    expect(rows[3]).toMatchObject({ line: 5, request: null, errors: ['Each line must be a JSON object with a question.'] });
    expect(rows[4]!.errors).toEqual(['Each earlier turn is from the user or the assistant.']);
  });

  it('compares a follow-up by all the visitor’s turns', () => {
    expect(duplicateKey('And sale items?', [{ role: 'user', content: 'Returns?' }, { role: 'assistant', content: '30 days.' }])).toBe('returns?\nand sale items?');
  });
});

describe('parseFile', () => {
  it('refuses empty files and files over the row limit', () => {
    expect(parseFile('', 'csv').error).toBe('The file is empty.');
    expect(parseFile('question\n', 'csv').error).toBe('The file has a header but no questions.');
    const many = ['question', ...Array.from({ length: MAX_UPLOAD_ROWS + 1 }, (_, index) => `Q${index}`)].join('\n');
    expect(parseFile(many, 'csv').error).toMatch(/at most 1000 fit in one upload/);
  });

  it('reads both templates without problems', () => {
    const csv = parseFile(CSV_TEMPLATE, 'csv');
    const csvRows = readRows(csv, guessMapping(csv.columns), new Set());
    expect(csvRows).toHaveLength(3);
    expect(csvRows.every((row) => row.errors.length === 0)).toBe(true);
    const jsonl = readRows(parseFile(JSONL_TEMPLATE, 'jsonl'), [], new Set());
    expect(jsonl).toHaveLength(3);
    expect(jsonl.every((row) => row.errors.length === 0)).toBe(true);
    expect(jsonl[1]!.request!.history).toHaveLength(2);
  });
});
