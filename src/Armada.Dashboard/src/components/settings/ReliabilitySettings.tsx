import { useEffect, useState } from 'react';
import { updateSettings } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';

/** Outcome-data routing (RS-8.5, RS-8.6) as the settings API returns it. */
export interface RoutingDataSettingsData {
  rankByOutcomes?: boolean;
  suggestTiers?: boolean;
  minimumSamples?: number;
  halfLifeDays?: number;
}

/** The 1.5.0 reliability settings carried by GET and PUT /api/v1/settings. */
export interface ReliabilitySettingsData {
  timeoutScaleReferenceTokensPerSecond?: number;
  promptVariantCompactMaxContextTokens?: number;
  promptVariantLeanMaxContextTokens?: number;
  mergeQueueRequeueLimit?: number;
  mergeQueueBatchSize?: number;
  mergeQueueLeaseSeconds?: number;
  unclassifiedResumeLimit?: number;
  judgeReworkRounds?: number;
  maxEscalations?: number;
  escalateOnOpenEndpointBreaker?: boolean;
  routingData?: RoutingDataSettingsData | null;
}

export type ReliabilityNumberField =
  | 'timeoutScaleReferenceTokensPerSecond' | 'promptVariantCompactMaxContextTokens' | 'promptVariantLeanMaxContextTokens'
  | 'mergeQueueRequeueLimit' | 'mergeQueueBatchSize' | 'mergeQueueLeaseSeconds' | 'unclassifiedResumeLimit'
  | 'judgeReworkRounds' | 'maxEscalations' | 'minimumSamples' | 'halfLifeDays';

/** Server limits and defaults for each number field (the Admiral rejects or clamps values outside them). */
export const RELIABILITY_LIMITS: Record<ReliabilityNumberField, { min: number; max: number; def: number }> = {
  timeoutScaleReferenceTokensPerSecond: { min: 0, max: 1000, def: 30 },
  promptVariantCompactMaxContextTokens: { min: 0, max: 2097152, def: 32768 },
  promptVariantLeanMaxContextTokens: { min: 0, max: 2097152, def: 65536 },
  mergeQueueRequeueLimit: { min: 0, max: 10, def: 3 },
  mergeQueueBatchSize: { min: 1, max: 20, def: 1 },
  mergeQueueLeaseSeconds: { min: 5, max: 14400, def: 900 },
  unclassifiedResumeLimit: { min: 0, max: 3, def: 1 },
  judgeReworkRounds: { min: 1, max: 5, def: 2 },
  maxEscalations: { min: 0, max: 5, def: 2 },
  minimumSamples: { min: 5, max: 1000, def: 20 },
  halfLifeDays: { min: 1, max: 365, def: 14 },
};

export type ReliabilityDraft = Record<ReliabilityNumberField, string> & {
  escalateOnOpenEndpointBreaker: boolean;
  rankByOutcomes: boolean;
  suggestTiers: boolean;
};

export function toReliabilityDraft(s: ReliabilitySettingsData | null | undefined): ReliabilityDraft {
  const v = s ?? {};
  const r = v.routingData ?? {};
  const num = (field: ReliabilityNumberField, value: number | undefined) => String(value ?? RELIABILITY_LIMITS[field].def);
  return {
    timeoutScaleReferenceTokensPerSecond: num('timeoutScaleReferenceTokensPerSecond', v.timeoutScaleReferenceTokensPerSecond),
    promptVariantCompactMaxContextTokens: num('promptVariantCompactMaxContextTokens', v.promptVariantCompactMaxContextTokens),
    promptVariantLeanMaxContextTokens: num('promptVariantLeanMaxContextTokens', v.promptVariantLeanMaxContextTokens),
    mergeQueueRequeueLimit: num('mergeQueueRequeueLimit', v.mergeQueueRequeueLimit),
    mergeQueueBatchSize: num('mergeQueueBatchSize', v.mergeQueueBatchSize),
    mergeQueueLeaseSeconds: num('mergeQueueLeaseSeconds', v.mergeQueueLeaseSeconds),
    unclassifiedResumeLimit: num('unclassifiedResumeLimit', v.unclassifiedResumeLimit),
    judgeReworkRounds: num('judgeReworkRounds', v.judgeReworkRounds),
    maxEscalations: num('maxEscalations', v.maxEscalations),
    minimumSamples: num('minimumSamples', r.minimumSamples),
    halfLifeDays: num('halfLifeDays', r.halfLifeDays),
    escalateOnOpenEndpointBreaker: v.escalateOnOpenEndpointBreaker ?? false,
    rankByOutcomes: r.rankByOutcomes ?? true,
    suggestTiers: r.suggestTiers ?? false,
  };
}

/** Fields whose value is not a whole number within the server limits. */
export function validateReliabilityDraft(d: ReliabilityDraft): Partial<Record<ReliabilityNumberField, true>> {
  const errors: Partial<Record<ReliabilityNumberField, true>> = {};
  for (const field of Object.keys(RELIABILITY_LIMITS) as ReliabilityNumberField[]) {
    const text = d[field].trim();
    const n = Number(text);
    const { min, max } = RELIABILITY_LIMITS[field];
    if (text === '' || !Number.isInteger(n) || n < min || n > max) errors[field] = true;
  }
  return errors;
}

/** The PUT body for a valid draft. RoutingData is sent whole because the server replaces that object. */
export function reliabilityPayload(d: ReliabilityDraft): ReliabilitySettingsData {
  return {
    timeoutScaleReferenceTokensPerSecond: Number(d.timeoutScaleReferenceTokensPerSecond),
    promptVariantCompactMaxContextTokens: Number(d.promptVariantCompactMaxContextTokens),
    promptVariantLeanMaxContextTokens: Number(d.promptVariantLeanMaxContextTokens),
    mergeQueueRequeueLimit: Number(d.mergeQueueRequeueLimit),
    mergeQueueBatchSize: Number(d.mergeQueueBatchSize),
    mergeQueueLeaseSeconds: Number(d.mergeQueueLeaseSeconds),
    unclassifiedResumeLimit: Number(d.unclassifiedResumeLimit),
    judgeReworkRounds: Number(d.judgeReworkRounds),
    maxEscalations: Number(d.maxEscalations),
    escalateOnOpenEndpointBreaker: d.escalateOnOpenEndpointBreaker,
    routingData: {
      rankByOutcomes: d.rankByOutcomes,
      suggestTiers: d.suggestTiers,
      minimumSamples: Number(d.minimumSamples),
      halfLifeDays: Number(d.halfLifeDays),
    },
  };
}

interface ReliabilitySettingsProps {
  settings: ReliabilitySettingsData | null | undefined;
  /** Disable editing (remote proxy mode or insufficient permission). */
  locked: boolean;
  /** Called with the full settings payload the server returned after a save. */
  onSaved: (updated: Record<string, unknown>) => void;
  /** Toast helper from the host page. */
  notify: (severity: 'success' | 'error', message: string) => void;
}

/**
 * Settings page section for mixed open and frontier pipelines (1.5.0): timeout scaling for slow models, prompt variant
 * thresholds, recovery and escalation limits, merge queue requeue and batching, and routing from outcome data. Keeps a
 * local draft so auto-refresh never overwrites unsaved edits, validates against the server limits, and saves the
 * section, which applies live.
 */
export default function ReliabilitySettings({ settings, locked, onSaved, notify }: ReliabilitySettingsProps) {
  const { t } = useLocale();
  const [draft, setDraft] = useState<ReliabilityDraft>(() => toReliabilityDraft(settings));
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => { if (!dirty) setDraft(toReliabilityDraft(settings)); }, [settings, dirty]);

  const errors = validateReliabilityDraft(draft);
  const valid = Object.keys(errors).length === 0;

  async function save() {
    if (!valid) return;
    setSaving(true);
    try {
      const updated = await updateSettings(reliabilityPayload(draft) as Record<string, unknown>);
      setDirty(false);
      onSaved(updated as Record<string, unknown>);
      notify('success', t('Reliability settings saved and applied.'));
    } catch (e: unknown) {
      notify('error', t('Failed: {{message}}', { message: e instanceof Error ? e.message : t('Unknown error') }));
    } finally {
      setSaving(false);
    }
  }

  function set<K extends keyof ReliabilityDraft>(field: K, value: ReliabilityDraft[K]) {
    setDraft((d) => ({ ...d, [field]: value }));
    setDirty(true);
  }

  function numberInput(field: ReliabilityNumberField, label: string, help: string) {
    const id = `reliability-${field}`;
    const { min, max, def } = RELIABILITY_LIMITS[field];
    const limits = t('{{min}} to {{max}}, default {{def}}.', { min: min.toLocaleString(), max: max.toLocaleString(), def: def.toLocaleString() });
    const error = errors[field];
    return (
      <div className="form-group">
        <label htmlFor={id} title={`${help} ${limits}`}>{label}</label>
        <input id={id} title={`${help} ${limits}`} type="number" min={min} max={max} value={draft[field]} onChange={(e) => set(field, e.target.value)} aria-invalid={Boolean(error)} aria-describedby={`${id}-help`} />
        <span id={`${id}-help`} className={error ? 'field-error' : 'text-dim form-help'}>{error ? t('Must be a whole number from {{min}} to {{max}}.', { min: min.toLocaleString(), max: max.toLocaleString() }) : limits}</span>
      </div>
    );
  }

  function checkbox(field: 'escalateOnOpenEndpointBreaker' | 'rankByOutcomes' | 'suggestTiers', label: string, help: string) {
    return (
      <div className="form-group">
        <label className="settings-checkbox-label" title={help}>
          <input type="checkbox" checked={draft[field]} onChange={(e) => set(field, e.target.checked)} title={help} /> {label}
        </label>
      </div>
    );
  }

  return (
    <div className="settings-section" style={{ marginTop: '1.5rem' }} data-testid="reliability-settings">
      <h3>{t('Reliability and Routing')}</h3>
      <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
        {t('How Armada recovers, escalates and routes when pipelines mix open models with frontier models. Changes apply immediately.')}
      </p>
      <fieldset disabled={locked} className="settings-fieldset">
        <div className="settings-grid">
          {numberInput('timeoutScaleReferenceTokensPerSecond', t('Timeout scale reference (tokens/s)'), t('Models measured slower than this get longer timeouts, at most four times the global values. 0 turns scaling off.'))}
          {numberInput('promptVariantCompactMaxContextTokens', t('Compact prompt up to (tokens)'), t('Served context at or below which a mission gets the Compact prompt when nothing else chooses one. 0 turns it off.'))}
          {numberInput('promptVariantLeanMaxContextTokens', t('Lean prompt up to (tokens)'), t('Served context at or below which a mission gets the Lean prompt when Compact does not apply. 0 turns it off.'))}
          {numberInput('unclassifiedResumeLimit', t('Unclassified resumes'), t('In-place resumes after an Unclassified exit before the mission escalates.'))}
          {numberInput('judgeReworkRounds', t('Judge rework rounds'), t('Automatic Judge rework rounds for one slice before a different Worker captain is tried.'))}
          {numberInput('maxEscalations', t('Maximum escalations'), t('Escalation hops one mission may take. 0 turns automatic escalation off.'))}
          {numberInput('mergeQueueRequeueLimit', t('Merge queue requeue limit'), t('Times landing work is retried from a fresh fetch after a non-fast-forward push before it fails.'))}
          {numberInput('mergeQueueBatchSize', t('Merge queue batch size'), t('Queued entries of one vessel and branch merged, verified and pushed together. 1 turns batching off.'))}
          {numberInput('mergeQueueLeaseSeconds', t('Merge queue lease (seconds)'), t('Lifetime of the merge queue processing lease.'))}
          {numberInput('minimumSamples', t('Outcome samples needed'), t('Attempts a model needs for a persona before its results rank it or suggest a tier.'))}
          {numberInput('halfLifeDays', t('Outcome half-life (days)'), t('Days after which an attempt counts half as much as a new one.'))}
          {checkbox('escalateOnOpenEndpointBreaker', t('Escalate when the endpoint breaker is open'), t('A transient failure on a captain whose endpoint circuit breaker is open escalates right away instead of resuming.'))}
          {checkbox('rankByOutcomes', t('Rank captains by outcomes'), t('Within a tier, captains whose model has enough recorded results for the persona are tried most successful first.'))}
          {checkbox('suggestTiers', t('Suggest model profile tiers'), t('Raise a tier suggestion in Needs You from outcome data. A suggestion changes nothing until an administrator applies it.'))}
        </div>
        <div className="settings-actions">
          <button type="button" className="btn-primary btn-sm" onClick={() => void save()} disabled={!valid || saving || !dirty} title={t('Save this section to the Admiral. It applies immediately; other sections are saved separately.')}>
            {saving ? t('Saving...') : t('Save Reliability Settings')}
          </button>
          {dirty && <button type="button" className="btn btn-sm" onClick={() => { setDirty(false); setDraft(toReliabilityDraft(settings)); }} title={t('Throw away the unsaved edits in this section and show the saved values again.')}>{t('Discard changes')}</button>}
          {dirty && <span className="text-dim">{t('Unsaved changes')}</span>}
        </div>
      </fieldset>
    </div>
  );
}
