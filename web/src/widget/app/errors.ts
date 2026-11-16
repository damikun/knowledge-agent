import { isAbortError } from '../../lib/sse';
import { CONVERSATION_BUSY, DAILY_CAP_REACHED, isApiError, WidgetApiError } from './api';

type NoticeKind = 'rateLimited' | 'dailyCap' | 'busy' | 'offline' | 'error';

export interface Notice {
  kind: NoticeKind;
  text?: string;
}

export interface FieldErrors {
  name?: string;
  email?: string;
}

export type FormResult = { ok: true } | { ok: false; errors: FieldErrors };

export function noticeFor(error: unknown): Notice {
  if (!(error instanceof WidgetApiError)) {
    return { kind: 'error' };
  }
  if (error.status === 0) {
    return { kind: 'offline' };
  }
  if (error.status === 429) {
    return { kind: error.code === DAILY_CAP_REACHED ? 'dailyCap' : 'rateLimited' };
  }
  if (error.code === CONVERSATION_BUSY) {
    return { kind: 'busy' };
  }
  const message = error.errorFor('message');
  return message ? { kind: 'error', text: message } : { kind: 'error' };
}

export function contactErrors(error: unknown): FieldErrors | null {
  if (!isApiError(error, 400)) {
    return null;
  }
  const errors: FieldErrors = {};
  const name = error.errorFor('name');
  const email = error.errorFor('email');
  if (name) {
    errors.name = name;
  }
  if (email) {
    errors.email = email;
  }
  return name || email ? errors : null;
}

export function quietly(work: Promise<unknown>): Promise<void> {
  return work.then(
    () => undefined,
    (error: unknown) => {
      if (!isAbortError(error)) {
        console.debug('Knowledge Agent:', error);
      }
    },
  );
}
