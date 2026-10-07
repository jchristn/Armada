import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { deleteVesselHealthOverride, getVesselHealth, setVesselHealthOverride } from '../../../api/client';
import { useLocale } from '../../../context/LocaleContext';
import { useNotifications } from '../../../context/NotificationContext';
import type {
  VesselHealthCriterion,
  VesselHealthDetail,
  VesselHealthOverride,
  VesselHealthStatus,
} from '../../../types/models';
import {
  HEALTH_CRITERIA,
  HEALTH_STATUSES,
  OVERRIDE_CRITERIA,
  criterionLabel,
  describeFinding,
  driftLabel,
  formatCount,
  msg,
  severityLabel,
  statusLabel,
} from '../../../lib/health/healthText';
import { useFocusTrap } from '../../../lib/useFocusTrap';
import CopyButton, { copyToClipboard } from '../../shared/CopyButton';
import DataTable from '../../shared/DataTable';
import ConfirmDialog from '../../shared/ConfirmDialog';
import LoadingIndicator from '../../shared/LoadingIndicator';
import HealthStatusBadge from './HealthStatusBadge';
import './vesselHealth.css';

export type HealthDetailSection = 'summary' | 'findings' | 'dependencies' | 'overrides' | 'json';

interface VesselHealthDetailModalProps {
  vesselId: string;
  /** Name shown while the detail loads. */
  vesselName?: string | null;
  /** Default branch used as the divergence base in tooltips. */
  defaultBranch?: string | null;
  onClose: () => void;
  /** Section to open first ("overrides" focuses the add-override form). */
  initialSection?: HealthDetailSection;
  /** Pre-selects a criterion in the add-override form. */
  initialOverrideCriterion?: VesselHealthCriterion;
  /** TenantAdmin: enables Re-evaluate and override editing (the server enforces this too). */
  canAdmin: boolean;
  /** Starts a forced single-vessel evaluation; omitted hides the button. */
  onReevaluate?: (vesselId: string) => void;
  /** True while an evaluation job is running. */
  evaluationRunning?: boolean;
  /** Increment to make the modal reload (for example when an evaluation finishes). */
  refreshToken?: number;
  /** Called after an override change so the caller can refresh its table. */
  onChanged?: () => void;
  /** Opens branch management for this vessel. */
  onOpenBranches?: (vesselId: string, vesselName: string) => void;
}

const SECTIONS: Array<{ key: HealthDetailSection; label: string }> = [
  { key: 'summary', label: msg('Summary') },
  { key: 'findings', label: msg('Findings') },
  { key: 'dependencies', label: msg('Dependencies') },
  { key: 'overrides', label: msg('Overrides') },
  { key: 'json', label: msg('Raw JSON') },
];

/** Latest test check run status names (CheckRun statuses the row may carry). */
const LAST_RUN_LABELS: Record<string, string> = {
  Passed: msg('Passed'),
  Failed: msg('Failed'),
  Canceled: msg('Canceled'),
  Cancelled: msg('Cancelled'),
};

/**
 * Vessel health inspector: summary numbers, every criterion's finding as a localized sentence, outdated
 * and vulnerable dependencies, manual overrides (add/remove for tenant admins), and the raw JSON.
 */
export default function VesselHealthDetailModal({
  vesselId,
  vesselName,
  defaultBranch,
  onClose,
  initialSection = 'summary',
  initialOverrideCriterion,
  canAdmin,
  onReevaluate,
  evaluationRunning = false,
  refreshToken = 0,
  onChanged,
  onOpenBranches,
}: VesselHealthDetailModalProps) {
  const { t, locale, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const containerRef = useRef<HTMLDivElement>(null);
  const [detail, setDetail] = useState<VesselHealthDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [section, setSection] = useState<HealthDetailSection>(initialSection);
  const [confirmRemove, setConfirmRemove] = useState<VesselHealthOverride | null>(null);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<{ criterion: VesselHealthCriterion; status: VesselHealthStatus; note: string }>({
    criterion: initialOverrideCriterion ?? 'Overall',
    status: 'Pass',
    note: '',
  });
  const [formError, setFormError] = useState('');

  useFocusTrap(containerRef, true, onClose, confirmRemove === null);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const result = await getVesselHealth(vesselId);
      setDetail({
        health: result.health,
        findings: result.findings ?? [],
        dependencies: result.dependencies ?? [],
        overrides: result.overrides ?? [],
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [vesselId]);

  useEffect(() => {
    void load();
  }, [load, refreshToken]);

  const overrideByCriterion = useMemo(() => {
    const map = new Map<string, VesselHealthOverride>();
    for (const o of detail?.overrides ?? []) map.set(o.criterion, o);
    return map;
  }, [detail]);

  const findingsByCriterion = useMemo(() => {
    const order = new Map(HEALTH_CRITERIA.map((c, i) => [c, i]));
    return [...(detail?.findings ?? [])].sort((a, b) => (order.get(a.criterion) ?? 99) - (order.get(b.criterion) ?? 99));
  }, [detail]);

  const health = detail?.health;
  const name = health?.vesselName || vesselName || vesselId;
  const evaluated = !!health?.id;
  const base = defaultBranch || t('the default branch');

  function startOverride(criterion: VesselHealthCriterion) {
    const existing = overrideByCriterion.get(criterion);
    setForm({ criterion, status: existing?.status ?? 'Pass', note: existing?.note ?? '' });
    setFormError('');
    setSection('overrides');
  }

  async function submitOverride(event: FormEvent) {
    event.preventDefault();
    if (!HEALTH_STATUSES.includes(form.status)) {
      setFormError(t('Choose a status.'));
      return;
    }
    if (form.note.length > 4000) {
      setFormError(t('The note can be at most {{count}} characters.', { count: formatCount(locale, 4000) }));
      return;
    }
    setSaving(true);
    setFormError('');
    try {
      const updated = await setVesselHealthOverride(vesselId, form.criterion, form.status, form.note.trim() || null);
      setDetail({ health: updated.health, findings: updated.findings ?? [], dependencies: updated.dependencies ?? [], overrides: updated.overrides ?? [] });
      pushToast('success', t('Override saved for {{criterion}}.', { criterion: criterionLabel(t, form.criterion) }));
      setForm((f) => ({ ...f, note: '' }));
      onChanged?.();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  }

  async function removeOverride(o: VesselHealthOverride) {
    setConfirmRemove(null);
    try {
      const updated = await deleteVesselHealthOverride(vesselId, o.criterion);
      setDetail({ health: updated.health, findings: updated.findings ?? [], dependencies: updated.dependencies ?? [], overrides: updated.overrides ?? [] });
      pushToast('success', t('Override removed for {{criterion}}.', { criterion: criterionLabel(t, o.criterion) }));
      onChanged?.();
    } catch (err) {
      pushToast('error', t('Could not remove the override: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    }
  }

  const json = useMemo(() => (detail ? JSON.stringify(detail, null, 2) : ''), [detail]);

  function field(label: string, value: string | number | null | undefined, opts?: { mono?: boolean; title?: string }) {
    const shown = value === null || value === undefined || value === '' ? '-' : String(value);
    return (
      <div className="record-detail-field">
        <span className="record-detail-label">{label}</span>
        <span className={`record-detail-value${opts?.mono ? ' mono' : ''}`} title={opts?.title}>{shown}</span>
      </div>
    );
  }

  function yesNo(value: boolean | null | undefined): string {
    if (value === null || value === undefined) return '-';
    return value ? t('Yes') : t('No');
  }

  function aheadBehind(ahead: number | null | undefined, behind: number | null | undefined): string {
    if ((ahead === null || ahead === undefined) && (behind === null || behind === undefined)) return '-';
    return t('{{ahead}} ahead, {{behind}} behind', { ahead: formatCount(locale, ahead ?? 0), behind: formatCount(locale, behind ?? 0) });
  }

  const overallOverride = overrideByCriterion.get('Overall');

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        ref={containerRef}
        className="modal vh-detail-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="vh-detail-title"
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="vh-detail-header">
          <div className="vh-detail-heading">
            <div className="vh-detail-title-row">
              <h3 id="vh-detail-title">{name}</h3>
              {health && <HealthStatusBadge status={health.overallStatus} overridden={!!overallOverride} overrideNote={overallOverride?.note ?? undefined} />}
            </div>
            <div className="vh-detail-sub">
              <span className="id-display">
                <span className="mono id-value" title={vesselId}>{vesselId}</span>
                <CopyButton text={vesselId} title={t('Copy vessel ID')} />
              </span>
              <span className="text-dim" title={health?.evaluatedUtc ? formatDateTime(health.evaluatedUtc) : undefined}>
                {health?.evaluatedUtc
                  ? t('Evaluated {{when}}', { when: formatRelativeTime(health.evaluatedUtc) })
                  : t('Never evaluated')}
              </span>
            </div>
          </div>
          <div className="vh-detail-actions">
            <Link className="btn btn-sm" to={`/vessels/${encodeURIComponent(vesselId)}`} onClick={onClose}>{t('Open vessel')}</Link>
            {onReevaluate && canAdmin && (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                disabled={evaluationRunning}
                title={evaluationRunning ? t('An evaluation is already running.') : t('Run every check for this vessel now, including dependency checks.')}
                onClick={() => onReevaluate(vesselId)}
              >
                {evaluationRunning ? t('Evaluating...') : t('Re-evaluate')}
              </button>
            )}
            <button type="button" className="btn btn-sm" onClick={onClose} aria-label={t('Close')} title={t('Close')}>&times;</button>
          </div>
        </div>

        <div className="vh-detail-tabs" role="tablist" aria-label={t('Health detail sections')}>
          {SECTIONS.map((s) => (
            <button
              key={s.key}
              type="button"
              role="tab"
              aria-selected={section === s.key}
              className={`vh-detail-tab${section === s.key ? ' active' : ''}`}
              onClick={() => setSection(s.key)}
            >
              {t(s.label)}
              {s.key === 'dependencies' && detail && detail.dependencies.length > 0 && <span className="vh-count">{formatCount(locale, detail.dependencies.length)}</span>}
              {s.key === 'overrides' && detail && detail.overrides.length > 0 && <span className="vh-count">{formatCount(locale, detail.overrides.length)}</span>}
            </button>
          ))}
        </div>

        <div className="vh-detail-body">
          {loading && !detail && <LoadingIndicator label={t('Loading health...')} />}
          {error && (
            <div className="alert alert-error vh-inline-alert" role="alert">
              <span>{t('Could not load vessel health: {{message}}', { message: error })}</span>
              <button type="button" className="btn btn-sm" onClick={() => void load()}>{t('Retry')}</button>
            </div>
          )}

          {detail && health && section === 'summary' && (
            <div>
              {!evaluated && (
                <div className="vh-empty-inline">
                  <p>{t('This vessel has not been evaluated yet.')}</p>
                  {onReevaluate && canAdmin && (
                    <button type="button" className="btn btn-primary btn-sm" disabled={evaluationRunning} onClick={() => onReevaluate(vesselId)}>{t('Evaluate now')}</button>
                  )}
                </div>
              )}
              {health.errorCode && (
                <div className="alert alert-warning vh-inline-alert">{describeFinding({ detailCode: health.errorCode }, t, locale)}</div>
              )}
              <div className="record-detail-grid vh-summary-grid">
                {field(t('Fleet'), health.fleetName ?? health.fleetId)}
                {field(t('Current branch'), health.currentBranch, { mono: true })}
                {field(t('Versus {{base}}', { base }), aheadBehind(health.aheadOfDefault, health.behindDefault))}
                {field(t('Versus upstream'), aheadBehind(health.aheadOfUpstream, health.behindUpstream))}
                {field(t('Dirty'), health.isDirty === null || health.isDirty === undefined ? '-' : (health.isDirty ? t('Yes') : t('No')))}
                {field(t('Untracked files'), formatCount(locale, health.untrackedCount))}
                {field(t('Branches'), health.branchCount === null || health.branchCount === undefined ? '-' : t('{{count}} ({{stale}} stale, {{armada}} armada/*)', {
                  count: formatCount(locale, health.branchCount),
                  stale: formatCount(locale, health.staleBranchCount ?? 0),
                  armada: formatCount(locale, health.armadaBranchCount ?? 0),
                }))}
                {field(t('Primary language'), health.primaryLanguage)}
                {field(t('Projects'), formatCount(locale, health.projectCount))}
                {field(t('Outdated packages'), health.outdatedCount === null || health.outdatedCount === undefined ? '-' : t('{{count}} ({{major}} major)', {
                  count: formatCount(locale, health.outdatedCount),
                  major: formatCount(locale, health.outdatedMajorCount ?? 0),
                }))}
                {field(t('Vulnerable packages'), health.vulnerableCount === null || health.vulnerableCount === undefined ? '-' : t('{{count}} (max {{severity}})', {
                  count: formatCount(locale, health.vulnerableCount),
                  severity: severityLabel(t, health.maxVulnerabilitySeverity),
                }))}
                {field(t('Last test run'), health.lastCheckRunStatus ? t(LAST_RUN_LABELS[health.lastCheckRunStatus] ?? health.lastCheckRunStatus) : '-')}
                {field(t('CI configuration'), yesNo(health.hasCiConfig))}
                {field(t('License'), yesNo(health.hasLicense))}
                {field(t('Readme'), yesNo(health.hasReadme))}
                {field(t('Readiness errors'), formatCount(locale, health.readinessErrorCount))}
                {field(t('Recent failed missions'), formatCount(locale, health.recentMissionFailureCount))}
                {field(t('Last commit'), health.lastCommitUtc ? formatRelativeTime(health.lastCommitUtc) : '-', { title: health.lastCommitUtc ? formatDateTime(health.lastCommitUtc) : undefined })}
                {field(t('Evaluated'), health.evaluatedUtc ? formatDateTime(health.evaluatedUtc) : t('Never'))}
                {field(t('Evaluation time'), health.evaluationDurationMs === null || health.evaluationDurationMs === undefined ? '-' : t('{{count}} ms', { count: formatCount(locale, health.evaluationDurationMs) }))}
                {field(t('Dependencies checked'), health.dependenciesEvaluatedUtc ? formatDateTime(health.dependenciesEvaluatedUtc) : '-')}
                {field(t('Evaluated path'), health.evaluatedPath, { mono: true, title: health.evaluatedPath ?? undefined })}
              </div>
              <div className="vh-section-actions">
                {onOpenBranches && (
                  <button type="button" className="btn btn-sm" onClick={() => onOpenBranches(vesselId, name)}>{t('Manage branches')}</button>
                )}
                <button type="button" className="btn btn-sm" onClick={() => setSection('findings')}>{t('View findings')}</button>
              </div>
            </div>
          )}

          {detail && section === 'findings' && (
            detail.findings.length === 0 ? (
              <p className="text-dim">{t('No findings yet. Findings appear after the first evaluation.')}</p>
            ) : (
              <DataTable
                tableKey="vessel-health-findings"
                className="vh-findings-table"
                rows={findingsByCriterion}
                rowKey={(finding) => finding.criterion}
                recordCount={null}
                columns={[
                  { key: 'criterion', label: t('Criterion'), required: true, cellClassName: 'vh-nowrap', render: (finding) => criterionLabel(t, finding.criterion) },
                  {
                    key: 'status', label: t('Status'), cellClassName: 'vh-nowrap',
                    render: (finding) => {
                      const o = overrideByCriterion.get(finding.criterion);
                      return (
                        <>
                          <HealthStatusBadge status={finding.status} />
                          {o && (
                            <span className="vh-override-chip">
                              <span className="text-dim">{t('Overridden to')}</span>{' '}
                              <HealthStatusBadge status={o.status} overridden overrideNote={o.note ?? undefined} />
                            </span>
                          )}
                        </>
                      );
                    },
                  },
                  {
                    // The override note used to be a second line under the details; it is in the tooltip (and the
                    // override chip and the Overrides tab show it too).
                    key: 'details', label: t('Details'),
                    cellTitle: (finding) => {
                      const note = overrideByCriterion.get(finding.criterion)?.note;
                      return note ? t('Note: {{note}}', { note }) : undefined;
                    },
                    render: (finding) => describeFinding(finding, t, locale),
                  },
                  ...(canAdmin ? [{
                    key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
                    render: (finding: VesselHealthDetail['findings'][number]) => (
                      <button type="button" className="btn btn-sm" onClick={() => startOverride(finding.criterion)}>
                        {overrideByCriterion.get(finding.criterion) ? t('Edit override') : t('Override...')}
                      </button>
                    ),
                  }] : []),
                ]}
              />
            )
          )}

          {detail && section === 'dependencies' && (
            detail.dependencies.length === 0 ? (
              <p className="text-dim">{t('No outdated or vulnerable dependencies were found.')}</p>
            ) : (
              <DataTable
                tableKey="vessel-health-dependencies"
                className="vh-deps-table"
                rows={detail.dependencies}
                rowKey={(dep) => dep.id ?? `${dep.projectPath}-${dep.packageName}-${dep.currentVersion}`}
                columns={[
                  { key: 'ecosystem', label: t('Ecosystem'), cellClassName: 'vh-nowrap', render: (dep) => dep.ecosystem },
                  {
                    key: 'project', label: t('Project'), cellClassName: 'mono',
                    render: (dep) => <span className="vh-path cell-one-line" title={dep.projectPath ?? undefined}>{dep.projectPath || '-'}</span>,
                  },
                  { key: 'package', label: t('Package'), required: true, cellClassName: 'mono vh-nowrap', render: (dep) => dep.packageName },
                  {
                    key: 'version', label: t('Version'), cellClassName: 'mono vh-nowrap',
                    render: (dep) => (
                      <>
                        {dep.currentVersion || '-'}
                        {dep.latestVersion ? <> {'\u2192'} {dep.latestVersion}</> : null}
                      </>
                    ),
                  },
                  {
                    key: 'drift', label: t('Drift'), cellClassName: 'vh-nowrap',
                    render: (dep) => (dep.drift && dep.drift !== 'None'
                      ? <span className={`tag vh-drift vh-drift-${dep.drift.toLowerCase()}`}>{driftLabel(t, dep.drift)}</span>
                      : <span className="text-dim">-</span>),
                  },
                  {
                    key: 'vulnerability', label: t('Vulnerability'), cellClassName: 'vh-nowrap',
                    render: (dep) => (dep.isVulnerable
                      ? <span className={`tag vh-sev vh-sev-${(dep.severity ?? 'None').toLowerCase()}`}>{severityLabel(t, dep.severity)}</span>
                      : <span className="text-dim">-</span>),
                  },
                  {
                    key: 'advisory', label: t('Advisory'), cellClassName: 'vh-nowrap',
                    render: (dep) => (dep.advisoryUrl
                      ? <a href={dep.advisoryUrl} target="_blank" rel="noopener noreferrer">{t('Advisory')}</a>
                      : <span className="text-dim">-</span>),
                  },
                ]}
              />
            )
          )}

          {detail && section === 'overrides' && (
            <div>
              {detail.overrides.length === 0 ? (
                <p className="text-dim">{t('No overrides. An override sets a manual status for one criterion or for Overall.')}</p>
              ) : (
                <ul className="vh-override-list">
                  {detail.overrides.map((o) => (
                    <li key={o.criterion} className="vh-override-item">
                      <div className="vh-override-main">
                        <strong>{criterionLabel(t, o.criterion)}</strong>
                        <HealthStatusBadge status={o.status} overridden overrideNote={o.note ?? undefined} />
                        <span className="text-dim" title={o.lastUpdateUtc ? formatDateTime(o.lastUpdateUtc) : undefined}>
                          {o.lastUpdateUtc ? formatRelativeTime(o.lastUpdateUtc) : ''}
                        </span>
                      </div>
                      {o.note && <div className="vh-note">{o.note}</div>}
                      {canAdmin && (
                        <div className="vh-override-actions">
                          <button type="button" className="btn btn-sm" onClick={() => startOverride(o.criterion)}>{t('Edit')}</button>
                          <button type="button" className="btn btn-sm btn-danger" onClick={() => setConfirmRemove(o)}>{t('Remove')}</button>
                        </div>
                      )}
                    </li>
                  ))}
                </ul>
              )}

              {canAdmin ? (
                <form className="vh-override-form" onSubmit={submitOverride} noValidate>
                  <h4>{overrideByCriterion.has(form.criterion) ? t('Edit override') : t('Add override')}</h4>
                  {formError && <div className="alert alert-error vh-inline-alert" role="alert">{formError}</div>}
                  <div className="vh-form-row">
                    <label>
                      {t('Criterion')}
                      <select value={form.criterion} onChange={(e) => setForm({ ...form, criterion: e.target.value as VesselHealthCriterion })}>
                        {OVERRIDE_CRITERIA.map((c) => (
                          <option key={c} value={c}>{criterionLabel(t, c)}</option>
                        ))}
                      </select>
                    </label>
                    <label>
                      {t('Status')}
                      <select value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as VesselHealthStatus })}>
                        {HEALTH_STATUSES.map((s) => (
                          <option key={s} value={s}>{statusLabel(t, s)}</option>
                        ))}
                      </select>
                    </label>
                  </div>
                  <label>
                    {t('Note')}
                    <textarea
                      value={form.note}
                      maxLength={4000}
                      rows={3}
                      placeholder={t('Why this status is set by hand (optional)')}
                      onChange={(e) => setForm({ ...form, note: e.target.value })}
                    />
                  </label>
                  <div className="modal-actions">
                    <button type="submit" className="btn btn-primary btn-sm" disabled={saving}>{saving ? t('Saving...') : t('Save override')}</button>
                  </div>
                </form>
              ) : (
                <p className="text-dim">{t('Only tenant administrators can change overrides.')}</p>
              )}
            </div>
          )}

          {detail && section === 'json' && (
            <div className="vh-json">
              <button type="button" className="btn btn-sm vh-json-copy" onClick={() => { void copyToClipboard(json).then(() => pushToast('success', t('Copied!'))).catch(() => undefined); }}>
                {t('Copy')}
              </button>
              <pre data-i18n-skip="true">{json}</pre>
            </div>
          )}
        </div>

        <ConfirmDialog
          open={confirmRemove !== null}
          title={msg('Remove override')}
          message={confirmRemove ? t('Remove the {{criterion}} override? The evaluated status applies again.', { criterion: criterionLabel(t, confirmRemove.criterion) }) : ''}
          confirmLabel={msg('Remove')}
          danger
          onConfirm={() => { if (confirmRemove) void removeOverride(confirmRemove); }}
          onCancel={() => setConfirmRemove(null)}
        />
      </div>
    </div>
  );
}
