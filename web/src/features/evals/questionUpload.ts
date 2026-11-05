import type { EvalTurn, SaveEvalItemRequest } from './types';

// Limits mirror the API's (CustomDatasetService), which checks them again.

export const MAX_UPLOAD_ROWS = 1000;
export const MAX_UPLOAD_BYTES = 5 * 1024 * 1024;
const MAX_QUESTION = 4000;
const MAX_ANSWER = 20000;
const MAX_LIST = 50;
const MAX_FACT = 1000;
const MAX_SOURCE = 500;
const MAX_TAGS = 20;
const MAX_TAG = 50;
const MAX_TURNS = 20;

export type UploadFormat = 'csv' | 'jsonl';

export type UploadField = 'question' | 'referenceAnswer' | 'expectedFacts' | 'expectedSources' | 'tags' | 'answerable' | 'ignore';

export const UPLOAD_FIELD_LABELS: Readonly<Record<UploadField, string>> = {
  question: 'Question',
  referenceAnswer: 'Expected answer',
  expectedFacts: 'Key facts',
  expectedSources: 'Sources to cite',
  tags: 'Tags',
  answerable: 'Should answer',
  ignore: 'Leave out',
};

export interface UploadRow {
  /** 1-based line of the file (CSV: the data row, after the header). */
  line: number;
  request: SaveEvalItemRequest | null;
  errors: string[];
  /** The dataset, or an earlier row of the file, already has the question. */
  duplicate: boolean;
}

export interface ParsedFile {
  format: UploadFormat;
  /** CSV only: the header row. */
  columns: string[];
  /** CSV only: the data rows as cells. */
  cells: string[][];
  /** JSONL only: each line as parsed, or the reason it could not be. */
  records: Array<{ line: number; value: unknown; error: string | null }>;
  error: string | null;
}

export function formatOf(fileName: string): UploadFormat | null {
  const lower = fileName.toLowerCase();
  if (lower.endsWith('.csv') || lower.endsWith('.tsv') || lower.endsWith('.txt')) {
    return 'csv';
  }
  if (lower.endsWith('.jsonl') || lower.endsWith('.ndjson') || lower.endsWith('.json')) {
    return 'jsonl';
  }
  return null;
}

// Excel saves CSV with a byte order mark, which would stick to the first header.
function withoutBom(text: string): string {
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

/** Comma, semicolon (Excel in many locales) or tab, whichever the header line has most of. */
export function detectDelimiter(text: string): string {
  const firstLine = withoutBom(text).split(/\r?\n/, 1)[0] ?? '';
  let best = ',';
  let bestCount = 0;
  for (const candidate of [',', ';', '\t']) {
    let count = 0;
    let quoted = false;
    for (const character of firstLine) {
      if (character === '"') {
        quoted = !quoted;
      } else if (!quoted && character === candidate) {
        count++;
      }
    }
    if (count > bestCount) {
      best = candidate;
      bestCount = count;
    }
  }
  return best;
}

/** RFC 4180 CSV: quoted cells may hold delimiters, quotes ("") and line breaks. Blank lines are dropped. */
export function parseCsv(text: string, delimiter = detectDelimiter(text)): string[][] {
  const source = withoutBom(text);
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;
  for (let index = 0; index < source.length; index++) {
    const character = source[index]!;
    if (quoted) {
      if (character === '"') {
        if (source[index + 1] === '"') {
          cell += '"';
          index++;
        } else {
          quoted = false;
        }
      } else {
        cell += character;
      }
    } else if (character === '"' && cell === '') {
      quoted = true;
    } else if (character === delimiter) {
      row.push(cell);
      cell = '';
    } else if (character === '\n' || character === '\r') {
      if (character === '\r' && source[index + 1] === '\n') {
        index++;
      }
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
    } else {
      cell += character;
    }
  }
  if (cell !== '' || row.length > 0) {
    row.push(cell);
    rows.push(row);
  }
  return rows.filter((cells) => cells.some((value) => value.trim() !== ''));
}

const HEADER_GUESSES: ReadonlyArray<[UploadField, readonly string[]]> = [
  ['question', ['question', 'questions', 'q', 'prompt', 'input', 'query']],
  ['referenceAnswer', ['expectedanswer', 'referenceanswer', 'answer', 'expected', 'expectedoutput', 'output', 'reference']],
  ['expectedFacts', ['facts', 'keyfacts', 'expectedfacts', 'mustcontain']],
  ['expectedSources', ['sources', 'expectedsources', 'source', 'citations', 'documents']],
  ['tags', ['tags', 'tag', 'labels', 'category']],
  ['answerable', ['answerable', 'shouldanswer', 'answer?']],
];

/** Unknown columns, and second columns for the same field, map to nothing. */
export function guessMapping(columns: readonly string[]): UploadField[] {
  const used = new Set<UploadField>();
  return columns.map((column) => {
    const key = column.toLowerCase().replace(/[^a-z?]/g, '');
    const field = HEADER_GUESSES.find(([, names]) => names.includes(key))?.[0];
    if (!field || used.has(field)) {
      return 'ignore';
    }
    used.add(field);
    return field;
  });
}

export function parseFile(text: string, format: UploadFormat): ParsedFile {
  if (format === 'csv') {
    const [header = [], ...cells] = parseCsv(text);
    const columns = header.map((column) => column.trim());
    return {
      format,
      columns,
      cells,
      records: [],
      error:
        columns.length === 0
          ? 'The file is empty.'
          : cells.length === 0
            ? 'The file has a header but no questions.'
            : cells.length > MAX_UPLOAD_ROWS
              ? `The file has ${cells.length} questions; at most ${MAX_UPLOAD_ROWS} fit in one upload. Split the file.`
              : null,
    };
  }
  const records: ParsedFile['records'] = [];
  withoutBom(text)
    .split(/\r?\n/)
    .forEach((line, index) => {
      if (line.trim() === '') {
        return;
      }
      try {
        records.push({ line: index + 1, value: JSON.parse(line), error: null });
      } catch {
        records.push({ line: index + 1, value: null, error: 'This line is not valid JSON.' });
      }
    });
  return {
    format,
    columns: [],
    cells: [],
    records,
    error:
      records.length === 0
        ? 'The file is empty.'
        : records.length > MAX_UPLOAD_ROWS
          ? `The file has ${records.length} questions; at most ${MAX_UPLOAD_ROWS} fit in one upload. Split the file.`
          : null,
  };
}

// Facts use "|" for alternatives ("30 days|thirty days"), so lists in a cell are separated by semicolons or lines.
function listOf(value: unknown, splitCommas = false): string[] {
  const parts = Array.isArray(value)
    ? value.map((entry) => (typeof entry === 'string' || typeof entry === 'number' ? String(entry) : ''))
    : typeof value === 'string'
      ? value.split(splitCommas ? /[;\n,]/ : /[;\n]/)
      : [];
  const seen = new Set<string>();
  return parts
    .map((part) => part.trim())
    .filter((part) => {
      const key = part.toLowerCase();
      if (!part || seen.has(key)) {
        return false;
      }
      seen.add(key);
      return true;
    });
}

function answerableOf(value: unknown, errors: string[]): boolean {
  if (value === undefined || value === null || value === '') {
    return true;
  }
  if (typeof value === 'boolean') {
    return value;
  }
  const text = String(value).trim().toLowerCase();
  if (['yes', 'y', 'true', '1'].includes(text)) {
    return true;
  }
  if (['no', 'n', 'false', '0'].includes(text)) {
    return false;
  }
  errors.push('Should answer must be yes or no.');
  return true;
}

function historyOf(value: unknown, errors: string[]): EvalTurn[] {
  if (value === undefined || value === null) {
    return [];
  }
  if (!Array.isArray(value)) {
    errors.push('history must be a list of turns with a role and content.');
    return [];
  }
  const turns: EvalTurn[] = [];
  for (const entry of value) {
    const record = entry && typeof entry === 'object' ? (entry as Record<string, unknown>) : {};
    const role = typeof record.role === 'string' ? record.role.trim().toLowerCase() : '';
    const content = typeof record.content === 'string' ? record.content.trim() : '';
    if (!content) {
      continue;
    }
    const normalized = role === 'visitor' ? 'user' : role === 'agent' ? 'assistant' : role;
    if (normalized !== 'user' && normalized !== 'assistant') {
      errors.push('Each earlier turn is from the user or the assistant.');
      return [];
    }
    turns.push({ role: normalized, content });
  }
  if (turns.length > MAX_TURNS) {
    errors.push(`At most ${MAX_TURNS} earlier turns.`);
  }
  return turns;
}

function checkList(values: string[], max: number, maxLength: number, noun: string, errors: string[]) {
  if (values.length > max) {
    errors.push(`At most ${max} ${noun}.`);
  } else if (values.some((value) => value.length > maxLength)) {
    errors.push(`Each of the ${noun} can have at most ${maxLength} characters.`);
  }
}

interface RawQuestion {
  question?: unknown;
  referenceAnswer?: unknown;
  expectedFacts?: unknown;
  expectedSources?: unknown;
  tags?: unknown;
  answerable?: unknown;
  history?: unknown;
}

function toRequest(raw: RawQuestion): { request: SaveEvalItemRequest | null; errors: string[] } {
  const errors: string[] = [];
  const question = typeof raw.question === 'string' || typeof raw.question === 'number' ? String(raw.question).trim() : '';
  if (!question) {
    errors.push('The question is empty.');
  } else if (question.length > MAX_QUESTION) {
    errors.push(`The question can have at most ${MAX_QUESTION} characters.`);
  }
  const answer = typeof raw.referenceAnswer === 'string' ? raw.referenceAnswer.trim() : '';
  if (answer.length > MAX_ANSWER) {
    errors.push(`The expected answer can have at most ${MAX_ANSWER} characters.`);
  }
  const answerable = answerableOf(raw.answerable, errors);
  const facts = listOf(raw.expectedFacts);
  const sources = listOf(raw.expectedSources);
  const tags = listOf(raw.tags, true);
  checkList(facts, MAX_LIST, MAX_FACT, 'facts', errors);
  checkList(sources, MAX_LIST, MAX_SOURCE, 'sources', errors);
  checkList(tags, MAX_TAGS, MAX_TAG, 'tags', errors);
  const history = historyOf(raw.history, errors);
  if (errors.length > 0) {
    return { request: null, errors };
  }
  return {
    request: {
      question,
      referenceAnswer: answerable ? answer || null : null,
      expectedFacts: answerable ? facts : [],
      expectedSources: answerable ? sources : [],
      answerable,
      tags,
      source: 'upload',
      history,
    },
    errors,
  };
}

function pick(record: Record<string, unknown>, keys: readonly string[]): unknown {
  for (const key of keys) {
    if (record[key] !== undefined) {
      return record[key];
    }
  }
  return undefined;
}

// The portal's field names, the built-in datasets' snake_case, and common short names.
function rawFromJson(value: unknown): RawQuestion | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null;
  }
  const record = value as Record<string, unknown>;
  return {
    question: pick(record, ['question', 'input', 'prompt']),
    referenceAnswer: pick(record, ['referenceAnswer', 'reference_answer', 'expectedAnswer', 'expected_answer', 'answer', 'expected_output']),
    expectedFacts: pick(record, ['expectedFacts', 'expected_facts', 'facts', 'keyFacts', 'key_facts']),
    expectedSources: pick(record, ['expectedSources', 'expected_sources', 'sources']),
    tags: pick(record, ['tags']),
    answerable: pick(record, ['answerable']),
    history: pick(record, ['history', 'turns']),
  };
}

/** Case- and whitespace-insensitive, the way the API compares questions. */
export function duplicateKey(question: string, history: readonly EvalTurn[] = []): string {
  return [...history.filter((turn) => turn.role === 'user').map((turn) => turn.content), question]
    .map((text) => text.split(/\s+/).filter(Boolean).join(' ').toLowerCase())
    .join('\n');
}

export function readRows(file: ParsedFile, mapping: readonly UploadField[], existing: ReadonlySet<string>): UploadRow[] {
  const raws: Array<{ line: number; raw: RawQuestion | null; error: string | null }> =
    file.format === 'csv'
      ? file.cells.map((cells, index) => {
          const raw: Record<string, string> = {};
          mapping.forEach((field, column) => {
            if (field !== 'ignore') {
              raw[field] = cells[column] ?? '';
            }
          });
          return { line: index + 1, raw, error: null };
        })
      : file.records.map((record) => ({
          line: record.line,
          raw: record.error ? null : rawFromJson(record.value),
          error: record.error ?? (rawFromJson(record.value) ? null : 'Each line must be a JSON object with a question.'),
        }));
  const seen = new Set(existing);
  return raws.map(({ line, raw, error }) => {
    if (error || !raw) {
      return { line, request: null, errors: [error ?? 'The row could not be read.'], duplicate: false };
    }
    const { request, errors } = toRequest(raw);
    if (!request) {
      return { line, request, errors, duplicate: false };
    }
    const key = duplicateKey(request.question, request.history);
    const duplicate = seen.has(key);
    seen.add(key);
    return { line, request, errors, duplicate };
  });
}

/** Quote every cell so commas stay inside it. */
function csvCell(value: string): string {
  return `"${value.replace(/"/g, '""')}"`;
}

export const CSV_TEMPLATE = [
  ['question', 'expected_answer', 'facts', 'sources', 'tags', 'answerable'],
  ['How long do I have to return an order?', 'You can return it within 30 days of delivery.', '30 days', 'Returns policy', 'returns', 'yes'],
  ['Do you ship to Norway?', 'Yes. Delivery to Norway takes 5 to 7 working days.', '5 to 7 working days;Norway', '', 'shipping', 'yes'],
  ['What is the CEO’s home address?', '', '', '', 'privacy', 'no'],
]
  .map((row) => row.map(csvCell).join(','))
  .join('\r\n');

export const JSONL_TEMPLATE = [
  {
    question: 'How long do I have to return an order?',
    referenceAnswer: 'You can return it within 30 days of delivery.',
    expectedFacts: ['30 days'],
    expectedSources: ['Returns policy'],
    tags: ['returns'],
  },
  {
    question: 'And for sale items?',
    referenceAnswer: 'Sale items can be returned within 14 days.',
    expectedFacts: ['14 days'],
    tags: ['returns'],
    history: [
      { role: 'user', content: 'How long do I have to return an order?' },
      { role: 'assistant', content: 'You can return it within 30 days of delivery.' },
    ],
  },
  { question: 'What is the CEO’s home address?', answerable: false, tags: ['privacy'] },
]
  .map((line) => JSON.stringify(line))
  .join('\n');
