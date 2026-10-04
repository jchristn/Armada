import { useLocale } from '../../context/LocaleContext';
import { localeOptionLabel } from '../../i18n/runtime';

interface LanguageSelectorProps {
  className?: string;
  compact?: boolean;
  showLabel?: boolean;
}

export default function LanguageSelector({
  className = '',
  compact = false,
  showLabel = false,
}: LanguageSelectorProps) {
  const { locale, setLocale, supportedLocales, t } = useLocale();
  const wrapClassName = [
    'locale-select-wrap',
    compact ? 'compact' : '',
    showLabel ? 'with-label' : '',
    className,
  ].filter(Boolean).join(' ');

  return (
    <label className={wrapClassName} title={t('Language')}>
      {showLabel && <span className="locale-select-label">{t('Language')}</span>}
      <select
        className="locale-select"
        value={locale}
        onChange={(e) => setLocale(e.target.value)}
        aria-label={t('Language')}
        title={t('Language')}
      >
        {supportedLocales.map((item) => (
          <option key={item.code} value={item.code}>
            {localeOptionLabel(item)}
          </option>
        ))}
      </select>
    </label>
  );
}
