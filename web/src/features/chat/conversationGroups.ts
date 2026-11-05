import type { ConversationListItem } from './types';

export interface ConversationGroup {
  key: string;
  label: string;
  items: ConversationListItem[];
}

const DAY_MS = 86_400_000;

function startOfDay(date: Date): number {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
}

/** Whole calendar days between the date and today, in local time. */
function daysAgo(date: Date, now: Date): number {
  return Math.round((startOfDay(now) - startOfDay(date)) / DAY_MS);
}

const monthFormat = new Intl.DateTimeFormat(undefined, { month: 'long' });
const monthYearFormat = new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric' });

interface Bucket {
  key: string;
  label: string;
  order: string;
}

function bucketFor(item: ConversationListItem, now: Date): Bucket {
  if (item.pinned) {
    return { key: 'pinned', label: 'Pinned', order: '0' };
  }
  const date = new Date(item.updatedAt);
  if (Number.isNaN(date.getTime())) {
    return { key: 'older', label: 'Older', order: '9' };
  }
  const days = daysAgo(date, now);
  if (days <= 0) {
    return { key: 'today', label: 'Today', order: '1' };
  }
  if (days === 1) {
    return { key: 'yesterday', label: 'Yesterday', order: '2' };
  }
  if (days <= 7) {
    return { key: 'week', label: 'Previous 7 days', order: '3' };
  }
  if (days <= 30) {
    return { key: 'month', label: 'Previous 30 days', order: '4' };
  }
  const month = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
  const label = date.getFullYear() === now.getFullYear() ? monthFormat.format(date) : monthYearFormat.format(date);
  // Later months sort first: invert the digits so a plain string sort works.
  const inverted = month.replace(/\d/g, (digit) => String(9 - Number(digit)));
  return { key: month, label, order: `5${inverted}` };
}

export function groupConversations(items: readonly ConversationListItem[], now: Date = new Date()): ConversationGroup[] {
  const groups = new Map<string, ConversationGroup & { order: string }>();
  for (const item of items) {
    const bucket = bucketFor(item, now);
    const group = groups.get(bucket.key);
    if (group) {
      group.items.push(item);
    } else {
      groups.set(bucket.key, { key: bucket.key, label: bucket.label, order: bucket.order, items: [item] });
    }
  }
  return [...groups.values()]
    .sort((a, b) => a.order.localeCompare(b.order))
    .map(({ key, label, items: grouped }) => ({ key, label, items: grouped.sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)) }));
}

const timeFormat = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' });
const weekdayFormat = new Intl.DateTimeFormat(undefined, { weekday: 'short' });
const dayMonthFormat = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' });
const fullDateFormat = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' });

/** Short enough for the end of a row: the time today, the weekday this week, else the date. */
export function formatRowTime(value: string, now: Date = new Date()): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  const days = daysAgo(date, now);
  if (days <= 0) {
    return timeFormat.format(date);
  }
  if (days < 7) {
    return weekdayFormat.format(date);
  }
  return date.getFullYear() === now.getFullYear() ? dayMonthFormat.format(date) : fullDateFormat.format(date);
}
