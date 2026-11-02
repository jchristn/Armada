import { Link } from 'react-router-dom';
import { useLocale } from '../../context/LocaleContext';
import type { Captain, UsageSnapshotView } from '../../types/models';
import { USAGE_LEVEL_TEXT, captainCaptureActive, captainHeadline, captainUsagePath, usageLevel, viewKind } from '../../lib/usage';
import type { UsageLoadStatus } from '../../lib/useUsage';
import UsageState from './UsageState';
import { usageWindowLabel } from '../../lib/usageWindowLabel';

interface CaptainUsageCellProps {
  captain: Captain;
  views: UsageSnapshotView[] | undefined;
  status: UsageLoadStatus;
}

/** Captains table usage cell: the highest applicable window with its name and freshness, or the explicit state. */
export default function CaptainUsageCell({ captain, views, status }: CaptainUsageCellProps) {
  const { t, formatRelativeTime } = useLocale();
  if (status === 'loading' && !views) return <UsageState kind="loading" compact />;
  if (status === 'error' && !views) return <UsageState kind="error" compact />;
  const list = views ?? [];
  if (list.length === 0) return <UsageState kind={captainCaptureActive(captain) ? 'empty' : 'disabled'} compact />;
  const headline = captainHeadline(list);
  if (!headline) return <UsageState kind="empty" compact />;
  const kind = viewKind(headline.view);
  const host = headline.view.harborId ?? headline.view.hostId;
  const age = headline.view.observedUtc ? formatRelativeTime(headline.view.observedUtc) : null;
  if (!headline.window) return <UsageState kind={kind === 'ok' ? 'empty' : kind} compact host={host} age={age} />;
  const percent = headline.window.usedPercent as number;
  const level = usageLevel(percent);
  const percentText = `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(percent)}%`;
  const label = usageWindowLabel(headline.window, t);
  return (
    <Link to={captainUsagePath(captain.id)} className={`usage-cell usage-level-${level}`}
      title={t('{{window}} at {{percent}} ({{level}}), observed {{age}}. Open the captain usage section.', { window: label, percent: percentText, level: t(USAGE_LEVEL_TEXT[level]), age: age ?? '-' })}>
      <span className="usage-cell-value">{t('{{percent}} used', { percent: percentText })}</span>
      {' · '}
      <span className="usage-cell-window">{label}</span>
      {kind !== 'ok' && <UsageState kind={kind} compact host={host} age={age} />}
    </Link>
  );
}
