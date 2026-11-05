import { Check, Copy } from 'lucide-react';
import { useState } from 'react';
import { Button } from './Button';

export function CopyBlock({
  code,
  label = 'Copy snippet',
  copiedText = 'The snippet is on your clipboard.',
}: {
  code: string;
  label?: string;
  copiedText?: string;
}) {
  const [copied, setCopied] = useState(false);
  const [failed, setFailed] = useState(false);

  const copy = () => {
    setFailed(false);
    const clipboard = typeof navigator === 'undefined' ? undefined : navigator.clipboard;
    if (!clipboard) {
      setFailed(true);
      return;
    }
    clipboard.writeText(code).then(
      () => {
        setCopied(true);
        window.setTimeout(() => setCopied(false), 2000);
      },
      () => setFailed(true),
    );
  };

  return (
    <div className="space-y-2">
      <pre className="overflow-x-auto rounded-lg border border-border-subtle bg-surface-subtle p-3 font-mono text-xs leading-5 whitespace-pre-wrap break-all text-fg">
        <code>{code}</code>
      </pre>
      <div className="flex flex-wrap items-center gap-2">
        <Button size="sm" icon={copied ? Check : Copy} onClick={copy}>
          {copied ? 'Copied' : label}
        </Button>
        <span role="status" className="text-xs text-fg-muted">
          {failed ? 'Copying is not available here. Select the code and copy it.' : copied ? copiedText : ''}
        </span>
      </div>
    </div>
  );
}
