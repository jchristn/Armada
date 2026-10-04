import { useCallback, useEffect, useState } from 'react';
import { getSettings, updateSettings } from '../../../api/client';
import { useAuth } from '../../../context/AuthContext';
import { useLocale } from '../../../context/LocaleContext';
import { useNotifications } from '../../../context/NotificationContext';
import type { RepositoryHealthSettings, RepositoryHealthThresholds, VesselHealthCriterion } from '../../../types/models';
import { HEALTH_CRITERIA, criterionLabel, formatCount, msg } from '../../../lib/health/healthText';
import './vesselHealth.css';

type NumericSettingKey = 'intervalMinutes' | 'maxConcurrency' | 'dependencyMaxAgeHours' | 'dependencyCommandTimeoutSeconds' | 'staleBranchDays' | 'missionWindowDays';
type ThresholdKey = keyof RepositoryHealthThresholds;

interface NumericFieldDef<K extends string> {
  key: K;
  label: string;
  help: string;
  min: number;
  max: number;
}

/** Ranges mirror RepositoryHealthSettings.cs and RepositoryHealthThresholds.cs (the server clamps too). */
export const REPOSITORY_HEALTH_FIELDS: NumericFieldDef<NumericSettingKey>[] = [
  { key: 'intervalMinutes', label: msg('Evaluation interval (minutes)'), help: msg('Minutes between scheduled evaluations; 0 turns the schedule off.'), min: 0, max: 10080 },
  { key: 'maxConcurrency', label: msg('Max concurrency'), help: msg('Vessels evaluated at the same time within one job.'), min: 1, max: 32 },
  { key: 'dependencyMaxAgeHours', label: msg('Dependency result max age (hours)'), help: msg('Refresh dependency results after this long even when manifests are unchanged.'), min: 1, max: 720 },
  { key: 'dependencyCommandTimeoutSeconds', label: msg('Dependency command timeout (seconds)'), help: msg('Timeout for each dotnet or npm call; a timeout grades Unknown.'), min: 10, max: 900 },
  { key: 'staleBranchDays', label: msg('Stale branch age (days)'), help: msg('A branch whose last commit is older than this counts as stale.'), min: 1, max: 3650 },
  { key: 'missionWindowDays', label: msg('Mission failure window (days)'), help: msg('Window for counting failed and landing-failed missions.'), min: 1, max: 90 },
];

export const REPOSITORY_HEALTH_THRESHOLD_FIELDS: NumericFieldDef<ThresholdKey>[] = [
  { key: 'behindWarn', label: msg('Behind: warn at'), help: msg('Commits behind the default branch that warn.'), min: 1, max: 100000 },
  { key: 'behindFail', label: msg('Behind: fail at'), help: msg('Commits behind the default branch that fail.'), min: 1, max: 100000 },
  { key: 'staleBranchWarn', label: msg('Stale branches: warn at'), help: msg('Stale branches that warn.'), min: 1, max: 10000 },
  { key: 'staleBranchFail', label: msg('Stale branches: fail at'), help: msg('Stale branches that fail.'), min: 1, max: 10000 },
  { key: 'missionFailureWarn', label: msg('Failed missions: warn at'), help: msg('Recent failed missions that warn.'), min: 1, max: 1000 },
  { key: 'missionFailureFail', label: msg('Failed missions: fail at'), help: msg('Recent failed missions that fail.'), min: 1, max: 1000 },
];

const DEFAULTS: RepositoryHealthSettings = {
  intervalMinutes: 360,
  maxConcurrency: 4,
  fetchBeforeEvaluate: true,
  dependencyMaxAgeHours: 24,
  dependencyCommandTimeoutSeconds: 120,
  staleBranchDays: 90,
  missionWindowDays: 7,
  scoredCriteria: ['GitDivergence', 'WorkingTree', 'Branches', 'Dependencies', 'Vulnerabilities', 'TestInfrastructure', 'ArmadaReadiness', 'MissionOutcomes'],
  thresholds: { behindWarn: 1, behindFail: 21, staleBranchWarn: 4, staleBranchFail: 11, missionFailureWarn: 1, missionFailureFail: 3 },
};

type Drafts = Record<string, string>;

function toDrafts(s: RepositoryHealthSettings): Drafts {
  const d: Drafts = {};
  for (const f of REPOSITORY_HEALTH_FIELDS) d[f.key] = String(s[f.key]);
  for (const f of REPOSITORY_HEALTH_THRESHOLD_FIELDS) d[`thresholds.${f.key}`] = String(s.thresholds[f.key]);
  return d;
}

/** Returns an English error key for a numeric draft, or null when valid. */
export function validateRange(raw: string, min: number, max: number): string | null {
  const value = raw.trim();
  if (!/^-?\d+$/.test(value)) return msg('Enter a whole number.');
  const n = parseInt(value, 10);
  if (n < min || n > max) return msg('Must be between {{min}} and {{max}}.');
  return null;
}

/**
 * Settings page section for `RepositoryHealth`: schedule, concurrency, dependency freshness, branch and
 * mission windows, scored criteria and thresholds. Loads and saves on its own (PUT replaces the whole
 * RepositoryHealth object), validates the backend ranges per field, and is editable only by admins.
 */
export default function RepositoryHealthSettingsSection({ locked = false }: { locked?: boolean }) {
  const { t, locale } = useLocale();
  const { isAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const [settings, setSettings] = useState<RepositoryHealthSettings | null>(null);
  const [drafts, setDrafts] = useState<Drafts>({});
  const [loadError, setLoadError] = useState('');
  const [saving, setSaving] = useState(false);

  const apply = useCallback((source: Partial<RepositoryHealthSettings> | null | undefined) => {
    const merged: RepositoryHealthSettings = {
      ...DEFAULTS,
      ...(source ?? {}),
      thresholds: { ...DEFAULTS.thresholds, ...(source?.thresholds ?? {}) },
      scoredCriteria: Array.isArray(source?.scoredCriteria) ? source!.scoredCriteria : DEFAULTS.scoredCriteria,
    };
    setSettings(merged);
    setDrafts(toDrafts(merged));
  }, []);

  const load = useCallback(async () => {
    try {
      const data = await getSettings();
      apply((data as { repositoryHealth?: Partial<RepositoryHealthSettings> }).repositoryHealth);
      setLoadError('');
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : String(err));
    }
  }, [apply]);

  useEffect(() => { void load(); }, [load]);

  const errors: Record<string, string | null> = {};
  for (const f of REPOSITORY_HEALTH_FIELDS) errors[f.key] = validateRange(drafts[f.key] ?? '', f.min, f.max);
  for (const f of REPOSITORY_HEALTH_THRESHOLD_FIELDS) errors[`thresholds.${f.key}`] = validateRange(drafts[`thresholds.${f.key}`] ?? '', f.min, f.max);
  const num = (key: string) => parseInt(drafts[key] ?? '', 10);
  const crossErrors: string[] = [];
  if (!errors['thresholds.behindWarn'] && !errors['thresholds.behindFail'] && num('thresholds.behindFail') < num('thresholds.behindWarn')) crossErrors.push(t('Behind: fail at must be at least the warn value.'));
  if (!errors['thresholds.staleBranchWarn'] && !errors['thresholds.staleBranchFail'] && num('thresholds.staleBranchFail') < num('thresholds.staleBranchWarn')) crossErrors.push(t('Stale branches: fail at must be at least the warn value.'));
  if (!errors['thresholds.missionFailureWarn'] && !errors['thresholds.missionFailureFail'] && num('thresholds.missionFailureFail') < num('thresholds.missionFailureWarn')) crossErrors.push(t('Failed missions: fail at must be at least the warn value.'));
  const hasErrors = Object.values(errors).some((e) => e !== null) || crossErrors.length > 0;
  const editable = isAdmin && !locked;

  function errorText(key: string, f: { min: number; max: number }): string | null {
    const e = errors[key];
    if (!e) return null;
    return t(e, { min: formatCount(locale, f.min), max: formatCount(locale, f.max) });
  }

  function toggleCriterion(c: VesselHealthCriterion) {
    if (!settings) return;
    const next = settings.scoredCriteria.includes(c) ? settings.scoredCriteria.filter((x) => x !== c) : [...settings.scoredCriteria, c];
    setSettings({ ...settings, scoredCriteria: HEALTH_CRITERIA.filter((x) => next.includes(x)) });
  }

  async function save() {
    if (!settings || hasErrors) return;
    const payload: RepositoryHealthSettings = {
      ...settings,
      intervalMinutes: num('intervalMinutes'),
      maxConcurrency: num('maxConcurrency'),
      dependencyMaxAgeHours: num('dependencyMaxAgeHours'),
      dependencyCommandTimeoutSeconds: num('dependencyCommandTimeoutSeconds'),
      staleBranchDays: num('staleBranchDays'),
      missionWindowDays: num('missionWindowDays'),
      thresholds: {
        behindWarn: num('thresholds.behindWarn'),
        behindFail: num('thresholds.behindFail'),
        staleBranchWarn: num('thresholds.staleBranchWarn'),
        staleBranchFail: num('thresholds.staleBranchFail'),
        missionFailureWarn: num('thresholds.missionFailureWarn'),
        missionFailureFail: num('thresholds.missionFailureFail'),
      },
    };
    setSaving(true);
    try {
      const updated = await updateSettings({ repositoryHealth: payload });
      apply((updated as { repositoryHealth?: Partial<RepositoryHealthSettings> }).repositoryHealth ?? payload);
      pushToast('success', t('Repository health settings saved. They apply immediately.'));
    } catch (err) {
      pushToast('error', t('Failed: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setSaving(false);
    }
  }

  function numberInput<K extends string>(f: NumericFieldDef<K>, key: string) {
    const err = errorText(key, f);
    const id = `rh-${key.replace('.', '-')}`;
    return (
      <div className="form-group" key={key}>
        <label htmlFor={id} title={t(f.help)}>{t(f.label)}</label>
        <input
          id={id}
          type="number"
          min={f.min}
          max={f.max}
          step={1}
          className={err ? 'vh-invalid' : undefined}
          aria-invalid={err ? true : undefined}
          aria-describedby={`${id}-help`}
          value={drafts[key] ?? ''}
          onChange={(e) => setDrafts({ ...drafts, [key]: e.target.value })}
        />
        <small id={`${id}-help`}>
          {t(f.help)} {t('Range {{min}} to {{max}}.', { min: formatCount(locale, f.min), max: formatCount(locale, f.max) })}
        </small>
        {err && <span className="vh-field-error" role="alert">{err}</span>}
      </div>
    );
  }

  return (
    <div className="settings-section vh-settings" style={{ marginTop: '1.5rem' }}>
      <h3 title={t('How Armada grades vessel health and how often it evaluates.')}>{t('Repository Health')}</h3>
      <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
        {t('Controls scheduled vessel health evaluation, dependency freshness, and the thresholds behind Warn and Fail. Changes apply immediately.')}
      </p>
      {loadError && (
        <div className="alert alert-error vh-inline-alert" role="alert">
          <span>{t('Could not load repository health settings: {{message}}', { message: loadError })}</span>
          <button type="button" className="btn btn-sm" onClick={() => void load()}>{t('Retry')}</button>
        </div>
      )}
      {!isAdmin && <p className="text-dim">{t('Only administrators can change these settings.')}</p>}
      {settings && (
        <fieldset disabled={!editable} style={{ border: 'none', margin: 0, padding: 0 }}>
          <div className="settings-grid">
            {REPOSITORY_HEALTH_FIELDS.map((f) => numberInput(f, f.key))}
            <div className="form-group">
              <label className="settings-checkbox-label" title={t('Run git fetch before measuring divergence. If the fetch fails, divergence is Unknown.')}>
                <input
                  type="checkbox"
                  checked={settings.fetchBeforeEvaluate}
                  onChange={(e) => setSettings({ ...settings, fetchBeforeEvaluate: e.target.checked })}
                />
                <span>{t('Fetch before evaluating')}</span>
              </label>
            </div>
          </div>

          <h4>{t('Scored criteria')}</h4>
          <p className="text-dim" style={{ fontSize: '0.8rem', margin: '0 0 0.5rem' }}>{t('The overall status is the worst status among these criteria.')}</p>
          <div className="vh-criteria-grid" role="group" aria-label={t('Scored criteria')}>
            {HEALTH_CRITERIA.map((c) => (
              <label key={c}>
                <input type="checkbox" checked={settings.scoredCriteria.includes(c)} onChange={() => toggleCriterion(c)} />
                <span>{criterionLabel(t, c)}</span>
              </label>
            ))}
          </div>
          {settings.scoredCriteria.length === 0 && (
            <div className="alert alert-warning vh-inline-alert">{t('With no scored criteria every vessel is Unknown overall.')}</div>
          )}

          <h4>{t('Thresholds')}</h4>
          <div className="settings-grid">
            {REPOSITORY_HEALTH_THRESHOLD_FIELDS.map((f) => numberInput(f, `thresholds.${f.key}`))}
          </div>
          {crossErrors.map((e) => <div key={e} className="vh-field-error" role="alert">{e}</div>)}

          <button
            type="button"
            className="btn-primary btn-sm"
            disabled={saving || hasErrors}
            onClick={() => { void save(); }}
            title={hasErrors ? t('Fix the highlighted fields first.') : t('Save repository health settings')}
          >
            {saving ? t('Saving...') : t('Save Repository Health Settings')}
          </button>
        </fieldset>
      )}
    </div>
  );
}
