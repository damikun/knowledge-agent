import { useMutation, useQueryClient } from '@tanstack/react-query';
import { RotateCcw } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Button } from '../../components/ui/Button';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { SideSheet } from '../../components/ui/SideSheet';
import { toast } from '../../components/ui/Toast';
import { formatNumber, formatRelativeTime } from '../../lib/format';
import { modelKeys, modelsApi } from './api';
import { ProfileForm } from './ProfileForm';
import { isEmbeddingProfile, isRerankingOff, profileRole } from './profileSettings';
import type { ModelProfileDto, ModelSettingsDto, ProviderDto } from './types';

const SOURCE_LABELS: Record<string, string> = {
  configuration: 'Configuration',
  override: 'Changed in the portal',
  prompt: 'The prompt',
  request: 'The request',
};

interface Row {
  label: string;
  configured: ReactNode;
  override: ReactNode;
  effective: ReactNode;
}

function value(item: string | number | null | undefined, format?: (v: number) => string): ReactNode {
  if (item === null || item === undefined || item === '') {
    return <span className="font-sans text-fg-muted">–</span>;
  }
  return typeof item === 'number' && format ? format(item) : String(item);
}

function model(settings: ModelSettingsDto): ReactNode {
  return settings.model ? settings.model : <span className="font-sans text-fg-muted">Provider default</span>;
}

function rows(profile: ModelProfileDto): Row[] {
  const c = profile.configured;
  const o = profile.override;
  const e = profile.effective;
  const common: Row[] = [
    { label: 'Provider', configured: value(c.provider), override: o ? value(o.provider) : null, effective: value(e?.providerKey) },
    { label: 'Model', configured: model(c), override: o ? model(o) : null, effective: value(e?.model) },
  ];
  if (isEmbeddingProfile(profile)) {
    return [...common, { label: 'Dimensions', configured: value(c.dimensions), override: o ? value(o.dimensions) : null, effective: value(e?.dimensions) }];
  }
  return [
    ...common,
    { label: 'Reasoning effort', configured: value(c.reasoningEffort), override: o ? value(o.reasoningEffort) : null, effective: value(e?.reasoningEffort) },
    { label: 'Reasoning output', configured: value(c.reasoningOutput), override: o ? value(o.reasoningOutput) : null, effective: value(e?.reasoningOutput) },
    {
      label: 'Max output tokens',
      configured: value(c.maxOutputTokens, formatNumber),
      override: o ? value(o.maxOutputTokens, formatNumber) : null,
      effective: value(e?.maxOutputTokens, formatNumber),
    },
    { label: 'Temperature', configured: value(c.temperature), override: o ? value(o.temperature) : null, effective: value(e?.temperature) },
  ];
}

function ProfileDetails({ profile }: { profile: ModelProfileDto }) {
  const hasOverride = profile.override !== null && profile.override !== undefined;
  return (
    <div className="space-y-2">
      <div className="overflow-x-auto">
        <table className="w-full min-w-96 border-separate border-spacing-0 text-left text-small" aria-label={`Settings of the ${profileRole(profile.name).label.toLowerCase()}`}>
          <thead>
            <tr className="text-xs text-fg-muted">
              <th scope="col" className="border-b border-border py-1.5 pr-3 font-medium">
                <span className="sr-only">Setting</span>
              </th>
              <th scope="col" className="border-b border-border py-1.5 pr-3 font-medium">
                Configured
              </th>
              <th scope="col" className="border-b border-border py-1.5 pr-3 font-medium">
                Portal
              </th>
              <th scope="col" className="border-b border-border py-1.5 font-medium text-fg">
                In use
              </th>
            </tr>
          </thead>
          <tbody>
            {rows(profile).map((row, index) => (
              <tr key={row.label}>
                <th scope="row" className="border-b border-border-subtle py-1.5 pr-3 font-medium whitespace-nowrap text-fg-secondary">
                  {row.label}
                </th>
                <td className="border-b border-border-subtle py-1.5 pr-3 font-mono text-xs text-fg-secondary">{row.configured}</td>
                <td className="border-b border-border-subtle py-1.5 pr-3 font-mono text-xs text-fg-secondary">
                  {hasOverride ? row.override : index === 0 ? <span className="font-sans text-fg-muted">Not changed</span> : null}
                </td>
                <td className="border-b border-border-subtle py-1.5 font-mono text-xs font-medium text-fg">{row.effective}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="text-xs text-fg-muted">
        {profile.effective ? `In use from: ${SOURCE_LABELS[profile.effective.source.toLowerCase()] ?? profile.effective.source}.` : null}
        {hasOverride && profile.overrideUpdatedAt ? ` Changed ${formatRelativeTime(profile.overrideUpdatedAt)}.` : null}
      </p>
    </div>
  );
}

export interface ProfileSheetProps {
  open: boolean;
  onClose: () => void;
  profile: ModelProfileDto;
  providers: ProviderDto[];
}

/** Configuration settings stay, so the change can be undone. */
export function ProfileSheet({ open, onClose, profile, providers }: ProfileSheetProps) {
  const queryClient = useQueryClient();
  const role = profileRole(profile.name);
  const [confirmReset, setConfirmReset] = useState(false);
  const hasOverride = profile.override !== null && profile.override !== undefined;
  const reset = useMutation({
    mutationFn: () => modelsApi.resetProfile(profile.name),
    onSuccess: () => {
      setConfirmReset(false);
      void queryClient.invalidateQueries({ queryKey: modelKeys.profiles });
      toast({ title: `${role.label} reset`, description: 'It uses the settings from configuration again.', tone: 'success' });
      onClose();
    },
  });
  const close = () => {
    setConfirmReset(false);
    reset.reset();
    onClose();
  };

  return (
    <SideSheet open={open} onClose={close} title={role.label} description={role.description}>
      <div className="space-y-6">
        {profile.error && isRerankingOff(profile) ? (
          <Alert tone="info" title="Reranking is off">
            Search results keep their fused order. Add a Voyage API key under Providers, or choose a chat provider to rerank with an LLM. Details:{' '}
            {profile.error}
          </Alert>
        ) : profile.error ? (
          <Alert tone={profile.effective ? 'warning' : 'danger'} title={profile.effective ? 'Check these settings' : 'No provider can serve this job'}>
            {profile.error}
          </Alert>
        ) : null}
        <ProfileForm profile={profile} providers={providers} onDone={close} />
        <CollapsibleSection title="Details" description="Where each setting comes from." defaultOpen={hasOverride}>
          <div className="space-y-4">
            <ProfileDetails profile={profile} />
            {hasOverride ? (
              // Confirmed inline: a dialog over the sheet would stack two modals.
              confirmReset ? (
                <Alert
                  tone="warning"
                  title="Undo the change made in the portal?"
                  live
                  action={
                    <div className="flex gap-1.5">
                      <Button size="sm" variant="ghost" onClick={() => setConfirmReset(false)} disabled={reset.isPending}>
                        Keep
                      </Button>
                      <Button size="sm" variant="danger" loading={reset.isPending} onClick={() => reset.mutate()}>
                        Reset
                      </Button>
                    </div>
                  }
                >
                  It uses the settings from configuration again.
                </Alert>
              ) : (
                <Button size="sm" variant="ghost" icon={RotateCcw} onClick={() => setConfirmReset(true)}>
                  Reset to configuration
                </Button>
              )
            ) : null}
            {reset.isError ? <ErrorMessage error={reset.error} /> : null}
          </div>
        </CollapsibleSection>
      </div>
    </SideSheet>
  );
}
