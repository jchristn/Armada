import { View } from 'react-native';
import { statusDescription } from '@dashboard/lib/statusDescriptions';
import { useLocale } from '../../i18n/LocaleContext';
import { statusTone } from '../../lib/statusTone';
import { StatusBadge } from '../ui';

/**
 * The dashboard's StatusBadge for an entity status: translated label, a tone from the status, and the dashboard's
 * tooltip (what the status means and what to do next) as the accessibility hint.
 */
export function EntityStatusBadge({ status, testID }: { status: string | null | undefined; testID?: string }) {
  const { t } = useLocale();
  const value = status ?? '';
  if (!value) return null;
  const description = statusDescription(value);
  return (
    <View testID={testID} accessible accessibilityLabel={t(value)} accessibilityHint={description ? t(description) : undefined}>
      <StatusBadge label={t(value)} tone={statusTone(value)} />
    </View>
  );
}
