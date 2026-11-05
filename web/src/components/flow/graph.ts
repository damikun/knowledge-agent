export type GraphTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export interface GraphNode<K extends string = string> {
  id: string;
  kind: K;
  title: string;
  /** A short state for the badge, such as "On" or "1.2 s". */
  status: string | null;
  tone: GraphTone;
  lines: string[];
  /** Why this needs attention; set together with the warning or danger tone. */
  problem: string | null;
  /** Drawn quieter: present but not on any path. */
  dimmed?: boolean;
}

export interface GraphEdge {
  id: string;
  source: string;
  target: string;
  /** Dashed edges are paths that only sometimes run, such as a request for a person. */
  dashed?: boolean;
  muted?: boolean;
  label?: string;
  /** Where the edge leaves and enters; by default it follows the layout direction. */
  sourceSide?: 'bottom' | 'right';
  targetSide?: 'top' | 'left';
}

export interface Graph<N extends GraphNode = GraphNode> {
  nodes: N[];
  edges: GraphEdge[];
}

export interface PositionedNode<N extends GraphNode = GraphNode> {
  node: N;
  /** Top-left corner, as React Flow places nodes. */
  x: number;
  y: number;
  width: number;
  height: number;
}

export const NODE_WIDTH = 260;
const BASE_HEIGHT = 46;
const LINE_HEIGHT = 20;

/** Nodes have a fixed size, so dagre can place them before the browser measures anything. */
export function nodeHeight(node: Pick<GraphNode, 'lines' | 'problem'>): number {
  return BASE_HEIGHT + LINE_HEIGHT * (node.lines.length + (node.problem ? 1 : 0));
}

export function graphSize(positioned: readonly PositionedNode[]): { width: number; height: number } {
  if (positioned.length === 0) {
    return { width: 0, height: 0 };
  }
  const left = Math.min(...positioned.map((p) => p.x));
  const top = Math.min(...positioned.map((p) => p.y));
  return {
    width: Math.max(...positioned.map((p) => p.x + p.width)) - left,
    height: Math.max(...positioned.map((p) => p.y + p.height)) - top,
  };
}
