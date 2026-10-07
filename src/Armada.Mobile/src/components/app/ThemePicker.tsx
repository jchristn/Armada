import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import type { ThemePreference } from '../../theme/palette';
import { SegmentedControl } from '../ui';

/** Theme choice: follow the device, light, dark, or high contrast. */
export function ThemePicker() {
  const { preference, setPreference } = useTheme();
  const { t } = useLocale();
  const options: { value: ThemePreference; label: string; testID: string }[] = [
    { value: 'system', label: t('System'), testID: 'theme-system' },
    { value: 'light', label: t('Light'), testID: 'theme-light' },
    { value: 'dark', label: t('Dark'), testID: 'theme-dark' },
    { value: 'highContrast', label: t('High contrast'), testID: 'theme-highContrast' },
  ];
  return <SegmentedControl label={t('Theme')} options={options} value={preference} onChange={setPreference} />;
}
