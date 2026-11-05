const JOB_KINDS: ReadonlyArray<{ kind: string; label: string; description: string }> = [
  { kind: 'ingestion.source', label: 'Source ingestion', description: 'Extracts, chunks and embeds one source.' },
  { kind: 'ingestion.refresh-web', label: 'Web source refresh', description: 'Re-ingests every ready web page to pick up changes.' },
  { kind: 'evaluations.run', label: 'Evaluation run', description: 'Asks the questions of one evaluation run and scores the answers.' },
  { kind: 'conversations.close-idle', label: 'Close quiet conversations', description: 'Closes widget conversations that went quiet, after the time set on each widget.' },
  { kind: 'handover.mail-poll', label: 'Email replies', description: 'Reads the reply mailbox: staff answers go to the visitor, visitor answers to the chat.' },
  { kind: 'widgets.retention', label: 'Widget data cleanup', description: 'Deletes widget conversations and visitors older than the retention set on each widget.' },
];

export function jobKindLabel(kind: string): string {
  return JOB_KINDS.find((known) => known.kind === kind)?.label ?? kind;
}

export function jobKindDescription(kind: string): string | null {
  return JOB_KINDS.find((known) => known.kind === kind)?.description ?? null;
}

const WHOLE_NUMBER = /^\d+$/;

function clock(hour: string, minute: string): string {
  return `${hour.padStart(2, '0')}:${minute.padStart(2, '0')}`;
}

/** Null for uncommon shapes, which show as-is. `atTime` says whether the text names a time of day (UTC). */
export function describeCron(cron: string): { text: string; atTime: boolean } | null {
  const fields = cron.trim().split(/\s+/);
  if (fields.length !== 5) {
    return null;
  }
  const [minute = '', hour = '', dayOfMonth, month, dayOfWeek] = fields;
  if (dayOfMonth !== '*' || month !== '*' || dayOfWeek !== '*') {
    return null;
  }
  if (hour === '*') {
    if (minute === '*') {
      return { text: 'Every minute', atTime: false };
    }
    const step = /^\*\/(\d+)$/.exec(minute)?.[1];
    if (step) {
      return { text: step === '1' ? 'Every minute' : `Every ${Number(step)} minutes`, atTime: false };
    }
    return WHOLE_NUMBER.test(minute) ? { text: `Every hour at :${minute.padStart(2, '0')}`, atTime: true } : null;
  }
  return WHOLE_NUMBER.test(minute) && WHOLE_NUMBER.test(hour) ? { text: `Every day at ${clock(hour, minute)}`, atTime: true } : null;
}
