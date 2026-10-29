import type { BlackBoxCascadeEdge, BlackBoxCascadeGraph, BlackBoxCascadeNode } from '../types/blackbox';

export const CASCADE_NODE_WIDTH = 210;
export const CASCADE_NODE_HEIGHT = 58;
export const CASCADE_COLUMN_GAP = 70;
export const CASCADE_ROW_GAP = 18;
export const CASCADE_PADDING = 16;

export interface CascadeNodeBox { node: BlackBoxCascadeNode; x: number; y: number; depth: number }
export interface CascadeEdgePath { edge: BlackBoxCascadeEdge; d: string }
export interface CascadeLayout { nodes: CascadeNodeBox[]; edges: CascadeEdgePath[]; width: number; height: number }

/** Visual class of a mission status in the graph. */
export function cascadeStatusClass(status: string): 'failed' | 'blocked' | 'complete' | 'other' {
  if (status === 'Failed' || status === 'LandingFailed' || status === 'Cancelled') return 'failed';
  if (status === 'Pending' || status === 'Blocked') return 'blocked';
  if (status === 'Complete') return 'complete';
  return 'other';
}

/**
 * Layered left-to-right layout: a mission's column is one more than its dependency's (missions without a dependency
 * in the graph are in column 0), rows keep the server's order (oldest first). Edges are cubic curves from the right
 * of the source box to the left of the target box; an edge within one column bends out to the right.
 */
export function layoutCascade(graph: Pick<BlackBoxCascadeGraph, 'nodes' | 'edges'>): CascadeLayout {
  const byId = new Map(graph.nodes.map((n) => [n.missionId, n]));
  const depth = new Map<string, number>();
  const depthOf = (id: string, seen: Set<string>): number => {
    const known = depth.get(id);
    if (known !== undefined) return known;
    const node = byId.get(id);
    const parent = node?.dependsOnMissionId;
    let value = 0;
    if (parent && byId.has(parent) && !seen.has(parent)) {
      seen.add(id);
      value = depthOf(parent, seen) + 1;
    }
    depth.set(id, value);
    return value;
  };
  for (const n of graph.nodes) depthOf(n.missionId, new Set([n.missionId]));

  const rows = new Map<number, number>();
  const boxes: CascadeNodeBox[] = graph.nodes.map((node) => {
    const d = depth.get(node.missionId) ?? 0;
    const row = rows.get(d) ?? 0;
    rows.set(d, row + 1);
    return {
      node,
      depth: d,
      x: CASCADE_PADDING + d * (CASCADE_NODE_WIDTH + CASCADE_COLUMN_GAP),
      y: CASCADE_PADDING + row * (CASCADE_NODE_HEIGHT + CASCADE_ROW_GAP),
    };
  });
  const boxById = new Map(boxes.map((b) => [b.node.missionId, b]));
  const columns = Math.max(1, ...[...rows.keys()].map((k) => k + 1));
  const maxRows = Math.max(1, ...rows.values());

  const edges: CascadeEdgePath[] = [];
  for (const edge of graph.edges) {
    const from = boxById.get(edge.fromMissionId);
    const to = boxById.get(edge.toMissionId);
    if (!from || !to) continue;
    const midY1 = from.y + CASCADE_NODE_HEIGHT / 2;
    const midY2 = to.y + CASCADE_NODE_HEIGHT / 2;
    let d: string;
    if (to.x > from.x) {
      const x1 = from.x + CASCADE_NODE_WIDTH;
      const x2 = to.x;
      const c = (x2 - x1) / 2;
      d = `M ${x1} ${midY1} C ${x1 + c} ${midY1}, ${x2 - c} ${midY2}, ${x2} ${midY2}`;
    } else {
      const x1 = from.x + CASCADE_NODE_WIDTH;
      const x2 = to.x + CASCADE_NODE_WIDTH;
      const bend = Math.max(x1, x2) + CASCADE_COLUMN_GAP / 2;
      d = `M ${x1} ${midY1} C ${bend} ${midY1}, ${bend} ${midY2}, ${x2} ${midY2}`;
    }
    edges.push({ edge, d });
  }

  return {
    nodes: boxes,
    edges,
    width: CASCADE_PADDING * 2 + columns * CASCADE_NODE_WIDTH + (columns - 1) * CASCADE_COLUMN_GAP + CASCADE_COLUMN_GAP / 2,
    height: CASCADE_PADDING * 2 + maxRows * CASCADE_NODE_HEIGHT + (maxRows - 1) * CASCADE_ROW_GAP,
  };
}
