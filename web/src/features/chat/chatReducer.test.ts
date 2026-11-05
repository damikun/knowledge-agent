import { describe, expect, it } from 'vitest';
import { citationS1, citationS2, citationS3, CONVERSATION_ID, ev, fullTurnEvents, TURN_ID, turnDto } from '../../test/fixtures';
import {
  buildToolTree,
  chatReducer,
  citationIndex,
  createChatState,
  INTERRUPTED_MESSAGE,
  mergeTurns,
  turnFromDto,
  type ChatState,
} from './chatReducer';
import type { ChatEvent } from './types';

function run(events: ChatEvent[], state: ChatState = createChatState()): ChatState {
  let next = chatReducer(state, { type: 'send', id: 'local-1', message: 'How many vacation days do I get?', at: 1_000 });
  events.forEach((event, index) => {
    next = chatReducer(next, { type: 'event', event, at: 1_000 + (index + 1) * 100 });
  });
  return next;
}

describe('chatReducer', () => {
  it('starts a streaming turn on send', () => {
    const state = chatReducer(createChatState(), { type: 'send', id: 'local-1', message: 'Hi', at: 5 });
    expect(state.streaming).toBe(true);
    expect(state.turns).toHaveLength(1);
    expect(state.turns[0]).toMatchObject({ id: 'local-1', key: 'local-1', userMessage: 'Hi', status: 'streaming', persisted: false, startedAt: 5 });
  });

  it('folds a full event sequence (search, nested researcher, answer, title update, done) into one turn', () => {
    const events = fullTurnEvents();
    const state = run(events);
    const turn = state.turns[0]!;

    expect(state.streaming).toBe(false);
    expect(state.conversationId).toBe(CONVERSATION_ID);
    expect(state.title).toBe('Vacation policy');

    expect(turn.status).toBe('complete');
    expect(turn.id).toBe(TURN_ID);
    expect(turn.key).toBe('local-1');
    expect(turn.persisted).toBe(true);
    expect(turn.finishReason).toBe('stop');
    expect(turn.durationMs).toBe(events.length * 100);
    expect(turn.provider).toBe('anthropic');
    expect(turn.model).toBe('claude-opus-5-5');

    expect(turn.text).toBe('New employees get **25 days** [S1]. Managers approve requests [S2], and days carry over until March [S3].');
    expect(turn.reasoning).toBe('The user asks about vacation. I should search the handbook.');

    // Usage is summed per agent; a missing totalTokens is derived from input + output.
    expect(turn.usage).toEqual({ inputTokens: 2000, outputTokens: 300, totalTokens: 2300 });

    expect(turn.tools.map((t) => t.callId)).toEqual(['call-search', 'call-research']);
    const [search, research] = turn.tools;
    expect(search).toMatchObject({
      toolName: 'search_knowledge_base',
      status: 'success',
      summary: 'Found 2 passages in 2 sources',
      durationMs: 412,
      arguments: { query: 'vacation days', top_k: 6 },
      children: [],
      subAgent: null,
    });
    expect(search!.citations.map((c) => c.ref)).toEqual(['S1', 'S2']);

    expect(research).toMatchObject({ toolName: 'delegate_research', status: 'success', summary: 'Unused days carry over until March.' });
    expect(research!.children).toHaveLength(1);
    expect(research!.children[0]).toMatchObject({
      callId: 'call-r1',
      agent: 'researcher',
      parentCallId: 'call-research',
      status: 'success',
      summary: 'Found 1 passage',
    });
    expect(research!.subAgent).toMatchObject({
      agent: 'researcher',
      provider: 'anthropic',
      model: 'claude-opus-5-5',
      text: 'Days carry over until March [S3].',
      reasoning: 'Looking for carry-over.',
      status: 'completed',
      finishReason: 'stop',
      iterations: 2,
      usage: { inputTokens: 900, outputTokens: 120, totalTokens: 1020 },
    });

    expect(turn.text).not.toContain('Days carry over until March [S3].');

    expect(turn.citations.map((c) => c.ref).sort()).toEqual(['S1', 'S2', 'S3']);
  });

  it('separates answer paragraphs written before and after a tool call', () => {
    const state = run([
      ev('text-delta', { text: 'Let me check.' }),
      ev('tool-call', { callId: 'c1', toolName: 'find_sources', arguments: {} }),
      ev('tool-result', { callId: 'c1', toolName: 'find_sources', isError: false, summary: '3 sources', durationMs: 5, citations: [] }),
      ev('text-delta', { text: 'You have three sources.' }),
    ]);
    expect(state.turns[0]!.text).toBe('Let me check.\n\nYou have three sources.');
  });

  it('keeps errors inline: orchestrator errors fail the turn, sub-agent errors stay in their card', () => {
    const state = run([
      ev('tool-call', { callId: 'call-research', toolName: 'delegate_research', arguments: { task: 'x' } }),
      ev('error', { agent: 'researcher', parentCallId: 'call-research', message: 'Researcher timed out', code: 'provider_error' }),
      ev('tool-result', { callId: 'call-research', toolName: 'delegate_research', isError: true, summary: 'Research failed', durationMs: 120000, citations: [] }),
      ev('error', { message: 'The model provider returned an error.', code: 'provider_error' }),
      ev('done', { conversationId: CONVERSATION_ID, turnId: TURN_ID, finishReason: 'error' }),
    ]);
    const turn = state.turns[0]!;
    expect(turn.status).toBe('error');
    expect(turn.errors).toEqual([{ message: 'The model provider returned an error.', code: 'provider_error', agent: 'orchestrator' }]);
    expect(turn.tools[0]!.status).toBe('error');
    expect(turn.tools[0]!.subAgent!.errors).toEqual([{ message: 'Researcher timed out', code: 'provider_error', agent: 'researcher' }]);
    expect(state.streaming).toBe(false);
  });

  it('updates the title from conversation events and ignores events after done', () => {
    let state = run([ev('conversation', { conversationId: 'c-1', title: 'New conversation', isNew: true })]);
    expect(state.title).toBe('New conversation');
    state = chatReducer(state, { type: 'event', event: ev('conversation', { conversationId: 'c-1', title: 'Renamed', isNew: false }), at: 2 });
    expect(state.title).toBe('Renamed');
    state = chatReducer(state, { type: 'event', event: ev('done', { conversationId: 'c-1', turnId: 't-1', finishReason: 'stop' }), at: 3 });
    const afterDone = chatReducer(state, { type: 'event', event: ev('text-delta', { text: 'late' }), at: 4 });
    expect(afterDone.turns[0]!.text).toBe('');
  });

  it('marks a stream that ends without done as interrupted', () => {
    let state = run([ev('text-delta', { text: 'Partial' }), ev('tool-call', { callId: 'c1', toolName: 'search_knowledge_base', arguments: { query: 'q' } })]);
    state = chatReducer(state, { type: 'stream-ended', at: 9_000 });
    const turn = state.turns[0]!;
    expect(turn.status).toBe('error');
    expect(turn.errors.at(-1)).toEqual({ message: INTERRUPTED_MESSAGE, code: 'interrupted', agent: 'orchestrator' });
    expect(turn.tools[0]!.status).toBe('cancelled');
    expect(turn.durationMs).toBe(8_000);
    expect(state.streaming).toBe(false);
  });

  it('marks an aborted turn as cancelled and settles running tools and sub-agents', () => {
    let state = run([
      ev('tool-call', { callId: 'call-research', toolName: 'delegate_research', arguments: { task: 'x' } }),
      ev('agent-started', { agent: 'researcher', parentCallId: 'call-research', provider: 'openai', model: 'gpt-5-mini' }),
    ]);
    state = chatReducer(state, { type: 'aborted', at: 2_000 });
    const turn = state.turns[0]!;
    expect(turn.status).toBe('cancelled');
    expect(turn.tools[0]!.status).toBe('cancelled');
    expect(turn.tools[0]!.subAgent!.status).toBe('cancelled');
    expect(state.streaming).toBe(false);
  });

  it('records client-side failures (HTTP errors) on the turn', () => {
    let state = chatReducer(createChatState(), { type: 'send', id: 'l', message: 'Hi', at: 0 });
    state = chatReducer(state, { type: 'failed', message: 'Message: The message must not be empty.', at: 10 });
    expect(state.turns[0]).toMatchObject({ status: 'error', errors: [{ message: 'Message: The message must not be empty.', code: null, agent: 'orchestrator' }] });
  });

  it('creates a placeholder card for nested events whose parent call is unknown', () => {
    const state = run([ev('text-delta', { agent: 'researcher', parentCallId: 'ghost', text: 'Hello' })]);
    expect(state.turns[0]!.tools[0]).toMatchObject({ callId: 'ghost', toolName: 'delegate_research' });
    expect(state.turns[0]!.tools[0]!.subAgent!.text).toBe('Hello');
  });

  it('parses JSON-encoded string arguments', () => {
    const state = run([ev('tool-call', { callId: 'c', toolName: 'search_knowledge_base', arguments: '{"query":"pricing"}' })]);
    expect(state.turns[0]!.tools[0]!.arguments).toEqual({ query: 'pricing' });
  });

  it('resets to an empty state for another conversation', () => {
    const state = chatReducer(run(fullTurnEvents()), { type: 'reset', conversationId: 'other' });
    expect(state).toEqual(createChatState('other'));
  });

  it('remembers the timeline sequence a message was sent after', () => {
    const state = chatReducer(createChatState(), { type: 'send', id: 'l', message: 'Hi', at: 0, sentAfterSequence: 7 });
    expect(state.turns[0]!.sentAfterSequence).toBe(7);
    expect(chatReducer(createChatState(), { type: 'send', id: 'l', message: 'Hi', at: 0 }).turns[0]!.sentAfterSequence).toBeNull();
  });

  it('follows a turn started elsewhere under its server id and folds its events in', () => {
    let state = chatReducer(createChatState(), { type: 'follow', turnId: TURN_ID, message: 'How many vacation days do I get?', at: 5 });
    expect(state.streaming).toBe(true);
    expect(state.turns[0]).toMatchObject({ key: TURN_ID, id: TURN_ID, persisted: false, status: 'streaming', startedAt: null });
    for (const event of fullTurnEvents()) {
      state = chatReducer(state, { type: 'event', event, at: 10 });
    }
    expect(state.turns[0]).toMatchObject({ key: TURN_ID, id: TURN_ID, persisted: true, status: 'complete', durationMs: null });
    expect(state.turns[0]!.text).toContain('Managers approve requests');
  });

  it('shows an answer stopped on the server as stopped, keeping its text and settling running tools', () => {
    const state = run([
      ev('text-delta', { text: 'New employees get' }),
      ev('tool-call', { callId: 'c1', toolName: 'search_knowledge_base', arguments: { query: 'carry over' } }),
      ev('done', { conversationId: CONVERSATION_ID, turnId: TURN_ID, finishReason: 'stopped' }),
    ]);
    expect(state.streaming).toBe(false);
    expect(state.turns[0]).toMatchObject({ status: 'cancelled', id: TURN_ID, persisted: true, finishReason: 'stopped', text: 'New employees get', errors: [] });
    expect(state.turns[0]!.tools[0]!.status).toBe('cancelled');

    let followed = chatReducer(createChatState(), { type: 'follow', turnId: TURN_ID, message: 'Hi', at: 0 });
    followed = chatReducer(followed, { type: 'event', event: ev('done', { conversationId: CONVERSATION_ID, turnId: TURN_ID, finishReason: 'stopped' }), at: 1 });
    expect(followed.turns[0]!.status).toBe('cancelled');
  });

  it('takes back a question the API refused before its turn started', () => {
    const finished = run(fullTurnEvents());
    const refused = chatReducer(chatReducer(finished, { type: 'send', id: 'l-2', message: 'Next', at: 1 }), { type: 'refused' });
    expect(refused.turns).toEqual(finished.turns);
    expect(refused.streaming).toBe(false);
    expect(chatReducer(finished, { type: 'refused' }).turns).toEqual(finished.turns);
  });
});

describe('history mapping', () => {
  it('rebuilds nested tool calls from the flat persisted list', () => {
    const tree = buildToolTree([
      { callId: 'a', toolName: 'search_knowledge_base', agent: 'orchestrator', parentCallId: null, arguments: { query: 'q' }, summary: 'ok', isError: false, durationMs: 10 },
      { callId: 'b', toolName: 'delegate_research', agent: 'orchestrator', parentCallId: null, arguments: { task: 't' }, summary: 'done', isError: false, durationMs: 30 },
      { callId: 'c', toolName: 'read_source', agent: 'researcher', parentCallId: 'b', arguments: { source_id: 's' }, summary: 'read', isError: true, durationMs: 5 },
    ]);
    expect(tree.map((t) => t.callId)).toEqual(['a', 'b']);
    expect(tree[1]!.children.map((t) => t.callId)).toEqual(['c']);
    expect(tree[1]!.children[0]!.status).toBe('error');
    expect(tree[1]!.subAgent).toMatchObject({ agent: 'researcher', status: 'completed' });
  });

  it('maps a persisted turn to the view model', () => {
    const turn = turnFromDto(turnDto({ error: 'Provider failed' }));
    expect(turn).toMatchObject({
      id: TURN_ID,
      key: TURN_ID,
      persisted: true,
      text: 'You get 25 days [S1].',
      status: 'error',
      usage: { inputTokens: 1200, outputTokens: 80, totalTokens: 1280 },
      errors: [{ message: 'Provider failed', code: null, agent: 'orchestrator' }],
      durationMs: 2400,
    });
    expect(turn.citations).toEqual([citationS1]);
  });

  it('maps a stored stopped turn to a stopped view with its text', () => {
    expect(turnFromDto(turnDto({ assistantMessage: 'You get', finishReason: 'stopped' }))).toMatchObject({ status: 'cancelled', text: 'You get', errors: [] });
    expect(turnFromDto(turnDto({ finishReason: 'cancelled' })).status).toBe('cancelled');
    expect(turnFromDto(turnDto({ finishReason: 'stopped', error: 'The answer could not be saved.' })).status).toBe('error');
  });

  it('prefers the richer live version of a turn and appends turns the server does not list yet', () => {
    const history = [turnFromDto(turnDto({ id: 't1' })), turnFromDto(turnDto({ id: TURN_ID }))];
    const live = run(fullTurnEvents()).turns;
    const pending = chatReducer(run([]), { type: 'send', id: 'local-2', message: 'Next', at: 1 }).turns;

    const merged = mergeTurns(history, [...live, pending[1]!]);
    expect(merged.map((t) => t.id)).toEqual(['t1', TURN_ID, 'local-2']);
    expect(merged[1]).toBe(live[0]);
    expect(merged[1]!.reasoning).not.toBe('');
  });

  it('indexes citations across the conversation', () => {
    const index = citationIndex([turnFromDto(turnDto({ citations: [citationS1] })), turnFromDto(turnDto({ id: 'x', citations: [citationS2, citationS3] }))]);
    expect([...index.keys()]).toEqual(['S1', 'S2', 'S3']);
  });
});
