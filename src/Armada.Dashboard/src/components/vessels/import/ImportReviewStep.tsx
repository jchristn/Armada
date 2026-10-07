import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import type { Fleet, Pipeline, VesselImportCandidateStatus, VesselImportHint, VesselImportItem } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import DataTable from '../../shared/DataTable';
import {
  CANDIDATE_STATUSES,
  CANDIDATE_STATUS_META,
  IMPORTABLE_STATUSES,
  candidateStatusBadge,
  hintLabel,
} from '../../../lib/vesselImportLabels';
import { IMPORT_LANDING_MODES } from '../../../lib/vesselImport';

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

      <DataTable
        tableKey="vessel-import-review"
        wrapClassName="import-table-wrap"
        rows={visible}
        rowKey={(c) => c.id || c.path}
        onRowClick={(c) => { if (selectable(c)) toggle(c.path); }}
        isRowClickable={selectable}
        rowClassName={(c) => (selectable(c) ? undefined : 'row-muted')}
        pagination={{
          pageNumber: page,
          pageSize,
          totalPages,
          totalRecords: filtered.length,
          onPageChange: setPageNumber,
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        emptyMessage={t('No candidates match the current filters.')}
        selection={{
          isSelected: (c) => selected.includes(c.path),
          isSelectable: selectable,
          onToggle: (c) => toggle(c.path),
          allSelected: allFilteredSelected,
          onToggleAll: () => toggleAllFiltered(),
          selectAllLabel: t('Select all importable candidates in this view'),
          rowLabel: (c) => t('Select {{name}}', { name: c.proposedName }),
        }}
        columns={[
          { key: 'name', label: t('Name'), required: true, render: (c) => <strong data-i18n-skip="true">{c.proposedName}</strong> },
          {
            // The existing-vessel link used to be a second line under the badge; it sits beside it on one line now.
            key: 'status', label: t('Status'), cellClassName: 'cell-nowrap',
            render: (c) => (
              <>
                <CodeStatusBadge {...candidateStatusBadge(t, c.candidateStatus)} />
                {c.candidateStatus === 'AlreadyOnboarded' && c.existingVesselId && (
                  <>
                    {' '}
                    <Link to={`/vessels/${c.existingVesselId}`} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()}>{t('Open existing vessel')}</Link>
                  </>
                )}
              </>
            ),
          },
          {
            key: 'path', label: t('Path'), cellClassName: 'mono', cellTitle: (c) => c.path,
            render: (c) => <span className="cell-clip" data-i18n-skip="true"><span>{c.path}</span></span>,
          },
          {
            key: 'remote', label: t('Remote'), cellClassName: 'mono text-dim', cellTitle: (c) => c.remoteUrl ?? '',
            render: (c) => <span className="cell-clip" data-i18n-skip="true"><span>{c.remoteUrl || t('(no origin)')}</span></span>,
          },
          {
            key: 'branch', label: t('Branch'), cellClassName: 'mono text-dim', cellTitle: (c) => c.defaultBranch || '',
            render: (c) => <span className="cell-clip" data-i18n-skip="true"><span>{c.defaultBranch || '-'}</span></span>,
          },
        ]}
      />

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
              {IMPORT_LANDING_MODES.map((m) => <option key={m.value || 'default'} value={m.value}>{t(m.label)}</option>)}
            </select>
          </label>
        </div>
        <p className="text-dim form-help">{t('Imported vessels use the repository origin as the remote and the discovered folder as the working directory. The checkout is never deleted when a vessel is removed.')}</p>
      </fieldset>
    </div>
  );
}
