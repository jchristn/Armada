import type { AskBlackBoxCard, AskBlackBoxDigest, AskBlackBoxFollowUp } from '../types/models';

/**
 * Ask Armada black box helpers (MISSION_BLACKBOX_PLAN.md 10.3), shared by the dashboard and the mobile app: the
 * /blackbox command arguments, the card's status chips, and follow-up arguments (the canonical question the entry
 * points pre-fill is blackBoxAskPrompt in lib/blackbox). Host-agnostic (no DOM).
 */

/** `/blackbox <missionId> [question]` typed in the composer: the mission id and the optional question. */
export function parseBlackBoxCommand(args: string): { missionId: string; question: string } | null {
  const match = /^\s*(msn_[A-Za-z0-9_]+)\s*([\s\S]*)$/.exec(args ?? '');
  if (!match) return null;
  return { missionId: match[1], question: match[2].trim() };
}

/** One status chip on the card: an English label key, the value, and a tone for styling. */
export interface BlackBoxChip {
  key: string;
  label: string;
  value: string;
  tone: 'neutral' | 'warn' | 'bad' | 'good';
  /** English key for the chip's tooltip. */
  title: string;
}

function present(value: string | null | undefined): value is string {
  return !!value && value !== 'Unknown';
}

/** The card's status chips, in display order: triage (with confidence), kind, step, capture mode, resolution. */
export function blackBoxChips(digest: AskBlackBoxDigest | null | undefined): BlackBoxChip[] {
  if (!digest) return [];
  const chips: BlackBoxChip[] = [];
  if (present(digest.triage)) {
    const tone = digest.triage === 'ArmadaDefect' ? 'bad' : digest.triage === 'Transient' ? 'neutral' : 'warn';
    chips.push({ key: 'triage', label: 'Triage', value: digest.triageConfidence ? `${digest.triage} (${digest.triageConfidence})` : digest.triage, tone, title: 'Who or what the black box thinks caused the failure, and how sure it is.' });
  }
  if (present(digest.kind)) chips.push({ key: 'kind', label: 'Kind', value: digest.kind, tone: 'bad', title: 'The kind of failure Armada recorded.' });
  if (present(digest.step)) chips.push({ key: 'step', label: 'Step', value: present(digest.phase) ? `${digest.phase} / ${digest.step}` : digest.step, tone: 'neutral', title: 'The phase and step where the mission failed.' });
  if (present(digest.captureMode) && digest.captureMode !== 'Inline') {
    chips.push({ key: 'capture', label: 'Captured by', value: digest.captureMode, tone: 'warn', title: 'Recorded after the fact, so some facts (such as the workspace and the captain log) may be missing.' });
  }
  if (present(digest.resolution)) {
    const resolved = digest.resolution === 'Resolved' || digest.resolution === 'ArmadaFixed';
    chips.push({ key: 'resolution', label: 'Resolution', value: digest.resolution, tone: resolved ? 'good' : 'neutral', title: 'Whether someone has resolved this failure.' });
  }
  return chips;
}

/** A follow-up's tool arguments; an empty object when the text is missing or not a JSON object. */
export function followUpArguments(followUp: AskBlackBoxFollowUp): Record<string, unknown> {
  if (!followUp.argumentsText) return {};
  try {
    const parsed: unknown = JSON.parse(followUp.argumentsText);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

/** The follow-ups in the card's order, tools (proposals) first as the server sends them. */
export function cardFollowUps(card: AskBlackBoxCard | null | undefined): AskBlackBoxFollowUp[] {
  return (card?.followUps ?? []).filter((f) => !!f && !!f.name && (!!f.toolName || !!f.prompt));
}

/** Short form of a commit hash for display. */
export function shortCommit(hash: string | null | undefined): string {
  return hash ? hash.slice(0, 10) : '';
}
