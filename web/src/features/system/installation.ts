import type { ComponentStatus, JobEngine, SystemComponentHealthDto, SystemHealthDto } from './types';

// The installation's parts and the paths between them, drawn by the System page and by toMermaid().

export type InstallationZone = 'outside' | 'server' | 'tracing';

export type InstallationNodeId =
  | 'visitors'
  | 'team'
  | 'providers'
  | 'portal'
  | 'api'
  | 'keycloak'
  | 'postgres'
  | 'jobs'
  | 'email'
  | 'slack'
  | 'langfuse'
  | 'clickhouse'
  | 'redis'
  | 'minio';

export interface ConfigureLink {
  label: string;
  /** A portal path or an external docs URL. */
  href: string;
  external?: boolean;
}

export interface InstallationNodeDef {
  id: InstallationNodeId;
  title: string;
  role: string;
  /** Null for people and outside services. */
  image: string | null;
  about: string;
  zone: InstallationZone;
  /** Smaller cards for the stores behind the trace service. */
  small?: boolean;
  /** Overrides the usual card width. */
  width?: number;
  /** Top-left corner on the canvas. */
  x: number;
  y: number;
  configure: ConfigureLink | null;
}

export type Side = 'top' | 'right' | 'bottom' | 'left';

export interface InstallationEdgeDef {
  id: string;
  source: InstallationNodeId;
  target: InstallationNodeId;
  label?: string;
  /** Optional or asynchronous paths, drawn dashed. */
  dashed?: boolean;
  from: Side;
  to: Side;
}

const DOCS_URL = 'https://github.com/damikun/knowledge-agent/blob/main/docs';

const docs = (page: string, anchor: string, label = 'Read the configuration docs'): ConfigureLink => ({
  label,
  href: `${DOCS_URL}/${page}#${anchor}`,
  external: true,
});

const NODE_SIZE = { width: 200, height: 84 } as const;
const SMALL_NODE_SIZE = { width: 136, height: 64 } as const;

/** ClickHouse sits outside the tracing frame because the search index lives there too. */
export const BOUNDARIES = {
  server: { label: 'Your server', x: 206, y: 130, width: 1064, height: 570 },
  tracing: { label: 'Tracing', x: 936, y: 410, width: 314, height: 250 },
} as const;

export const INSTALLATION_NODES: readonly InstallationNodeDef[] = [
  {
    id: 'visitors',
    title: 'Website visitors',
    role: 'Chat in the widget',
    image: null,
    about: 'People on your website who ask questions in the chat widget.',
    zone: 'outside',
    width: 176,
    x: 0,
    y: 235,
    configure: { label: 'Open Widgets', href: '/widgets' },
  },
  {
    id: 'team',
    title: 'Your team',
    role: 'Portal, Slack and email',
    image: null,
    about: 'Staff who answer handed over conversations in the portal, in Slack or by email, and keep the agent set up.',
    zone: 'outside',
    width: 176,
    x: 0,
    y: 495,
    configure: docs('configuration.md', 'sign-in', 'How staff sign in'),
  },
  {
    id: 'providers',
    title: 'Model providers',
    role: 'Anthropic, OpenAI, Ollama, Voyage',
    image: null,
    about: 'The language and embedding models that write the answers and index your sources.',
    zone: 'outside',
    width: 236,
    x: 486,
    y: 0,
    configure: { label: 'Open AI models', href: '/models' },
  },
  {
    id: 'portal',
    title: 'Portal',
    role: 'Inbox, settings, this page',
    image: 'knowledge-agent-web',
    about: 'The web app your team uses; it talks only to the API.',
    zone: 'server',
    x: 236,
    y: 170,
    configure: null,
  },
  {
    id: 'email',
    title: 'Email',
    role: 'Requests, replies',
    image: 'SMTP + IMAP',
    about: 'Emails requests for a person to your team and reads their replies back into the conversation.',
    zone: 'server',
    x: 236,
    y: 430,
    configure: { label: 'Open Notifications', href: '/notifications' },
  },
  {
    id: 'slack',
    title: 'Slack',
    role: 'Requests, replies',
    image: 'Socket Mode',
    about: 'Posts requests for a person to a Slack channel and carries replies from the thread back to the visitor.',
    zone: 'server',
    x: 236,
    y: 560,
    configure: { label: 'Open Notifications', href: '/notifications' },
  },
  {
    id: 'api',
    title: 'Knowledge Agent API',
    role: 'Agents, widget, handover',
    image: 'knowledge-agent-api',
    about: 'Runs the agents, serves the widget and the portal API, and starts every job and handover.',
    zone: 'server',
    width: 236,
    x: 486,
    y: 300,
    configure: docs('configuration.md', 'other-sections', 'Configuration reference'),
  },
  {
    id: 'jobs',
    title: 'Inngest',
    role: 'Background jobs, retries',
    image: 'inngest/inngest',
    about: 'Runs background work such as reading new sources and checking for email replies, and retries what fails.',
    zone: 'server',
    width: 236,
    x: 486,
    y: 560,
    configure: docs('configuration.md', 'background-jobs'),
  },
  {
    id: 'keycloak',
    title: 'Keycloak',
    role: 'Staff sign-in',
    image: 'keycloak/keycloak:26',
    about: 'Signs your team in to the portal and holds their accounts and roles.',
    zone: 'server',
    x: 806,
    y: 170,
    configure: docs('configuration.md', 'sign-in'),
  },
  {
    id: 'postgres',
    title: 'PostgreSQL',
    role: 'Conversations, settings',
    image: 'postgres:17-alpine',
    about: 'Stores conversations, sources, settings and people, and announces new messages to every API instance.',
    zone: 'server',
    x: 806,
    y: 300,
    configure: docs('configuration.md', 'other-sections'),
  },
  {
    id: 'langfuse',
    title: 'Langfuse',
    role: 'Traces, prompts, datasets',
    image: 'langfuse + langfuse-worker',
    about: 'Keeps a trace of every answer, the prompt versions and the evaluation datasets.',
    zone: 'tracing',
    width: 220,
    x: 1000,
    y: 430,
    configure: docs('configuration.md', 'prompts'),
  },
  {
    id: 'clickhouse',
    title: 'ClickHouse',
    role: 'Search index, traces',
    image: 'clickhouse-server',
    about: 'Holds the search index of your sources for the API, and the traces for Langfuse.',
    zone: 'server',
    small: true,
    x: 786,
    y: 575,
    configure: docs('configuration.md', 'search'),
  },
  {
    id: 'redis',
    title: 'Redis',
    role: 'Queue',
    image: 'redis:7',
    about: 'The queue between the Langfuse web app and its worker.',
    zone: 'tracing',
    small: true,
    x: 950,
    y: 575,
    configure: null,
  },
  {
    id: 'minio',
    title: 'MinIO',
    role: 'Files',
    image: 'chainguard/minio',
    about: 'Stores the raw trace events and media Langfuse receives.',
    zone: 'tracing',
    small: true,
    x: 1094,
    y: 575,
    configure: null,
  },
];

export const INSTALLATION_EDGES: readonly InstallationEdgeDef[] = [
  { id: 'visitors-api', source: 'visitors', target: 'api', label: 'Chat and answers', from: 'right', to: 'left' },
  { id: 'team-portal', source: 'team', target: 'portal', from: 'right', to: 'left' },
  { id: 'portal-api', source: 'portal', target: 'api', label: 'Requests, live events', from: 'right', to: 'top' },
  { id: 'api-providers', source: 'api', target: 'providers', label: 'Model requests', from: 'top', to: 'bottom' },
  { id: 'api-keycloak', source: 'api', target: 'keycloak', label: 'Sign-in', from: 'right', to: 'left' },
  { id: 'api-postgres', source: 'api', target: 'postgres', label: 'Data', from: 'right', to: 'left' },
  { id: 'api-jobs', source: 'api', target: 'jobs', label: 'Background jobs', dashed: true, from: 'bottom', to: 'top' },
  { id: 'api-email', source: 'api', target: 'email', label: 'Handover', dashed: true, from: 'left', to: 'right' },
  { id: 'api-slack', source: 'api', target: 'slack', dashed: true, from: 'left', to: 'right' },
  { id: 'email-team', source: 'email', target: 'team', dashed: true, from: 'left', to: 'right' },
  { id: 'slack-team', source: 'slack', target: 'team', dashed: true, from: 'left', to: 'right' },
  { id: 'api-langfuse', source: 'api', target: 'langfuse', label: 'Traces and prompts', dashed: true, from: 'right', to: 'left' },
  { id: 'api-clickhouse', source: 'api', target: 'clickhouse', label: 'Search index', from: 'bottom', to: 'left' },
  { id: 'langfuse-postgres', source: 'langfuse', target: 'postgres', from: 'top', to: 'right' },
  { id: 'langfuse-clickhouse', source: 'langfuse', target: 'clickhouse', from: 'bottom', to: 'top' },
  { id: 'langfuse-redis', source: 'langfuse', target: 'redis', from: 'bottom', to: 'top' },
  { id: 'langfuse-minio', source: 'langfuse', target: 'minio', from: 'bottom', to: 'top' },
];

export const STATUS_LABELS: Record<ComponentStatus, string> = {
  healthy: 'Healthy',
  degraded: 'Degraded',
  down: 'Down',
  notConfigured: 'Not set up',
  unknown: 'Status unknown',
};

export const STATUS_TONES = {
  healthy: 'success',
  degraded: 'warning',
  down: 'danger',
  notConfigured: 'neutral',
  unknown: 'neutral',
} as const satisfies Record<ComponentStatus, string>;

export interface InstallationNode extends InstallationNodeDef {
  /** Null for people outside the server, which can't be checked. */
  status: ComponentStatus | null;
  detail: string;
  checkedAt: string | null;
  /** Not set up or not used in this installation. */
  dimmed: boolean;
}

interface InstallationEdge extends InstallationEdgeDef {
  /** A path this installation doesn't use, e.g. the search index when it's kept in memory. */
  muted: boolean;
}

export interface Installation {
  nodes: InstallationNode[];
  edges: InstallationEdge[];
  /** False when the API couldn't say, e.g. an older API without the health endpoint. */
  known: boolean;
}

const UNKNOWN = 'The API did not report this part, so its state is not known.';

/** Merges the static picture with each node's reported state and the variants that run. */
export function buildInstallation(health: SystemHealthDto | null | undefined, engine: JobEngine | null | undefined): Installation {
  const byKey = new Map<string, SystemComponentHealthDto>((health?.components ?? []).map((component) => [component.key, component]));
  const known = health != null;
  const from = (key: string): Pick<InstallationNode, 'status' | 'detail' | 'checkedAt'> => {
    const component = byKey.get(key);
    return component
      ? { status: component.status, detail: component.detail, checkedAt: component.checkedAt }
      : { status: 'unknown', detail: UNKNOWN, checkedAt: null };
  };

  const database = byKey.get('database');
  const vectors = byKey.get('vectorStore');
  const jobs = byKey.get('jobs');
  const langfuse = from('langfuse');
  const inProcess = jobs?.variant ? jobs.variant === 'inprocess' : engine === 'inprocess';
  const inMemoryDb = database?.variant === 'inmemory';
  const vectorsInMemory = vectors?.variant === 'inmemory';
  const langfuseOff = langfuse.status === 'notConfigured';

  // The API doesn't check the stores behind Langfuse; they follow Langfuse's own state.
  const store = (): Pick<InstallationNode, 'status' | 'detail' | 'checkedAt'> =>
    langfuseOff
      ? { status: 'notConfigured', detail: 'Not used, because Langfuse is not set up.', checkedAt: langfuse.checkedAt }
      : { status: 'unknown', detail: 'Langfuse uses it; the API does not connect to it, so it is not checked from here.', checkedAt: null };

  const nodes = INSTALLATION_NODES.map((def): InstallationNode => {
    const base = { ...def, dimmed: false };
    switch (def.id) {
      case 'visitors':
      case 'team':
        return { ...base, status: null, detail: 'Outside your server.', checkedAt: null };
      case 'providers':
        return { ...base, ...from('models') };
      case 'portal':
        // Anyone viewing this page is using the portal.
        return { ...base, status: 'healthy', detail: 'You are using it right now.', checkedAt: null };
      case 'api':
        return {
          ...base,
          ...(known ? from('api') : { status: 'healthy', detail: 'Answering requests.', checkedAt: null }),
          ...(vectorsInMemory && vectors ? { detail: `Answering requests. Search index: ${lowerFirst(vectors.detail)}.` } : {}),
        };
      case 'jobs':
        return inProcess
          ? { ...base, title: 'Jobs inside the API', role: 'Background jobs, retries', image: 'in-process runner', ...from('jobs') }
          : { ...base, ...from('jobs') };
      case 'keycloak': {
        const signIn = from('signIn');
        return { ...base, ...signIn, dimmed: signIn.status === 'notConfigured' };
      }
      case 'postgres':
        return inMemoryDb
          ? { ...base, title: 'In-memory store', role: 'Lost on restart', image: 'EF Core in-memory', ...from('database') }
          : { ...base, ...from('database') };
      case 'email':
      case 'slack': {
        const state = from(def.id);
        return { ...base, ...state, dimmed: state.status === 'notConfigured' };
      }
      case 'langfuse':
        return { ...base, ...langfuse, dimmed: langfuseOff };
      case 'clickhouse':
        if (vectors && !vectorsInMemory) {
          return { ...base, ...from('vectorStore') };
        }
        return { ...base, role: 'Traces', ...store(), dimmed: langfuseOff };
      default:
        return { ...base, ...store(), dimmed: langfuseOff };
    }
  });

  const dimmed = new Set(nodes.filter((node) => node.dimmed).map((node) => node.id));
  const edges = INSTALLATION_EDGES.map(
    (edge): InstallationEdge => ({
      ...edge,
      muted: dimmed.has(edge.source) || dimmed.has(edge.target) || (edge.id === 'api-clickhouse' && vectorsInMemory),
    }),
  );
  return { nodes, edges, known };
}

function lowerFirst(text: string): string {
  return text.charAt(0).toLowerCase() + text.slice(1).replace(/\.$/, '');
}

export function installationProblems(installation: Installation): InstallationNode[] {
  const weight: Partial<Record<ComponentStatus, number>> = { down: 2, degraded: 1 };
  return installation.nodes
    .filter((node) => node.status && weight[node.status])
    .sort((a, b) => (weight[b.status!] ?? 0) - (weight[a.status!] ?? 0));
}

export function nodeSize(node: Pick<InstallationNodeDef, 'small' | 'width'>): { width: number; height: number } {
  const size = node.small ? SMALL_NODE_SIZE : NODE_SIZE;
  return { width: node.width ?? size.width, height: size.height };
}

/** For sizing the frame before React Flow measures anything. */
export function installationSize(): { width: number; height: number } {
  const right = Math.max(...INSTALLATION_NODES.map((node) => node.x + nodeSize(node).width), BOUNDARIES.server.x + BOUNDARIES.server.width);
  const bottom = Math.max(...INSTALLATION_NODES.map((node) => node.y + nodeSize(node).height), BOUNDARIES.server.y + BOUNDARIES.server.height);
  return { width: right, height: bottom };
}

function mermaidLabel(node: InstallationNodeDef): string {
  const parts = [node.title, node.role, node.image ? `<i>${node.image}</i>` : null].filter(Boolean);
  return `${node.id}["${parts.join('<br/>').replace(/"/g, '#quot;')}"]`;
}

/** The same picture as a Mermaid flowchart. */
export function toMermaid(): string {
  const zone = (z: InstallationZone) => INSTALLATION_NODES.filter((node) => node.zone === z);
  const lines = ['flowchart LR'];
  for (const node of zone('outside')) {
    lines.push(`  ${mermaidLabel(node)}`);
  }
  lines.push(`  subgraph server["${BOUNDARIES.server.label}"]`);
  for (const node of zone('server')) {
    lines.push(`    ${mermaidLabel(node)}`);
  }
  lines.push(`    subgraph tracing["${BOUNDARIES.tracing.label}"]`);
  for (const node of zone('tracing')) {
    lines.push(`      ${mermaidLabel(node)}`);
  }
  lines.push('    end', '  end');
  for (const edge of INSTALLATION_EDGES) {
    const label = edge.label ? `|"${edge.label}"|` : '';
    lines.push(`  ${edge.source} ${edge.dashed ? '-.->' : '-->'}${label} ${edge.target}`);
  }
  lines.push('  style server stroke-dasharray: 6 4', '  style tracing stroke-dasharray: 6 4');
  return lines.join('\n');
}
