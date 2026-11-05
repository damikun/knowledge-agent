import { useEffect, useState } from 'react';

const SEARCH_DEBOUNCE_MS = 300;

export interface DebouncedSearch {
  input: string;
  setInput: (value: string) => void;
  /** The trimmed input once typing pauses; an emptied input applies at once. */
  query: string;
  flush: () => void;
  clear: () => void;
}

export function useDebouncedSearch(delayMs = SEARCH_DEBOUNCE_MS): DebouncedSearch {
  const [input, setInput] = useState('');
  const [applied, setApplied] = useState('');
  const typed = input.trim();

  useEffect(() => {
    if (typed === applied) {
      return undefined;
    }
    const timer = window.setTimeout(() => setApplied(typed), typed === '' ? 0 : delayMs);
    return () => window.clearTimeout(timer);
  }, [typed, applied, delayMs]);

  return {
    input,
    setInput,
    query: typed === '' ? '' : applied,
    flush: () => setApplied(typed),
    clear: () => {
      setInput('');
      setApplied('');
    },
  };
}
