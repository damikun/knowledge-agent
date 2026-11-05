import { ExternalLink } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link } from 'react-router';
import { buttonClass } from '../../components/ui/Button';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { SideSheet } from '../../components/ui/SideSheet';
import { StatusDot } from '../../components/ui/StatusDot';
import { formatDateTime } from '../../lib/format';
import { useMediaQuery } from '../../lib/hooks/useMediaQuery';
import { useJobsOverview, useSystemHealth } from './api';
import { describeNode, InstallationDiagram, NODE_ICONS } from './InstallationDiagram';
import {
  BOUNDARIES,
  buildInstallation,
  installationProblems,
  installationSize,
  STATUS_LABELS,
  STATUS_TONES,
  type ConfigureLink,
  type InstallationNode,
  type InstallationNodeId,
  type InstallationZone,
} from './installation';
import { useReturnState } from '../../lib/returnTo';

type View = 'diagram' | 'list';

const VIEWS = [
  { value: 'diagram', label: 'Diagram' },
  { value: 'list', label: 'Show as list' },
] as const;

const ZONE_LABELS: Record<InstallationZone, string> = {
  outside: 'Outside your server',
  server: BOUNDARIES.server.label,
  tracing: BOUNDARIES.tracing.label,
};

const WIDE = '(min-width: 1000px)';

function Status({ node }: { node: InstallationNode }) {
  if (!node.status) {
    return <span className="text-xs text-fg-muted">Outside</span>;
  }
  return (
    <span className="inline-flex items-center gap-1.5 text-xs font-medium whitespace-nowrap text-fg-secondary">
      <StatusDot tone={STATUS_TONES[node.status]} />
      {STATUS_LABELS[node.status]}
    </span>
  );
}

function ConfigureAction({ link }: { link: ConfigureLink }) {
  const returnState = useReturnState();
  if (link.external) {
    return (
      <a href={link.href} target="_blank" rel="noopener noreferrer" className={buttonClass({ variant: 'secondary' })}>
        {link.label}
        <ExternalLink className="size-4" aria-hidden />
        <span className="sr-only">(new tab)</span>
      </a>
    );
  }
  return (
    <Link to={link.href} state={returnState} className={buttonClass({ variant: 'secondary' })}>
      {link.label}
    </Link>
  );
}

function PartDetails({ node }: { node: InstallationNode }) {
  return (
    <div className="space-y-5 text-sm">
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3">
        <dt className="text-fg-muted">State</dt>
        <dd className="min-w-0">
          <Status node={node} />
          <p className="mt-1 text-fg-secondary">{node.detail}</p>
        </dd>
        {node.checkedAt ? (
          <>
            <dt className="text-fg-muted">Checked</dt>
            <dd>
              <time dateTime={node.checkedAt}>{formatDateTime(node.checkedAt)}</time>
            </dd>
          </>
        ) : null}
        {node.image ? (
          <>
            <dt className="text-fg-muted">Runs as</dt>
            <dd className="min-w-0 font-mono text-xs leading-5 break-words text-fg">{node.image}</dd>
          </>
        ) : null}
      </dl>
      {node.configure ? (
        <div>
          <h3 className="text-sm font-medium text-fg">Where to set it up</h3>
          <div className="mt-2">
            <ConfigureAction link={node.configure} />
          </div>
        </div>
      ) : null}
    </div>
  );
}

function PartList({ nodes, onSelect }: { nodes: InstallationNode[]; onSelect: (node: InstallationNode) => void }) {
  return (
    <div className="space-y-5">
      {(['outside', 'server', 'tracing'] as const).map((zone) => (
        <div key={zone}>
          <h3 className="text-xs font-medium tracking-wide text-fg-muted uppercase">{ZONE_LABELS[zone]}</h3>
          <ul className="mt-2 divide-y divide-border-subtle rounded-xl border border-border" aria-label={ZONE_LABELS[zone]}>
            {nodes
              .filter((node) => node.zone === zone)
              .map((node) => {
                const Icon = NODE_ICONS[node.id];
                return (
                  <li key={node.id}>
                    <button
                      type="button"
                      onClick={() => onSelect(node)}
                      aria-label={`${describeNode(node)}. Opens details.`}
                      className="flex w-full min-w-0 items-center gap-3 px-3 py-2.5 text-left hover:bg-fill-hover focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus"
                    >
                      <span aria-hidden className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-secondary">
                        <Icon className="size-4" />
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-medium text-fg">{node.title}</span>
                        <span className="block truncate text-xs text-fg-secondary">{node.role}</span>
                        {node.image ? <span className="block truncate font-mono text-[11px] text-fg-muted">{node.image}</span> : null}
                      </span>
                      <Status node={node} />
                    </button>
                  </li>
                );
              })}
          </ul>
        </div>
      ))}
    </div>
  );
}

function Summary({ problems, known }: { problems: InstallationNode[]; known: boolean }) {
  if (!known) {
    return <p className="text-small text-fg-muted">Status unknown: this API does not report the state of its parts yet.</p>;
  }
  if (problems.length === 0) {
    return (
      <p className="inline-flex items-center gap-1.5 text-small text-fg-secondary">
        <StatusDot tone="success" />
        Every part that was checked is working.
      </p>
    );
  }
  return (
    <ul className="flex flex-wrap gap-x-4 gap-y-1 text-small text-fg-secondary" aria-label="Needs attention">
      {problems.map((node) => (
        <li key={node.id} className="inline-flex items-center gap-1.5">
          <StatusDot tone={STATUS_TONES[node.status!]} />
          {node.title}: {STATUS_LABELS[node.status!].toLowerCase()}
        </li>
      ))}
    </ul>
  );
}

export function InstallationSection() {
  const health = useSystemHealth();
  const overview = useJobsOverview();
  const installation = useMemo(() => buildInstallation(health.data, overview.data?.engine), [health.data, overview.data?.engine]);
  const problems = installationProblems(installation);
  const size = useMemo(() => installationSize(), []);
  const wide = useMediaQuery(WIDE);
  const [view, setView] = useState<View>('diagram');
  const [selectedId, setSelectedId] = useState<InstallationNodeId | null>(null);
  const selected = selectedId ? installation.nodes.find((node) => node.id === selectedId) ?? null : null;
  const showDiagram = wide && view === 'diagram';

  return (
    <section aria-labelledby="installation-heading" className="space-y-3">
      <div className="flex flex-wrap items-end justify-between gap-x-4 gap-y-2">
        <div className="min-w-0">
          <h2 id="installation-heading" className="text-section text-fg">
            How your installation is built
          </h2>
          <p className="mt-0.5 text-sm text-fg-secondary">
            The parts of this installation and how they connect. Select a part to see its state and where to set it up.
          </p>
        </div>
        {wide ? <SegmentedControl label="View" options={VIEWS} value={view} onChange={setView} /> : null}
      </div>
      {health.isPending ? <p className="text-small text-fg-muted">Checking each part</p> : <Summary problems={problems} known={installation.known} />}
      <div className="rounded-2xl border border-border bg-surface p-3 shadow-card">
        {showDiagram ? (
          <InstallationDiagram installation={installation} selectedId={selectedId} onSelect={(node) => setSelectedId(node.id)} size={size} />
        ) : (
          <PartList nodes={installation.nodes} onSelect={(node) => setSelectedId(node.id)} />
        )}
      </div>
      <SideSheet open={selected !== null} onClose={() => setSelectedId(null)} title={selected?.title ?? ''} description={selected?.about}>
        {selected ? <PartDetails node={selected} /> : null}
      </SideSheet>
    </section>
  );
}
