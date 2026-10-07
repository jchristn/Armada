import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import type { Fleet, Pipeline, VesselImportCandidateStatus, VesselImportHint, VesselImportItem } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import Pagination from '../../shared/Pagination';
import {
  CANDIDATE_STATUSES,
  CANDIDATE_STATUS_META,
  IMPORTABLE_STATUSES,
  candidateStatusBadge,
  hintLabel,
} from '../../../lib/vesselImportLabels';

export interface ImportDefaults {
  fleetId: string;
  pipelineId: string;
  landingMode: string;
}

interface ImportReviewStepProps {
  candidates: VesselImportItem[];
  truncated: boolean;
  hints: VesselImportHint[];
  selected: string[];
  onSelectedChange: (paths: string[]) => void;
  fleets: Fleet[];
  pipelines: Pipeline[];
  defaults: ImportDefaults;
  onDefaultsChange: (defaults: ImportDefaults) => void;
}

const LANDING_MODES: Array<{ value: string; label: string }> = [
  { value: '', label: 'Default (use global setting)' },
  { value: 'LocalMerge', label: 'Local Merge' },
  { value: 'MergeAndPush', label: 'Merge and Push' },
  { value: 'PullRequest', label: 'Pull Request' },
  { value: 'MergeQueue', label: 'Merge Queue' },
  { value: 'None', label: 'None' },
];

/** Review step: candidate table with status badges and filter, selection, fleet picker and defaults. */
export default function ImportReviewStep({
  candidates,
  truncated,
  hints,
  selected,
  onSelectedChange,
  fleets,
  pipelines,
  defaults,
  onDefaultsChange,
}: ImportReviewStepProps) {
  const { t } = useLocale();
  const [statusFilter, setStatusFilter] = useState<VesselImportCandidateStatus | ''>('');
  const [search, setSearch] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(50);

  const counts = useMemo(() => {
    const c: Partial<Record<VesselImportCandidateStatus, number>> = {};
    for (const item of candidates) c[item.candidateStatus] = (c[item.candidateStatus] ?? 0) + 1;
    return c;
  }, [candidates]);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return candidates.filter((c) => (!statusFilter || c.candidateStatus === statusFilter)
      && (!term || c.path.toLowerCase().includes(term) || c.proposedName.toLowerCase().includes(term)));
  }, [candidates, statusFilter, search]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const page = Math.min(pageNumber, totalPages);
  const visible = filtered.slice((page - 1) * pageSize, page * pageSize);
  const selectable = (c: VesselImportItem) => IMPORTABLE_STATUSES.includes(c.candidateStatus);
  const filteredSelectable = filtered.filter(selectable);
  const allFilteredSelected = filteredSelectable.length > 0 && filteredSelectable.every((c) => selected.includes(c.path));

  function toggle(path: string) {
    onSelectedChange(selected.includes(path) ? selected.filter((p) => p !== path) : [...selected, path]);
  }

  function toggleAllFiltered() {
    if (allFilteredSelected) onSelectedChange(selected.filter((p) => !filteredSelectable.some((c) => c.path === p)));
    else onSelectedChange(Array.from(new Set([...selected, ...filteredSelectable.map((c) => c.path)])));
  }

  function selectAllNew() {
    onSelectedChange(candidates.filter((c) => c.candidateStatus === 'New').map((c) => c.path));
  }

  return (
    <div className="import-review">
      {(hints.length > 0 || truncated) && (
        <div className="alert alert-info import-hints" role="note">
          {hints.map((h) => <p key={h.code}>{hintLabel(t, h.code, h.message)}</p>)}
          {truncated && !hints.some((h) => h.code === 'CandidateLimitReached') && <p>{hintLabel(t, 'CandidateLimitReached', '')}</p>}
        </div>
      )}

      <div className="import-status-chips" role="group" aria-label={t('Filter candidates by status')}>
        <button type="button" className={`chip${statusFilter === '' ? ' active' : ''}`} aria-pressed={statusFilter === ''} onClick={() => { setStatusFilter(''); setPageNumber(1); }}>
          {t('All')} <span className="chip-count">{candidates.length.toLocaleString()}</span>
        </button>
        {CANDIDATE_STATUSES.filter((s) => (counts[s] ?? 0) > 0).map((s) => (
          <button key={s} type="button" className={`chip${statusFilter === s ? ' active' : ''}`} aria-pressed={statusFilter === s} title={t(CANDIDATE_STATUS_META[s].description)} onClick={() => { setStatusFilter(s); setPageNumber(1); }}>
            {t(CANDIDATE_STATUS_META[s].label)} <span className="chip-count">{(counts[s] ?? 0).toLocaleString()}</span>
          </button>
        ))}
      </div>

      <div className="toolbar-row">
        <input type="search" value={search} onChange={(e) => { setSearch(e.target.value); setPageNumber(1); }} placeholder={t('Path or name contains...')} aria-label={t('Path or name contains...')} />
        <button type="button" className="btn btn-sm" onClick={selectAllNew}>{t('Select all new')}</button>
        <button type="button" className="btn btn-sm" onClick={() => onSelectedChange([])} disabled={selected.length === 0}>{t('Clear selection')}</button>
        <span className="text-dim toolbar-note">{t('{count, plural, one {# selected} other {# selected}}', { count: selected.length })}</span>
      </div>

      <Pagination
        pageNumber={page}
        pageSize={pageSize}
        totalPages={totalPages}
        totalRecords={filtered.length}
        onPageChange={setPageNumber}
        onPageSizeChange={(s) => { setPageSize(s); setPageNumber(1); }}
      />
      <div className="table-wrap import-table-wrap">
        <table>
          <thead>
            <tr>
              <th className="col-checkbox" scope="col">
                <input type="checkbox" checked={allFilteredSelected} disabled={filteredSelectable.length === 0} onChange={toggleAllFiltered} aria-label={t('Select all importable candidates in this view')} title={t('Select all importable candidates in this view')} />
              </th>
              <th scope="col">{t('Name')}</th>
              <th scope="col">{t('Status')}</th>
              <th scope="col">{t('Path')}</th>
              <th scope="col">{t('Remote')}</th>
              <th scope="col">{t('Branch')}</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((c) => {
              const badge = candidateStatusBadge(t, c.candidateStatus);
              const canSelect = selectable(c);
              return (
                <tr key={c.id || c.path} className={canSelect ? 'clickable' : 'row-muted'} onClick={() => { if (canSelect) toggle(c.path); }}>
                  <td className="col-checkbox" onClick={(e) => e.stopPropagation()}>
                    <input
                      type="checkbox"
                      checked={selected.includes(c.path)}
                      disabled={!canSelect}
                      onChange={() => toggle(c.path)}
                      aria-label={t('Select {{name}}', { name: c.proposedName })}
                    />
                  </td>
                  <td><strong data-i18n-skip="true">{c.proposedName}</strong></td>
                  <td>
                    <CodeStatusBadge {...badge} />
                    {c.candidateStatus === 'AlreadyOnboarded' && c.existingVesselId && (
                      <div className="cell-subline">
                        <Link to={`/vessels/${c.existingVesselId}`} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()}>{t('Open existing vessel')}</Link>
                      </div>
                    )}
                  </td>
                  <td className="mono" title={c.path} data-i18n-skip="true"><span className="cell-clip"><span>{c.path}</span></span></td>
                  <td className="mono text-dim" title={c.remoteUrl ?? ''} data-i18n-skip="true"><span className="cell-clip"><span>{c.remoteUrl || t('(no origin)')}</span></span></td>
                  <td className="mono text-dim" title={c.defaultBranch || ''} data-i18n-skip="true"><span className="cell-clip"><span>{c.defaultBranch || '-'}</span></span></td>
                </tr>
              );
            })}
            {visible.length === 0 && <tr><td colSpan={6} className="text-dim">{t('No candidates match the current filters.')}</td></tr>}
          </tbody>
        </table>
      </div>

      <fieldset className="form-fieldset import-defaults">
        <legend className="form-label">{t('Defaults for the new vessels')}</legend>
        <div className="form-grid-3">
          <label className="form-field">
            <span className="form-label">{t('Fleet')}</span>
            <select value={defaults.fleetId} onChange={(e) => onDefaultsChange({ ...defaults, fleetId: e.target.value })}>
              <option value="">{t('No fleet')}</option>
              {fleets.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
          </label>
          <label className="form-field">
            <span className="form-label">{t('Default Pipeline')}</span>
            <select value={defaults.pipelineId} onChange={(e) => onDefaultsChange({ ...defaults, pipelineId: e.target.value })}>
              <option value="">{t('None (WorkerOnly)')}</option>
              {pipelines.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </label>
          <label className="form-field">
            <span className="form-label">{t('Landing Mode')}</span>
            <select value={defaults.landingMode} onChange={(e) => onDefaultsChange({ ...defaults, landingMode: e.target.value })}>
              {LANDING_MODES.map((m) => <option key={m.value || 'default'} value={m.value}>{t(m.label)}</option>)}
            </select>
          </label>
        </div>
        <p className="text-dim form-help">{t('Imported vessels use the repository origin as the remote and the discovered folder as the working directory. The checkout is never deleted when a vessel is removed.')}</p>
      </fieldset>
    </div>
  );
}
