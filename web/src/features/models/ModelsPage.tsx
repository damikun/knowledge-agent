import { Cpu } from 'lucide-react';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { Card, EmptyState, ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { Page } from '../../components/ui/Page';
import { useScrollToHash } from '../../lib/hooks/useScrollToHash';
import { useProfiles, useProviders } from './api';
import { MAIN_PROFILES, profileRole } from './profileSettings';
import { ProvidersSection } from './ProvidersSection';
import { RoleCard } from './RoleCard';
import type { ModelProfileDto } from './types';

const OTHER_ORDER = ['researcher', 'utility', 'judge'];

function isMain(profile: ModelProfileDto): boolean {
  return (MAIN_PROFILES as readonly string[]).includes(profile.name.toLowerCase());
}

function RolesSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Loading models
      </p>
      <div aria-hidden className="grid gap-4 md:grid-cols-2">
        {Array.from({ length: 4 }, (_, index) => (
          <Skeleton key={index} shape="card" className="h-48" />
        ))}
      </div>
    </>
  );
}

export function ModelsPage() {
  const profiles = useProfiles();
  const providers = useProviders();
  useScrollToHash();

  const all = profiles.data ?? [];
  const main = MAIN_PROFILES.flatMap((name) => all.find((profile) => profile.name.toLowerCase() === name) ?? []);
  const others = all
    .filter((profile) => !isMain(profile))
    .sort((a, b) => {
      const ia = OTHER_ORDER.indexOf(a.name.toLowerCase());
      const ib = OTHER_ORDER.indexOf(b.name.toLowerCase());
      return (ia === -1 ? 99 : ia) - (ib === -1 ? 99 : ib) || a.name.localeCompare(b.name);
    });

  return (
    <Page>
      <PageHeader title="AI models" description="The models your agents think and search with: connect a provider, then pick the model for each job." />

      <section aria-labelledby="roles-heading" className="space-y-3">
        <div>
          <h2 id="roles-heading" className="text-section text-fg">
            Models in use
          </h2>
          <p className="mt-0.5 max-w-160 text-sm text-fg-secondary">Each job uses its own model. Jobs set to auto take the first provider that is ready.</p>
        </div>
        {profiles.isPending ? (
          <RolesSkeleton />
        ) : profiles.isError ? (
          <ErrorMessage error={profiles.error} title="Could not load the models" />
        ) : all.length === 0 ? (
          <Card>
            <EmptyState icon={Cpu} title="No model profiles" description="The API did not report any profiles (AI:Profiles)." />
          </Card>
        ) : (
          <div className="grid gap-4 md:grid-cols-2">
            {main.map((profile) => (
              <RoleCard key={profile.name} profile={profile} providers={providers.data ?? []} />
            ))}
          </div>
        )}
      </section>

      <ProvidersSection />

      {others.length > 0 ? (
        <CollapsibleSection title="Advanced" description={`More jobs: ${others.map((profile) => profileRole(profile.name).label.replace(/ model$/, '').toLowerCase()).join(', ')}.`}>
          <div className="grid gap-4 md:grid-cols-2">
            {others.map((profile) => (
              <RoleCard key={profile.name} profile={profile} providers={providers.data ?? []} />
            ))}
          </div>
        </CollapsibleSection>
      ) : null}
    </Page>
  );
}
