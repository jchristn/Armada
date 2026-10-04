import { useId, useRef, type ReactNode } from 'react';
import { useDialog } from '../../lib/dialogA11y';
import { useLocale } from '../../context/LocaleContext';

interface DialogShellProps {
  open: boolean;
  /** Already-localized title. */
  title: ReactNode;
  /** Already-localized subtitle. */
  subtitle?: ReactNode;
  onClose: () => void;
  children: ReactNode;
  /** Footer actions; rendered in a bar that stays visible while the body scrolls. */
  footer?: ReactNode;
  /** `md` (default, 760px), `lg` (1040px) or `xl` (1240px). */
  size?: 'md' | 'lg' | 'xl';
  /** Render as a right-hand drawer instead of a centered modal. */
  variant?: 'modal' | 'drawer';
  /** When false, clicking the backdrop does not close the dialog (use while a request is in flight). */
  dismissible?: boolean;
  className?: string;
  /** Stack above other dialogs (e.g. a confirm launched from a drawer). */
  zIndex?: number;
}

/**
 * Accessible dialog frame with a fixed header (title, subtitle, close button), a scrolling body and a fixed
 * footer, so long forms never push their actions off screen. Used by the import wizard, fleet action
 * modals and the run target drawer. Escape closes it when dismissible.
 */
export default function DialogShell({
  open,
  title,
  subtitle,
  onClose,
  children,
  footer,
  size = 'md',
  variant = 'modal',
  dismissible = true,
  className,
  zIndex,
}: DialogShellProps) {
  const { t } = useLocale();
  const titleId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  // Focus trap, Escape (when dismissible), initial focus and focus return come from the shared dialog stack.
  useDialog(panelRef, open, () => closeRef.current(), dismissible);

  if (!open) return null;

  return (
    <div
      className={`dialog-shell-overlay dialog-shell-overlay-${variant}`}
      style={zIndex ? { zIndex } : undefined}
      onClick={() => { if (dismissible) onClose(); }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className={`dialog-shell dialog-shell-${variant} dialog-shell-${size}${className ? ` ${className}` : ''}`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="dialog-shell-header">
          <div className="dialog-shell-heading">
            <h3 id={titleId}>{title}</h3>
            {subtitle && <div className="dialog-shell-subtitle text-dim">{subtitle}</div>}
          </div>
          <button type="button" className="btn btn-sm dialog-shell-close" onClick={onClose} aria-label={t('Close')} title={t('Close')}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" aria-hidden="true"><path d="M18 6 6 18" /><path d="m6 6 12 12" /></svg>
          </button>
        </div>
        <div className="dialog-shell-body">{children}</div>
        {footer && <div className="dialog-shell-footer">{footer}</div>}
      </div>
    </div>
  );
}
