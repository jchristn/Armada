import type { BlackBoxEntry, BlackBoxEntrySummary } from '@dashboard/types/blackbox';
import type { StatusTone } from '../../components/ui/StatusBadge';
import { entrySubject } from './blackBoxModel';

// The black box resilience section (analysis.resilience, docs/BLACKBOX.md "Resilience bias"): whether Armada's own
// handoff and landing could have healed the failure, the detection gap, code pointers at the build commit, proposed
// Armada changes as mission briefs and the regression test. Typed locally (camelCase, as stored) so the app reads it
// whether or not the shared dashboard types carry it yet.

export type SelfHealable = 'Yes' | 'No' | 'Unknown';

export interface ResilienceChange {
  rank: number;
  title: string;
  change: string;
  acceptanceTest?: string | null;
  severity?: string;
  ruleId?: string | null;
  codePointers?: string[];
  missionBrief?: string | null;
}

export interface ResilienceRegressionTest {
  suite: string;
  description: string;
}

export interface Resilience {
  inScope?: boolean;
  scopeReason?: string | null;
  selfHealable?: SelfHealable;
  selfHealingProposal?: string | null;
  detectionGapKind?: string;
  detectionGap?: string | null;
  invariant?: string | null;
  codePointers?: string[];
  armadaVersion?: string | null;
  armadaCommit?: string | null;
  proposedChanges?: ResilienceChange[];
  regressionTest?: ResilienceRegressionTest | null;
  severity?: string;
  occurrences30Days?: number;
  armadaVersionCount?: number;
  attemptedRecoveries?: string[];
  noChangeReason?: string | null;
  ruleIds?: string[];
}

interface AnalysisWithResilience {
  resilience?: Resilience | null;
  triage?: { verdict?: string; suspected?: boolean; priorApplied?: boolean } | null;
}

/** The entry's resilience section, or null before analysis. */
export function resilienceOf(entry: BlackBoxEntry | null | undefined): Resilience | null {
  const analysis = entry?.record?.analysis as AnalysisWithResilience | null | undefined;
  return analysis?.resilience ?? null;
}

/** True when the entry is triaged ArmadaDefect (the "Suspected Armada defect" badge). */
export function isArmadaDefect(entry: Pick<BlackBoxEntrySummary, 'triage'> | null | undefined): boolean {
  return entry?.triage === 'ArmadaDefect';
}

/** True when the ArmadaDefect verdict rests on the handoff and landing prior alone (BBX-PRIOR-ARMADA). */
export function isPriorOnly(entry: BlackBoxEntry | null | undefined): boolean {
  const analysis = entry?.record?.analysis as AnalysisWithResilience | null | undefined;
  return !!analysis?.triage?.suspected;
}

/** Proposed Armada changes, most impactful first. */
export function rankedChanges(res: Resilience | null | undefined): ResilienceChange[] {
  return [...(res?.proposedChanges ?? [])].sort((a, b) => a.rank - b.rank);
}

/** The most impactful proposed Armada change, or null. */
export function topChange(res: Resilience | null | undefined): ResilienceChange | null {
  return rankedChanges(res)[0] ?? null;
}

/** The mission brief of a change (the server's MissionBrief, or one built from the title, change and test). */
export function changeBrief(change: ResilienceChange): string {
  if (change.missionBrief) return change.missionBrief;
  return [change.title, change.change, change.acceptanceTest ? `Acceptance test: ${change.acceptanceTest}` : null].filter(Boolean).join('\n\n');
}

/** Badge tone of a resilience severity. */
export function severityTone(severity: string | null | undefined): StatusTone {
  switch (severity) {
    case 'Critical':
    case 'High': return 'failed';
    case 'Medium': return 'warning';
    default: return 'info';
  }
}

/** Badge tone of a self-healable verdict. */
export function selfHealableTone(value: SelfHealable | null | undefined): StatusTone {
  return value === 'Yes' ? 'warning' : value === 'No' ? 'info' : 'skipped';
}

/** The build the code pointers refer to ("1.2.0 commit abc1234"), or null. */
export function buildLabel(res: Resilience, entry: BlackBoxEntry): string | null {
  const version = res.armadaVersion ?? entry.record?.site?.armadaVersion ?? null;
  const commit = res.armadaCommit ?? entry.record?.site?.armadaCommit ?? null;
  if (!version && !commit) return null;
  return [version, commit ? `commit ${commit}` : null].filter(Boolean).join(' ');
}

/**
 * The "Draft Armada fix mission" prompt the app pre-fills in Ask Armada (the same text the server's black box card
 * follow-up sends). Kept in English: it is a message to the captain, not interface text.
 */
export function armadaFixPrompt(entry: Pick<BlackBoxEntrySummary, 'id' | 'subjectType' | 'missionId' | 'voyageId'>, res: Resilience | null | undefined): string {
  const subject = entrySubject(entry);
  const top = topChange(res);
  return `Draft an Armada fix mission for black box entry ${entry.id} (${subject.kind} ${subject.id}): start from the entry's Resilience section`
    + (top ? ` (top proposed change: "${top.title}")` : '')
    + ', with its acceptance test, regression test and code pointers at the Armada build commit, and propose it on the Armada vessel for my approval.';
}
