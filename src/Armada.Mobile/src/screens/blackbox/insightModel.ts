import type {
  BlackBoxCascadeEdge,
  BlackBoxCascadeEdgeKind,
  BlackBoxCascadeGraph,
  BlackBoxCascadeNode,
  BlackBoxTimelineItem,
  BriefLintFinding,
  MissionOverlapKind,
  MissionOverlapPreview,
  MissionOverlapWarning,
} from '@dashboard/types/blackbox';

/**
 * Pure helpers for the black box prevention and forensics views on mobile (BLACKBOX.md section 12): dispatch overlap
 * and brief lint warnings, the combined timeline, and the voyage cascade graph as a list.
 */

/** Overlap warnings and brief lint findings, from a create response (mission or voyage) or an overlap preview. */
export interface DispatchWarnings {
  overlap: MissionOverlapWarning[];
  lint: BriefLintFinding[];
}

/** The warnings dispatch returned on a created mission or voyage (both fields are transient and often absent). */
export function dispatchWarningsOf(created: { overlapWarnings?: MissionOverlapWarning[] | null; briefLintWarnings?: BriefLintFinding[] | null } | null | undefined): DispatchWarnings {
  return { overlap: created?.overlapWarnings ?? [], lint: created?.briefLintWarnings ?? [] };
}

/** The warnings an overlap preview found (its overlaps and its brief lint). */
export function previewWarnings(preview: MissionOverlapPreview | null | undefined): DispatchWarnings {
  return { overlap: preview?.warnings ?? [], lint: preview?.briefLint?.findings ?? [] };
}

export function warningCount(warnings: DispatchWarnings | null | undefined): number {
  return warnings ? warnings.overlap.length + warnings.lint.length : 0;
}

/** Whether a preview is worth asking for: a vessel and some brief text. */
export function canPreview(vesselId: string | null | undefined, title: string | null | undefined, description: string | null | undefined): boolean {
  return !!vesselId && !!((title ?? '').trim() || (description ?? '').trim());
}

/** English label key of an overlap kind. */
export function overlapKindLabel(kind: MissionOverlapKind): string {
  if (kind === 'ActiveMission') return 'Active mission';
  if (kind === 'Reservation') return 'Reserved directory';
  if (kind === 'Hotspot') return 'Conflict hotspot';
  return 'Overlap';
}

/** A stable key for a timeline row (items have no id of their own). */
export function timelineItemKey(item: BlackBoxTimelineItem): string {
  return [item.timestampUtc, item.source, item.kind, item.entityId ?? '', item.message].join('|');
}

/** Where a timeline row's entity opens, when it is one the app has a screen for. */
export function timelineEntityRoute(entityId: string | null | undefined): string | null {
  if (!entityId) return null;
  if (entityId.startsWith('msn_')) return `/missions/${entityId}`;
  if (entityId.startsWith('vyg_')) return `/voyages/${entityId}`;
  if (entityId.startsWith('bbx_')) return `/blackbox/${entityId}`;
  if (entityId.startsWith('cpt_')) return `/captains/${entityId}`;
  if (entityId.startsWith('dck_')) return `/docks/${entityId}`;
  if (entityId.startsWith('mrg_')) return `/merge-queue/${entityId}`;
  return null;
}

/** Hours from the failure, signed and rounded ("+2.5h", "-0.3h"). */
export function formatHoursFromFailure(hours: number): string {
  const rounded = Math.round(hours * 10) / 10;
  return `${rounded > 0 ? '+' : ''}${rounded}h`;
}

/** The root first, then the failed missions, then the rest, each group in the graph's (oldest first) order. */
export function orderedCascadeNodes(graph: BlackBoxCascadeGraph): BlackBoxCascadeNode[] {
  const rank = (n: BlackBoxCascadeNode) => (n.isRoot ? 0 : n.entryId || n.failureKind ? 1 : 2);
  return graph.nodes.map((node, index) => ({ node, index })).sort((a, b) => rank(a.node) - rank(b.node) || a.index - b.index).map((x) => x.node);
}

/** Whether the graph has anything a failure view should show (a root, an entry, or a cascade edge). */
export function cascadeHasFailures(graph: BlackBoxCascadeGraph | null | undefined): boolean {
  if (!graph || graph.nodes.length === 0) return false;
  return !!graph.rootMissionId || graph.nodes.some((n) => !!n.entryId) || graph.edges.some((e) => e.kind !== 'DependsOn');
}

/** One edge as seen from a node: the other mission and the sentence (an English key with {{title}}). */
export interface CascadeRelation {
  edge: BlackBoxCascadeEdge;
  otherMissionId: string;
  label: string;
}

const INCOMING: Record<BlackBoxCascadeEdgeKind, string> = {
  DependsOn: 'Depends on {{title}}',
  CancelledBecause: 'Cancelled because {{title}} failed',
  BlockedBy: 'Blocked by {{title}}',
  Sibling: 'Failed independently after {{title}}',
  Unknown: 'Related to {{title}}',
};

const OUTGOING: Record<BlackBoxCascadeEdgeKind, string> = {
  DependsOn: '{{title}} depends on this',
  CancelledBecause: '{{title}} was cancelled because this failed',
  BlockedBy: '{{title}} is blocked by this',
  Sibling: '{{title}} also failed independently',
  Unknown: 'Related to {{title}}',
};

/** The edges touching a mission: first those into it (its causes), then those out of it (its effects). */
export function cascadeRelations(graph: BlackBoxCascadeGraph, missionId: string): CascadeRelation[] {
  const incoming = graph.edges.filter((e) => e.toMissionId === missionId)
    .map((edge) => ({ edge, otherMissionId: edge.fromMissionId, label: INCOMING[edge.kind] ?? INCOMING.Unknown }));
  const outgoing = graph.edges.filter((e) => e.fromMissionId === missionId)
    .map((edge) => ({ edge, otherMissionId: edge.toMissionId, label: OUTGOING[edge.kind] ?? OUTGOING.Unknown }));
  return [...incoming, ...outgoing];
}

/** A mission's display name in the graph (its title, else its id). */
export function cascadeNodeName(graph: BlackBoxCascadeGraph, missionId: string): string {
  const node = graph.nodes.find((n) => n.missionId === missionId);
  return node?.title || missionId;
}

/** The first warning's message, for a toast where there is no room for the list. */
export function firstWarningMessage(warnings: DispatchWarnings): string {
  return warnings.overlap[0]?.message ?? warnings.lint[0]?.message ?? '';
}

/** Every warning of several creates (a retry creates one mission per failed mission). */
export function mergeWarnings(all: DispatchWarnings[]): DispatchWarnings {
  return { overlap: all.flatMap((w) => w.overlap), lint: all.flatMap((w) => w.lint) };
}
