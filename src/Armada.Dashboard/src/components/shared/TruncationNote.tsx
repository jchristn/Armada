import { useLocale } from '../../context/LocaleContext';

interface TruncationNoteProps {
  /** Rows this view holds (one bounded page). */
  shown: number;
  /** The server's total for the same query. */
  total: number;
  className?: string;
}

/**
 * Says so when a view holds one bounded page of a larger set, so the first page is never mistaken for the whole list.
 * Renders nothing when every row is shown.
 */
export default function TruncationNote({ shown, total, className }: TruncationNoteProps) {
  const { t } = useLocale();
  if (!(total > shown)) return null;
  return (
    <p className={className ?? 'text-dim'} data-testid="truncation-note" role="note">
      {t('Showing {{shown}} of {{total}}', { shown, total })}
    </p>
  );
}
