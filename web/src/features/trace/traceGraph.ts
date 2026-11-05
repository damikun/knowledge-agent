import { capitalize, formatDuration, pluralize } from '../../lib/format';
import type { ToolCallView, TurnView } from '../chat/chatReducer';
import { extractCitationRefs } from '../chat/citations';
import { isDelegateTool, isTransferTool, summarizeToolArguments, toolMeta } from '../chat/tools';
import { ToolNames } from '../chat/types';
import type { Graph, GraphEdge, GraphNode, GraphTone } from '../../components/flow/graph';

type TraceNodeKind = 'question' | 'tool' | 'researcher' | 'transfer' | 'answer';

export interface TraceNode extends GraphNode<TraceNodeKind> {
  call: ToolCallView | null;
}

export type TraceGraph = Graph<TraceNode>;

export const QUESTION_NODE = 'question';
export const ANSWER_NODE = 'answer';

function clip(text: string, length = 60): string {
  const flat = text.replace(/\s+/g, ' ').trim();
  return flat.length > length ? `${flat.slice(0, length - 1)}…` : flat;
}

function callTone(call: ToolCallView): GraphTone {
  if (call.status === 'error') {
    return 'danger';
  }
  if (call.status === 'running') {
    return 'info';
  }
  return call.status === 'cancelled' ? 'warning' : 'neutral';
}

function callStatus(call: ToolCallView): string | null {
  if (call.status === 'running') {
    return 'Running';
  }
  if (call.status === 'cancelled') {
    return 'Stopped';
  }
  return call.durationMs != null ? formatDuration(call.durationMs) : null;
}

function argument(call: ToolCallView, name: string): string | null {
  const args = call.arguments;
  const value = args && typeof args === 'object' && !Array.isArray(args) ? args[name] : undefined;
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

function transferNode(call: ToolCallView): TraceNode {
  const reason = argument(call, 'reason');
  const target = call.toolName === ToolNames.TransferToAgent ? argument(call, 'agent') : null;
  // The summary only arrives with the result; until then show the agent key.
  const title = call.status === 'success' && call.summary ? call.summary : target ? `Handing to ${target}` : toolMeta(call.toolName).label;
  return {
    id: `call:${call.callId}`,
    kind: 'transfer',
    title: clip(title, 40),
    status: callStatus(call),
    tone: call.status === 'error' ? 'danger' : 'info',
    lines: reason ? [clip(reason)] : [],
    problem: call.status === 'error' ? clip(call.summary ?? 'The hand-off failed', 48) : null,
    call,
  };
}

function callNode(call: ToolCallView, sourceTitle?: (id: string) => string | undefined): TraceNode {
  if (isTransferTool(call.toolName)) {
    return transferNode(call);
  }
  const delegate = isDelegateTool(call.toolName) || call.subAgent !== null;
  const { primary, details } = summarizeToolArguments(call.toolName, call.arguments, sourceTitle);
  const lines: string[] = [];
  if (primary) {
    lines.push(clip(primary));
  }
  if (call.toolName === ToolNames.SearchKnowledgeBase || call.citations.length > 0) {
    lines.push(pluralize(call.citations.length, 'passage'));
  } else if (details.length > 0) {
    lines.push(clip(details.join(', ')));
  }
  if (delegate) {
    lines.push(pluralize(call.children.length, 'step'));
  }
  return {
    id: `call:${call.callId}`,
    kind: delegate ? 'researcher' : 'tool',
    title: delegate ? (call.subAgent?.agent ? capitalize(call.subAgent.agent) : 'Researcher') : toolMeta(call.toolName).label,
    status: callStatus(call),
    tone: callTone(call),
    lines,
    problem: call.status === 'error' ? clip(call.summary ?? 'The tool failed', 48) : null,
    call,
  };
}

export function buildTraceGraph(turn: TurnView, sourceTitle?: (id: string) => string | undefined): TraceGraph {
  const nodes: TraceNode[] = [];
  const edges: GraphEdge[] = [];
  const link = (source: string, target: string, label?: string) => edges.push({ id: `${source}->${target}`, source, target, ...(label ? { label } : {}) });

  nodes.push({ id: QUESTION_NODE, kind: 'question', title: 'Question', status: null, tone: 'neutral', lines: [clip(turn.userMessage)], problem: null, call: null });

  let previous = QUESTION_NODE;
  for (const call of turn.tools) {
    const node = callNode(call, sourceTitle);
    nodes.push(node);
    link(previous, node.id);
    previous = node.id;
    // A researcher's steps run inside its call, so they hang off it in a side chain.
    let inner = node.id;
    for (const child of call.children) {
      const childNode = callNode(child, sourceTitle);
      nodes.push(childNode);
      link(inner, childNode.id);
      inner = childNode.id;
    }
  }

  const cited = new Set(extractCitationRefs(turn.text).map((ref) => ref.toUpperCase()));
  const failed = turn.status === 'error' || turn.errors.some((error) => error.code !== 'cancelled');
  const lines = [turn.text ? clip(turn.text) : turn.status === 'streaming' ? 'Writing…' : 'No answer text'];
  lines.push(cited.size > 0 ? `Cites ${pluralize(cited.size, 'passage')}` : 'No citations');
  nodes.push({
    id: ANSWER_NODE,
    kind: 'answer',
    title: 'Answer',
    status: turn.status === 'streaming' ? 'Writing' : turn.durationMs ? formatDuration(turn.durationMs) : null,
    tone: failed ? 'danger' : turn.status === 'cancelled' ? 'warning' : turn.status === 'streaming' ? 'info' : 'success',
    lines,
    problem: failed ? clip(turn.errors[0]?.message ?? 'The answer failed', 48) : null,
    call: null,
  });
  link(previous, ANSWER_NODE);
  return { nodes, edges };
}
