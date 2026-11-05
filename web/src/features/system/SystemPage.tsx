import { ExternalLink } from 'lucide-react';
import { useId } from 'react';
import { useSearchParams } from 'react-router';
import { buttonClass } from '../../components/ui/Button';
import { Alert, PageHeader } from '../../components/ui/feedback';
import { Page } from '../../components/ui/Page';
import { TabPanel, Tabs } from '../../components/ui/Tabs';
import { safeExternalUrl } from '../../lib/format';
import { useJobsOverview, useSystemInfo } from './api';
import { HelpSettingsSection } from '../help';
import { InstallationSection } from './InstallationSection';
import { PublicAddressSection } from './PublicAddressSection';
import { SchedulesSection } from './SchedulesSection';

type SystemTab = 'general' | 'schedules' | 'overview';

const TABS = [
  { id: 'general', label: 'General' },
  { id: 'schedules', label: 'Schedules' },
  { id: 'overview', label: 'System overview' },
] as const satisfies ReadonlyArray<{ id: SystemTab; label: string }>;

function parseTab(value: string | null): SystemTab {
  return TABS.find((tab) => tab.id === value)?.id ?? 'general';
}

function ToolLink({ href, children }: { href: string; children: string }) {
  return (
    <a href={href} target="_blank" rel="noopener noreferrer" className={buttonClass({ variant: 'secondary', size: 'sm' })}>
      {children}
      <ExternalLink className="size-4" aria-hidden />
      <span className="sr-only">(new tab)</span>
    </a>
  );
}

function ToolLinks() {
  const overview = useJobsOverview();
  const info = useSystemInfo();
  const dashboardUrl = overview.data?.engine === 'inngest' ? safeExternalUrl(overview.data.dashboardUrl) : null;
  const tracesUrl = info.data?.langfuseConfigured ? safeExternalUrl(info.data.langfuseUrl) : null;
  if (!dashboardUrl && !tracesUrl) {
    return null;
  }
  return (
    <section aria-labelledby="system-tools-heading" className="space-y-2">
      <div>
        <h2 id="system-tools-heading" className="text-section text-fg">
          For troubleshooting
        </h2>
        <p className="mt-0.5 text-sm text-fg-secondary">The tools behind the product, each in a new tab.</p>
      </div>
      <div className="flex flex-wrap gap-2">
        {tracesUrl ? <ToolLink href={tracesUrl}>Answer traces</ToolLink> : null}
        {dashboardUrl ? <ToolLink href={dashboardUrl}>Run history</ToolLink> : null}
      </div>
    </section>
  );
}

// Only in development and tests, where no job service runs.
function InProcessNotice() {
  return (
    <Alert title="Development mode">
      Background work runs inside the app here, so a restart forgets what was queued.
    </Alert>
  );
}

function SchedulesTab() {
  const overview = useJobsOverview();
  return (
    <div className="space-y-6">
      <p className="text-sm text-fg-secondary">
        The work that runs on its own in the background: reading new sources, checking for replies by email, refreshing websites at night. Anything that
        fails is tried again automatically.
      </p>
      {overview.data?.engine === 'inprocess' ? <InProcessNotice /> : null}
      <SchedulesSection />
    </div>
  );
}

/** The tab is in ?tab=. */
export function SystemPage() {
  const [search, setSearch] = useSearchParams();
  const tab = parseTab(search.get('tab'));
  const idPrefix = useId();

  const openTab = (next: SystemTab) =>
    setSearch((current) => {
      const params = new URLSearchParams(current);
      if (next === 'general') {
        params.delete('tab');
      } else {
        params.set('tab', next);
      }
      return params;
    });

  return (
    <Page>
      <PageHeader title="System" description="Settings of this installation, the work that runs in the background, and how the parts fit together." />
      <Tabs idPrefix={idPrefix} label="System sections" items={TABS} value={tab} onChange={openTab} className="overflow-x-auto" />
      <TabPanel idPrefix={idPrefix} id={tab} className="focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-focus">
        {tab === 'general' ? (
          <div className="space-y-8">
            <PublicAddressSection />
            <HelpSettingsSection />
            <ToolLinks />
          </div>
        ) : tab === 'schedules' ? (
          <SchedulesTab />
        ) : (
          <InstallationSection />
        )}
      </TabPanel>
    </Page>
  );
}
