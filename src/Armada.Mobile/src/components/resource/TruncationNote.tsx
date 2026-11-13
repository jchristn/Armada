import { AppText } from '../ui/AppText';
import { useLocale } from '../../i18n/LocaleContext';
import { resourceStyles } from './styles';

/**
 * Says so when a list holds one bounded page of a larger set, so the first page is never mistaken for the whole list.
 * Renders nothing when every row is shown.
 */
export function TruncationNote({ shown, total }: { shown: number; total: number }) {
  const { t } = useLocale();
  if (!(total > shown)) return null;
  return (
    <AppText variant="caption" muted style={resourceStyles.pad} testID="truncation-note">
      {t('Showing {{shown}} of {{total}}', { shown, total })}
    </AppText>
  );
}
