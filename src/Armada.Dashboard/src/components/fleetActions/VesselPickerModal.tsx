import { useEffect, useMemo, useState } from 'react';
import { listFleets, listVessels } from '../../api/client';
import type { Fleet, Vessel } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import DialogShell from '../shared/DialogShell';
import { ErrorState, LoadingState } from '../shared/StateBlocks';
import { MAX_RUN_VESSELS } from './RunActionModal';

interface VesselPickerModalProps {
  open: boolean;
  onClose: () => void;
  /** Called with the chosen vessel ids when the operator continues. */
  onPicked: (vesselIds: string[]) => void;
  /** Already-localized title. */
  title?: string;
}

/**
 * Choose target vessels for a fleet action run when the operator did not start from the Vessels table.
 * Loads the tenant's vessels (the same full list the Vessels page uses) and filters them by name and fleet.
 */
export default function VesselPickerModal({ open, onClose, onPicked, title }: VesselPickerModalProps) {
  const { t } = useLocale();
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [fleetId, setFleetId] = useState('');
  const [selected, setSelected] = useState<string[]>([]);

  async function load() {
    setLoading(true);
    setError('');
    try {
      const [v, f] = await Promise.all([listVessels({ pageSize: 9999 }), listFleets({ pageSize: 9999 })]);
      setVessels(v.objects || []);
      setFleets(f.objects || []);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load vessels.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (!open) return;
    setSelected([]);
    setSearch('');
    setFleetId('');
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return vessels.filter((v) => (!fleetId || v.fleetId === fleetId) && (!term || v.name.toLowerCase().includes(term) || (v.workingDirectory ?? '').toLowerCase().includes(term)));
  }, [vessels, search, fleetId]);

  const allVisibleSelected = filtered.length > 0 && filtered.every((v) => selected.includes(v.id));

  function toggle(id: string) {
    setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));
  }

  function toggleAllVisible() {
    if (allVisibleSelected) setSelected((s) => s.filter((id) => !filtered.some((v) => v.id === id)));
    else setSelected((s) => Array.from(new Set([...s, ...filtered.map((v) => v.id)])));
  }

  const tooMany = selected.length > MAX_RUN_VESSELS;

  return (
    <DialogShell
      open={open}
      onClose={onClose}
      title={title ?? t('Choose vessels')}
      subtitle={t('{count, plural, one {# vessel selected} other {# vessels selected}}', { count: selected.length })}
      size="lg"
      footer={(
        <>
          <button type="button" className="btn" onClick={onClose}>{t('Cancel')}</button>
          <button type="button" className="btn btn-primary" disabled={selected.length === 0 || tooMany} onClick={() => onPicked(selected)}>
            {t('Continue')}
          </button>
        </>
      )}
    >
      <div className="toolbar-row">
        <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Vessel name or path contains...')} aria-label={t('Vessel name or path contains...')} />
        <select value={fleetId} onChange={(e) => setFleetId(e.target.value)} aria-label={t('Filter vessels by fleet')}>
          <option value="">{t('All Fleets')}</option>
          {fleets.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
        </select>
      </div>
      {tooMany && <div className="alert alert-error" role="alert">{t('A run can target at most 500 vessels.')}</div>}
      {error && <ErrorState message={error} onRetry={() => void load()} />}
      {loading && <LoadingState />}
      {!loading && !error && vessels.length === 0 && <p className="text-dim">{t('No vessels yet. Import repositories from the Vessels page first.')}</p>}
      {!loading && !error && vessels.length > 0 && (
        <div className="table-wrap picker-table-wrap">
          <table>
            <thead>
              <tr>
                <th className="col-checkbox" scope="col">
                  <input type="checkbox" checked={allVisibleSelected} onChange={toggleAllVisible} aria-label={t('Select all visible vessels')} title={t('Select all visible vessels')} />
                </th>
                <th scope="col">{t('Name')}</th>
                <th scope="col">{t('Working Directory')}</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((v) => (
                <tr key={v.id} className="clickable" onClick={() => toggle(v.id)}>
                  <td className="col-checkbox" onClick={(e) => e.stopPropagation()}>
                    <input type="checkbox" checked={selected.includes(v.id)} onChange={() => toggle(v.id)} aria-label={t('Select {{name}}', { name: v.name })} />
                  </td>
                  <td><strong>{v.name}</strong></td>
                  <td className="mono text-dim" title={v.workingDirectory ?? ''}><span className="cell-clip"><span>{v.workingDirectory || '-'}</span></span></td>
                </tr>
              ))}
              {filtered.length === 0 && <tr><td colSpan={3} className="text-dim">{t('No vessels match the current filters.')}</td></tr>}
            </tbody>
          </table>
        </div>
      )}
    </DialogShell>
  );
}
