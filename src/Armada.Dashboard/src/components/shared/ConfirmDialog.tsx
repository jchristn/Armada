import { useEffect, useId, useRef, useState } from 'react';
import { useDialog } from '../../lib/dialogA11y';
import { useLocale } from '../../context/LocaleContext';

interface ConfirmDialogProps {
  open: boolean;
  message: string;
  title?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  width?: string;
  requireDeleteConfirm?: boolean;
  resourceName?: string;
}

export default function ConfirmDialog({
  open,
  message,
  title = 'Confirm',
  confirmLabel = 'Yes',
  cancelLabel = 'Cancel',
  danger = false,
  onConfirm,
  onCancel,
  width,
  requireDeleteConfirm = false,
  resourceName,
}: ConfirmDialogProps) {
  const { t } = useLocale();
  const [confirmationText, setConfirmationText] = useState('');
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const messageId = useId();
  useDialog(panelRef, open, onCancel);

  useEffect(() => {
    if (open) setConfirmationText('');
  }, [open]);

  if (!open) return null;

  return (
    <div className="modal-overlay" style={{ zIndex: 1500 }} onClick={onCancel}>
      <div
        ref={panelRef}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={messageId}
        tabIndex={-1}
        className="modal-box"
        style={width ? { maxWidth: width } : undefined}
        onClick={e => e.stopPropagation()}
      >
        <h3 id={titleId} style={{ marginTop: 0, marginBottom: '1rem' }}>{t(title)}</h3>
        <p id={messageId} style={{ fontSize: '0.9rem', marginBottom: '1.25rem', color: 'var(--text)' }}>
          {message}
        </p>
        {requireDeleteConfirm && (
          <>
            <p style={{ fontSize: '0.9rem', marginBottom: '0.5rem', color: 'var(--text-dim)' }}>
              {resourceName
                ? t('Are you sure you wish to delete: {{resourceName}}', { resourceName })
                : t('Are you sure you wish to delete this resource?')}
            </p>
            <p style={{ fontSize: '0.9rem', marginBottom: '0.75rem', color: 'var(--text-dim)' }}>
              {t('Type `delete` into the confirmation box to continue.')}
            </p>
            <input
              autoFocus
              value={confirmationText}
              onChange={e => setConfirmationText(e.target.value)}
              placeholder={t('delete')}
              aria-label={t('Type `delete` into the confirmation box to continue.')}
            />
          </>
        )}
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onCancel}>
            {t(cancelLabel)}
          </button>
          <button
            type="button"
            className={`btn ${danger ? 'btn-danger' : 'btn-primary'}`}
            onClick={onConfirm}
            disabled={requireDeleteConfirm && confirmationText !== 'delete'}
          >
            {t(confirmLabel)}
          </button>
        </div>
      </div>
    </div>
  );
}
