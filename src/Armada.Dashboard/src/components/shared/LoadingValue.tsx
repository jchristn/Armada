import type { ReactNode } from 'react';
import { useLocale } from '../../context/LocaleContext';

interface LoadingValueProps {
  /** True while the value is not known yet. */
  loading: boolean;
  /** The value, shown once it is known. */
  children?: ReactNode;
  /** Accessible label while loading; defaults to "Loading...". */
  label?: string;
}

/**
 * A value that may still be loading (a count on a card, a total, a cell): a small inline spinner until it is known,
 * so a value that has not arrived never reads as 0 or blank.
 */
export default function LoadingValue({ loading, children, label }: LoadingValueProps) {
  const { t } = useLocale();
  if (!loading) return <>{children}</>;
  const text = label ?? t('Loading...');
  return <span className="loading-value" role="status" aria-label={text} title={text}><span className="loading-value-spinner" aria-hidden="true" /></span>;
}
