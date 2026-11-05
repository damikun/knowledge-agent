import { CircleCheck, CircleX, Settings2 } from 'lucide-react';
import { Button } from './Button';
import { Alert } from './feedback';

export interface TestResultProps {
  result: { ok: boolean; message: string; details?: string[] };
  fixLabel?: string;
  onFix?: () => void;
}

export function TestResult({ result, fixLabel, onFix }: TestResultProps) {
  return (
    <Alert
      tone={result.ok ? 'success' : 'danger'}
      icon={result.ok ? CircleCheck : CircleX}
      title={result.ok ? 'Test passed' : 'Test failed'}
      live
      action={
        result.ok || !onFix || !fixLabel ? null : (
          <Button size="sm" variant="secondary" icon={Settings2} onClick={onFix}>
            {fixLabel}
          </Button>
        )
      }
    >
      {result.message ? <p>{result.message}</p> : null}
      {result.details?.length ? (
        <ul className="mt-1 list-disc space-y-0.5 pl-4 text-xs">
          {result.details.map((detail) => (
            <li key={detail}>{detail}</li>
          ))}
        </ul>
      ) : null}
    </Alert>
  );
}
