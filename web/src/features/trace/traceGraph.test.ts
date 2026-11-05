import { describe, expect, it } from 'vitest';
import { ev } from '../../test/fixtures';
import { tracedTurn } from '../../test/trace';
import { chatReducer, createChatState, type TurnView } from '../chat/chatReducer';
import { layoutGraph } from '../../components/flow/layout';
import { ANSWER_NODE, buildTraceGraph, QUESTION_NODE } from './traceGraph';

describe('buildTraceGraph', () => {
  it('chains the question, each tool call and the answer, with the researcher steps beside', () => {
    const graph = buildTraceGraph(tracedTurn());
    expect(graph.nodes.map((node) => node.id)).toEqual([QUESTION_NODE, 'call:call-search', 'call:call-research', 'call:call-r1', ANSWER_NODE]);
    expect(graph.edges.map((edge) => `${edge.source} > ${edge.target}`)).toEqual([
      'question > call:call-search',
      'call:call-search > call:call-research',
      'call:call-research > call:call-r1',
      'call:call-research > answer',
    ]);
  });

  it('describes each step with its query, passages and time', () => {
    const graph = buildTraceGraph(tracedTurn());
    const byId = new Map(graph.nodes.map((node) => [node.id, node]));
    expect(byId.get(QUESTION_NODE)?.lines).toEqual(['How many vacation days do I get?']);
    expect(byId.get('call:call-search')).toMatchObject({ kind: 'tool', title: 'Searched the knowledge base', status: '412 ms', lines: ['vacation days', '2 passages'] });
    expect(byId.get('call:call-research')).toMatchObject({ kind: 'researcher', title: 'Researcher', status: '3.1 s', lines: ['Check carry-over rules', '1 step'] });
    expect(byId.get('call:call-r1')).toMatchObject({ status: '250 ms', lines: ['carry over vacation', '1 passage'] });
    expect(byId.get(ANSWER_NODE)).toMatchObject({ kind: 'answer', tone: 'success', lines: [expect.stringContaining('New employees get'), 'Cites 3 passages'] });
  });

  it('marks failed tool calls and failed answers', () => {
    const turn = tracedTurn();
    const failed: TurnView = {
      ...turn,
      status: 'error',
      errors: [{ message: 'The provider timed out', code: 'provider_error', agent: 'orchestrator' }],
      tools: [{ ...turn.tools[0]!, status: 'error', summary: 'Search is unavailable' }],
    };
    const graph = buildTraceGraph(failed);
    expect(graph.nodes.find((node) => node.id === 'call:call-search')).toMatchObject({ tone: 'danger', problem: 'Search is unavailable' });
    expect(graph.nodes.find((node) => node.id === ANSWER_NODE)).toMatchObject({ tone: 'danger', problem: 'The provider timed out' });
  });

  it('places the steps top to bottom', () => {
    const placed = layoutGraph(buildTraceGraph(tracedTurn()), { direction: 'TB' });
    const y = (id: string) => placed.find((p) => p.node.id === id)!.y;
    expect(y(QUESTION_NODE)).toBeLessThan(y('call:call-search'));
    expect(y('call:call-search')).toBeLessThan(y(ANSWER_NODE));
  });
  it('shows a hand-off to a specialist as its own step, with who took over and why', () => {
    let state = chatReducer(createChatState(), { type: 'send', id: 'client-1', message: 'Is invoice 42 paid?', at: 0 });
    for (const event of [
      ev('tool-call', { callId: 'call-transfer', toolName: 'transfer_to_agent', arguments: { agent: 'billing', reason: 'question about an invoice' } }),
      ev('tool-result', { callId: 'call-transfer', toolName: 'transfer_to_agent', isError: false, summary: 'Handed to Billing assistant', durationMs: 2, citations: [] }),
      ev('tool-call', { agent: 'billing', callId: 'call-billing', toolName: 'read_source', arguments: { source_id: 'invoice-42' } }),
      ev('text-delta', { agent: 'billing', text: 'Invoice 42 is paid.' }),
    ]) {
      state = chatReducer(state, { type: 'event', event, at: 1000 });
    }
    const graph = buildTraceGraph(state.turns[0]!);
    expect(graph.nodes.map((node) => node.id)).toEqual([QUESTION_NODE, 'call:call-transfer', 'call:call-billing', ANSWER_NODE]);
    expect(graph.nodes[1]).toMatchObject({ kind: 'transfer', title: 'Handed to Billing assistant', lines: ['question about an invoice'], tone: 'info' });
    expect(graph.nodes[3]!.lines[0]).toBe('Invoice 42 is paid.');
  });
});
