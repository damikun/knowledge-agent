import type { AttributeSource, AttributeType, ContactCondition, FilterAttribute } from './types';

export const OP_LABELS: Record<string, string> = {
  is: 'is',
  is_not: 'is not',
  contains: 'contains',
  set: 'has any value',
  not_set: 'has no value',
  eq: 'equals',
  neq: 'does not equal',
  gt: 'is more than',
  lt: 'is less than',
  between: 'is between',
  true: 'is yes',
  false: 'is no',
  before: 'is before',
  after: 'is after',
  within_days: 'is in the last',
};

export const VALUELESS_OPS = new Set(['set', 'not_set', 'true', 'false']);

export const SOURCE_LABELS: Record<AttributeSource, string> = {
  verified: 'Verified',
  server: 'Server',
  admin: 'Admin',
  browser: 'Browser',
};

export const SOURCE_HINTS: Record<AttributeSource, string> = {
  verified: 'Sent by identify() for a signed-in user',
  server: 'Sent by your server, signed with the identity secret',
  admin: 'Edited in the portal',
  browser: 'Sent from the page by update(); visitors can change it, so it is not verified',
};

export const TYPE_LABELS: Record<AttributeType | 'widget', string> = {
  string: 'Text',
  number: 'Number',
  boolean: 'Yes or no',
  date: 'Date',
  widget: 'Widget',
};

export interface BuiltInSegment {
  id: string;
  name: string;
  filter: ContactCondition[];
  /** People who talked to a widget or signed in; the list's default. */
  all?: boolean;
}

export const BUILT_IN_SEGMENTS: readonly BuiltInSegment[] = [
  { id: 'all', name: 'All contacts', filter: [], all: true },
  { id: 'identified', name: 'Identified', filter: [{ attr: 'identified', op: 'true' }] },
  { id: 'returning', name: 'Returning', filter: [{ attr: 'returning', op: 'true' }] },
  { id: 'new', name: 'New this week', filter: [{ attr: 'first_seen', op: 'within_days', value: 7 }] },
];

function base64UrlEncode(text: string): string {
  const bytes = new TextEncoder().encode(text);
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function base64UrlDecode(text: string): string {
  const base64 = text.replace(/-/g, '+').replace(/_/g, '/');
  const binary = atob(base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '='));
  return new TextDecoder().decode(Uint8Array.from(binary, (char) => char.charCodeAt(0)));
}

/** base64url JSON of the conditions. */
export function encodeFilter(conditions: readonly ContactCondition[]): string {
  return base64UrlEncode(JSON.stringify(conditions));
}

/** Empty when the URL holds no filter this page wrote. */
export function decodeFilter(text: string | null): ContactCondition[] {
  if (!text) {
    return [];
  }
  try {
    const parsed: unknown = JSON.parse(base64UrlDecode(text));
    return Array.isArray(parsed)
      ? parsed.filter((item): item is ContactCondition => typeof item === 'object' && item !== null && typeof item.attr === 'string' && typeof item.op === 'string')
      : [];
  } catch {
    return [];
  }
}

/** e.g. "Plan is pro" or "Seats is between 5 and 10". */
export function describeCondition(condition: ContactCondition, attribute: FilterAttribute | undefined, widgetName?: (id: string) => string): string {
  const label = attribute?.label ?? condition.attr;
  const op = OP_LABELS[condition.op] ?? condition.op;
  if (VALUELESS_OPS.has(condition.op)) {
    return `${label} ${op}`;
  }
  const value = condition.value;
  if (condition.op === 'between' && Array.isArray(value)) {
    return `${label} ${op} ${value[0]} and ${value[1]}`;
  }
  if (condition.op === 'within_days') {
    return `${label} ${op} ${String(value)} ${value === 1 ? 'day' : 'days'}`;
  }
  if (attribute?.type === 'widget' && widgetName) {
    return `${label} ${op} ${widgetName(String(value))}`;
  }
  return `${label} ${op} ${String(value)}`;
}

export function formatAttributeValue(value: unknown, type: string): string {
  if (typeof value === 'boolean') {
    return value ? 'Yes' : 'No';
  }
  if (type === 'date' && typeof value === 'string') {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }
  return value === null || value === undefined ? '' : String(value);
}
