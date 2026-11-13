import { useEffect, useId, useRef } from 'react';
import { useDialog } from '../../lib/dialogA11y';
import { useLocale } from '../../context/LocaleContext';

interface DeleteProgressDialogProps {
  /** True while a delete is in flight or has just failed. */
  open: boolean;
  /** Already-localized title, for example "Deleting vessel" or "Deleting 12 missions". */
  title: string;
  /** Already-localized progress message shown while the delete is pending. */
  message: string;
  /** Empty while the delete is pending; the already-localized failure text once it failed. */
  error: string;
  onRetry: () => void;
  onDismiss: () => void;
}

/**
 * Modal shown from the moment a delete starts until the server answers. Deletes can cascade through a lot of
 * related data, so this says the delete may take some time. While pending it cannot be dismissed (no buttons,
 * Escape and backdrop clicks are ignored) so nothing else can be submitted; on failure it turns into an error with
 * Dismiss and Retry so the user is never left in the progress state. Driven by {@link useDeleteProgress}.
 */
export default function DeleteProgressDialog({ open, title, message, error, onRetry, onDismiss }: DeleteProgressDialogProps) {
  const { t } = useLocale();
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const messageId = useId();
  const failed = !!error;
  const dismissRef = useRef<HTMLButtonElement>(null);
  useDialog(panelRef, open, onDismiss, failed);

  // The panel took focus while pending; move it to Dismiss when the failure actions appear.
  useEffect(() => {
    if (open && failed) dismissRef.current?.focus();
  }, [open, failed]);

  if (!open) return null;

  return (
    <div className="modal-overlay" style={{ zIndex: 1600 }} onClick={() => { if (failed) onDismiss(); }}>
      <div
        ref={panelRef}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={messageId}
        aria-busy={!failed}
        tabIndex={-1}
        className={failed ? 'modal error-modal delete-progress' : 'modal-box delete-progress'}
        onClick={(e) => e.stopPropagation()}
      >
        {failed ? (
          <>
            <div className="error-modal-header">
              <span className="error-modal-icon" aria-hidden="true">!</span>
              <h3 id={titleId}>{t('Error')}</h3>
            </div>
            <p id={messageId} className="error-modal-message">{error}</p>
            <div className="modal-actions">
              <button ref={dismissRef} type="button" className="btn" onClick={onDismiss} title={t('Close this message. Nothing is retried; fix the problem and try the action again.')}>{t('Dismiss')}</button>
              <button type="button" className="btn btn-danger" onClick={onRetry} title={t('Try the delete again.')}>{t('Retry')}</button>
            </div>
          </>
        ) : (
          <>
            <h3 id={titleId} style={{ marginTop: 0, marginBottom: '1rem' }}>{title}</h3>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.85rem' }}>
              <span className="loading-indicator-spinner" aria-hidden="true" style={{ flexShrink: 0 }} />
              <p id={messageId} role="status" aria-live="polite" style={{ fontSize: '0.9rem', margin: 0, color: 'var(--text)' }}>
                {message}
              </p>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
