import { useCallback, useEffect, useState } from 'react';
import { getSettings, updateSettings } from '../../../api/client';
import { useAuth } from '../../../context/AuthContext';
import { useLocale } from '../../../context/LocaleContext';
import { useNotifications } from '../../../context/NotificationContext';
import type { RepositoryHealthSettings, VesselHealthCriterion } from '../../../types/models';
import { mergeRepositoryHealth, REPOSITORY_HEALTH_FIELDS, REPOSITORY_HEALTH_THRESHOLD_FIELDS, validateRange, type NumericFieldDef } from '../../../lib/settingsRanges';
import { HEALTH_CRITERIA, criterionLabel, formatCount } from '../../../lib/health/healthText';
import LoadingIndicator from '../../shared/LoadingIndicator';
import { criterionHelp } from './criterionHelp';
import './vesselHealth.css';

export { REPOSITORY_HEALTH_FIELDS, REPOSITORY_HEALTH_THRESHOLD_FIELDS, validateRange } from '../../../lib/settingsRanges';

type Drafts = Record<string, string>;

function fieldTip(t: (text: string) => string, key: string): string {
  switch (key) {
    case 'intervalMinutes': return t('How often, in minutes, the Admiral re-grades every active vessel. Default 360 (6 hours). 0 turns the schedule off; manual Evaluate still works.');
    case 'maxConcurrency': return t('How many vessels one evaluation job grades at once. Higher finishes sooner but runs more git, dotnet and npm processes at the same time. Default 4.');
    case 'dependencyMaxAgeHours': return t('Package checks are skipped while manifests and lock files are unchanged, but re-run once results are this many hours old so new advisories show up. Default 24.');
    case 'dependencyCommandTimeoutSeconds': return t('Longest each dotnet or npm command may run before it is stopped and the check is graded Unknown. Default 120 seconds.');
    case 'staleBranchDays': return t('A local branch whose newest commit is older than this many days counts as stale for the Branches check. Default 90.');
    case 'missionWindowDays': return t('How many days back the Mission outcomes check counts Failed and LandingFailed missions. Default 7.');
    case 'thresholds.behindWarn': return t('Git divergence warns when the checkout is at least this many commits behind its default branch or upstream. Default 1.');
    case 'thresholds.behindFail': return t('Git divergence fails when the checkout is at least this many commits behind. Must be at least the warn value. Default 21.');
    case 'thresholds.staleBranchWarn': return t('The Branches check warns once at least this many stale branches exist. Default 4.');
    case 'thresholds.staleBranchFail': return t('The Branches check fails once at least this many stale branches exist. Must be at least the warn value. Default 11.');
    case 'thresholds.missionFailureWarn': return t('Mission outcomes warns once at least this many missions failed within the window. Default 1.');
    case 'thresholds.missionFailureFail': return t('Mission outcomes fails once at least this many missions failed within the window. Must be at least the warn value. Default 3.');
    default: return '';
  }
}

function toDrafts(s: RepositoryHealthSettings): Drafts {
  const d: Drafts = {};
  for (const f of REPOSITORY_HEALTH_FIELDS) d[f.key] = String(s[f.key]);
  for (const f of REPOSITORY_HEALTH_THRESHOLD_FIELDS) d[`thresholds.${f.key}`] = String(s.thresholds[f.key]);
  return d;
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
    const merged: RepositoryHealthSettings = mergeRepositoryHealth(source);
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
    const tip = `${fieldTip(t, key) || t(f.help)} ${t('Range {{min}} to {{max}}.', { min: formatCount(locale, f.min), max: formatCount(locale, f.max) })}`;
    return (
      <div className="form-group" key={key}>
        <label htmlFor={id} title={tip}>{t(f.label)}</label>
        <input
          id={id}
          title={tip}
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
      <h3 title={t('How Armada grades each vessel (git state, packages, tests, CI, failed missions) and how often it re-checks. Applies to all vessels in every tenant as soon as you save.')}>{t('Repository Health')}</h3>
      <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
        {t('Controls scheduled vessel health evaluation, dependency freshness, and the thresholds behind Warn and Fail. Changes apply immediately.')}
      </p>
      {loadError && (
        <div className="alert alert-error vh-inline-alert" role="alert">
          <span>{t('Could not load repository health settings: {{message}}', { message: loadError })}</span>
          <button type="button" className="btn btn-sm" onClick={() => void load()} title={t('Try loading the repository health settings again.')}>{t('Retry')}</button>
        </div>
      )}
      {!isAdmin && <p className="text-dim">{t('Only administrators can change these settings.')}</p>}
      {!settings && !loadError && <LoadingIndicator compact label={t('Loading...')} />}
      {settings && (
        <fieldset disabled={!editable} style={{ border: 'none', margin: 0, padding: 0 }}>
          <div className="settings-grid">
            {REPOSITORY_HEALTH_FIELDS.map((f) => numberInput(f, f.key))}
            <div className="form-group">
              <label className="settings-checkbox-label" title={t('Run git fetch before measuring divergence so behind counts reflect the remote. It never changes remote settings. If the fetch fails, divergence is Unknown. On by default.')}>
                <input
                  title={t('Run git fetch before measuring divergence so behind counts reflect the remote. It never changes remote settings. If the fetch fails, divergence is Unknown. On by default.')}
                  type="checkbox"
                  checked={settings.fetchBeforeEvaluate}
                  onChange={(e) => setSettings({ ...settings, fetchBeforeEvaluate: e.target.checked })}
                />
                <span>{t('Fetch before evaluating')}</span>
              </label>
            </div>
          </div>

          <h4 title={t('Checked criteria decide a vessel overall status (its worst one wins). Unchecked criteria are still evaluated and shown, but never change the overall status.')}>{t('Scored criteria')}</h4>
          <p className="text-dim" style={{ fontSize: '0.8rem', margin: '0 0 0.5rem' }}>{t('The overall status is the worst status among these criteria.')}</p>
          <div className="vh-criteria-grid" role="group" aria-label={t('Scored criteria')}>
            {HEALTH_CRITERIA.map((c) => (
              <label key={c} title={criterionHelp(t, c)}>
                <input title={criterionHelp(t, c)} type="checkbox" checked={settings.scoredCriteria.includes(c)} onChange={() => toggleCriterion(c)} />
                <span>{criterionLabel(t, c)}</span>
              </label>
            ))}
          </div>
          {settings.scoredCriteria.length === 0 && (
            <div className="alert alert-warning vh-inline-alert">{t('With no scored criteria every vessel is Unknown overall.')}</div>
          )}

          <h4 title={t('Counts at which a check moves from Pass to Warn and from Warn to Fail. Each fail value must be at least its warn value.')}>{t('Thresholds')}</h4>
          <div className="settings-grid">
            {REPOSITORY_HEALTH_THRESHOLD_FIELDS.map((f) => numberInput(f, `thresholds.${f.key}`))}
          </div>
          {crossErrors.map((e) => <div key={e} className="vh-field-error" role="alert">{e}</div>)}

          <button
            type="button"
            className="btn-primary btn-sm"
            disabled={saving || hasErrors}
            onClick={() => { void save(); }}
            title={hasErrors ? t('Fix the highlighted fields first.') : t('Save these settings. They take effect immediately for every vessel; existing grades update at the next evaluation.')}
          >
            {saving ? t('Saving...') : t('Save Repository Health Settings')}
          </button>
        </fieldset>
      )}
    </div>
  );
}
