import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import {
  getVesselCleanupInventory,
  listVesselCleanupResources,
  runVesselCleanup,
  type VesselCleanupDisposition,
  type VesselCleanupInventory,
  type VesselCleanupItemResult,
  type VesselCleanupResource,
} from '../../api/client';
import type { EnumerationResult } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { formatBytes } from '../../lib/format';
import ConfirmDialog from '../shared/ConfirmDialog';
import LoadingIndicator from '../shared/LoadingIndicator';

interface VesselCleanupModalProps {
  vesselId: string;
  vesselName: string;
  open: boolean;
  onClose: () => void;
}

const GROUPS: { disposition: VesselCleanupDisposition; title: string; hint: string }[] = [
  {
    disposition: 'Safe',
    title: 'Safe to clean up',
    hint: 'Evidence shows nothing needs these resources. Select the ones to remove.',
  },
  {
    disposition: 'InUse',
    title: 'Still in use',
    hint: 'Running, queued, review, landing, retry, captain, or merge-queue work still references these. They cannot be cleaned up.',
  },
  {
    disposition: 'Unknown',
    title: 'Unknown disposition',
    hint: 'These could not be proven safe (uncommitted or unmerged work, shared paths, unreachable Harbors, or unclear ownership). They cannot be cleaned up.',
  },
];

/** Rows shown per disposition group; the server pages each group and reports the exact total. */
export const CLEANUP_GROUP_PAGE_SIZE = 50;
/** Page size used when every safe resource must be read (select all), page by page. */
const SELECT_ALL_PAGE_SIZE = 1000;
const MAX_SELECT_ALL_PAGES = 100;

type GroupPages = Record<VesselCleanupDisposition, EnumerationResult<VesselCleanupResource> | null>;
const EMPTY_PAGES: GroupPages = { Safe: null, InUse: null, Unknown: null };

/**
 * Vessel cleanup: opens with a read-only inventory of the vessel's docks, worktrees, and Armada-managed branches,
 * grouped as safe, in use, or unknown with the reason for each. Only safe resources can be selected; cleanup asks for
 * confirmation, the server revalidates every item before removing it, and the per-item outcomes are shown with the
 * refreshed inventory.
 */
export default function VesselCleanupModal({ vesselId, vesselName, open, onClose }: VesselCleanupModalProps) {
  const { t } = useLocale();
  const titleId = useId();
  const [loading, setLoading] = useState(false);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState('');
  const [inventory, setInventory] = useState<VesselCleanupInventory | null>(null);
  // Selected safe keys with their measured size (null when unmeasured), so selection survives paging.
  const [selected, setSelected] = useState<Map<string, number | null>>(new Map());
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [results, setResults] = useState<VesselCleanupItemResult[] | null>(null);
  const [pages, setPages] = useState<GroupPages>(EMPTY_PAGES);
  const [selectingAll, setSelectingAll] = useState(false);

  // Page 1 replaces the group; later pages append (Load more), keeping the server's exact totals.
  const loadGroup = useCallback(async (disposition: VesselCleanupDisposition, pageNumber: number) => {
    const page = await listVesselCleanupResources(vesselId, { pageNumber, pageSize: CLEANUP_GROUP_PAGE_SIZE, status: disposition });
    setPages(prev => {
      const existing = prev[disposition];
      if (pageNumber <= 1 || !existing) return { ...prev, [disposition]: page };
      return { ...prev, [disposition]: { ...page, objects: [...existing.objects, ...(page.objects ?? [])] } };
    });
  }, [vesselId]);

  const loadGroups = useCallback(async (summary: VesselCleanupInventory) => {
    setPages(EMPTY_PAGES);
    if (summary.resourceCount === 0) return;
    await Promise.all(GROUPS.map(g => loadGroup(g.disposition, 1)));
  }, [loadGroup]);

  const scan = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const summary = await getVesselCleanupInventory(vesselId);
      setInventory(summary);
      setSelected(new Map());
      await loadGroups(summary);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, [vesselId, loadGroups]);

  useEffect(() => {
    if (!open) return;
    setInventory(null);
    setResults(null);
    void scan();
  }, [open, scan]);

  const grouped = useMemo(() => ({
    Safe: pages.Safe?.objects ?? [],
    InUse: pages.InUse?.objects ?? [],
    Unknown: pages.Unknown?.objects ?? [],
  }), [pages]);

  if (!open) return null;

  const safeTotal = pages.Safe?.totalRecords ?? inventory?.safeCount ?? 0;
  const allSafeSelected = safeTotal > 0 && selected.size >= safeTotal;
  const selectedBytes = [...selected.values()].reduce<number>((sum, size) => sum + (size ?? 0), 0);

  const toggle = (r: VesselCleanupResource) => {
    setSelected(prev => {
      const next = new Map(prev);
      if (next.has(r.key)) next.delete(r.key); else next.set(r.key, r.sizeBytes);
      return next;
    });
  };

  // Every safe resource, not just this page: read the Safe group page by page (1000 per page) explicitly.
  const toggleAllSafe = async () => {
    if (allSafeSelected) { setSelected(new Map()); return; }
    setSelectingAll(true);
    setError('');
    try {
      const next = new Map<string, number | null>();
      for (let pageNumber = 1; pageNumber <= MAX_SELECT_ALL_PAGES; pageNumber++) {
        const page = await listVesselCleanupResources(vesselId, { pageNumber, pageSize: SELECT_ALL_PAGE_SIZE, status: 'Safe' });
        for (const r of page.objects ?? []) next.set(r.key, r.sizeBytes);
        if (!page.objects?.length || pageNumber >= page.totalPages) break;
      }
      setSelected(next);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setSelectingAll(false);
    }
  };

  const runCleanup = async () => {
    setConfirmOpen(false);
    setRunning(true);
    setError('');
    try {
      // runVesselCleanup sends at most 1000 keys per request and merges the outcomes.
      const result = await runVesselCleanup(vesselId, [...selected.keys()]);
      setResults(result.results);
      setInventory(result.inventory);
      setSelected(new Map());
      await loadGroups(result.inventory);
    } catch (e) {
      setError(t('Cleanup failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
    } finally {
      setRunning(false);
    }
  };

  const typeLabel = (r: VesselCleanupResource) => t(r.resourceType);

  const owner = (r: VesselCleanupResource) => {
    const parts: string[] = [];
    if (r.voyageId) parts.push(`${t('Voyage')} ${r.voyageId}`);
    if (r.missionId) parts.push(`${t('Mission')} ${r.missionId}${r.missionStatus ? ` (${r.missionStatus})` : ''}`);
    if (r.dockId && r.resourceType !== 'Dock') parts.push(`${t('Dock')} ${r.dockId}`);
    if (r.captainId) parts.push(`${t('Captain')} ${r.captainId}`);
    parts.push(r.harborId ? `${t('Harbor')} ${r.harborId}` : t('Admiral host'));
    return parts;
  };

  const resultsSummary = results
    ? t('{{deleted}} removed, {{skipped}} skipped, {{failed}} failed.', {
      deleted: String(results.filter(r => r.status === 'Deleted').length),
      skipped: String(results.filter(r => r.status === 'Skipped').length),
      failed: String(results.filter(r => r.status === 'Failed').length),
    })
    : '';

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal vessel-cleanup-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        style={{ width: '95vw', maxWidth: '95vw', height: '95vh', maxHeight: '95vh', overflowY: 'auto' }}
        onClick={e => e.stopPropagation()}
      >
        <h3 id={titleId} style={{ marginTop: 0 }}>{t('Cleanup: {{name}}', { name: vesselName })}</h3>
        <p className="text-dim" style={{ marginTop: 0 }}>
          {t('Opening this window only inspects the vessel; nothing is deleted until you select safe resources and confirm. Remote branches, default branches, and the primary checkout are never removed.')}
        </p>

        {error && (
          <div role="alert" className="vessel-cleanup-error" style={{ color: 'var(--red)', marginBottom: '0.75rem' }}>
            {error}{' '}
            <button type="button" className="btn btn-sm" onClick={() => void scan()} disabled={loading || running}>{t('Retry')}</button>
          </div>
        )}

        {results && (
          <div className="vessel-cleanup-results" aria-label={t('Cleanup results')} style={{ marginBottom: '1rem' }}>
            <h4 style={{ margin: '0 0 0.4rem' }}>{t('Cleanup results')}</h4>
            <p className="text-dim" style={{ margin: '0 0 0.4rem' }}>{resultsSummary}</p>
            <ul style={{ margin: 0, paddingLeft: '1.2rem' }}>
              {results.map(r => (
                <li key={r.key} data-status={r.status}>
                  <strong style={{ color: r.status === 'Failed' ? 'var(--red)' : r.status === 'Deleted' ? 'var(--green)' : undefined }}>{t(r.status)}</strong>
                  {' '}<span className="mono">{r.identity ?? r.key}</span>: {r.message}
                </li>
              ))}
            </ul>
          </div>
        )}

        {(loading || running) && <LoadingIndicator label={running ? t('Cleaning up selected resources...') : t('Scanning vessel resources...')} />}

        {!loading && inventory && (
          <>
            <p className="vessel-cleanup-summary" style={{ margin: '0 0 0.75rem' }}>
              {t('{{safe}} safe, {{inUse}} in use, {{unknown}} unknown. Safe resources use {{size}} measured on disk.', {
                safe: String(inventory.safeCount),
                inUse: String(inventory.inUseCount),
                unknown: String(inventory.unknownCount),
                size: formatBytes(inventory.safeMeasuredBytes),
              })}
              {inventory.safeUnmeasuredCount > 0 && ` ${t('{{count}} safe resources could not be measured.', { count: String(inventory.safeUnmeasuredCount) })}`}
            </p>

            {inventory.resourceCount === 0 && (
              <p className="text-dim vessel-cleanup-empty">{t('Nothing to clean up: this vessel has no docks, orphaned worktrees, or Armada branches.')}</p>
            )}

            {inventory.resourceCount > 0 && GROUPS.map(group => {
              const rows = grouped[group.disposition];
              const page = pages[group.disposition];
              const total = page?.totalRecords ?? rows.length;
              const selectable = group.disposition === 'Safe';
              return (
                <section key={group.disposition} className="vessel-cleanup-group" data-disposition={group.disposition} aria-label={t(group.title)} style={{ marginBottom: '1rem' }}>
                  <h4 style={{ margin: '0 0 0.25rem' }}>{t(group.title)} ({total})</h4>
                  <p className="text-dim" style={{ margin: '0 0 0.4rem', fontSize: '0.85rem' }}>{t(group.hint)}</p>
                  {rows.length === 0 ? (
                    <p className="text-dim" style={{ margin: 0 }}>{t('None.')}</p>
                  ) : (
                    <div className="table-wrap"><table className="table-dense">
                      <thead>
                        <tr>
                          <th style={{ width: '2rem' }}>
                            {selectable && (
                              <input type="checkbox" aria-label={t('Select all safe resources')} checked={allSafeSelected} onChange={() => void toggleAllSafe()} disabled={running || selectingAll} />
                            )}
                          </th>
                          <th>{t('Type')}</th>
                          <th>{t('Resource')}</th>
                          <th>{t('Owner')}</th>
                          <th>{t('Reason')}</th>
                          <th className="text-right">{t('Size')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {rows.map(r => (
                          <tr key={r.key}>
                            <td>
                              <input
                                type="checkbox"
                                aria-label={t('Select {{name}}', { name: r.identity })}
                                checked={selectable && selected.has(r.key)}
                                disabled={!selectable || running}
                                title={selectable ? undefined : t('Only safe resources can be cleaned up.')}
                                onChange={() => toggle(r)}
                              />
                            </td>
                            <td>{typeLabel(r)}</td>
                            <td>
                              <div className="mono">{r.identity}</div>
                              {r.path && r.path !== r.identity && <div className="mono text-dim" style={{ fontSize: '0.8rem' }}>{r.path}</div>}
                              {r.branchName && r.branchName !== r.identity && <div className="mono text-dim" style={{ fontSize: '0.8rem' }}>{r.branchName}</div>}
                            </td>
                            <td className="text-dim" style={{ fontSize: '0.85rem' }}>{owner(r).map(p => <div key={p}>{p}</div>)}</td>
                            <td style={{ fontSize: '0.85rem' }}>{r.reason}</td>
                            <td className="text-right cell-nowrap">{r.sizeBytes == null ? <span className="text-dim" title={t('Size could not be measured.')}>-</span> : formatBytes(r.sizeBytes)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table></div>
                  )}
                  {page && page.pageNumber < page.totalPages && (
                    <div className="vessel-cleanup-pager" style={{ marginTop: '0.4rem' }}>
                      <button type="button" className="btn btn-sm" disabled={running} onClick={() => void loadGroup(group.disposition, page.pageNumber + 1)}>{t('Load more')}</button>
                    </div>
                  )}
                </section>
              );
            })}
          </>
        )}

        <div className="modal-actions" style={{ marginTop: '1rem' }}>
          <button type="button" className="btn" onClick={() => void scan()} disabled={loading || running} title={t('Inspect the vessel again. Read-only.')}>{t('Rescan')}</button>
          <button
            type="button"
            className="btn btn-danger"
            onClick={() => setConfirmOpen(true)}
            disabled={selected.size === 0 || loading || running}
            title={t('Remove the selected safe resources after you confirm. Each one is checked again just before removal.')}
          >
            {t('Clean up selected ({{count}})', { count: String(selected.size) })}
          </button>
          <button type="button" className="btn" onClick={onClose} disabled={running}>{t('Close')}</button>
        </div>

        <ConfirmDialog
          open={confirmOpen}
          title="Clean up vessel resources"
          confirmLabel="Clean up"
          danger
          message={t('Remove {{count}} selected resources ({{size}} measured) from {{name}}? Each one is checked again just before removal and skipped if it is no longer safe. This cannot be undone.', {
            count: String(selected.size),
            size: formatBytes(selectedBytes),
            name: vesselName,
          })}
          onConfirm={() => void runCleanup()}
          onCancel={() => setConfirmOpen(false)}
        />
      </div>
    </div>
  );
}
