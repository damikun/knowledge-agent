export type GraphTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export interface GraphNode<K extends string = string> {
  id: string;
  kind: K;
  title: string;
  status: string | null;
  tone: GraphTone;
  lines: string[];
  problem: string | null;
  dimmed?: boolean;
}

export interface GraphEdge {
  id: string;
  source: string;
  target: string;
  dashed?: boolean;
  muted?: boolean;
  label?: string;
  sourceSide?: 'bottom' | 'right';
  targetSide?: 'top' | 'left';
}

export interface Graph<N extends GraphNode = GraphNode> {
  nodes: N[];
  edges: GraphEdge[];
}

export interface PositionedNode<N extends GraphNode = GraphNode> {
  node: N;
  x: number;
  y: number;
  width: number;
  height: number;
}

export const NODE_WIDTH = 260;
const BASE_HEIGHT = 46;
const LINE_HEIGHT = 20;

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
