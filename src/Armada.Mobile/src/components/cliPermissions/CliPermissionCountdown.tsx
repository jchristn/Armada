import { formatCountdown, useCountdown } from '@dashboard/lib/cliPermissions';
import { useLocale } from '../../i18n/LocaleContext';
import { AppText } from '../ui/AppText';

/** "Expires in 4:59" for a pending CLI permission request, ticking every second; "Expiring now" at zero. */
export function CliPermissionCountdown({ expiresUtc, active }: { expiresUtc: string | null | undefined; active: boolean }) {
  const { t } = useLocale();
  const remaining = useCountdown(expiresUtc, active);
  if (!active || remaining === null || !expiresUtc) return null;
  return (
    <AppText variant="caption" color={remaining <= 60 ? 'danger' : 'textMuted'} testID="cli-permission-countdown">
      {remaining > 0 ? t('Expires in {{time}}', { time: formatCountdown(remaining) }) : t('Expiring now')}
    </AppText>
  );
}
