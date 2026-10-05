import { useLocale } from '../../context/LocaleContext';
import { formatCountdown, useCountdown } from '../../lib/cliPermissions';

interface CliPermissionCountdownProps {
  expiresUtc: string | null | undefined;
  /** Tick only while the request is pending. */
  active: boolean;
}

/** "Expires in 4:59" for a pending CLI permission request, ticking every second; "Expiring now" at zero. */
export default function CliPermissionCountdown({ expiresUtc, active }: CliPermissionCountdownProps) {
  const { t, formatDateTime } = useLocale();
  const remaining = useCountdown(expiresUtc, active);
  if (!active || remaining === null || !expiresUtc) return null;
  return (
    <span className={`cli-perm-countdown${remaining <= 60 ? ' is-urgent' : ''}`} title={formatDateTime(expiresUtc)}>
      {remaining > 0 ? t('Expires in {{time}}', { time: formatCountdown(remaining) }) : t('Expiring now')}
    </span>
  );
}
