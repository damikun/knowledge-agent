import { clsx } from 'clsx';
import { CircleCheck, CircleX, KeyRound } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Alert } from '../../components/ui/feedback';
import { formatDuration, formatRelativeTime, pluralize } from '../../lib/format';
import { describeApiKey, modelFieldFor } from './providerSettings';
import type { ProviderDto, ProviderTestResultDto } from './types';

const VISIBLE_MODELS = 24;

function Detail({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-baseline gap-3">
      <dt className="w-32 shrink-0 text-fg-muted">{label}</dt>
      <dd className="min-w-0 break-all text-fg">{children}</dd>
    </div>
  );
}

function Missing({ children = '–' }: { children?: ReactNode }) {
  return <span className="font-sans text-fg-muted">{children}</span>;
}

function ApiKeyStatus({ provider }: { provider: ProviderDto }) {
  const label = describeApiKey(provider);
  if (!label) {
    return null;
  }
  const source = provider.apiKeySource;
  return (
    <span
      className={clsx(
        'inline-flex items-center gap-1.5',
        source === 'portal' && 'text-accent-text',
        source === 'none' && !provider.configured && 'text-warning-text',
      )}
    >
      <KeyRound className="size-3.5 shrink-0" aria-hidden />
      {label}
    </span>
  );
}

export function ConnectionResult({
  provider,
  result,
  disabled,
  onUseModel,
}: {
  provider: ProviderDto;
  result: ProviderTestResultDto;
  disabled: boolean;
  onUseModel: (model: string) => void;
}) {
  const [showAll, setShowAll] = useState(false);
  const models = showAll ? result.models : result.models.slice(0, VISIBLE_MODELS);
  const pickable = provider.supportsChat || provider.supportsEmbeddings;
  return (
    <Alert
      tone={result.ok ? 'success' : 'danger'}
      icon={result.ok ? CircleCheck : CircleX}
      title={
        <>
          {result.ok ? 'Connection works' : 'Connection failed'} <span className="font-normal text-fg-muted">· {formatDuration(result.durationMs)}</span>
        </>
      }
    >
      {result.message ? <p>{result.message}</p> : null}
      {result.models.length > 0 ? (
        <div className="mt-2 space-y-1.5">
          <p className="text-xs">
            {pluralize(result.models.length, 'model')} available{pickable ? '. Pick one to use it as a default:' : '.'}
          </p>
          <ul className="flex flex-wrap gap-1" aria-label={`Models reported by ${provider.key}`}>
            {models.map((model) => {
              const field = modelFieldFor(provider, model);
              return (
                <li key={model} className="max-w-full">
                  <button
                    type="button"
                    disabled={disabled || !field}
                    onClick={() => onUseModel(model)}
                    aria-label={field ? `Use ${model} as default ${field === 'defaultChatModel' ? 'chat' : 'embedding'} model` : model}
                    className="max-w-full truncate rounded-sm border border-border bg-surface px-1.5 py-0.5 font-mono text-xs text-fg-secondary hover:border-accent hover:text-accent-text disabled:pointer-events-none disabled:text-fg-disabled"
                  >
                    {model}
                  </button>
                </li>
              );
            })}
          </ul>
          {result.models.length > VISIBLE_MODELS ? (
            <button type="button" aria-expanded={showAll} onClick={() => setShowAll((value) => !value)} className="text-xs font-medium text-accent-text hover:underline">
              {showAll ? 'Show fewer' : `Show all ${result.models.length}`}
            </button>
          ) : null}
        </div>
      ) : null}
    </Alert>
  );
}

export function testSummary(provider: ProviderDto, result: ProviderTestResultDto): string {
  const models = result.models.length > 0 ? ` ${pluralize(result.models.length, 'model')} available.` : '';
  return `${provider.key}: ${result.ok ? 'connection works' : 'connection failed'} (${formatDuration(result.durationMs)}). ${result.message}${models}`;
}

export function ProviderSettings({ provider }: { provider: ProviderDto }) {
  return (
    <>
    <dl className="space-y-1 text-small">
      {provider.usesApiKey ? (
        <Detail label="API key">
          <ApiKeyStatus provider={provider} />
        </Detail>
      ) : null}
      {provider.usesEndpoint || provider.endpoint ? (
        <Detail label="Endpoint">
          {provider.endpoint ? (
            <span className="font-mono text-xs">{provider.endpoint}</span>
          ) : provider.endpointRequired ? (
            <span className="text-warning-text">Not set</span>
          ) : (
            <Missing>Provider default</Missing>
          )}
        </Detail>
      ) : null}
      {provider.supportsChat ? (
        <Detail label="Chat model">{provider.defaultChatModel ? <span className="font-mono text-xs">{provider.defaultChatModel}</span> : <Missing />}</Detail>
      ) : null}
      {provider.supportsEmbeddings ? (
        <Detail label="Embedding model">
          {provider.defaultEmbeddingModel ? <span className="font-mono text-xs">{provider.defaultEmbeddingModel}</span> : <Missing />}
          {provider.defaultEmbeddingDimensions ? <span className="text-fg-muted"> · {provider.defaultEmbeddingDimensions} dimensions</span> : null}
        </Detail>
      ) : null}
    </dl>
    {provider.hasOverride ? (
      <p className="text-xs text-fg-muted">Settings saved in the portal{provider.overrideUpdatedAt ? ` ${formatRelativeTime(provider.overrideUpdatedAt)}` : ''}</p>
    ) : null}
    </>
  );
}
