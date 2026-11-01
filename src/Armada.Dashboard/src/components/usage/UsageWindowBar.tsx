import { useLocale } from '../../context/LocaleContext';
import type { UsageWindow } from '../../types/models';
import { USAGE_LEVEL_TEXT, usageLevel } from '../../lib/usage';

interface UsageWindowBarProps {
  window: UsageWindow;
  /** The observation is stale or failed: the bar keeps its value but is not presented as fresh. */
  stale?: boolean;
}

/** One limit window: label, percentage text, level text, progress bar and exact reset with countdown. */
export default function UsageWindowBar({ window, stale = false }: UsageWindowBarProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const known = window.usedPercent !== null && window.usedPercent !== undefined && !Number.isNaN(window.usedPercent);
  const percent = known ? Math.max(0, Math.min(100, window.usedPercent as number)) : 0;
  const level = known ? usageLevel(window.usedPercent as number) : 'normal';
  const label = window.model ? `${window.name} (${window.model})` : window.name;
  const percentText = known ? `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(window.usedPercent as number)}%` : t('Unknown');
  return (
    <div className={`usage-window${stale ? ' usage-window-stale' : ''}`}>
      <div className="usage-window-head">
        <span className="usage-window-name">{label}</span>
        <span className={`usage-window-value usage-level-${known ? level : 'unknown'}`}>
          {percentText}
          {known && <span className="usage-window-level"> {t(USAGE_LEVEL_TEXT[level])}</span>}
          {stale && <span className="usage-window-level"> {t('Stale')}</span>}
        </span>
      </div>
      {known ? (
        <div
          className={`usage-bar usage-bar-${level}${stale ? ' usage-bar-stale' : ''}`}
          role="progressbar"
          aria-label={t('{{window}} usage', { window: label })}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(percent)}
          aria-valuetext={`${percentText} ${t(USAGE_LEVEL_TEXT[level])}`}
        >
          <span className="usage-bar-fill" style={{ width: `${percent}%` }} />
        </div>
      ) : (
        <div className="usage-bar usage-bar-unknown" aria-hidden="true" />
      )}
      <div className="usage-window-meta text-dim">
        {window.resetsUtc
          ? t('Resets {{time}} ({{countdown}})', { time: formatDateTime(window.resetsUtc), countdown: formatRelativeTime(window.resetsUtc) })
          : t('Reset time not reported')}
        {window.coverageNote && <span> - {window.coverageNote}</span>}
      </div>
    </div>
  );
}
