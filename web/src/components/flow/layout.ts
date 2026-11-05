import dagre from '@dagrejs/dagre';
import { NODE_WIDTH, nodeHeight, type Graph, type GraphNode, type PositionedNode } from './graph';

export type LayoutDirection = 'LR' | 'TB';

export type { PositionedNode } from './graph';

export interface LayoutOptions {
  direction: LayoutDirection;
  nodeSep?: number;
  rankSep?: number;
  width?: (node: GraphNode) => number;
}

/** Places every node with dagre; edges to unknown nodes are left out instead of growing phantom nodes. */
export function layoutGraph<N extends GraphNode>(graph: Graph<N>, options: LayoutOptions): PositionedNode<N>[] {
  const g = new dagre.graphlib.Graph();
  g.setGraph({ rankdir: options.direction, nodesep: options.nodeSep ?? 24, ranksep: options.rankSep ?? 72, marginx: 16, marginy: 16 });
  g.setDefaultEdgeLabel(() => ({}));
  const widthOf = options.width ?? (() => NODE_WIDTH);
  for (const node of graph.nodes) {
    g.setNode(node.id, { width: widthOf(node), height: nodeHeight(node) });
  }
  for (const edge of graph.edges) {
    if (g.hasNode(edge.source) && g.hasNode(edge.target)) {
      g.setEdge(edge.source, edge.target);
    }
  }
  dagre.layout(g);
  return graph.nodes.map((node) => {
    const placed = g.node(node.id) as { x: number; y: number; width: number; height: number };
    return { node, x: placed.x - placed.width / 2, y: placed.y - placed.height / 2, width: placed.width, height: placed.height };
  });
}

