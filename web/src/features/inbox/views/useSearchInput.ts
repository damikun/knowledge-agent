import { useEffect, useEffectEvent, useState } from 'react';
import { useDebouncedValue } from '../../../lib/hooks/useDebouncedValue';

/** Applies typed words once typing pauses, and follows the applied query when it changes from outside (cleared filters, back navigation). */
export function useSearchInput(applied: string, onApply: (query: string) => void, delayMs = 300) {
  const [input, setInput] = useState(applied);
  const [seen, setSeen] = useState(applied);
  if (applied !== seen) {
    setSeen(applied);
    if (applied.trim() !== input.trim()) {
      setInput(applied);
    }
  }

  const typed = useDebouncedValue(input.trim(), delayMs);
  const apply = useEffectEvent((query: string) => {
    if (query !== applied.trim()) {
      onApply(query);
    }
  });
  useEffect(() => apply(typed), [typed]);

  return { input, setInput };
}
