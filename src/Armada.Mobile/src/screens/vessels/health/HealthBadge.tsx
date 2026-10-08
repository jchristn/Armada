import type { VesselHealthStatus } from '@dashboard/types/models';
import { statusLabel } from '@dashboard/lib/health/healthText';
import { StatusBadge, type StatusTone } from '../../../components/ui/StatusBadge';
import { useLocale } from '../../../i18n/LocaleContext';

/** Badge tone of a health status (the label always carries the meaning; tone only adds color). */
export function healthTone(status: VesselHealthStatus | null | undefined): StatusTone {
  switch (status) {
    case 'Pass': return 'success';
    case 'Warn': return 'warning';
    case 'Fail': return 'failed';
    case 'NotApplicable': return 'skipped';
    default: return 'pending';
  }
}

/**
 * A health status badge (the dashboard's HealthStatusBadge). `prefix` names the criterion for screen readers and
 * compact rows ("Tests: Warn"); an overridden status shows a `*` like the dashboard.
 */
export function HealthBadge({ status, prefix, overridden }: { status: VesselHealthStatus | null | undefined; prefix?: string; overridden?: boolean }) {
  const { t } = useLocale();
  const text = `${prefix ? `${prefix}: ` : ''}${statusLabel(t, status)}${overridden ? ' *' : ''}`;
  return <StatusBadge label={text} tone={healthTone(status)} />;
}
