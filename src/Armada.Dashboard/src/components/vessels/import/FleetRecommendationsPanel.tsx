import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  apiErrorCode,
  applyFleetRecommendations,
  cancelJob,
  categorizeVesselImport,
} from '../../../api/client';
import type {
  FleetRecommendationApplyRequest,
  FleetRecommendationApplyResult,
  VesselImportBatch,
  VesselImportFleetRecommendation,
  VesselImportItem,
} from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import { useNotifications } from '../../../context/NotificationContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import ConfirmDialog from '../../shared/ConfirmDialog';
import { categorizationBadge, importErrorLabel } from '../../../lib/vesselImportLabels';

/** Name of the bucket the server fills with vessels the captain did not assign; it is never created as a fleet. */
export const UNCATEGORIZED_FLEET = 'Uncategorized';

/** One editable fleet card. */
export interface FleetDraft {
  key: string;
  name: string;
  description: string;
  rationale: string;
  vesselIds: string[];
  appliedFleetId: string | null;
}

let draftCounter = 0;

/** Turn stored recommendations into editable drafts. */
export function toDrafts(recommendations: VesselImportFleetRecommendation[]): FleetDraft[] {
  return recommendations.map((r) => ({
    key: r.id || `draft-${draftCounter++}`,
    name: r.name,
    description: r.description ?? '',
    rationale: r.rationale ?? '',
    vesselIds: [...r.vesselIds],
    appliedFleetId: r.appliedFleetId,
  }));
}

/** Move a vessel from whichever draft holds it to the target draft. */
export function moveVessel(drafts: FleetDraft[], vesselId: string, targetKey: string): FleetDraft[] {
  return drafts.map((d) => {
    const without = d.vesselIds.filter((id) => id !== vesselId);
    return d.key === targetKey ? { ...d, vesselIds: [...without, vesselId] } : { ...d, vesselIds: without };
  });
}

/** Validation errors per draft key (English source strings). */
export function validateDrafts(drafts: FleetDraft[]): Record<string, string> {
  const errors: Record<string, string> = {};
  const seen = new Map<string, string>();
  for (const d of drafts) {
    const name = d.name.trim();
    if (d.vesselIds.length === 0) continue;
    if (!name) { errors[d.key] = 'Give this fleet a name.'; continue; }
    if (name.length > 256) { errors[d.key] = 'Fleet names must be 256 characters or fewer.'; continue; }
    const lower = name.toLowerCase();
    if (seen.has(lower)) errors[d.key] = 'Another fleet already uses this name.';
    else seen.set(lower, d.key);
  }
  return errors;
}

/** Request body for the apply endpoint: fleets without vessels are dropped and names trimmed. */
export function buildApplyPayload(drafts: FleetDraft[]): FleetRecommendationApplyRequest {
  return {
    Fleets: drafts
      .filter((d) => d.vesselIds.length > 0)
      .map((d) => ({ Name: d.name.trim(), Description: d.description.trim() || null, VesselIds: [...d.vesselIds] })),
  };
}

interface FleetRecommendationsPanelProps {
  batch: VesselImportBatch;
  items: VesselImportItem[];
  recommendations: VesselImportFleetRecommendation[];
  /** Called after a retry, stop, or apply changed the batch, so the caller can refresh and resume polling. */
  onChanged: () => void;
}

/**
 * Fleet recommendations for an import batch. Shows the background run while the captain works (with a stop
 * button), the error and a retry when it failed, and editable fleet cards when it completed: rename, edit the
 * description, move vessels between fleets, add and remove fleets, then apply after a confirmation. Applied
 * recommendations are listed with links to their fleets and can be edited and applied again.
 */
export default function FleetRecommendationsPanel({ batch, items, recommendations, onChanged }: FleetRecommendationsPanelProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const status = batch.categorizationStatus ?? 'None';
  const [drafts, setDrafts] = useState<FleetDraft[]>(() => toDrafts(recommendations));
  const [editing, setEditing] = useState(status === 'Completed');
  const [confirmApply, setConfirmApply] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [showErrors, setShowErrors] = useState(false);
  const [applied, setApplied] = useState<FleetRecommendationApplyResult | null>(null);

  const recommendationKey = recommendations.map((r) => r.id + ':' + r.vesselIds.join(',') + ':' + (r.appliedFleetId ?? '')).join('|');
  useEffect(() => {
    setDrafts(toDrafts(recommendations));
    setEditing(status === 'Completed');
    // Reset the editor whenever the server-side recommendations change (new run, applied).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [recommendationKey, status]);

  const vesselNames = useMemo(() => {
    const map = new Map<string, string>();
    for (const item of items) {
      const id = item.vesselId ?? item.existingVesselId;
      if (id) map.set(id, item.proposedName);
    }
    return map;
  }, [items]);

  const errors = validateDrafts(drafts);
  const hasErrors = Object.keys(errors).length > 0;
  const assignedCount = drafts.filter((d) => d.name.trim().toLowerCase() !== UNCATEGORIZED_FLEET.toLowerCase()).reduce((n, d) => n + d.vesselIds.length, 0);
  const fleetCount = drafts.filter((d) => d.vesselIds.length > 0 && d.name.trim().toLowerCase() !== UNCATEGORIZED_FLEET.toLowerCase()).length;

  function update(key: string, patch: Partial<FleetDraft>) {
    setDrafts((ds) => ds.map((d) => (d.key === key ? { ...d, ...patch } : d)));
  }

  function addFleet() {
    setDrafts((ds) => [...ds, { key: `new-${draftCounter++}`, name: '', description: '', rationale: '', vesselIds: [], appliedFleetId: null }]);
  }

  async function retry() {
    setBusy(true);
    setError('');
    try {
      await categorizeVesselImport(batch.id);
      onChanged();
    } catch (err: unknown) {
      setError(importErrorLabel(t, apiErrorCode(err), err instanceof Error ? err.message : t('Could not start fleet categorization.')));
    } finally {
      setBusy(false);
    }
  }

  async function stop() {
    if (!batch.categorizationJobId) return;
    setBusy(true);
    setError('');
    try {
      await cancelJob(batch.categorizationJobId);
      onChanged();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Could not stop the captain.'));
    } finally {
      setBusy(false);
    }
  }

  function requestApply() {
    setShowErrors(true);
    if (hasErrors || fleetCount === 0) return;
    setConfirmApply(true);
  }

  async function apply() {
    setConfirmApply(false);
    setBusy(true);
    setError('');
    try {
      const result = await applyFleetRecommendations(batch.id, buildApplyPayload(drafts));
      setApplied(result);
      setEditing(false);
      pushToast('success', t('{count, plural, one {Fleets applied: # vessel assigned.} other {Fleets applied: # vessels assigned.}}', { count: result.assignments.length }));
      onChanged();
    } catch (err: unknown) {
      setError(importErrorLabel(t, apiErrorCode(err), err instanceof Error ? err.message : t('Applying the fleets failed.')));
    } finally {
      setBusy(false);
    }
  }

  if (status === 'None') return null;

  return (
    <section className="fleet-recs" aria-labelledby={`fleet-recs-title-${batch.id}`}>
      <div className="fleet-recs-head">
        <h4 id={`fleet-recs-title-${batch.id}`}>{t('Fleet recommendations')}</h4>
        <CodeStatusBadge {...categorizationBadge(t, status)} />
      </div>

      {error && <div className="alert alert-error" role="alert">{error}</div>}

      {(status === 'Pending' || status === 'Running') && (
        <div className="alert alert-info fleet-recs-running" role="status" aria-live="polite">
          <span className="spinner-inline" aria-hidden="true" />
          <div>
            <p>
              {status === 'Pending'
                ? t('Fleet recommendations will start when the import finishes.')
                : t('A captain is reading the imported repositories and recommending fleets. This runs in the background and can take several minutes.')}
            </p>
            <p className="text-dim">
              {t('You can close this dialog; the header shows the task while it runs and you are notified when it finishes. Reopen this batch from the import history to review the fleets.')}
            </p>
            <div className="fleet-recs-actions">
              {batch.categorizationJobId && <Link to="/jobs" className="btn btn-sm">{t('Open the Jobs page')}</Link>}
              {batch.categorizationJobId && status === 'Running' && (
                <button type="button" className="btn btn-sm" onClick={() => void stop()} disabled={busy}>{t('Stop the captain')}</button>
              )}
            </div>
          </div>
        </div>
      )}

      {status === 'Failed' && (
        <div className="alert alert-error" role="alert">
          <p><strong>{t('The captain could not recommend fleets.')}</strong></p>
          {batch.categorizationError && <p className="mono fleet-recs-error" data-i18n-skip="true">{batch.categorizationError}</p>}
          <button type="button" className="btn btn-sm" onClick={() => void retry()} disabled={busy}>
            {busy ? t('Starting...') : t('Retry categorization')}
          </button>
        </div>
      )}

      {(status === 'Completed' || status === 'Applied') && (
        <>
          {status === 'Applied' && !editing && (
            <div className="alert alert-success" role="status">
              <p>
                {applied
                  ? t('{count, plural, one {Fleets applied: # vessel assigned.} other {Fleets applied: # vessels assigned.}}', { count: applied.assignments.length })
                  : t('These fleets were applied. Vessels in Uncategorized kept their fleet.')}
              </p>
              <button type="button" className="btn btn-sm" onClick={() => setEditing(true)}>{t('Edit and apply again')}</button>
            </div>
          )}
          {editing && (
            <p className="text-dim">
              {t('Review the captain\'s fleets. Rename them, move repositories between fleets, add or remove fleets, then apply. Existing fleets with the same name are reused.')}
            </p>
          )}

          <div className="fleet-recs-grid">
            {drafts.map((d) => {
              const isUncategorized = d.name.trim().toLowerCase() === UNCATEGORIZED_FLEET.toLowerCase();
              const fieldError = showErrors ? errors[d.key] : undefined;
              return (
                <article key={d.key} className={`card fleet-rec-card${isUncategorized ? ' fleet-rec-uncategorized' : ''}`} aria-label={d.name || t('Unnamed fleet')}>
                  {editing ? (
                    <>
                      <label className="form-field">
                        <span className="form-label">{t('Fleet name')}</span>
                        <input type="text" value={d.name} onChange={(e) => update(d.key, { name: e.target.value })} aria-invalid={!!fieldError} />
                        {fieldError && <span className="field-error" role="alert">{t(fieldError)}</span>}
                      </label>
                      <label className="form-field">
                        <span className="form-label">{t('Description')}</span>
                        <textarea rows={2} value={d.description} onChange={(e) => update(d.key, { description: e.target.value })} />
                      </label>
                    </>
                  ) : (
                    <>
                      <h5 className="fleet-rec-name">
                        {d.appliedFleetId ? <Link to={`/fleets/${d.appliedFleetId}`}>{d.name}</Link> : d.name}
                      </h5>
                      {d.description && <p className="text-dim" data-i18n-skip="true">{d.description}</p>}
                    </>
                  )}
                  {isUncategorized && <p className="text-dim form-help">{t('Repositories in Uncategorized are left without a fleet when you apply.')}</p>}
                  {d.rationale && (
                    <details className="fleet-rec-rationale">
                      <summary>{t('Why the captain grouped these')}</summary>
                      <p data-i18n-skip="true">{d.rationale}</p>
                    </details>
                  )}
                  <ul className="fleet-rec-vessels" aria-label={t('Repositories in this fleet')}>
                    {d.vesselIds.map((vesselId) => (
                      <li key={vesselId}>
                        <span className="fleet-rec-vessel-name" data-i18n-skip="true">{vesselNames.get(vesselId) ?? vesselId}</span>
                        {editing && drafts.length > 1 && (
                          <select
                            value={d.key}
                            onChange={(e) => setDrafts((ds) => moveVessel(ds, vesselId, e.target.value))}
                            aria-label={t('Move {{name}} to another fleet', { name: vesselNames.get(vesselId) ?? vesselId })}
                          >
                            {drafts.map((target) => (
                              <option key={target.key} value={target.key}>{target.name.trim() || t('Unnamed fleet')}</option>
                            ))}
                          </select>
                        )}
                      </li>
                    ))}
                    {d.vesselIds.length === 0 && <li className="text-dim">{t('No repositories. Move some here or remove this fleet.')}</li>}
                  </ul>
                  {editing && (
                    <button
                      type="button"
                      className="btn btn-sm"
                      onClick={() => setDrafts((ds) => ds.filter((x) => x.key !== d.key))}
                      disabled={d.vesselIds.length > 0}
                      title={d.vesselIds.length > 0 ? t('Move its repositories to other fleets first') : undefined}
                    >
                      {t('Remove fleet')}
                    </button>
                  )}
                </article>
              );
            })}
          </div>

          {editing && (
            <div className="fleet-recs-footer">
              <button type="button" className="btn btn-sm" onClick={addFleet}>{t('+ Add fleet')}</button>
              <span className="text-dim">
                {t('{count, plural, one {# fleet} other {# fleets}}', { count: fleetCount })}
                {' - '}
                {t('{count, plural, one {# repository assigned} other {# repositories assigned}}', { count: assignedCount })}
              </span>
              {showErrors && fleetCount === 0 && <span className="field-error" role="alert">{t('Assign at least one repository to a named fleet.')}</span>}
              <button type="button" className="btn btn-primary" onClick={requestApply} disabled={busy}>
                {busy ? t('Applying...') : t('Apply fleets')}
              </button>
            </div>
          )}
        </>
      )}

      <ConfirmDialog
        open={confirmApply}
        title={t('Apply these fleets?')}
        message={t('{count, plural, one {# repository will be assigned to its recommended fleet. Missing fleets are created; fleets with the same name are reused.} other {# repositories will be assigned to their recommended fleets. Missing fleets are created; fleets with the same name are reused.}}', { count: assignedCount })}
        confirmLabel={t('Apply fleets')}
        cancelLabel={t('Cancel')}
        onConfirm={() => void apply()}
        onCancel={() => setConfirmApply(false)}
      />
    </section>
  );
}
