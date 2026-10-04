import type { ReactNode } from 'react';
import { useLocale } from '../../context/LocaleContext';
import LoadingIndicator from './LoadingIndicator';

/** Page or table level empty state: what is missing and what to do next. Strings are pre-localized. */
export function EmptyState({ title, children, actions }: { title: string; children?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="empty-state card" role="status">
      <h4 className="empty-state-title">{title}</h4>
      {children && <div className="empty-state-body text-dim">{children}</div>}
      {actions && <div className="empty-state-actions">{actions}</div>}
    </div>
  );
}

/** Error state with a retry button; callers keep their filter state so retry re-runs the same query. */
export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  const { t } = useLocale();
  return (
    <div className="alert alert-error error-state" role="alert">
      <span>{message}</span>
      {onRetry && <button type="button" className="btn btn-sm" onClick={onRetry}>{t('Retry')}</button>}
    </div>
  );
}

/** Loading state that does not shift layout much. */
export function LoadingState({ label }: { label?: string }) {
  const { t } = useLocale();
  return <LoadingIndicator label={label ?? t('Loading...')} />;
}
