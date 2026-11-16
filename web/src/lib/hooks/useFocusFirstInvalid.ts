import { useEffect, useRef } from 'react';

const INVALID = '[aria-invalid="true"]';

export function useFocusFirstInvalid<T extends HTMLElement = HTMLFormElement>(invalidAttempts: number, validationError: unknown, selector = INVALID) {
  const ref = useRef<T>(null);
  useEffect(() => {
    if (invalidAttempts > 0 || validationError) {
      ref.current?.querySelector<HTMLElement>(selector)?.focus();
    }
  }, [invalidAttempts, validationError, selector]);
  return ref;
}
