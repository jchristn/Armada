import { useEffect, useState, useCallback } from 'react';
import { getVesselBranches, pushVesselBranch, mergeVesselBranch, type BranchInfo } from '../../api/client';
import { useNotifications } from '../../context/NotificationContext';
import { useLocale } from '../../context/LocaleContext';
import CopyButton from '../shared/CopyButton';
import LoadingIndicator from '../shared/LoadingIndicator';

interface BranchesModalProps {
  vesselId: string;
  vesselName: string;
  open: boolean;
  onClose: () => void;
}

/**
 * Branch management for a vessel: lists branches with current/default flags and ahead/behind counts,
 * and lets the operator push a branch or merge one branch into another. Read from and write to the
 * vessel's repository via the /branches endpoints.
 */
export default function BranchesModal({ vesselId, vesselName, open, onClose }: BranchesModalProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [loading, setLoading] = useState(false);
  const [branches, setBranches] = useState<BranchInfo[]>([]);
  const [defaultBranch, setDefaultBranch] = useState<string>('main');
  const [error, setError] = useState<string>('');
  const [busy, setBusy] = useState<string>('');

  const [mergeSource, setMergeSource] = useState<string>('');
  const [mergeTarget, setMergeTarget] = useState<string>('');
  const [mergePush, setMergePush] = useState<boolean>(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const result = await getVesselBranches(vesselId);
      setBranches(result.branches || []);
      setDefaultBranch(result.defaultBranch || 'main');
      if (result.error) setError(result.error);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, [vesselId]);

  useEffect(() => {
    if (open) load();
  }, [open, load]);

  async function handlePush(branch: string) {
    setBusy('push:' + branch);
    try {
      await pushVesselBranch(vesselId, branch);
      pushToast('success', t('Pushed {{branch}}', { branch }));
      await load();
    } catch (e) {
      pushToast('error', t('Push failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
    } finally {
      setBusy('');
    }
  }

  async function handleMerge() {
    if (!mergeSource || !mergeTarget) {
      pushToast('warning', t('Select a source and a target branch.'));
      return;
    }
    if (mergeSource === mergeTarget) {
      pushToast('warning', t('Source and target must differ.'));
      return;
    }
    setBusy('merge');
    try {
      await mergeVesselBranch(vesselId, mergeSource, mergeTarget, mergePush);
      pushToast('success', t('Merged {{source}} into {{target}}', { source: mergeSource, target: mergeTarget }));
      await load();
    } catch (e) {
      pushToast('error', t('Merge failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
    } finally {
      setBusy('');
    }
  }

  if (!open) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ width: 'min(980px, 95vw)', maxWidth: 'min(980px, 95vw)', maxHeight: '92vh', overflowY: 'auto' }} onClick={e => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h3 style={{ margin: 0 }}>{t('Manage Branches')} -- {vesselName}</h3>
          <button className="btn btn-sm" onClick={load} disabled={loading}>{t('Refresh')}</button>
        </div>

        {error && <p className="text-dim" style={{ color: 'var(--red)' }}>{error}</p>}

        {loading && branches.length === 0 ? (
          <LoadingIndicator label={t('Loading branches...')} />
        ) : (
          <>
            <div className="table-wrap" style={{ marginTop: '0.75rem' }}>
              <table>
                <thead>
                  <tr>
                    <th>{t('Branch')}</th>
                    <th>{t('Ahead / Behind')}</th>
                    <th>{t('Last Commit')}</th>
                    <th>{t('Actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {branches.map(b => (
                    <tr key={b.name}>
                      <td>
                        <span style={{ fontFamily: 'monospace' }}>{b.name}</span>
                        {b.isDefault && <span className="tag" style={{ marginLeft: 6 }}>{t('default')}</span>}
                        {b.isCurrent && <span className="tag connected" style={{ marginLeft: 6 }}>{t('current')}</span>}
                      </td>
                      <td>
                        <span style={{ color: 'var(--green)' }}>+{b.ahead}</span>
                        {' / '}
                        <span style={{ color: 'var(--red)' }}>-{b.behind}</span>
                      </td>
                      <td>
                        <div style={{ fontSize: '0.85rem' }}>{b.commitSubject || '-'}</div>
                        <div className="text-dim" style={{ fontSize: '0.75rem' }}>
                          {b.commitHash || ''}{b.commitDate ? ' -- ' + new Date(b.commitDate).toLocaleString() : ''}
                        </div>
                      </td>
                      <td>
                        <button className="btn btn-sm" onClick={() => handlePush(b.name)} disabled={busy === 'push:' + b.name}>
                          {busy === 'push:' + b.name ? t('Pushing...') : t('Push')}
                        </button>
                        <CopyButton text={b.name} title={t('Copy branch name')} />
                      </td>
                    </tr>
                  ))}
                  {branches.length === 0 && !loading && (
                    <tr><td colSpan={4} className="text-dim">{t('No branches found.')}</td></tr>
                  )}
                </tbody>
              </table>
            </div>

            <div className="card" style={{ marginTop: '1rem' }}>
              <h4 style={{ marginTop: 0 }}>{t('Merge a branch')}</h4>
              <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', alignItems: 'center' }}>
                <label style={{ display: 'flex', flexDirection: 'column', fontSize: '0.8rem' }}>
                  {t('Source')}
                  <select value={mergeSource} onChange={e => setMergeSource(e.target.value)}>
                    <option value="">{t('Select...')}</option>
                    {branches.map(b => <option key={b.name} value={b.name}>{b.name}</option>)}
                  </select>
                </label>
                <span style={{ alignSelf: 'flex-end', paddingBottom: 6 }}>{t('into')}</span>
                <label style={{ display: 'flex', flexDirection: 'column', fontSize: '0.8rem' }}>
                  {t('Target')}
                  <select value={mergeTarget} onChange={e => setMergeTarget(e.target.value)}>
                    <option value="">{t('Select...')}</option>
                    {branches.map(b => <option key={b.name} value={b.name}>{b.name}</option>)}
                  </select>
                </label>
                <label style={{ display: 'flex', gap: 4, alignItems: 'center', alignSelf: 'flex-end', paddingBottom: 6 }}>
                  <input type="checkbox" checked={mergePush} onChange={e => setMergePush(e.target.checked)} />
                  {t('Push after merge')}
                </label>
                <button className="btn btn-primary btn-sm" style={{ alignSelf: 'flex-end' }} onClick={handleMerge} disabled={busy === 'merge'}>
                  {busy === 'merge' ? t('Merging...') : t('Merge')}
                </button>
              </div>
            </div>
          </>
        )}

        <div className="modal-actions" style={{ marginTop: '1rem' }}>
          <button className="btn" onClick={onClose}>{t('Close')}</button>
        </div>
      </div>
    </div>
  );
}
