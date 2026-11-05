import { useId, type ReactNode } from 'react';
import { ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { Page } from '../../components/ui/Page';
import { useWidgets } from '../widgets/api';
import { AlertSettingsCard } from './AlertSettingsCard';
import { useIntegrations } from './api';
import { EmailSettingsCard } from './EmailSettingsCard';
import { SlackSettingsCard } from './SlackSettingsCard';

function CardsSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Loading the notification settings
      </p>
      <div aria-hidden className="grid gap-4 lg:grid-cols-2">
        {Array.from({ length: 2 }, (_, index) => (
          <Skeleton key={index} shape="card" className="h-80" />
        ))}
      </div>
    </>
  );
}

function Group({ title, description, children }: { title: string; description: string; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="space-y-3">
      <div>
        <h2 id={id} className="text-section text-fg">
          {title}
        </h2>
        <p className="text-small text-fg-muted">{description}</p>
      </div>
      {children}
    </section>
  );
}

export function NotificationsPage() {
  const integrations = useIntegrations();
  const widgets = useWidgets();

  return (
    <Page>
      <PageHeader
        title="Notifications"
        description="How your team hears about visitors who wait for a person and about problems, and where it can reply: by email or in Slack. The Inbox always gets every request too. Tools for your agents live under Tools."
      />
      <Group
        title="Email, Slack and alerts"
        description="Each widget chooses which of these get its requests for a person."
      >
        {integrations.isPending ? (
          <CardsSkeleton />
        ) : integrations.isError ? (
          <ErrorMessage error={integrations.error} title="Could not load the notification settings" />
        ) : (
          <div className="grid items-start gap-4 lg:grid-cols-2">
            <EmailSettingsCard email={integrations.data.email} widgets={widgets.data} />
            <SlackSettingsCard slack={integrations.data.slack} widgets={widgets.data} />
            <AlertSettingsCard />
          </div>
        )}
      </Group>
    </Page>
  );
}
