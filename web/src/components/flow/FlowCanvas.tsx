import {
  Background,
  BackgroundVariant,
  Handle,
  Panel,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Edge,
  type Node,
  type NodeProps,
} from '@xyflow/react';
import { clsx } from 'clsx';
import { Maximize, Minus, Plus, type LucideIcon } from 'lucide-react';
import { useEffect, useId, useMemo, type KeyboardEvent } from 'react';
import { Badge } from '../ui/Badge';
import { TONES } from '../ui/tone';
import type { GraphEdge, GraphNode, GraphTone } from './graph';
import type { PositionedNode } from './graph';
import type { LayoutDirection } from './layout';
import './flow.css';

interface CardData extends Record<string, unknown> {
  node: GraphNode;
  icon: LucideIcon;
  selected: boolean;
}

type CardNode = Node<CardData, 'card'>;

const BORDER: Record<GraphTone, string> = {
  neutral: 'border-border',
  info: 'border-border',
  success: 'border-border',
  warning: 'border-warning',
  danger: 'border-danger',
};

function CardNodeView({ data }: NodeProps<CardNode>) {
  const { node, icon: Icon, selected } = data;
  const tone = TONES[node.tone];
  return (
    <div
      className={clsx(
        'flex h-full w-full flex-col overflow-hidden rounded-2xl border bg-surface px-3 py-2.5 text-left hover:border-border-hover',
        selected ? 'border-accent ring-1 ring-accent' : BORDER[node.tone],
        node.dimmed && !selected && 'opacity-60',
      )}
    >
      {/* Handles on every side, so a layout can bring edges in from any direction. */}
      <Handle id="top" type="target" position={Position.Top} isConnectable={false} />
      <Handle id="left" type="target" position={Position.Left} isConnectable={false} />
      <div className="flex min-w-0 items-center gap-2">
        <Icon className={clsx('size-4 shrink-0', node.tone === 'neutral' || node.tone === 'success' ? 'text-fg-muted' : tone.icon)} aria-hidden />
        <span className="min-w-0 flex-1 truncate text-sm leading-5 font-medium text-fg" title={node.title}>
          {node.title}
        </span>
        {node.status ? (
          <Badge tone={node.tone} className="shrink-0">
            {node.status}
          </Badge>
        ) : null}
      </div>
      {node.lines.map((line, index) => (
        <p key={index} className="truncate text-small leading-5 text-fg-secondary" title={line}>
          {line}
        </p>
      ))}
      {node.problem ? (
        <p className={clsx('truncate text-small leading-5 font-medium', tone.text)} title={node.problem}>
          {node.problem}
        </p>
      ) : null}
      <Handle id="bottom" type="source" position={Position.Bottom} isConnectable={false} />
      <Handle id="right" type="source" position={Position.Right} isConnectable={false} />
    </div>
  );
}

const NODE_TYPES = { card: CardNodeView };

function ZoomButton({ label, icon: Icon, onClick }: { label: string; icon: LucideIcon; onClick: () => void }) {
  return (
    <button
      type="button"
      className="flex size-8 items-center justify-center text-fg-secondary hover:bg-fill-hover hover:text-fg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
      aria-label={label}
      title={label}
      onClick={onClick}
    >
      <Icon className="size-4" aria-hidden />
    </button>
  );
}

// Our own buttons instead of React Flow's Controls, so they look like every other control in the portal.
function ZoomControls() {
  const flow = useReactFlow();
  return (
    <Panel position="bottom-right">
      <div className="flex flex-col overflow-hidden rounded-lg border border-border bg-raised shadow-control" role="group" aria-label="Zoom">
        <ZoomButton label="Zoom in" icon={Plus} onClick={() => void flow.zoomIn({ duration: 150 })} />
        <ZoomButton label="Zoom out" icon={Minus} onClick={() => void flow.zoomOut({ duration: 150 })} />
        <ZoomButton label="Fit to screen" icon={Maximize} onClick={() => void flow.fitView({ duration: 150, padding: 0.1 })} />
      </div>
    </Panel>
  );
}

function RefitOnChange({ signature }: { signature: string }) {
  const flow = useReactFlow();
  useEffect(() => {
    const frame = requestAnimationFrame(() => void flow.fitView({ padding: 0.08 }));
    return () => cancelAnimationFrame(frame);
  }, [flow, signature]);
  return null;
}

export interface FlowCanvasProps<N extends GraphNode> {
  label: string;
  positioned: readonly PositionedNode<N>[];
  edges: readonly GraphEdge[];
  direction: LayoutDirection;
  iconOf: (node: N) => LucideIcon;
  /** Clicking a node, or Enter or Space on a focused one. */
  onActivate: (node: N) => void;
  /** For the node's accessible name, e.g. "Opens the agent editor". */
  actionOf?: (node: N) => string;
  /** "link" when activating navigates, "button" when it opens something in place. */
  nodeRole?: 'link' | 'button';
  selectedId?: string | null;
  /** False: no pan, wheel zoom or zoom buttons, so the page scrolls over it. */
  interactive?: boolean;
  className?: string;
}

function accessibleName(node: GraphNode, action: string | undefined): string {
  return [node.title, node.status, ...node.lines, node.problem, action].filter(Boolean).join('. ');
}

function Canvas<N extends GraphNode>({
  label,
  positioned,
  edges,
  direction,
  iconOf,
  onActivate,
  actionOf,
  nodeRole = 'button',
  selectedId = null,
  interactive = true,
  className,
}: FlowCanvasProps<N>) {
  // useId may contain characters a url(#id) reference cannot carry.
  const markerId = `ka-arrow-${useId().replace(/[^a-zA-Z0-9]/g, '')}`;
  const byId = useMemo(() => new Map(positioned.map((item) => [item.node.id, item.node])), [positioned]);
  const signature = useMemo(() => positioned.map((p) => `${p.node.id}:${p.x}:${p.y}:${p.height}`).join('|'), [positioned]);

  const nodes = useMemo<CardNode[]>(
    () =>
      positioned.map(({ node, x, y, width, height }) => ({
        id: node.id,
        type: 'card',
        position: { x, y },
        width,
        height,
        data: { node, icon: iconOf(node), selected: node.id === selectedId },
        ariaLabel: accessibleName(node, actionOf?.(node)),
        ariaRole: nodeRole,
        draggable: false,
        connectable: false,
      })),
    [positioned, iconOf, selectedId, actionOf, nodeRole],
  );

  const flowEdges = useMemo<Edge[]>(
    () =>
      edges
        .filter((edge) => byId.has(edge.source) && byId.has(edge.target))
        .map((edge) => ({
          id: edge.id,
          source: edge.source,
          target: edge.target,
          sourceHandle: edge.sourceSide ?? (direction === 'LR' ? 'right' : 'bottom'),
          targetHandle: edge.targetSide ?? (direction === 'LR' ? 'left' : 'top'),
          type: 'smoothstep',
          label: edge.label,
          markerEnd: markerId,
          focusable: false,
          selectable: false,
          className: clsx(edge.muted && 'ka-edge-muted', edge.dashed && 'ka-edge-dashed'),
        })),
    [edges, byId, markerId, direction],
  );

  // React Flow's own Enter only selects; it bubbles here, where it does what a click does.
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Enter' && event.key !== ' ') {
      return;
    }
    const target = event.target instanceof HTMLElement ? event.target : null;
    const id = target?.classList.contains('react-flow__node') ? target.dataset.id : undefined;
    const node = id ? byId.get(id) : undefined;
    if (node) {
      event.preventDefault();
      onActivate(node);
    }
  };

  return (
    <div className={clsx('relative h-full w-full', className)} onKeyDown={onKeyDown} role="region" aria-label={label}>
      <svg aria-hidden width="0" height="0" className="absolute">
        <defs>
          <marker id={markerId} viewBox="-6 -5 8 10" refX="0" refY="0" markerWidth="8" markerHeight="8" orient="auto-start-reverse">
            <path className="ka-flow-arrow" d="M -5 -4 L 0 0 L -5 4 z" />
          </marker>
        </defs>
      </svg>
      <ReactFlow
        className="ka-flow"
        nodes={nodes}
        edges={flowEdges}
        nodeTypes={NODE_TYPES}
        onNodeClick={(_, node) => {
          const found = byId.get(node.id);
          if (found) {
            onActivate(found);
          }
        }}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        edgesFocusable={false}
        fitView
        fitViewOptions={{ padding: 0.08 }}
        minZoom={0.2}
        maxZoom={1.25}
        panOnDrag={interactive}
        zoomOnScroll={interactive}
        zoomOnPinch={interactive}
        zoomOnDoubleClick={interactive}
        preventScrolling={interactive}
        attributionPosition="bottom-left"
        ariaLabelConfig={{ 'node.a11yDescription.default': 'Press Enter to open.' }}
      >
        {interactive ? <Background variant={BackgroundVariant.Dots} gap={16} size={1} /> : null}
        {interactive ? <ZoomControls /> : null}
        <RefitOnChange signature={signature} />
      </ReactFlow>
    </div>
  );
}

/** A read-only React Flow graph in the portal's tokens: fixed layout, fit to view, zoom buttons and keyboard access. */
export function FlowCanvas<N extends GraphNode>(props: FlowCanvasProps<N>) {
  return (
    <ReactFlowProvider>
      <Canvas {...props} />
    </ReactFlowProvider>
  );
}
