import { Handle, Position, ReactFlow, ReactFlowProvider, useReactFlow, useStore, type Edge, type Node, type NodeProps } from '@xyflow/react';
import { clsx } from 'clsx';
import {
  Activity,
  Archive,
  Database,
  DatabaseZap,
  Globe,
  KeyRound,
  Layers,
  LayoutDashboard,
  ListChecks,
  Mail,
  MessagesSquare,
  Server,
  Sparkles,
  Users,
  type LucideIcon,
} from 'lucide-react';
import { useCallback, useEffect, useId, useMemo, useState, type KeyboardEvent } from 'react';
import { StatusDot } from '../../components/ui/StatusDot';
import { BOUNDARIES, nodeSize, STATUS_LABELS, STATUS_TONES, type Installation, type InstallationNode, type InstallationNodeId, type Side } from './installation';
import '../../components/flow/flow.css';
import './installation.css';

export const NODE_ICONS: Record<InstallationNodeId, LucideIcon> = {
  visitors: Globe,
  team: Users,
  providers: Sparkles,
  portal: LayoutDashboard,
  api: Server,
  jobs: ListChecks,
  keycloak: KeyRound,
  postgres: Database,
  email: Mail,
  slack: MessagesSquare,
  langfuse: Activity,
  clickhouse: DatabaseZap,
  redis: Layers,
  minio: Archive,
};

/** For the node's accessible name and the list. */
export function describeNode(node: InstallationNode): string {
  return [node.title, node.status ? STATUS_LABELS[node.status] : null, node.role, node.image].filter(Boolean).join('. ');
}

interface PartData extends Record<string, unknown> {
  node: InstallationNode;
  selected: boolean;
}

interface FrameData extends Record<string, unknown> {
  label: string;
  inner: boolean;
}

type PartNode = Node<PartData, 'part'>;
type FrameNode = Node<FrameData, 'frame'>;

const SIDES: Record<Side, Position> = { top: Position.Top, right: Position.Right, bottom: Position.Bottom, left: Position.Left };

function Handles() {
  return (
    <>
      {(Object.keys(SIDES) as Side[]).map((side) => (
        <span key={side}>
          <Handle id={`in-${side}`} type="target" position={SIDES[side]} isConnectable={false} />
          <Handle id={`out-${side}`} type="source" position={SIDES[side]} isConnectable={false} />
        </span>
      ))}
    </>
  );
}

function PartView({ data }: NodeProps<PartNode>) {
  const { node, selected } = data;
  const Icon = NODE_ICONS[node.id];
  return (
    <div
      className={clsx(
        'flex h-full w-full flex-col justify-center overflow-hidden rounded-xl border bg-raised text-left shadow-card hover:border-border-hover',
        node.small ? 'gap-0.5 px-2.5 py-2' : 'gap-0.5 px-3 py-2.5',
        selected ? 'border-accent ring-1 ring-accent' : node.status === 'down' ? 'border-danger' : node.status === 'degraded' ? 'border-warning' : 'border-border',
        node.dimmed && !selected && 'opacity-60',
      )}
    >
      <Handles />
      <div className="flex min-w-0 items-center gap-2">
        <span aria-hidden className={clsx('flex shrink-0 items-center justify-center rounded-md bg-fill-muted text-fg-secondary', node.small ? 'size-5' : 'size-6')}>
          <Icon className={node.small ? 'size-3' : 'size-3.5'} />
        </span>
        <span className={clsx('min-w-0 flex-1 truncate leading-5 font-medium text-fg', node.small ? 'text-[13px]' : 'text-[15px]')}>{node.title}</span>
        {node.status ? <StatusDot tone={STATUS_TONES[node.status]} className={node.status === 'unknown' ? 'opacity-50' : undefined} /> : null}
      </div>
      {node.small ? null : <p className="truncate text-[13px] leading-[18px] text-fg-secondary">{node.role}</p>}
      {node.image ? <p className={clsx('truncate font-mono leading-4 text-fg-muted', node.small ? 'text-[11px]' : 'text-xs')}>{node.image}</p> : null}
    </div>
  );
}

function FrameView({ data }: NodeProps<FrameNode>) {
  return (
    <div
      aria-hidden
      className={clsx(
        'pointer-events-none h-full w-full rounded-2xl border border-dashed',
        data.inner ? 'border-border bg-surface-subtle/40' : 'border-border-strong',
      )}
    >
      <span className={clsx('absolute top-2 text-xs font-medium tracking-wide text-fg-muted uppercase', data.inner ? 'right-3' : 'left-3')}>{data.label}</span>
    </div>
  );
}

const NODE_TYPES = { part: PartView, frame: FrameView };

// Refit once React Flow has measured the frame's new size.
function FitOnResize() {
  const flow = useReactFlow();
  const width = useStore((state) => state.width);
  const height = useStore((state) => state.height);
  useEffect(() => {
    const frame = requestAnimationFrame(() => void flow.fitView({ padding: 0.02 }));
    return () => cancelAnimationFrame(frame);
  }, [flow, width, height]);
  return null;
}

export interface InstallationDiagramProps {
  installation: Installation;
  selectedId: InstallationNodeId | null;
  onSelect: (node: InstallationNode) => void;
  /** The frame keeps these proportions, so the picture always fits without scrolling. */
  size: { width: number; height: number };
}

function Diagram({ installation, selectedId, onSelect, size }: InstallationDiagramProps) {
  const markerId = `ka-install-arrow-${useId().replace(/[^a-zA-Z0-9]/g, '')}`;
  const [width, setWidth] = useState(0);
  const measure = useCallback((element: HTMLDivElement | null) => {
    if (!element || typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver(([entry]) => setWidth(entry?.contentRect.width ?? 0));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  // A little padding, but never larger than the picture's own size.
  const height = Math.round(Math.min(size.height * 1.04, (width || size.width) * (size.height / size.width) * 1.04));

  const byId = useMemo(() => new Map(installation.nodes.map((node) => [node.id, node])), [installation.nodes]);

  const nodes = useMemo<Array<PartNode | FrameNode>>(() => {
    const frames: FrameNode[] = (['server', 'tracing'] as const).map((key) => {
      const box = BOUNDARIES[key];
      return {
        id: `frame:${key}`,
        type: 'frame',
        position: { x: box.x, y: box.y },
        width: box.width,
        height: box.height,
        data: { label: box.label, inner: key === 'tracing' },
        draggable: false,
        selectable: false,
        focusable: false,
        connectable: false,
        zIndex: -1,
      };
    });
    const parts: PartNode[] = installation.nodes.map((node) => ({
      id: node.id,
      type: 'part',
      position: { x: node.x, y: node.y },
      ...nodeSize(node),
      data: { node, selected: node.id === selectedId },
      ariaLabel: `${describeNode(node)}. Opens details.`,
      ariaRole: 'button',
      draggable: false,
      connectable: false,
    }));
    return [...frames, ...parts];
  }, [installation.nodes, selectedId]);

  const edges = useMemo<Edge[]>(
    () =>
      installation.edges.map((edge) => ({
        id: edge.id,
        source: edge.source,
        target: edge.target,
        sourceHandle: `out-${edge.from}`,
        targetHandle: `in-${edge.to}`,
        type: 'default',
        label: edge.label,
        labelBgPadding: [4, 2] as [number, number],
        labelBgBorderRadius: 4,
        markerEnd: markerId,
        focusable: false,
        selectable: false,
        className: clsx(edge.dashed && 'ka-edge-dashed', edge.muted && 'ka-edge-muted'),
      })),
    [installation.edges, markerId],
  );

  // React Flow's Enter only selects; open the details like a click does.
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Enter' && event.key !== ' ') {
      return;
    }
    const target = event.target instanceof HTMLElement ? event.target : null;
    const id = target?.classList.contains('react-flow__node') ? target.dataset.id : undefined;
    const node = id ? byId.get(id as InstallationNodeId) : undefined;
    if (node) {
      event.preventDefault();
      onSelect(node);
    }
  };

  return (
    <div ref={measure} className="relative w-full" style={{ height }} onKeyDown={onKeyDown} role="region" aria-label="How your installation is built, as a diagram">
      <svg aria-hidden width="0" height="0" className="absolute">
        <defs>
          <marker id={markerId} viewBox="-6 -5 8 10" refX="0" refY="0" markerWidth="8" markerHeight="8" orient="auto-start-reverse">
            <path className="ka-installation-arrow" d="M -5 -4 L 0 0 L -5 4 z" />
          </marker>
        </defs>
      </svg>
      <ReactFlow
        className="ka-flow ka-installation"
        nodes={nodes}
        edges={edges}
        nodeTypes={NODE_TYPES}
        onNodeClick={(_, clicked) => {
          const node = byId.get(clicked.id as InstallationNodeId);
          if (node) {
            onSelect(node);
          }
        }}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        edgesFocusable={false}
        fitView
        fitViewOptions={{ padding: 0.02 }}
        minZoom={0.2}
        maxZoom={1}
        panOnDrag={false}
        panOnScroll={false}
        zoomOnScroll={false}
        zoomOnPinch={false}
        zoomOnDoubleClick={false}
        preventScrolling={false}
        proOptions={{ hideAttribution: true }}
        ariaLabelConfig={{ 'node.a11yDescription.default': 'Press Enter to see details.' }}
      >
        <FitOnResize />
      </ReactFlow>
    </div>
  );
}

/** Read-only: fit to the frame, no pan or zoom. */
export function InstallationDiagram(props: InstallationDiagramProps) {
  return (
    <ReactFlowProvider>
      <Diagram {...props} />
    </ReactFlowProvider>
  );
}
