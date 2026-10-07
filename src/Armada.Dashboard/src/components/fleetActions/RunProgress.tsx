import type { FleetActionRun } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { runProgress } from '../../lib/fleetActionForm';

interface RunProgressProps {
  run: Pick<FleetActionRun, 'targetCount' | 'succeededCount' | 'failedCount' | 'skippedCount' | 'cancelledCount'>;
  /** Compact single-line variant for table cells. */
  compact?: boolean;
}

/**
 * Segmented progress bar (succeeded / failed / skipped / cancelled of total) with a text summary, so the
 * numbers are readable without relying on the bar colors.
 */
export default function RunProgress({ run, compact = false }: RunProgressProps) {
  const { t } = useLocale();
  const { total, done, percent } = runProgress(run);
  const pct = (n: number) => (total > 0 ? `${(n / total) * 100}%` : '0%');
  const summary = t('{{succeeded}} succeeded, {{failed}} failed, {{skipped}} skipped of {{total}}', {
    succeeded: run.succeededCount.toLocaleString(),
    failed: run.failedCount.toLocaleString(),
    skipped: run.skippedCount.toLocaleString(),
    total: total.toLocaleString(),
  });

  return (
    <div className={`run-progress${compact ? ' run-progress-compact' : ''}`}>
      <div
        className="run-progress-bar"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={total}
        aria-valuenow={done}
        aria-label={t('Targets finished')}
        aria-valuetext={summary}
      >
        <span className="run-progress-seg run-progress-succeeded" style={{ width: pct(run.succeededCount) }} />
        <span className="run-progress-seg run-progress-failed" style={{ width: pct(run.failedCount) }} />
        <span className="run-progress-seg run-progress-skipped" style={{ width: pct(run.skippedCount) }} />
        <span className="run-progress-seg run-progress-cancelled" style={{ width: pct(run.cancelledCount) }} />
      </div>
      <div className="run-progress-text text-dim">
        {summary}
        {!compact && <span> ({t('{{percent}}% finished', { percent: percent.toLocaleString() })})</span>}
        {run.cancelledCount > 0 && <span>, {t('{{count}} cancelled', { count: run.cancelledCount.toLocaleString() })}</span>}
      </div>
    </div>
  );
}
