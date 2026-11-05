import type { JsonObject, JsonValue } from '../../lib/types';

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

const SAMPLE_SOURCES: JsonValue[] = [
  {
    id: '0199c1e2-7a10-7d3e-9b55-3f6a1c2d4e01',
    title: 'Employee handbook 2026',
    type: 'pdf',
    status: 'ready',
    url: null,
    chunkCount: 42,
    pageCount: 18,
  },
  {
    id: '0199c1e2-7a10-7d3e-9b55-3f6a1c2d4e02',
    title: 'Security policy',
    type: 'url',
    status: 'ready',
    url: 'https://example.com/security-policy',
    chunkCount: 12,
    pageCount: null,
  },
  {
    id: '0199c1e2-7a10-7d3e-9b55-3f6a1c2d4e03',
    title: 'Onboarding checklist',
    type: 'markdown',
    status: 'ready',
    url: null,
    chunkCount: 5,
    pageCount: null,
  },
];

const SAMPLE_QUESTION = 'How many vacation days do new employees get, and who approves them?';
const SAMPLE_ANSWER = 'New employees get 25 vacation days per year [S1]; requests are approved by the line manager [S2].';

/** Guessed from the variable name. */
export function guessSampleValue(name: string): JsonValue {
  const n = name.toLowerCase();
  if (n.endsWith('count') || n.startsWith('max') || n.startsWith('min') || n.endsWith('limit')) {
    return 3;
  }
  if (n.endsWith('sources') || n.endsWith('documents')) {
    return SAMPLE_SOURCES;
  }
  if (n === 'today' || n.endsWith('date')) {
    return today();
  }
  if (n.endsWith('truncated')) {
    return false;
  }
  // isAdmin, hasSources, allowX, enabledX, use_x (camelCase or snake_case boundary).
  if (/^(is|has|allow|enable|use|show)([A-Z_]|$)/.test(name) || n.endsWith('enabled')) {
    return true;
  }
  if (n.includes('language') || n === 'locale') {
    return 'English';
  }
  if (n.includes('answer') || n.includes('response') || n.includes('reply')) {
    return SAMPLE_ANSWER;
  }
  if (n.includes('message') || n.includes('question') || n.includes('query') || n.includes('input')) {
    return SAMPLE_QUESTION;
  }
  if (n.includes('task')) {
    return 'Find the vacation policy for new employees and who approves requests.';
  }
  if (n.endsWith('name')) {
    return 'Knowledge Agent';
  }
  return `example ${name}`;
}

const KNOWN_SAMPLES: Record<string, () => JsonObject> = {
  'orchestrator-system': () => ({
    agentName: 'Knowledge Agent',
    appName: 'Knowledge Agent',
    today: today(),
    sourceCount: 3,
    chunkCount: 42,
    sourceTypes: [
      { type: 'url', count: 2 },
      { type: 'pdf', count: 1 },
    ],
    pendingSourceCount: 0,
    researcherEnabled: true,
    allowGeneralKnowledge: true,
    language: 'English',
  }),
  'researcher-system': () => ({
    appName: 'Knowledge Agent',
    today: today(),
    maxSearches: 6,
    focusSources: SAMPLE_SOURCES.slice(0, 2),
  }),
  'reply-suggestion': () => ({
    agentName: 'Shop assistant',
    appName: 'Knowledge Agent',
    staffName: 'Dana',
    today: today(),
    allowGeneralKnowledge: false,
    language: 'English',
  }),
  'eval-judge': () => ({
    question: SAMPLE_QUESTION,
    answer: SAMPLE_ANSWER,
    answerable: true,
    referenceAnswer: 'New employees receive 25 days of paid vacation per calendar year. Vacation requests are approved by the line manager.',
    expectedFacts: ['25 vacation days per year', 'approved by the line manager'],
    passages: [
      {
        ref: 'S1',
        title: 'Employee handbook 2026',
        text: 'New employees receive 25 days of paid vacation per calendar year. Unused days can be carried over until March.',
      },
      {
        ref: 'S2',
        title: 'Leave policy',
        text: 'Vacation requests are submitted in the HR portal and approved by the line manager.',
      },
    ],
  }),
};

export function sampleVariables(promptName: string, detectedVariables: readonly string[]): JsonObject {
  const base: JsonObject = KNOWN_SAMPLES[promptName]?.() ?? {};
  if (promptName === 'conversation-title' && detectedVariables.length === 0) {
    base.message = SAMPLE_QUESTION;
    base.answer = SAMPLE_ANSWER;
    base.language = 'English';
  }
  for (const name of detectedVariables) {
    if (!(name in base) && !name.startsWith('@')) {
      base[name] = guessSampleValue(name);
    }
  }
  return base;
}

export const PLAYGROUND_TEMPLATE = `You are {{agentName}}, the assistant of {{appName}}. Today is {{today}}.
{{#if sources}}
You can search {{sourceCount}} {{#if (eq sourceCount 1)}}source{{else}}sources{{/if}}:
{{#each sources}}
{{@index}}. {{title}} ({{upper type}}){{#if url}}: {{url}}{{/if}}
{{/each}}
{{else}}
No sources have been added yet.
{{/if}}
{{#unless allowGeneralKnowledge}}Only answer from the sources.{{/unless}}
Answer in {{default language "English"}}.`;

export const PLAYGROUND_VARIABLES: JsonObject = {
  agentName: 'orchestrator',
  appName: 'Knowledge Agent',
  today: '2026-10-22',
  sources: SAMPLE_SOURCES.slice(0, 2),
  sourceCount: 2,
  allowGeneralKnowledge: false,
  language: null,
};
