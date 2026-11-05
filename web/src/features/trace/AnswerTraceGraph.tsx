import { CircleHelp, MessageSquareText, Telescope, type LucideIcon } from 'lucide-react';
import { useCallback, useId, useMemo, useState } from 'react';
import type { TurnView } from '../chat/chatReducer';
import { toolMeta } from '../chat/tools';
import { FlowCanvas } from '../../components/flow/FlowCanvas';
import { layoutGraph } from '../../components/flow/layout';
import { TraceDetails } from './TraceDetails';
import { ANSWER_NODE, buildTraceGraph, type TraceNode } from './traceGraph';

const TRACE_NODE_WIDTH = 220;

function iconOf(node: TraceNode): LucideIcon {
  switch (node.kind) {
    case 'question':
      return CircleHelp;
    case 'answer':
      return MessageSquareText;
    case 'researcher':
      return Telescope;
    default:
      return node.call ? toolMeta(node.call.toolName).icon : CircleHelp;
  }
}

const actionOf = () => 'Shows its details';

export interface AnswerTraceProps {
  turn: TurnView;
  sourceTitle?: (id: string) => string | undefined;
  height?: number | string;
}

/** Load through AnswerTrace, which splits it into its own chunk. */
function AnswerTraceGraph({ turn, sourceTitle, height = '28rem' }: AnswerTraceProps) {
  const graph = useMemo(() => buildTraceGraph(turn, sourceTitle), [turn, sourceTitle]);
  const positioned = useMemo(() => layoutGraph(graph, { direction: 'TB', nodeSep: 24, rankSep: 36, width: () => TRACE_NODE_WIDTH }), [graph]);
  const [selectedId, setSelectedId] = useState<string>(ANSWER_NODE);
  const selected = graph.nodes.find((node) => node.id === selectedId) ?? null;
  const onActivate = useCallback((node: TraceNode) => setSelectedId(node.id), []);
  const panelId = useId();

  return (
    <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_18rem]">
      <div className="overflow-hidden rounded-2xl border border-border bg-surface-subtle" style={{ height }}>
        <FlowCanvas
          label="Answer trace: the question, each step and the answer. Select a step to see its details."
          positioned={positioned}
          edges={graph.edges}
          direction="TB"
          iconOf={iconOf}
          onActivate={onActivate}
          actionOf={actionOf}
          nodeRole="button"
          selectedId={selectedId}
        />
      </div>
      <aside
        id={panelId}
        aria-label="Step details"
        aria-live="polite"
        className="min-h-0 overflow-y-auto rounded-2xl border border-border bg-surface-subtle p-3 md:max-h-(--trace-height)"
        style={{ ['--trace-height' as string]: typeof height === 'number' ? `${height}px` : height }}
      >
        {selected ? <h3 className="mb-2 text-sm font-medium text-fg">{selected.title}</h3> : null}
        <TraceDetails node={selected} turn={turn} />
      </aside>
    </div>
  );
}

export default AnswerTraceGraph;
