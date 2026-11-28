import type {
  CaptainTier, ModelProfile, ModelProfileIdentity, ModelProfileRecovery, ModelProfileTimeouts, ModelProfileUpdateRequest, PromptVariant,
} from '../types/models';

type Translate = (key: string, vars?: Record<string, string | number>) => string;

/** Bounds of the recovery overrides, from ModelProfileRecovery (the server rejects values outside them). */
export const RECOVERY_FIELDS: ReadonlyArray<{ key: keyof ModelProfileRecovery; label: string; tip: string; min: number; max: number }> = [
  { key: 'maxResumes', label: 'Resumes', tip: 'In-place resumes after a transient or task failure, 0 to 10. Blank uses the global setting.', min: 0, max: 10 },
  { key: 'unclassifiedResumes', label: 'Unclassified resumes', tip: 'In-place resumes after an Unclassified exit, 0 to 3, never more than Resumes. Blank uses the global setting.', min: 0, max: 3 },
  { key: 'transientGraceMinutes', label: 'Outage grace (minutes)', tip: 'Minutes a transient endpoint outage is ridden out past the resumes, 0 to 120. Blank uses the global setting.', min: 0, max: 120 },
  { key: 'judgeReworkRounds', label: 'Judge rework rounds', tip: 'Automatic Judge rework rounds for a Worker on this model, 1 to 5. It can raise the global value but never lower it.', min: 1, max: 5 },
  { key: 'outputRepairAttempts', label: 'Output repair turns', tip: 'Output-contract repair turns per mission, 0 to 3. Blank uses the global setting.', min: 0, max: 3 },
  { key: 'escalateAfterResumes', label: 'Escalate after resumes', tip: 'Resumes after which a task failure moves to the next captain on the persona escalation ladder, 0 to 10, never more than Resumes. Blank escalates once the resumes are spent.', min: 0, max: 10 },
];

/** Bounds of the timeout overrides in minutes, from ModelProfileTimeouts. */
export const TIMEOUT_FIELDS: ReadonlyArray<{ key: keyof ModelProfileTimeouts; label: string; tip: string; min: number; max: number }> = [
  { key: 'stallThresholdMinutes', label: 'Stall threshold (minutes)', tip: 'Minutes without output before a captain counts as stalled, 1 to 240. Blank uses the measured-speed default or the global setting.', min: 1, max: 240 },
  { key: 'firstOutputMinutes', label: 'First output (minutes)', tip: 'Minutes a launched mission may wait for its first output, 1 to 240, so a slow model reading a long prompt is not stopped early.', min: 1, max: 240 },
  { key: 'toolWaitHardLimitMinutes', label: 'Tool wait limit (minutes)', tip: 'Minutes a quiet runtime tool may run before the stall check applies anyway, 1 to 1440.', min: 1, max: 1440 },
  { key: 'maxRuntimeMinutes', label: 'Maximum runtime (minutes)', tip: 'Minutes a mission may run before it fails as a runaway, 1 to 2880. Ignored while the global maximum is 0 (no cap).', min: 1, max: 2880 },
];

export const TIERS: CaptainTier[] = ['Economy', 'Standard', 'Premium'];
export const PROMPT_VARIANTS: PromptVariant[] = ['Current', 'Lean', 'Compact'];

/** Served context as "32,768 tokens (Discovered)", or a dash when unknown. */
export function servedContextLabel(identity: ModelProfileIdentity, t: Translate): string {
  if (identity.servedContextTokens == null) return '-';
  const tokens = t('{{count}} tokens', { count: identity.servedContextTokens.toLocaleString() });
  return identity.servedContextSource ? `${tokens} (${t(identity.servedContextSource)})` : tokens;
}

/** A capability as the administrator set it, else the probe result, else unknown. */
export function capabilityLabel(configured: boolean | null | undefined, probed: boolean | null | undefined, t: Translate): string {
  if (configured != null) return `${configured ? t('Yes') : t('No')} (${t('Set')})`;
  if (probed != null) return `${probed ? t('Yes') : t('No')} (${t('Probed')})`;
  return t('Unknown');
}

export type TriState = '' | 'true' | 'false';

/** The editable state of a profile, as the edit form holds it. */
export interface ModelProfileForm {
  tier: CaptainTier | '';
  toolsAvailable: TriState;
  mcpAvailable: TriState;
  template: PromptVariant | '';
  repeatContractAtEnd: TriState;
  maxFilesPerMission: string;
  recovery: Record<keyof ModelProfileRecovery, string>;
  timeouts: Record<keyof ModelProfileTimeouts, string>;
  notes: string;
}

function triState(value: boolean | null | undefined): TriState {
  return value == null ? '' : value ? 'true' : 'false';
}

function fromTriState(value: TriState): boolean | null {
  return value === '' ? null : value === 'true';
}

function num(value: number | null | undefined): string {
  return value == null ? '' : String(value);
}

function fromNum(value: string): number | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : Number(trimmed);
}

export function profileToForm(profile: ModelProfile): ModelProfileForm {
  const r = profile.recovery;
  const to = profile.timeouts;
  return {
    tier: profile.tiering.tier ?? '',
    toolsAvailable: triState(profile.capabilities.toolsAvailable),
    mcpAvailable: triState(profile.capabilities.mcpAvailable),
    template: (profile.rendering.template as PromptVariant | null) ?? '',
    repeatContractAtEnd: triState(profile.rendering.repeatContractAtEnd),
    maxFilesPerMission: num(profile.rendering.maxFilesPerMission),
    recovery: {
      maxResumes: num(r.maxResumes), unclassifiedResumes: num(r.unclassifiedResumes), transientGraceMinutes: num(r.transientGraceMinutes),
      judgeReworkRounds: num(r.judgeReworkRounds), outputRepairAttempts: num(r.outputRepairAttempts), escalateAfterResumes: num(r.escalateAfterResumes),
    },
    timeouts: {
      stallThresholdMinutes: num(to.stallThresholdMinutes), toolWaitHardLimitMinutes: num(to.toolWaitHardLimitMinutes),
      maxRuntimeMinutes: num(to.maxRuntimeMinutes), firstOutputMinutes: num(to.firstOutputMinutes),
    },
    notes: profile.notes ?? '',
  };
}

/** The PUT body for a form: every section is sent whole, so a cleared field removes its override. */
export function formToUpdate(form: ModelProfileForm): ModelProfileUpdateRequest {
  return {
    tiering: { tier: form.tier === '' ? null : form.tier },
    capabilities: { toolsAvailable: fromTriState(form.toolsAvailable), mcpAvailable: fromTriState(form.mcpAvailable) },
    rendering: { template: form.template === '' ? null : form.template, repeatContractAtEnd: fromTriState(form.repeatContractAtEnd), maxFilesPerMission: fromNum(form.maxFilesPerMission) },
    recovery: {
      maxResumes: fromNum(form.recovery.maxResumes), unclassifiedResumes: fromNum(form.recovery.unclassifiedResumes),
      transientGraceMinutes: fromNum(form.recovery.transientGraceMinutes), judgeReworkRounds: fromNum(form.recovery.judgeReworkRounds),
      outputRepairAttempts: fromNum(form.recovery.outputRepairAttempts), escalateAfterResumes: fromNum(form.recovery.escalateAfterResumes),
    },
    timeouts: {
      stallThresholdMinutes: fromNum(form.timeouts.stallThresholdMinutes), toolWaitHardLimitMinutes: fromNum(form.timeouts.toolWaitHardLimitMinutes),
      maxRuntimeMinutes: fromNum(form.timeouts.maxRuntimeMinutes), firstOutputMinutes: fromNum(form.timeouts.firstOutputMinutes),
    },
    notes: form.notes,
  };
}

/** The first rule the form breaks (bounds and the "never more than Resumes" rules), or null when it is valid. */
export function validateProfileForm(form: ModelProfileForm, t: Translate): string | null {
  const checks: Array<{ value: string; label: string; min: number; max: number }> = [
    ...RECOVERY_FIELDS.map(f => ({ value: form.recovery[f.key], label: f.label, min: f.min, max: f.max })),
    ...TIMEOUT_FIELDS.map(f => ({ value: form.timeouts[f.key], label: f.label, min: f.min, max: f.max })),
  ];
  for (const check of checks) {
    const value = fromNum(check.value);
    if (value != null && (!Number.isInteger(value) || value < check.min || value > check.max)) {
      return t('{{field}} must be a whole number from {{min}} to {{max}}.', { field: t(check.label), min: check.min, max: check.max });
    }
  }
  const files = fromNum(form.maxFilesPerMission);
  if (files != null && (!Number.isInteger(files) || files < 1)) return t('Maximum files per mission must be a whole number of 1 or more.');
  const resumes = fromNum(form.recovery.maxResumes);
  if (resumes != null) {
    const unclassified = fromNum(form.recovery.unclassifiedResumes);
    const escalate = fromNum(form.recovery.escalateAfterResumes);
    if ((unclassified != null && unclassified > resumes) || (escalate != null && escalate > resumes)) {
      return t('Unclassified resumes and Escalate after resumes cannot be more than Resumes.');
    }
  }
  if (form.notes.length > 4000) return t('Notes must be at most 4000 characters.');
  return null;
}
