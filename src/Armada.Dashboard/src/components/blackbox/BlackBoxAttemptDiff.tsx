import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { diffBlackBoxEntries } from '../../api/client';
import type { BlackBoxAttemptDiff as Diff, BlackBoxEntrySummary } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';

const FILE_LIMIT = 50;

/** The attempt to compare with by default: the one just before this entry, else the first other attempt. */
export function defaultDiffTarget(entryId: string, attempts: BlackBoxEntrySummary[]): string {
  const current = attempts.find((a) => a.id === entryId);
  const others = attempts.filter((a) => a.id !== entryId);
  if (others.length === 0) return '';
  if (current) {
    const earlier = others.filter((a) => a.attempt < current.attempt).sort((a, b) => b.attempt - a.attempt);
    if (earlier.length) return earlier[0].id;
  }
  return others[0].id;
}

function FileList({ title, files, total }: { title: string; files: string[]; total: number }) {
  const { t } = useLocale();
  return (
    <div>
      <h5 style={{ margin: '0 0 0.2rem' }}>{title} <span className="text-dim">({total})</span></h5>
      {files.length === 0 ? <div className="text-dim">{t('None')}</div> : (
        <ul className="blackbox-list mono" style={{ fontSize: '0.8rem' }}>
          {files.slice(0, FILE_LIMIT).map((f) => <li key={f}>{f}</li>)}
          {total > Math.min(files.length, FILE_LIMIT) && <li className="text-dim">{t('and {{count}} more', { count: total - Math.min(files.length, FILE_LIMIT) })}</li>}
        </ul>
      )}
    </div>
  );
}

/**
 * Attempt diff: pick another attempt of the mission (or paste any entry id) and see, side by side, what changed
 * between the two entries: captain, model, base commit, configuration, step, failure kind and the changed files.
 */
export default function BlackBoxAttemptDiff({ entryId, attempts }: { entryId: string; attempts: BlackBoxEntrySummary[] }) {
  const { t } = useLocale();
  const others = useMemo(() => attempts.filter((a) => a.id !== entryId), [attempts, entryId]);
  const [otherId, setOtherId] = useState(() => defaultDiffTarget(entryId, attempts));
  const [manualId, setManualId] = useState('');
  const [diff, setDiff] = useState<Diff | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!otherId) setOtherId(defaultDiffTarget(entryId, attempts));
    // Only fill in a default once attempts arrive.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attempts, entryId]);

  useEffect(() => {
    if (!otherId || otherId === entryId) { setDiff(null); setError(''); return; }
    let cancelled = false;
    setLoading(true);
    diffBlackBoxEntries(entryId, otherId)
      .then((d) => { if (!cancelled) { setDiff(d); setError(''); } })
      .catch((err: unknown) => { if (!cancelled) { setDiff(null); setError(err instanceof Error ? err.message : String(err)); } })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [entryId, otherId]);

  const attemptLabel = (a: BlackBoxEntrySummary) => `${t('Attempt {{attempt}}', { attempt: a.attempt })} · ${a.step}${a.failureKind ? ` · ${t(a.failureKind)}` : ''}`;

  return (
    <div data-testid="blackbox-attempt-diff">
      <div className="blackbox-diff-picker">
        {others.length > 0 && (
          <label>{t('Compare with')}
            <select value={others.some((a) => a.id === otherId) ? otherId : ''} onChange={(e) => setOtherId(e.target.value)} aria-label={t('Compare with attempt')}>
              <option value="">{t('Select an attempt...')}</option>
              {others.map((a) => <option key={a.id} value={a.id}>{attemptLabel(a)}</option>)}
            </select>
          </label>
        )}
        <form onSubmit={(e) => { e.preventDefault(); if (manualId.trim()) setOtherId(manualId.trim()); }} style={{ display: 'flex', gap: '0.4rem', alignItems: 'flex-end' }}>
          <label>{t('Or another entry')}
            <input value={manualId} onChange={(e) => setManualId(e.target.value)} placeholder="bbx_..." aria-label={t('Entry id to compare with')} className="mono" />
          </label>
          <button type="submit" className="btn btn-sm" disabled={!manualId.trim()} title={t('Compare this entry with the entry id you typed.')}>{t('Compare')}</button>
        </form>
      </div>

      {!otherId ? (
        <EmptyState title={t('Nothing to compare yet')}>{t('This mission has one recorded attempt. Type another entry id to compare with it.')}</EmptyState>
      ) : otherId === entryId ? (
        <div className="text-dim">{t('Pick a different entry; an entry cannot be compared with itself.')}</div>
      ) : loading && !diff ? <LoadingState /> : error ? (
        <ErrorState message={t('The comparison could not be loaded: {{message}}', { message: error })} />
      ) : diff ? (
        <>
          <p style={{ margin: '0 0 0.4rem', fontSize: '0.85rem' }}>
            {diff.sameMission ? t('Attempt {{attempt}} compared with attempt {{other}} of the same mission.', { attempt: diff.attempt, other: diff.otherAttempt }) : t('These entries belong to different missions.')}
            {' '}<span className={`blackbox-chip ${diff.sameFingerprint ? 'blackbox-chip-warning' : ''}`}>{diff.sameFingerprint ? t('Same failure fingerprint') : t('Different failure fingerprint')}</span>
          </p>
          {diff.differences.length === 0 ? <div className="text-dim">{t('No recorded values differ between these entries.')}</div> : (
            <table className="blackbox-diff-table" aria-label={t('Differences')}>
              <thead>
                <tr>
                  <th scope="col" style={{ width: '22%' }}>{t('Field')}</th>
                  <th scope="col"><Link to={`/blackbox/${encodeURIComponent(diff.entryId)}`} className="mono">{t('This entry')}</Link></th>
                  <th scope="col"><Link to={`/blackbox/${encodeURIComponent(diff.otherEntryId)}`} className="mono">{diff.sameMission ? t('Attempt {{attempt}}', { attempt: diff.otherAttempt }) : diff.otherEntryId}</Link></th>
                </tr>
              </thead>
              <tbody>
                {diff.differences.map((d) => (
                  <tr key={`${d.section}|${d.field}`} data-diff-field={d.field}>
                    <th scope="row"><span className="text-dim">{d.section}</span> <span className="mono">{d.field}</span></th>
                    <td className="blackbox-diff-new mono">{d.value ?? '-'}</td>
                    <td className="blackbox-diff-old mono">{d.otherValue ?? '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {diff.differenceCount > diff.differences.length && <div className="text-dim" style={{ fontSize: '0.8rem' }}>{t('Showing {{shown}} of {{total}} differences.', { shown: diff.differences.length, total: diff.differenceCount })}</div>}
          {diff.changedFilesKnown ? (
            <div className="blackbox-diff-files">
              <FileList title={t('Changed only in this entry')} files={diff.filesOnlyInEntry} total={diff.filesOnlyInEntryCount} />
              <FileList title={t('Changed only in the other entry')} files={diff.filesOnlyInOther} total={diff.filesOnlyInOtherCount} />
              <div><h5 style={{ margin: '0 0 0.2rem' }}>{t('Changed in both')}</h5><div>{diff.filesInBothCount}</div></div>
            </div>
          ) : <div className="text-dim" style={{ fontSize: '0.82rem', marginTop: '0.4rem' }}>{t('Changed files are not known for one of the entries.')}</div>}
        </>
      ) : null}
    </div>
  );
}
