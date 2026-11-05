/// <reference types="node" />
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { buildInstallation, INSTALLATION_EDGES, INSTALLATION_NODES, installationProblems, nodeSize, toMermaid } from './installation';
import { healthComponent as component, healthyInstallation } from '../../test/systemApi';

const node = (installation: ReturnType<typeof buildInstallation>, id: string) => installation.nodes.find((n) => n.id === id)!;

describe('the installation model', () => {
  it('connects only parts that exist, and no two parts overlap', () => {
    const ids = new Set(INSTALLATION_NODES.map((n) => n.id));
    expect(ids.size).toBe(INSTALLATION_NODES.length);
    for (const edge of INSTALLATION_EDGES) {
      expect(ids.has(edge.source) && ids.has(edge.target), edge.id).toBe(true);
    }
    for (const a of INSTALLATION_NODES) {
      for (const b of INSTALLATION_NODES) {
        if (a.id >= b.id) {
          continue;
        }
        const sa = nodeSize(a);
        const sb = nodeSize(b);
        const apart = a.x + sa.width <= b.x || b.x + sb.width <= a.x || a.y + sa.height <= b.y || b.y + sb.height <= a.y;
        expect(apart, `${a.id} and ${b.id} overlap`).toBe(true);
      }
    }
  });

  it('gives each part its reported state and the stores behind traces an honest unknown', () => {
    const installation = buildInstallation(healthyInstallation(), 'inngest');
    expect(installation.known).toBe(true);
    expect(node(installation, 'postgres')).toMatchObject({ title: 'PostgreSQL', status: 'healthy', detail: 'Connected', image: 'postgres:17-alpine' });
    expect(node(installation, 'jobs')).toMatchObject({ title: 'Inngest', status: 'healthy' });
    expect(node(installation, 'keycloak')).toMatchObject({ status: 'healthy' });
    expect(node(installation, 'providers')).toMatchObject({ status: 'healthy' });
    expect(node(installation, 'clickhouse')).toMatchObject({ status: 'healthy', detail: 'Search index reachable' });
    expect(node(installation, 'redis')).toMatchObject({ status: 'unknown' });
    expect(node(installation, 'slack')).toMatchObject({ status: 'notConfigured', dimmed: true });
    expect(node(installation, 'visitors').status).toBeNull();
    expect(node(installation, 'portal').status).toBe('healthy');
    expect(installation.edges.find((e) => e.id === 'api-slack')?.muted).toBe(true);
    expect(installation.edges.find((e) => e.id === 'api-email')?.muted).toBe(false);
    expect(installationProblems(installation)).toEqual([]);
  });

  it('names the in-memory stores and the in-process jobs for what they are', () => {
    const installation = buildInstallation(
      healthyInstallation([
        component('database', 'healthy', 'inmemory', 'In memory: everything is lost when the API restarts'),
        component('vectorStore', 'healthy', 'inmemory', 'In memory: sources must be indexed again after a restart'),
        component('jobs', 'healthy', 'inprocess', 'Jobs run inside the API; queued work is lost on restart'),
        component('signIn', 'notConfigured', 'none', 'Sign-in is off: everyone is an admin (development only)'),
        component('langfuse', 'notConfigured'),
      ]),
      'inprocess',
    );
    expect(node(installation, 'postgres')).toMatchObject({ title: 'In-memory store', image: 'EF Core in-memory' });
    expect(node(installation, 'jobs')).toMatchObject({ title: 'Jobs inside the API', image: 'in-process runner' });
    expect(node(installation, 'api').detail).toBe('Answering requests. Search index: in memory: sources must be indexed again after a restart.');
    expect(node(installation, 'keycloak')).toMatchObject({ status: 'notConfigured', dimmed: true });
    for (const id of ['langfuse', 'clickhouse', 'redis', 'minio']) {
      expect(node(installation, id)).toMatchObject({ status: 'notConfigured', dimmed: true });
    }
    expect(installation.edges.find((e) => e.id === 'api-clickhouse')?.muted).toBe(true);
  });

  it('lists what is down before what is degraded', () => {
    const installation = buildInstallation(
      healthyInstallation([component('langfuse', 'degraded'), component('signIn', 'down', 'oidc')]),
      'inngest',
    );
    expect(installationProblems(installation).map((n) => n.id)).toEqual(['keycloak', 'langfuse']);
  });

  it('shows every state as unknown when the API has no health endpoint', () => {
    const installation = buildInstallation(undefined, 'inngest');
    expect(installation.known).toBe(false);
    expect(node(installation, 'postgres').status).toBe('unknown');
    expect(node(installation, 'jobs')).toMatchObject({ title: 'Inngest', status: 'unknown' });
    expect(buildInstallation(undefined, 'inprocess').nodes.find((n) => n.id === 'jobs')?.title).toBe('Jobs inside the API');
    expect(node(installation, 'api').status).toBe('healthy');
  });
});

describe('the diagram in docs/architecture.md', () => {
  const START = '<!-- installation-diagram:start (made by web/src/features/system/installation.ts) -->';
  const END = '<!-- installation-diagram:end -->';
  const path = resolve(process.cwd(), '..', 'docs', 'architecture.md');

  it.skipIf(!existsSync(path))('is drawn from the same parts and paths as the System page', () => {
    const docs = readFileSync(path, 'utf8');
    const block = `${START}\n\`\`\`mermaid\n${toMermaid()}\n\`\`\`\n${END}`;
    const start = docs.indexOf(START);
    const end = docs.indexOf(END);
    expect(start, 'docs/architecture.md has no installation diagram markers').toBeGreaterThanOrEqual(0);
    const current = docs.slice(start, end + END.length);
    if (current !== block && process.env.KA_WRITE_DOCS === '1') {
      writeFileSync(path, docs.slice(0, start) + block + docs.slice(end + END.length));
      return;
    }
    expect(current, 'Run KA_WRITE_DOCS=1 npx vitest run src/features/system/installation.test.ts to update the docs').toBe(block);
  });

  it('draws optional paths dashed and keeps both frames', () => {
    const mermaid = toMermaid();
    expect(mermaid).toContain('subgraph server["Your server"]');
    expect(mermaid).toContain('subgraph tracing["Tracing"]');
    expect(mermaid).toContain('api -.->|"Background jobs"| jobs');
    expect(mermaid).toContain('visitors -->|"Chat and answers"| api');
  });
});
