/*
 * The detail pages' content: what a mission, voyage, captain or merge entry shows and which actions it offers, as
 * DetailPage data (detailHtml.ts renders it). Pure, for tests.
 */

import type { Captain, MergeEntry, Mission, MissionSummary, VoyageProgress } from '@dashboard/types/models';
import type { MissionAttempt, MissionAttemptHistory } from '../../transport/hostClient';
import { formatAge } from '../../util/format';
import { DetailAction, DetailListItem, DetailPage, DetailSection, Tone } from './detailHtml';
import { ACTIVE_MISSION_STATUSES, FINISHED_MISSION_STATUSES, OpsStore, QUEUED_MISSION_STATUSES } from './opsStore';
import { elapsed, humanize } from './presentation';

/** Tone for a mission, voyage, captain or merge status. */
export function statusTone(status: string): Tone {
  if (['Complete', 'Landed', 'Passed'].includes(status)) return 'ok';
  if (['Failed', 'LandingFailed', 'Quarantined'].includes(status)) return 'error';
  if (['Review', 'Blocked', 'Stalled', 'Stopping'].includes(status)) return 'warn';
  if (['Cancelled', 'Idle', 'Pending', 'Queued', 'Open'].includes(status)) return 'muted';
  return 'info';
}

function stamp(iso: string | null | undefined, now: Date): string {
  if (!iso) return '';
  const age = formatAge(iso, now);
  return `${new Date(iso).toLocaleString()}${age ? ` (${age})` : ''}`;
}

/** Tone for a stage attempt's disposition (StageDispositionEnum). */
export function dispositionTone(disposition: string | null | undefined): Tone {
  if (disposition === 'Completed') return 'ok';
  if (disposition === 'Failed') return 'error';
  if (disposition === 'Escalated' || disposition === 'Relaunched' || disposition === 'OutputRepair' || disposition === 'Interrupted') return 'warn';
  if (disposition === 'Cancelled' || disposition === 'Pending') return 'muted';
  return 'info';
}

function seconds(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms) || ms < 0) return '';
  if (ms < 1000) return `${Math.round(ms)} ms`;
  const total = Math.round(ms / 1000);
  if (total < 60) return `${total}s`;
  return `${Math.floor(total / 60)}m ${total % 60}s`;
}

/** One attempt as a list row: model and captain, then failure kind, repair, escalation, Judge verdict and duration. */
export function attemptRow(attempt: MissionAttempt): DetailListItem {
  const who = [attempt.model, attempt.captainName ?? attempt.captainId].filter(Boolean).join(' on ');
  const repair = attempt.isRepair ? `repair${attempt.contractRepairKind && attempt.contractRepairKind !== 'None' ? ` (${humanize(attempt.contractRepairKind)})` : ''}` : '';
  const failure = attempt.failureKind && attempt.failureKind !== 'None' ? humanize(attempt.failureKind) : '';
  const runtimeFailure = attempt.runtimeFailureKind && attempt.runtimeFailureKind !== 'None' ? `runtime ${humanize(attempt.runtimeFailureKind)}` : '';
  const parts = [
    failure,
    runtimeFailure,
    repair,
    attempt.escalated ? 'escalated' : '',
    attempt.judgeVerdict ? `Judge ${humanize(attempt.judgeVerdict)}` : '',
    seconds(attempt.durationMs),
  ].filter(Boolean);
  const round = attempt.reworkRound > 0 ? `, rework ${attempt.reworkRound}` : '';
  return {
    label: `Attempt ${attempt.attempt}${round}${who ? `: ${who}` : ''}`,
    description: [parts.join(' · '), attempt.failureReason ?? ''].filter(Boolean).join(' · '),
    status: { label: humanize(attempt.disposition ?? 'Unknown'), tone: dispositionTone(attempt.disposition) },
    link: attempt.captainId ? { command: 'armada.captain.open', args: [attempt.captainId] } : undefined,
  };
}

/** The attempts and escalation sections of a mission page (empty when the Admiral has no history for it). */
export function attemptSections(history: MissionAttemptHistory | undefined): DetailSection[] {
  if (!history) return [];
  const sections: DetailSection[] = [];
  const rows = [
    { label: 'Tier', value: history.tier ?? '' },
    { label: 'Escalations', value: history.escalationCount ? `${history.escalationCount}${history.escalatedFromModel ? ` (from ${history.escalatedFromModel})` : ''}` : '' },
    { label: 'Rework escalated', value: history.reworkEscalated ? 'Yes' : '' },
    { label: 'Redispatches', value: history.redispatchAttempts ? String(history.redispatchAttempts) : '' },
    { label: 'Output repairs', value: history.outputRepairAttempts ? String(history.outputRepairAttempts) : '' },
    { label: 'No-op redispatches', value: history.noOpRedispatchAttempts ? String(history.noOpRedispatchAttempts) : '' },
    { label: 'Stall recoveries', value: history.stallRecoveryAttempts ? String(history.stallRecoveryAttempts) : '' },
  ].filter((row) => row.value);
  if (rows.length) sections.push({ kind: 'fields', title: 'Recovery', rows });
  sections.push({ kind: 'list', title: `Attempts (${history.attempts.length})`, items: history.attempts.map(attemptRow), empty: 'No stage attempts recorded yet.' });
  return sections;
}

/** The section that says why a Pending mission is waiting, with its typed reason and the advice that clears it. */
export function blockerSection(mission: Mission): DetailSection | undefined {
  const blocker = mission.assignmentBlocker;
  if (!blocker || !QUEUED_MISSION_STATUSES.includes(mission.status)) return undefined;
  const advice = (blocker.advice ?? []).map((a) => `- ${a.text}`).join('\n');
  const text = [blocker.summary, advice].filter(Boolean).join('\n');
  return { kind: 'text', title: humanize(blocker.reason), text: text || humanize(blocker.reason), tone: 'warn' };
}

/** The mission page. logTail is the end of the log for a running mission; history is its attempt history. */
export function missionPage(mission: Mission, store: OpsStore, logTail: string | undefined, now: Date = new Date(), history?: MissionAttemptHistory): DetailPage {
  const active = ACTIVE_MISSION_STATUSES.includes(mission.status);
  const finished = FINISHED_MISSION_STATUSES.includes(mission.status);
  const actions: DetailAction[] = [
    { label: 'View Log', command: 'armada.mission.viewLog', args: [mission.id], primary: true },
    { label: 'View Changes', command: 'armada.mission.viewDiff', args: [mission.id] },
  ];
  if (mission.status === 'Review') {
    actions.push({ label: 'Approve', command: 'armada.mission.approve', args: [mission.id], primary: true });
    actions.push({ label: 'Deny', command: 'armada.mission.deny', args: [mission.id] });
  }
  if (!finished) actions.push({ label: 'Cancel Mission', command: 'armada.mission.cancel', args: [mission.id] });
  if (mission.status === 'Failed' || mission.status === 'LandingFailed' || mission.status === 'Cancelled') actions.push({ label: 'Restart', command: 'armada.mission.restart', args: [mission.id] });
  if (mission.status === 'Failed' || mission.status === 'LandingFailed' || mission.lastBlackBoxEntryId) actions.push({ label: 'Black Box', command: 'armada.openBlackBox', args: [mission.id] });
  if (mission.prUrl) actions.push({ label: 'Open Pull Request', command: 'armada.openUrl', args: [mission.prUrl] });
  actions.push({ label: 'More...', command: 'armada.resource.action', args: ['mission', mission.id] });
  actions.push({ label: 'Open in Browser', command: 'armada.openInBrowser', args: [`/missions/${mission.id}`] });

  const runtime = mission.startedUtc ? elapsed(mission.startedUtc, mission.completedUtc, now) : '';
  const sections: DetailSection[] = [
    {
      kind: 'fields',
      rows: [
        { label: 'Vessel', value: store.vesselName(mission.vesselId), link: mission.vesselId ? { command: 'armada.resource.open', args: ['vessel', mission.vesselId] } : undefined },
        { label: 'Captain', value: store.captainName(mission.captainId), link: mission.captainId ? { command: 'armada.captain.open', args: [mission.captainId] } : undefined },
        { label: 'Voyage', value: mission.voyageId ? store.voyages.find((v) => v.voyage.id === mission.voyageId)?.voyage.title ?? mission.voyageId : '', link: mission.voyageId ? { command: 'armada.voyage.open', args: [mission.voyageId] } : undefined },
        { label: 'Parent', value: mission.parentMissionId ?? '', code: true, link: mission.parentMissionId ? { command: 'armada.mission.open', args: [mission.parentMissionId] } : undefined },
        { label: 'Persona', value: mission.persona ?? '' },
        { label: 'Mode', value: mission.mode ? humanize(mission.mode) : '' },
        { label: 'Branch', value: mission.branchName ?? '', code: true },
        { label: 'Commit', value: mission.commitHash ?? '', code: true },
        { label: 'Pull request', value: mission.prUrl ?? '', link: mission.prUrl ? { command: 'armada.openUrl', args: [mission.prUrl] } : undefined },
        { label: 'Priority', value: String(mission.priority ?? '') },
        { label: 'Created', value: stamp(mission.createdUtc, now) },
        { label: 'Started', value: stamp(mission.startedUtc, now) },
        { label: 'Completed', value: stamp(mission.completedUtc, now) },
        { label: active ? 'Running for' : 'Runtime', value: runtime },
      ],
    },
  ];
  const blocker = blockerSection(mission);
  if (blocker) sections.push(blocker);
  if (mission.failureReason) sections.push({ kind: 'text', title: humanize(mission.failureKind) || 'Failure', text: mission.failureReason, tone: 'error' });
  sections.push(...attemptSections(history));
  if (mission.status === 'Review' && mission.reviewComment) sections.push({ kind: 'text', title: 'Review', text: mission.reviewComment });
  if (mission.description) sections.push({ kind: 'text', title: 'Brief', text: mission.description });
  if (logTail !== undefined) sections.push({ kind: 'pre', title: 'Live log', note: 'last lines, updating', text: logTail || '(no output yet)' });
  return { title: mission.title || mission.id, status: { label: humanize(mission.status), tone: statusTone(mission.status) }, subtitle: mission.id, actions, sections };
}

function missionRow(m: MissionSummary, store: OpsStore, now: Date): DetailListItem {
  const when = ACTIVE_MISSION_STATUSES.includes(m.status) ? elapsed(m.startedUtc ?? m.createdUtc, null, now) : formatAge(m.completedUtc ?? m.lastUpdateUtc, now);
  return {
    label: m.title || m.id,
    description: [store.captainName(m.captainId), when].filter(Boolean).join(' · '),
    status: { label: humanize(m.status), tone: statusTone(m.status) },
    link: { command: 'armada.mission.open', args: [m.id] },
  };
}

/** The voyage page. */
export function voyagePage(progress: VoyageProgress, missions: MissionSummary[], store: OpsStore, now: Date = new Date()): DetailPage {
  const voyage = progress.voyage;
  const live = voyage.status === 'Open' || voyage.status === 'InProgress';
  const actions: DetailAction[] = [];
  if (live) actions.push({ label: 'Cancel Voyage', command: 'armada.voyage.cancel', args: [voyage.id] });
  actions.push({ label: 'More...', command: 'armada.resource.action', args: ['voyage', voyage.id] });
  actions.push({ label: 'Open in Browser', command: 'armada.openInBrowser', args: [`/voyages/${voyage.id}`] });
  const order = (m: MissionSummary): number => (ACTIVE_MISSION_STATUSES.includes(m.status) ? 0 : m.status === 'Review' ? 1 : QUEUED_MISSION_STATUSES.includes(m.status) ? 2 : 3);
  const sorted = [...missions].sort((a, b) => order(a) - order(b));
  return {
    title: voyage.title || voyage.id,
    status: { label: humanize(voyage.status), tone: statusTone(voyage.status) },
    subtitle: voyage.id,
    actions,
    sections: [
      { kind: 'progress', done: progress.completedMissions, failed: progress.failedMissions, running: progress.inProgressMissions, total: progress.totalMissions },
      {
        kind: 'fields',
        rows: [
          { label: 'Vessel', value: progress.vesselCount > 1 ? `${progress.vesselCount} vessels` : store.vesselName(progress.vesselId) },
          { label: 'Landing', value: humanize(voyage.landingMode) },
          { label: 'Created', value: stamp(voyage.createdUtc, now) },
          { label: 'Completed', value: stamp(voyage.completedUtc, now) },
        ],
      },
      ...(voyage.description ? [{ kind: 'text', title: 'Description', text: voyage.description } as DetailSection] : []),
      { kind: 'list', title: `Missions (${missions.length})`, items: sorted.map((m) => missionRow(m, store, now)), empty: 'No missions loaded for this voyage.' },
    ],
  };
}

/** The captain page. */
export function captainPage(captain: Captain, missions: MissionSummary[], store: OpsStore, logTail: string | undefined, now: Date = new Date()): DetailPage {
  const actions: DetailAction[] = [{ label: 'View Log', command: 'armada.captain.viewLog', args: [captain.id], primary: true }];
  if (captain.state !== 'Idle') actions.push({ label: 'Stop Captain', command: 'armada.captain.stop', args: [captain.id] });
  actions.push({ label: 'Restart Captain', command: 'armada.captain.restart', args: [captain.id] });
  actions.push({ label: 'Edit', command: 'armada.resource.edit', args: ['captain', captain.id] });
  actions.push({ label: 'Open in Browser', command: 'armada.openInBrowser', args: [`/captains/${captain.id}`] });
  const active = missions.filter((m) => ACTIVE_MISSION_STATUSES.includes(m.status));
  const recent = missions.filter((m) => !ACTIVE_MISSION_STATUSES.includes(m.status)).slice(0, 15);
  const sections: DetailSection[] = [
    {
      kind: 'fields',
      rows: [
        { label: 'Runtime', value: captain.runtime },
        { label: 'Model', value: captain.model ?? '' },
        { label: 'Reasoning', value: captain.reasoningEffort ?? '' },
        { label: 'Concurrency', value: captain.maxConcurrency ? `${active.length}/${captain.maxConcurrency}` : '' },
        { label: 'Preferred persona', value: captain.preferredPersona ?? '' },
        { label: 'Last heartbeat', value: stamp(captain.lastHeartbeatUtc, now) },
        { label: 'Process', value: captain.processId ? String(captain.processId) : '' },
      ],
    },
  ];
  if (captain.quarantineReason) sections.push({ kind: 'text', title: 'Quarantined', text: captain.quarantineReason, tone: 'error' });
  sections.push({ kind: 'list', title: `Working on (${active.length})`, items: active.map((m) => missionRow(m, store, now)), empty: 'Not working on a mission.' });
  if (recent.length) sections.push({ kind: 'list', title: 'Recent missions', items: recent.map((m) => missionRow(m, store, now)) });
  if (logTail !== undefined) sections.push({ kind: 'pre', title: 'Live log', note: 'last lines, updating', text: logTail || '(no output yet)' });
  return { title: captain.name, status: { label: humanize(captain.state), tone: statusTone(captain.state) }, subtitle: captain.id, actions, sections };
}

/** The merge queue entry page. */
export function mergePage(entry: MergeEntry, store: OpsStore, now: Date = new Date()): DetailPage {
  const actions: DetailAction[] = [];
  if (entry.status === 'Queued' || entry.status === 'Passed' || entry.status === 'Failed') actions.push({ label: 'Process Now', command: 'armada.merge.process', args: [entry.id], primary: true });
  if (entry.status === 'Queued' || entry.status === 'Testing' || entry.status === 'Failed') actions.push({ label: 'Remove from Queue', command: 'armada.merge.cancel', args: [entry.id] });
  if (entry.missionId) actions.push({ label: 'Open Mission', command: 'armada.mission.open', args: [entry.missionId] });
  if (entry.status === 'Landed' || entry.status === 'Failed' || entry.status === 'Cancelled') actions.push({ label: 'Delete Entry', command: 'armada.merge.delete', args: [entry.id] });
  const sections: DetailSection[] = [
    {
      kind: 'fields',
      rows: [
        { label: 'Vessel', value: store.vesselName(entry.vesselId) },
        { label: 'Branch', value: entry.branchName, code: true },
        { label: 'Target', value: entry.targetBranch, code: true },
        { label: 'Mission', value: entry.missionId ?? '', code: true, link: entry.missionId ? { command: 'armada.mission.open', args: [entry.missionId] } : undefined },
        { label: 'Test command', value: entry.testCommand ?? '', code: true },
        { label: 'Exit code', value: entry.testExitCode === null ? '' : String(entry.testExitCode) },
        { label: 'Queued', value: stamp(entry.createdUtc, now) },
        { label: 'Completed', value: stamp(entry.completedUtc, now) },
      ],
    },
  ];
  if (entry.testOutput) sections.push({ kind: 'pre', title: 'Test output', text: entry.testOutput });
  return { title: `${entry.branchName} → ${entry.targetBranch}`, status: { label: humanize(entry.status), tone: statusTone(entry.status) }, subtitle: entry.id, actions, sections };
}
