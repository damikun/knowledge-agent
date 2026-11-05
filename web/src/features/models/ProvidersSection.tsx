import { Plug } from 'lucide-react';
import { Link } from 'react-router';
import { Card, EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { useProviders } from './api';
import { ProviderCard } from './ProviderCard';
import { PROVIDERS_ANCHOR } from './providerSettings';
import { useReturnState } from '../../lib/returnTo';

function CardsSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Loading providers
      </p>
      <div aria-hidden className="grid gap-4 lg:grid-cols-2">
        {Array.from({ length: 2 }, (_, index) => (
          <Skeleton key={index} shape="card" className="h-56" />
        ))}
      </div>
    </>
  );
}

export function ProvidersSection() {
  const returnState = useReturnState();
  const providers = useProviders();
  const sorted = [...(providers.data ?? [])].sort((a, b) => a.key.localeCompare(b.key));

  return (
    <section id={PROVIDERS_ANCHOR} aria-labelledby="providers-heading" tabIndex={-1} className="scroll-mt-4 space-y-3">
      <div>
        <h2 id="providers-heading" className="text-section text-fg">
          Providers &amp; API keys
        </h2>
        <p className="mt-0.5 max-w-160 text-sm text-fg-secondary">
          Keys are stored encrypted and never shown again. A new key for anthropic or openai takes over the jobs set to{' '}
          <code className="font-mono text-xs">auto</code> right away. After changing the embedding model,{' '}
          <Link to="/sources" state={returnState} className="font-medium text-accent-text hover:underline">
            re-index your sources
          </Link>
          .
        </p>
      </div>
      {providers.isPending ? (
        <CardsSkeleton />
      ) : providers.isError ? (
        <ErrorMessage error={providers.error} title="Could not load providers" />
      ) : sorted.length === 0 ? (
        <Card>
          <EmptyState icon={Plug} title="No providers" description="The API did not report any providers (AI:Providers)." />
        </Card>
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          {sorted.map((provider) => (
            <ProviderCard key={provider.key} provider={provider} />
          ))}
        </div>
      )}
    </section>
  );
}
