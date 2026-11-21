import { useEffect, useState, useCallback } from 'react';
import { getVesselBranches, pushVesselBranch, mergeVesselBranch, type BranchInfo } from '../../api/client';
import { useNotifications } from '../../context/NotificationContext';
import { useLocale } from '../../context/LocaleContext';
import CopyButton from '../shared/CopyButton';
import DataTable from '../shared/DataTable';
import LoadingIndicator from '../shared/LoadingIndicator';

interface BranchesModalProps {
  vesselId: string;
  vesselName: string;
  open: boolean;
  onClose: () => void;
  /** Preselect this branch as the merge source (for example a mission's branch), with the default branch as target. */
  initialMergeSource?: string | null;
  /** Called after a successful merge (for example to refresh the mission that owns the branch). */
  onMerged?: () => void;
}

/**
 * Branch management for a vessel: lists branches with current/default flags and ahead/behind counts,
 * and lets the operator push a branch or merge one branch into another. Read from and write to the
 * vessel's repository via the /branches endpoints.
 */
export default function BranchesModal({ vesselId, vesselName, open, onClose, initialMergeSource, onMerged }: BranchesModalProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);
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
      setLoaded(true);
    }
  }, [vesselId]);

  useEffect(() => {
    if (open) load();
  }, [open, load]);

  // Preselect the requested source branch and the default branch as target once the branches are known.
  useEffect(() => {
    if (!open || !initialMergeSource) return;
    setMergeSource(initialMergeSource);
    setMergeTarget(defaultBranch);
  }, [open, initialMergeSource, defaultBranch]);

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
      onMerged?.();
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
          <h3 style={{ margin: 0 }}>{t('{{name}} Branches', { name: vesselName })}</h3>
        </div>

        {error && <p className="text-dim" style={{ color: 'var(--red)' }}>{error}</p>}

        {(loading || !loaded) && branches.length === 0 && !error ? (
          <LoadingIndicator label={t('Loading branches...')} />
        ) : (
          <>
            <div style={{ marginTop: '0.75rem' }}>
              <DataTable
                tableKey="vessel-branches"
                rows={branches}
                rowKey={(b) => b.name}
                onRefresh={load}
                refreshTitle={t('Reload the branch list and ahead/behind counts from the vessel repository.')}
                busy={loading}
                emptyMessage={loading ? undefined : t('No branches found.')}
                columns={[
                  {
                    key: 'branch', label: t('Branch'), headerTitle: t('Branch name in the vessel repository (normally Armada\'s bare clone at the vessel Local Path).'), required: true, cellClassName: 'cell-nowrap',
                    render: (b) => (
                      <span className="id-display">
                        <span className="url-value mono" title={b.name}>{b.name}</span>
                        {b.isDefault && <span className="tag" title={t('This is the vessel default branch. Ahead and behind counts are measured against it, and missions land into it by default.')}>{t('default')}</span>}
                        {b.isCurrent && <span className="tag connected" title={t('The branch currently checked out (HEAD) in the repository Armada reads.')}>{t('current')}</span>}
                      </span>
                    ),
                  },
                  {
                    key: 'aheadBehind', label: t('Ahead / Behind'), headerTitle: t('Commits this branch has that the default branch does not (+), and commits the default branch has that this branch lacks (-).'), cellClassName: 'cell-nowrap',
                    render: (b) => (
                      <>
                        <span style={{ color: 'var(--green)' }}>+{b.ahead}</span>
                        {' / '}
                        <span style={{ color: 'var(--red)' }}>-{b.behind}</span>
                      </>
                    ),
                  },
                  {
                    // One line: the subject truncates; hash and date are their own columns (and in the tooltip).
                    key: 'lastCommit', label: t('Last Commit'), headerTitle: t('Subject line of the newest commit on the branch. Hover a cell for the full subject, hash and date.'), cellClassName: 'truncate-cell',
                    cellTitle: (b) => [b.commitSubject, b.commitHash, b.commitDate ? new Date(b.commitDate).toLocaleString() : ''].filter(Boolean).join(' -- ') || undefined,
                    render: (b) => <span className="truncate-text">{b.commitSubject || '-'}</span>,
                  },
                  {
                    key: 'commit', label: t('Commit'), headerTitle: t('Hash of the newest commit on the branch.'), defaultHidden: true, cellClassName: 'mono text-dim cell-nowrap',
                    render: (b) => b.commitHash || '-',
                  },
                  {
                    key: 'committed', label: t('Committed'), headerTitle: t('When the newest commit on the branch was made, in your local time.'), cellClassName: 'text-dim cell-nowrap',
                    render: (b) => (b.commitDate ? new Date(b.commitDate).toLocaleString() : '-'),
                  },
                  {
                    key: 'actions', label: t('Actions'), headerTitle: t('Push a branch to the remote, or copy its name.'), fixed: true, interactive: true, cellClassName: 'cell-nowrap',
                    render: (b) => (
                      <>
                        <button className="btn btn-sm" title={t('Push this branch to the vessel remote (origin), creating or updating it there. Requires a token with push access.')} onClick={() => handlePush(b.name)} disabled={busy === 'push:' + b.name}>
                          {busy === 'push:' + b.name ? t('Pushing...') : t('Push')}
                        </button>
                        <CopyButton text={b.name} />
                      </>
                    ),
                  },
                ]}
              />
            </div>

            <div className="card" style={{ marginTop: '1rem' }}>
              <h4 style={{ marginTop: 0 }} title={t('Merge one branch into another directly in the vessel repository, outside any mission. Use it to land a mission branch by hand.')}>{t('Merge a branch')}</h4>
              <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', alignItems: 'center' }}>
                <label style={{ display: 'flex', flexDirection: 'column', fontSize: '0.8rem' }} title={t('Branch whose commits you want to bring in, e.g. a mission branch such as armada/my-captain/msn_abc.')}>
                  {t('Source')}
                  <select title={t('Branch whose commits you want to bring in, e.g. a mission branch such as armada/my-captain/msn_abc.')} value={mergeSource} onChange={e => setMergeSource(e.target.value)}>
                    <option value="">{t('Select...')}</option>
                    {branches.map(b => <option key={b.name} value={b.name}>{b.name}</option>)}
                  </select>
                </label>
                <span style={{ alignSelf: 'flex-end', paddingBottom: 6 }}>{t('into')}</span>
                <label style={{ display: 'flex', flexDirection: 'column', fontSize: '0.8rem' }} title={t('Branch that receives the merge, usually the default branch. Git merges in the vessel repository; a conflict fails the merge and leaves the target unchanged.')}>
                  {t('Target')}
                  <select title={t('Branch that receives the merge, usually the default branch. Git merges in the vessel repository; a conflict fails the merge and leaves the target unchanged.')} value={mergeTarget} onChange={e => setMergeTarget(e.target.value)}>
                    <option value="">{t('Select...')}</option>
                    {branches.map(b => <option key={b.name} value={b.name}>{b.name}</option>)}
                  </select>
                </label>
                <label style={{ display: 'flex', gap: 4, alignItems: 'center', alignSelf: 'flex-end', paddingBottom: 6 }} title={t('After a successful merge, push the target branch to the remote. Uncheck to merge only in Armada\'s local repository and push later.')}>
                  <input title={t('After a successful merge, push the target branch to the remote. Uncheck to merge only in Armada\'s local repository and push later.')} type="checkbox" checked={mergePush} onChange={e => setMergePush(e.target.checked)} />
                  {t('Push after merge')}
                </label>
                <button className="btn btn-primary btn-sm" style={{ alignSelf: 'flex-end' }} onClick={handleMerge} disabled={busy === 'merge'} title={t('Merge the source branch into the target now, and push the target if Push after merge is checked. This changes the target branch and cannot be undone here.')}>
                  {busy === 'merge' ? t('Merging...') : t('Merge')}
                </button>
              </div>
            </div>
          </>
        )}

        <div className="modal-actions" style={{ marginTop: '1rem' }}>
          <button className="btn" onClick={onClose} title={t('Close branch management. Pushes and merges already done stay done.')}>{t('Close')}</button>
        </div>
      </div>
    </div>
  );
}
