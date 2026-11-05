import { Check, Copy } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { capitalize, formatNumber } from '../../lib/format';
import { useCopyToClipboard } from '../../lib/hooks/useCopyToClipboard';
import type { PromptMessageDto } from './types';

export function roleLabel(role: string): string {
  return capitalize(role);
}

function CopyButton({ text }: { text: string }) {
  const { copied, supported, copy } = useCopyToClipboard(1500);
  if (!supported) {
    return null;
  }
  return (
    <Button variant="ghost" size="sm" icon={copied ? Check : Copy} className="-my-1 -mr-1.5" onClick={() => copy(text)}>
      {copied ? 'Copied' : 'Copy'}
    </Button>
  );
}

export function RenderedOutput({ text, messages }: { text: string | null; messages?: PromptMessageDto[] }) {
  const all = text ?? (messages ?? []).map((m) => `[${m.role}]\n${m.content}`).join('\n\n');
  const chars = all.length;
  return (
    <div className="overflow-hidden rounded-lg border border-border-subtle">
      <div className="flex items-center justify-between gap-2 border-b border-border-subtle bg-surface-subtle px-3.5 py-1.5 text-xs text-fg-muted">
        <span className="tabular-nums">
          {formatNumber(chars)} characters · ~{formatNumber(Math.ceil(chars / 4))} tokens
        </span>
        <CopyButton text={all} />
      </div>
      {text !== null ? (
        <pre className="max-h-[32rem] overflow-auto px-3.5 py-3 font-mono text-small leading-5 break-words whitespace-pre-wrap text-fg" data-testid="rendered-output">
          {text}
        </pre>
      ) : (
        <ol className="max-h-[32rem] divide-y divide-border-subtle overflow-auto" data-testid="rendered-output">
          {(messages ?? []).map((message, index) => (
            <li key={index} className="px-3.5 py-3">
              <p className="mb-1 text-xs font-medium text-fg-muted">{roleLabel(message.role)}</p>
              <pre className="font-mono text-small leading-5 break-words whitespace-pre-wrap text-fg">{message.content}</pre>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
