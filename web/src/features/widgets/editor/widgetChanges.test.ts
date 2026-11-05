import { describe, expect, it } from 'vitest';
import { agentDto } from '../../../test/agentsApi';
import { widgetDto, widgetSettings } from '../../../test/widgetsApi';
import { initialWidgetValues } from '../widgetForm';
import { agentFacts, widgetChanges } from './widgetChanges';

const values = (overrides: Partial<ReturnType<typeof initialWidgetValues>> = {}) => ({
  ...initialWidgetValues(widgetDto(), widgetSettings()),
  ...overrides,
});

const fact = (facts: ReturnType<typeof widgetChanges>, label: string) => facts.find((item) => item.label === label);

describe('what comes from where', () => {
  it('names the agent knowledge, tools and when it brings in a person', () => {
    const facts = agentFacts(agentDto({ folderIds: [], allowGeneralKnowledge: true, tools: ['search'], handoverPolicy: 'onRequest' }));
    expect(fact(facts, 'Answers from')?.value).toBe('All your sources, and what the model knows');
    expect(fact(facts, 'Tools')?.value).toBe('1 tool');
    expect(fact(facts, 'A person')?.value).toBe('When asked');
  });

  it('marks a person switched off on the widget as limited only when the agent would offer one', () => {
    const off = values({ handoverEnabled: false });
    expect(fact(widgetChanges(off, agentDto({ handoverPolicy: 'whenUnsure' })), 'A person')).toMatchObject({ value: 'Off on this widget', limited: true });
    expect(fact(widgetChanges(off, agentDto({ handoverPolicy: 'never' })), 'A person')?.limited).toBe(false);
    expect(fact(widgetChanges(values({ handoverEnabled: true }), agentDto()), 'A person')?.limited).toBe(false);
  });

  it('marks hidden sources as limited', () => {
    expect(fact(widgetChanges(values({ citationMode: 'hidden' })), 'Sources shown')?.limited).toBe(true);
    expect(fact(widgetChanges(values({ citationMode: 'titles' })), 'Sources shown')?.limited).toBe(false);
  });
});
