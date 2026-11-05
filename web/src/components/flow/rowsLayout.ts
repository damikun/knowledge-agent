// No dagre here, so the small flows inside editors do not pull the layout engine into their chunks.
import { nodeHeight, type GraphNode, type PositionedNode } from './graph';

export interface RowsOptions {
  width: number;
  rowGap?: number;
  columnGap?: number;
}

/** Each row is centered under the widest one; a row of two is a branch. */
export function layoutRows<N extends GraphNode>(rows: readonly (readonly N[])[], options: RowsOptions): PositionedNode<N>[] {
  const rowGap = options.rowGap ?? 32;
  const columnGap = options.columnGap ?? 16;
  const rowWidth = (row: readonly N[]) => row.length * options.width + Math.max(0, row.length - 1) * columnGap;
  const total = Math.max(0, ...rows.map(rowWidth));
  const placed: PositionedNode<N>[] = [];
  let y = 0;
  for (const row of rows) {
    if (row.length === 0) {
      continue;
    }
    const height = Math.max(...row.map((node) => nodeHeight(node)));
    let x = (total - rowWidth(row)) / 2;
    for (const node of row) {
      placed.push({ node, x, y, width: options.width, height: nodeHeight(node) });
      x += options.width + columnGap;
    }
    y += height + rowGap;
  }
  return placed;
}
