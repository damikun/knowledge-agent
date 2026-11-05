import { ChevronDown, ExternalLink } from 'lucide-react';
import { useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { Popover } from '../../components/ui/Popover';
import { StatusDot } from '../../components/ui/StatusDot';
import type { Tone } from '../../components/ui/tone';
import { useProfiles } from '../models/api';
import { findEmbeddingProfile } from '../models/embedding';
import { useSystemInfo } from './api';
import { describeModel, findProfile, isApiUnreachable, ORCHESTRATOR_PROFILE } from './status';
import { useReturnState } from '../../lib/returnTo';

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-1">
      <dt className="text-fg-muted">{label}</dt>
      <dd className="min-w-0 truncate text-right font-medium text-fg">{children}</dd>
    </div>
  );
}

export function SystemStatus() {
  const returnState = useReturnState();
  const info = useSystemInfo();
  const profiles = useProfiles();
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);

  const orchestrator = findProfile(profiles.data, ORCHESTRATOR_PROFILE);
  const embedding = findEmbeddingProfile(profiles.data);
  const unreachable = isApiUnreachable(info.error) || isApiUnreachable(profiles.error);
  const loading = info.isPending || profiles.isPending;

  let tone: Tone = 'success';
  let summary = describeModel(orchestrator) ?? 'No chat model';
  if (loading) {
    tone = 'neutral';
    summary = 'Connecting…';
  } else if (unreachable) {
    tone = 'danger';
    summary = 'API unreachable';
  } else if (info.isError || profiles.isError || orchestrator?.error || !orchestrator?.effective || (embedding && !embedding.effective)) {
    tone = 'warning';
  }

  return (
    <>
      <Button
        ref={buttonRef}
        variant="ghost"
        size="sm"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label={`System status: ${summary}`}
        className="max-w-64"
      >
        <StatusDot tone={tone} pulse={loading} />
        <span className="hidden truncate sm:inline">{summary}</span>
        <ChevronDown className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
      </Button>
      <Popover open={open} onClose={() => setOpen(false)} anchorRef={buttonRef} label="System status" align="end" className="w-80 p-3">
        {unreachable ? (
          <p className="text-sm text-danger-text">
            The API is not responding. Start the backend (Aspire AppHost, docker compose or <code>dotnet run</code>) and this
            page reconnects automatically.
          </p>
        ) : (
          <div className="space-y-3">
            <section>
              <h3 className="mb-1 text-xs font-medium text-fg-muted">Models</h3>
              <dl className="text-xs">
                <Row label="Chat (orchestrator)">{describeModel(orchestrator) ?? orchestrator?.error ?? '–'}</Row>
                <Row label="Embeddings">
                  {embedding?.effective
                    ? `${embedding.effective.providerKey} · ${embedding.effective.model}${embedding.effective.dimensions ? ` (${embedding.effective.dimensions}d)` : ''}`
                    : (embedding?.error ?? '–')}
                </Row>
              </dl>
            </section>
            {info.data ? (
              <>
                <section>
                  <h3 className="mb-1 text-xs font-medium text-fg-muted">Stores</h3>
                  <dl className="text-xs">
                    <Row label="Database">{info.data.persistenceProvider}</Row>
                    <Row label="Vector store">{info.data.vectorStoreProvider}</Row>
                    <Row label="Prompts">{info.data.promptStore}</Row>
                    <Row label="Researcher sub-agent">{info.data.researcherEnabled ? 'Enabled' : 'Disabled'}</Row>
                  </dl>
                </section>
                <section>
                  <h3 className="mb-1.5 text-xs font-medium text-fg-muted">Providers</h3>
                  <div className="flex flex-wrap gap-1">
                    {info.data.providers.map((provider) => (
                      <Badge key={provider.key} tone={provider.configured ? 'success' : 'neutral'} title={provider.configured ? 'Configured' : 'Not configured'}>
                        {provider.key}
                      </Badge>
                    ))}
                  </div>
                </section>
                <div className="flex items-center justify-between border-t border-border-subtle pt-2 text-xs text-fg-muted">
                  <span>
                    v{info.data.version} · {info.data.environment}
                  </span>
                  <span className="flex items-center gap-3">
                    <a href="/scalar" target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 hover:text-fg">
                      API docs <ExternalLink className="size-3" aria-hidden />
                    </a>
                    <Link to="/models" state={returnState} onClick={() => setOpen(false)} className="text-accent-text hover:underline">
                      Models
                    </Link>
                  </span>
                </div>
              </>
            ) : null}
          </div>
        )}
      </Popover>
    </>
  );
}
