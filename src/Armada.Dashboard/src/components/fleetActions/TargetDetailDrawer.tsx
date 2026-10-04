import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getFleetActionRunTarget } from '../../api/client';
import type { FleetActionRunTarget } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import DialogShell from '../shared/DialogShell';
import CodeStatusBadge from '../shared/CodeStatusBadge';
import LogViewer from '../shared/LogViewer';
import CopyButton from '../shared/CopyButton';
import { ErrorState, LoadingState } from '../shared/StateBlocks';
import { formatDurationMs, reasonLabel, targetStatusBadge } from '../../lib/fleetActionLabels';
import { formatBytes } from '../../lib/format';

interface TargetDetailDrawerProps {
  runId: string;
  targetId: string | null;
  /** Already-localized maximum output size note, e.g. "64 KB". */
  onClose: () => void;
}

const PREVIEW_LINES = 30;

function tail(text: string, lines: number): string {
  const all = text.split(/\r?\n/);
  return all.length <= lines ? text : all.slice(all.length - lines).join('\n');
}

/**
 * Drawer with one Command target's detail: rendered command, exit code, and stdout/stderr. This is the only
 * place captured output is fetched (the target-detail endpoint), because output may contain secrets.
 */
export default function TargetDetailDrawer({ runId, targetId, onClose }: TargetDetailDrawerProps) {
  const { t, locale, formatDateTime } = useLocale();
  const [target, setTarget] = useState<FleetActionRunTarget | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [viewer, setViewer] = useState<'stdout' | 'stderr' | null>(null);

  const load = useCallback(async () => {
    if (!targetId) return;
    setLoading(true);
    setError('');
    try {
      setTarget(await getFleetActionRunTarget(runId, targetId));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load the target.'));
    } finally {
      setLoading(false);
    }
  }, [runId, targetId, t]);

  useEffect(() => {
    setTarget(null);
    setViewer(null);
    void load();
  }, [load]);

  const open = targetId !== null;
  const badge = target ? targetStatusBadge(t, target.status) : null;
  const reason = target ? reasonLabel(t, target.skipReason, target.failureReason) : '';
  const finished = target ? !['Pending', 'Running'].includes(target.status) : false;

  return (
    <>
      <DialogShell
        open={open}
        onClose={onClose}
        dismissible={viewer === null}
        variant="drawer"
        title={target ? target.vesselName : t('Target')}
        subtitle={target ? <span className="mono">{target.id}</span> : undefined}
        footer={(
          <>
            <button type="button" className="btn btn-sm" onClick={() => void load()} disabled={loading}>{t('Refresh')}</button>
            <button type="button" className="btn btn-sm" onClick={onClose}>{t('Close')}</button>
          </>
        )}
      >
        {error && <ErrorState message={error} onRetry={() => void load()} />}
        {loading && !target && <LoadingState />}
        {target && badge && (
          <div className="target-detail">
            <dl className="detail-kv">
              <dt>{t('Status')}</dt>
              <dd><CodeStatusBadge {...badge} /></dd>
              {reason && (
                <>
                  <dt>{t('Reason')}</dt>
                  <dd title={target.skipReason ?? target.failureReason ?? ''}>{reason} <span className="text-dim mono">({target.skipReason ?? target.failureReason})</span></dd>
                </>
              )}
              <dt>{t('Vessel')}</dt>
              <dd><Link to={`/vessels/${target.vesselId}`}>{target.vesselName}</Link></dd>
              <dt>{t('Exit code')}</dt>
              <dd className="mono">{target.exitCode ?? '-'}</dd>
              <dt>{t('Duration')}</dt>
              <dd className="mono">{formatDurationMs(t, locale, target.durationMs)}</dd>
              <dt>{t('Started')}</dt>
              <dd>{target.startedUtc ? formatDateTime(target.startedUtc) : '-'}</dd>
              <dt>{t('Completed')}</dt>
              <dd>{target.completedUtc ? formatDateTime(target.completedUtc) : '-'}</dd>
              {target.voyageId && (
                <>
                  <dt>{t('Voyage')}</dt>
                  <dd><Link to={`/voyages/${target.voyageId}`} className="mono">{target.voyageId}</Link></dd>
                </>
              )}
            </dl>

            {target.outputTruncated && (
              <div className="alert alert-warning" role="note">
                {t('Output was truncated. Only the end of each stream was kept (FleetActions.MaxOutputBytes).')}
              </div>
            )}

            <section className="target-detail-section">
              <div className="target-detail-section-head">
                <h4>{t('Rendered command')}</h4>
                {target.renderedText && <CopyButton text={target.renderedText} title={t('Copy command')} />}
              </div>
              <pre className="code-block" data-i18n-skip="true">{target.renderedText || t('(not rendered)')}</pre>
            </section>

            <section className="target-detail-section">
              <div className="target-detail-section-head">
                <h4>{t('Standard output')} <span className="text-dim">({formatBytes((target.outputText ?? '').length)})</span></h4>
                <button type="button" className="btn btn-sm" onClick={() => setViewer('stdout')} disabled={!target.outputText}>{t('Open full output')}</button>
              </div>
              {target.outputText
                ? <pre className="code-block code-block-scroll" data-i18n-skip="true">{tail(target.outputText, PREVIEW_LINES)}</pre>
                : <p className="text-dim">{t('No output captured.')}</p>}
            </section>

            <section className="target-detail-section">
              <div className="target-detail-section-head">
                <h4>{t('Standard error')} <span className="text-dim">({formatBytes((target.errorText ?? '').length)})</span></h4>
                <button type="button" className="btn btn-sm" onClick={() => setViewer('stderr')} disabled={!target.errorText}>{t('Open full error output')}</button>
              </div>
              {target.errorText
                ? <pre className="code-block code-block-scroll code-block-error" data-i18n-skip="true">{tail(target.errorText, PREVIEW_LINES)}</pre>
                : <p className="text-dim">{t('No error output captured.')}</p>}
            </section>
          </div>
        )}
      </DialogShell>
      <LogViewer
        open={viewer !== null && target !== null}
        title={target ? t('{{vessel}}: {{stream}}', { vessel: target.vesselName, stream: viewer === 'stderr' ? t('Standard error') : t('Standard output') }) : ''}
        content={target ? (viewer === 'stderr' ? target.errorText ?? '' : target.outputText ?? '') : ''}
        completed={finished}
        onClose={() => setViewer(null)}
      />
    </>
  );
}
