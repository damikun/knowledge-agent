import { useEffect, useState } from 'react';

export interface CopyToClipboard {
  copied: boolean;
  failed: boolean;
  supported: boolean;
  copy: (text: string) => void;
}

/** Copies text, and says so for `resetMs`; a copy within that time starts the wait again. */
export function useCopyToClipboard(resetMs = 2000): CopyToClipboard {
  const [copies, setCopies] = useState(0);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (copies === 0) {
      return undefined;
    }
    const timer = window.setTimeout(() => setCopies(0), resetMs);
    return () => window.clearTimeout(timer);
  }, [copies, resetMs]);

  const clipboard = typeof navigator === 'undefined' ? undefined : navigator.clipboard;
  const copy = (text: string) => {
    setFailed(false);
    if (!clipboard) {
      setFailed(true);
      return;
    }
    clipboard.writeText(text).then(
      () => setCopies((count) => count + 1),
      () => setFailed(true),
    );
  };

  return { copied: copies > 0, failed, supported: Boolean(clipboard), copy };
}
