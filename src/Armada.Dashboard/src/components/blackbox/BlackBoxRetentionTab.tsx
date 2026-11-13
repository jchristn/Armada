import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getBlackBoxRetention, getBlackBoxStorage, purgeBlackBox, runBlackBoxGc } from '../../api/client';
import type { BlackBoxGcReport, BlackBoxPurgeQuery, BlackBoxPurgeResult, BlackBoxResolution, BlackBoxRetentionStatus, BlackBoxStorageStatus } from '../../types/blackbox';
import type { Vessel } from '../../types/models';
import { useAuth } from '../../context/AuthContext';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import { BLACKBOX_RESOLUTIONS, blackBoxErrorStatus, formatBlackBoxBytes } from '../../lib/blackbox';
import ConfirmDialog from '../shared/ConfirmDialog';
import { ErrorState, LoadingState } from '../shared/StateBlocks';

function GcReport({ report }: { report: BlackBoxGcReport }) {
  const { t, formatDateTime } = useLocale();
  return (
    <div data-testid={report.dryRun ? 'blackbox-gc-preview' : 'blackbox-gc-report'}>
      <dl className="blackbox-kv">
        <dt>{t('Started')}</dt><dd>{formatDateTime(report.startedUtc)}{report.dryRun ? ` (${t('dry run')})` : ''}</dd>
        <dt>{t('Expired')}</dt><dd>{report.expired}</dd>
        <dt>{t('Evicted by quota')}</dt><dd>{report.evicted}</dd>
        <dt>{t('Cascaded')}</dt><dd>{report.cascaded}</dd>
        <dt>{t('Orphans removed')}</dt><dd>{report.orphansRemoved}</dd>
        <dt>{t('Objects missing')}</dt><dd>{report.objectsMissing}</dd>
        <dt>{t('Bytes freed')}</dt><dd>{formatBlackBoxBytes(report.bytesFreed)}</dd>
        <dt>{t('Failures')}</dt><dd>{report.failures}</dd>
      </dl>
      {!!report.candidateEntryIds?.length && (
        <p className="mono" style={{ fontSize: '0.8rem', overflowWrap: 'anywhere' }}>{report.candidateEntryIds.join(', ')}</p>
      )}
    </div>
  );
}

/**
 * Retention tab: the retention policy, entry counts and bytes, usage by storage target, the last and next garbage
 * collection run, a dry-run GC preview, and (global admins) purge by filter, always dry run first.
 */
export default function BlackBoxRetentionTab({ vessels }: { vessels: Vessel[] }) {
  const { t, formatDateTime } = useLocale();
  const { isAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const [status, setStatus] = useState<BlackBoxRetentionStatus | null>(null);
  const [storage, setStorage] = useState<BlackBoxStorageStatus | null>(null);
  const [error, setError] = useState('');
  const [denied, setDenied] = useState(false);
  const [loading, setLoading] = useState(true);
  const [preview, setPreview] = useState<BlackBoxGcReport | null>(null);
  const [running, setRunning] = useState('');
  const [confirmGc, setConfirmGc] = useState(false);
  const [purge, setPurge] = useState<{ vesselId: string; createdBefore: string; resolution: BlackBoxResolution | ''; includePinned: boolean }>({ vesselId: '', createdBefore: '', resolution: '', includePinned: false });
  const [purgeResult, setPurgeResult] = useState<BlackBoxPurgeResult | null>(null);
  const [confirmPurge, setConfirmPurge] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setStatus(await getBlackBoxRetention());
      setError('');
      setDenied(false);
    } catch (err: unknown) {
      const code = blackBoxErrorStatus(err);
      if (code === 401 || code === 403) setDenied(true);
      else setError(t('Retention status could not be loaded: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setLoading(false);
    }
    if (isAdmin) {
      try { setStorage(await getBlackBoxStorage()); } catch { setStorage(null); }
    }
  }, [isAdmin, t]);

  useEffect(() => { void load(); }, [load]);

  async function previewGc() {
    setRunning('preview');
    try {
      setPreview(await runBlackBoxGc(true));
    } catch (err: unknown) {
      pushToast('error', t('Failed: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setRunning('');
    }
  }

  async function runGc() {
    setConfirmGc(false);
    setRunning('gc');
    try {
      const report = await runBlackBoxGc(false);
      pushToast('success', t('Garbage collection finished: {{expired}} expired, {{evicted}} evicted, {{bytes}} freed.', { expired: report.expired, evicted: report.evicted, bytes: formatBlackBoxBytes(report.bytesFreed) }));
      setPreview(null);
      await load();
    } catch (err: unknown) {
      pushToast('error', t('Failed: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setRunning('');
    }
  }

  function purgeQuery(dryRun: boolean): BlackBoxPurgeQuery {
    return {
      dryRun,
      vesselId: purge.vesselId || undefined,
      createdBefore: purge.createdBefore ? `${purge.createdBefore}T00:00:00Z` : undefined,
      resolution: purge.resolution || undefined,
      includePinned: purge.includePinned || undefined,
    };
  }

  async function dryRunPurge() {
    setRunning('purge');
    try {
      setPurgeResult(await purgeBlackBox(purgeQuery(true)));
    } catch (err: unknown) {
      pushToast('error', t('Failed: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setRunning('');
    }
  }

  async function executePurge() {
    setConfirmPurge(false);
    setRunning('purge');
    try {
      const result = await purgeBlackBox(purgeQuery(false));
      pushToast('warning', t('Purged {{deleted}} entries ({{bytes}} freed).', { deleted: result.deleted, bytes: formatBlackBoxBytes(result.bytesFreed) }));
      setPurgeResult(null);
      await load();
    } catch (err: unknown) {
      pushToast('error', t('Failed: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setRunning('');
    }
  }

  function changePurge(next: Partial<typeof purge>) {
    setPurge((p) => ({ ...p, ...next }));
    // A different filter needs a new dry run before anything can be purged.
    setPurgeResult(null);
  }

  if (loading && !status) return <LoadingState />;
  if (denied) return <p className="text-dim" role="status">{t('Retention status is available to tenant administrators.')}</p>;
  if (error) return <ErrorState message={error} onRetry={() => void load()} />;
  if (!status) return null;
  const policy = status.policy;

  return (
    <div>
      <div className="blackbox-stat-grid">
        {([
          ['Entries', status.totalEntries.toLocaleString()],
          ['Pinned', status.pinnedEntries.toLocaleString()],
          ['Expired, awaiting GC', status.expiredEntries.toLocaleString()],
          ['Being deleted', status.deletingEntries.toLocaleString()],
          ['Stored bytes', formatBlackBoxBytes(status.totalBytes)],
        ] as const).map(([label, value]) => (
          <div className="stat-card" key={label}><div className="stat-label">{t(label)}</div><div className="stat-value">{value}</div></div>
        ))}
      </div>

      <section className="blackbox-section" aria-label={t('Policy')}>
        <h4>{t('Policy')} {isAdmin && <Link to="/server" style={{ fontSize: '0.8rem', fontWeight: 400 }}>{t('Change in Settings')}</Link>}</h4>
        <dl className="blackbox-kv">
          <dt>{t('Retention')}</dt><dd>{policy.retentionDays === 0 ? t('Keep forever') : t('{{days}} days', { days: policy.retentionDays })}</dd>
          <dt>{t('Resolved retention')}</dt><dd>{policy.resolvedRetentionDays === 0 ? t('Keep forever') : t('{{days}} days', { days: policy.resolvedRetentionDays })}</dd>
          <dt>{t('Keep latest per mission')}</dt><dd>{policy.keepLatestPerMission ? t('Yes') : t('No')}</dd>
          <dt>{t('Max entries per vessel')}</dt><dd>{policy.maxEntriesPerVessel.toLocaleString()}</dd>
          <dt>{t('Max total bytes')}</dt><dd>{formatBlackBoxBytes(policy.maxTotalBytes)}</dd>
          <dt>{t('Delete with mission')}</dt><dd>{policy.deleteWithMission ? t('Yes') : t('No')}</dd>
          <dt>{t('Delete with vessel')}</dt><dd>{policy.deleteWithVessel ? t('Yes') : t('No')}</dd>
          <dt>{t('GC interval')}</dt><dd>{t('{{minutes}} minutes', { minutes: policy.gcIntervalMinutes })}</dd>
          <dt>{t('Next GC run')}</dt><dd>{status.nextRunUtc ? formatDateTime(status.nextRunUtc) : '-'}</dd>
        </dl>
      </section>

      {storage && (
        <section className="blackbox-section" aria-label={t('Usage by storage target')}>
          <h4>{t('Usage by storage target')}</h4>
          <p className="text-dim" style={{ margin: '0 0 0.4rem' }}>
            {t('Current target: {{target}}.', { target: t(storage.target) })} {storage.health ? (storage.health.healthy ? t('Healthy.') : t('Unhealthy: {{message}}', { message: storage.health.message ?? '' })) : ''}
          </p>
          <div className="table-wrap">
            <table className="table-dense" aria-label={t('Usage by storage target')}>
              <thead><tr><th>{t('Target')}</th><th>{t('Entries')}</th><th>{t('Bytes')}</th></tr></thead>
              <tbody>
                {(storage.usage ?? []).map((u) => <tr key={u.target}><td>{t(u.target)}</td><td>{u.entries.toLocaleString()}</td><td>{formatBlackBoxBytes(u.bytes)}</td></tr>)}
                {(storage.usage ?? []).length === 0 && <tr><td colSpan={3} className="text-dim">{t('No stored entries.')}</td></tr>}
              </tbody>
            </table>
          </div>
        </section>
      )}

      <section className="blackbox-section" aria-label={t('Garbage collection')}>
        <h4>{t('Garbage collection')}</h4>
        {status.lastRun ? <><div className="text-dim" style={{ fontSize: '0.82rem' }}>{t('Last run')}</div><GcReport report={status.lastRun} /></> : <p className="text-dim">{t('Garbage collection has not run yet.')}</p>}
        {isAdmin && (
          <div className="blackbox-toolbar" style={{ marginTop: '0.5rem' }}>
            <button type="button" className="btn btn-sm" disabled={!!running} onClick={() => void previewGc()} title={t('List what garbage collection would delete now, without deleting anything.')}>{t('Preview garbage collection')}</button>
            <button type="button" className="btn btn-sm btn-danger" disabled={!!running} onClick={() => setConfirmGc(true)} title={t('Run one garbage collection pass now.')}>{t('Run garbage collection now')}</button>
          </div>
        )}
        {preview && <><div className="text-dim" style={{ fontSize: '0.82rem', marginTop: '0.5rem' }}>{t('Preview (nothing was deleted)')}</div><GcReport report={preview} /></>}
      </section>

      {isAdmin && (
        <section className="blackbox-section" aria-label={t('Purge by filter')}>
          <h4>{t('Purge by filter')}</h4>
          <p className="text-dim" style={{ margin: '0 0 0.4rem' }}>{t('Delete every entry that matches. Run the dry run first to see how many would go; pinned entries are kept unless included.')}</p>
          <div className="blackbox-filters">
            <label>{t('Vessel')}
              <select value={purge.vesselId} onChange={(e) => changePurge({ vesselId: e.target.value })} aria-label={t('Purge vessel')}>
                <option value="">{t('Any')}</option>
                {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
              </select>
            </label>
            <label>{t('Created before')}<input type="date" value={purge.createdBefore} onChange={(e) => changePurge({ createdBefore: e.target.value })} aria-label={t('Purge created before')} /></label>
            <label>{t('Resolution')}
              <select value={purge.resolution} onChange={(e) => changePurge({ resolution: e.target.value as BlackBoxResolution | '' })} aria-label={t('Purge resolution')}>
                <option value="">{t('Any')}</option>
                {BLACKBOX_RESOLUTIONS.map((r) => <option key={r} value={r}>{t(r)}</option>)}
              </select>
            </label>
            <label style={{ flexDirection: 'row', alignItems: 'center', gap: '0.4rem' }}>
              <input type="checkbox" checked={purge.includePinned} onChange={(e) => changePurge({ includePinned: e.target.checked })} aria-label={t('Include pinned entries')} />{t('Include pinned entries')}
            </label>
          </div>
          <div className="blackbox-toolbar">
            <button type="button" className="btn btn-sm" disabled={!!running} onClick={() => void dryRunPurge()} title={t('Count what this filter would delete, without deleting anything.')}>{t('Dry run purge')}</button>
            <button type="button" className="btn btn-sm btn-danger" disabled={!!running || !purgeResult || purgeResult.matched === 0} onClick={() => setConfirmPurge(true)} title={!purgeResult ? t('Run the dry run first.') : t('Delete the entries the dry run found.')}>
              {purgeResult ? t('Purge {{count}} entries', { count: purgeResult.matched - purgeResult.skippedPinned }) : t('Purge')}
            </button>
          </div>
          {purgeResult && (
            <p data-testid="blackbox-purge-preview">
              {t('Dry run: {{matched}} match, {{pinned}} pinned would be kept.', { matched: purgeResult.matched, pinned: purgeResult.skippedPinned })}
              {!!purgeResult.sampleEntryIds?.length && <span className="mono text-dim" style={{ display: 'block', fontSize: '0.8rem', overflowWrap: 'anywhere' }}>{purgeResult.sampleEntryIds.join(', ')}</span>}
            </p>
          )}
        </section>
      )}

      <ConfirmDialog
        open={confirmGc}
        title="Run garbage collection now"
        message={t('Delete every expired and over-quota entry now? Pinned entries and the latest entry of each mission are kept.')}
        confirmLabel="Run"
        danger
        onConfirm={() => void runGc()}
        onCancel={() => setConfirmGc(false)}
      />
      <ConfirmDialog
        open={confirmPurge}
        title="Purge black box entries"
        message={t('Permanently delete {{count}} black box entries and their evidence? This cannot be undone.', { count: purgeResult ? purgeResult.matched - purgeResult.skippedPinned : 0 })}
        confirmLabel="Purge"
        danger
        requireDeleteConfirm
        onConfirm={() => void executePurge()}
        onCancel={() => setConfirmPurge(false)}
      />
    </div>
  );
}
