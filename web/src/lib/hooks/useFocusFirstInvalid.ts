import { useEffect, useRef } from 'react';

const INVALID = '[aria-invalid="true"]';

/** Focuses the first invalid field once a failed submit, client or server side, has rendered its errors. */
export function useFocusFirstInvalid<T extends HTMLElement = HTMLFormElement>(invalidAttempts: number, validationError: unknown, selector = INVALID) {
  const ref = useRef<T>(null);
  useEffect(() => {
    if (invalidAttempts > 0 || validationError) {
      ref.current?.querySelector<HTMLElement>(selector)?.focus();
    }
  }, [invalidAttempts, validationError, selector]);
  return ref;
}
