import { StatusBadge } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';

/**
 * A captain's capability tier (the dashboard's CaptainTierBadge): Economy, Standard, or Premium as text, so it never
 * relies on color. Nothing when the tier is automatic.
 */
export function TierBadge({ tier }: { tier?: string | null }) {
  const { t } = useLocale();
  if (!tier) return null;
  const tone = tier === 'Premium' ? 'success' : tier === 'Economy' ? 'cancelled' : 'info';
  return <StatusBadge label={t(tier)} tone={tone} />;
}
