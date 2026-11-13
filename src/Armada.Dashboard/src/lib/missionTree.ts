import type { MissionSummary, MissionTreeNode, VoyageTree } from '../types/models';

/**
 * Pure helpers for a voyage's mission tree (GET /api/v1/voyages/{id}/tree), shared by the dashboard and the mobile app.
 */

const RUNNING = new Set(['Assigned', 'InProgress']);
const PERSONA_SUFFIX = /\s*\[([^\]]+)\]\s*$/;

/** "Add the table [Worker]" reads "Add the table"; the persona shows as the first stage instead. */
export function baseTitle(title: string): string {
  return title.replace(PERSONA_SUFFIX, '') || title;
}

/** The stage's persona: the trailing "[Judge]" of its title, else its persona, else "Stage". */
export function stageLabel(stage: MissionSummary): string {
  const match = PERSONA_SUFFIX.exec(stage.title);
  return match ? match[1] : stage.persona || 'Stage';
}

/** True when a row should show its pipeline: it has later stages or a persona-tagged title. */
export function hasPipeline(node: MissionTreeNode): boolean {
  return node.stages.length > 0 || PERSONA_SUFFIX.test(node.mission.title);
}

/** The integration mission runs after every other pipeline, so it is listed last at its level. */
export function orderedNodes(nodes: MissionTreeNode[]): MissionTreeNode[] {
  return [...nodes.filter(n => !n.mission.waitForVoyageWorkers), ...nodes.filter(n => n.mission.waitForVoyageWorkers)];
}

export interface MissionTreeRow {
  depth: number;
  node: MissionTreeNode;
}

/** Depth-first rows: each node, then its children one level deeper, integration last at every level. */
export function treeRows(roots: MissionTreeNode[]): MissionTreeRow[] {
  const rows: MissionTreeRow[] = [];
  const visit = (node: MissionTreeNode, depth: number) => {
    rows.push({ depth, node });
    orderedNodes(node.children).forEach(child => visit(child, depth + 1));
  };
  orderedNodes(roots).forEach(root => visit(root, 0));
  return rows;
}

/** "continues parent", "forks from parent", "integration", or null (English source text). */
export function branchLabel(mission: MissionSummary): string | null {
  if (mission.waitForVoyageWorkers) return 'integration';
  if (!mission.parentMissionId) return null;
  if (mission.branchStrategy === 'ContinueParent') return 'continues parent';
  if (mission.branchStrategy === 'ForkFromParent') return 'forks from parent';
  return null;
}

/** Logical missions running now: any stage Assigned or InProgress. */
export function countRunning(roots: MissionTreeNode[]): number {
  let running = 0;
  const visit = (node: MissionTreeNode) => {
    if ([node.mission, ...node.stages].some(m => RUNNING.has(m.status))) running++;
    node.children.forEach(visit);
  };
  roots.forEach(visit);
  return running;
}

/** True when the tree adds something a flat mission list does not show: a parent link or pipeline stages. */
export function treeIsInformative(tree: VoyageTree | null | undefined): boolean {
  if (!tree) return false;
  return tree.maxDepth > 0 || tree.roots.some(function hasStages(node: MissionTreeNode): boolean {
    return node.stages.length > 0 || node.children.some(hasStages);
  });
}
