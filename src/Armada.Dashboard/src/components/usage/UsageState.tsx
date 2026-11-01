import { useLocale } from '../../context/LocaleContext';
import { USAGE_KIND_HINT, USAGE_KIND_TEXT, type UsageDisplayKind } from '../../lib/usage';

interface UsageStateProps {
  kind: UsageDisplayKind;
  /** Host that needs a sign-in (AuthMissing). */
  host?: string | null;
  /** Relative age of the last good observation (Failed). */
  age?: string | null;
  /** Shown for the error state. */
  onRetry?: () => void;
  /** Compact form for table cells: label only, hint in the title. */
  compact?: boolean;
}

/** Text and next action for each distinct usage state. */
export default function UsageState({ kind, host, age, onRetry, compact = false }: UsageStateProps) {
  const { t } = useLocale();
  const hint = t(USAGE_KIND_HINT[kind], { host: host || t('the captain host'), age: age || t('an unknown time') });
  if (compact) {
    return <span className={`usage-state usage-state-${kind}`} title={hint}>{t(USAGE_KIND_TEXT[kind])}</span>;
  }
  return (
    <div className={`usage-state usage-state-${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      <strong>{t(USAGE_KIND_TEXT[kind])}</strong>
      <span className="usage-state-hint">{hint}</span>
      {kind === 'loading' && <span className="usage-skeleton" aria-hidden="true" />}
      {kind === 'error' && onRetry && (
        <button type="button" className="btn btn-sm" onClick={onRetry} title={t('Read the latest usage again.')}>{t('Retry')}</button>
      )}
    </div>
  );
}
