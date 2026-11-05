import { ArrowDownWideNarrow, ClipboardCheck, Cpu, Layers, MessagesSquare, ScanSearch, TextQuote, WandSparkles, type LucideIcon } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { StatusLabel } from '../../components/ui/StatusDot';
import type { Tone } from '../../components/ui/tone';
import { Button, buttonClass } from '../../components/ui/Button';
import { ProfileSheet } from './ProfileSheet';
import { isRerankingOff, profileRole } from './profileSettings';
import { PROVIDERS_PATH } from './providerSettings';
import type { ModelProfileDto, ProviderDto } from './types';

const ROLE_ICONS: Readonly<Record<string, LucideIcon>> = {
  orchestrator: MessagesSquare,
  embedding: Layers,
  rerank: ArrowDownWideNarrow,
  contextualizer: TextQuote,
  researcher: ScanSearch,
  utility: WandSparkles,
  judge: ClipboardCheck,
};

// Without these the agent can't answer or read sources; the other jobs degrade quietly.
const ESSENTIAL = new Set(['orchestrator', 'embedding']);

function status(profile: ModelProfileDto): { tone: Tone; label: string } {
  if (profile.effective && !profile.error) {
    return { tone: 'success', label: 'Ready' };
  }
  if (isRerankingOff(profile)) {
    return { tone: 'neutral', label: 'Off' };
  }
  if (profile.effective) {
    return { tone: 'warning', label: 'Check settings' };
  }
  return { tone: ESSENTIAL.has(profile.name.toLowerCase()) ? 'danger' : 'warning', label: 'Not connected' };
}

export function RoleCard({ profile, providers }: { profile: ModelProfileDto; providers: ProviderDto[] }) {
  const [open, setOpen] = useState(false);
  const role = profileRole(profile.name);
  const Icon = ROLE_ICONS[profile.name.toLowerCase()] ?? Cpu;
  const { tone, label } = status(profile);
  const effective = profile.effective;
  const off = isRerankingOff(profile);
  const changed = profile.override !== null && profile.override !== undefined;
  const headingId = `profile-${profile.name.replace(/[^\w-]/g, '_')}-heading`;

  return (
    <section aria-labelledby={headingId} className="flex flex-col rounded-2xl border border-border bg-surface p-4">
      <div className="flex items-start gap-3">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-secondary">
          <Icon className="size-5" aria-hidden />
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center justify-between gap-x-2 gap-y-1">
            <h3 id={headingId} className="text-sm font-semibold text-fg">
              {role.label}
            </h3>
            <StatusLabel tone={tone}>
              {label}
            </StatusLabel>
          </div>
          <p className="mt-0.5 text-small text-fg-secondary">{role.description}</p>
        </div>
      </div>

      <div className="mt-4 flex-1 rounded-lg bg-surface-subtle px-3 py-2.5">
        {effective ? (
          <>
            <p className="truncate font-mono text-small text-fg" title={effective.model}>
              {effective.model}
            </p>
            <p className="mt-0.5 truncate text-xs text-fg-muted">
              {effective.providerKey}
              {effective.providerType !== effective.providerKey ? ` (${effective.providerType})` : ''}
              {effective.dimensions ? `, ${effective.dimensions} dimensions` : ''}
            </p>
          </>
        ) : off ? (
          <p className="text-small text-fg-secondary">Search results keep their order. Add a Voyage key, or choose a chat model to rerank with.</p>
        ) : (
          <p className="text-small text-fg-secondary">No provider is ready for this job yet.</p>
        )}
        {effective && profile.error ? (
          <p className="mt-1 line-clamp-2 text-xs text-warning-text" title={profile.error}>
            {profile.error}
          </p>
        ) : null}
      </div>

      <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
        <p className="min-w-0 truncate text-xs text-fg-muted">
          <span className="font-mono">{profile.name}</span>
          {changed ? ', changed in the portal' : ''}
        </p>
        <div className="flex shrink-0 gap-1.5">
          {!effective && !off ? (
            <Link to={PROVIDERS_PATH} className={buttonClass({ variant: 'secondary', size: 'sm' })}>
              Connect
            </Link>
          ) : null}
          <Button size="sm" onClick={() => setOpen(true)} aria-label={`Change the ${role.label.toLowerCase()}`}>
            Change
          </Button>
        </div>
      </div>
      <ProfileSheet open={open} onClose={() => setOpen(false)} profile={profile} providers={providers} />
    </section>
  );
}
