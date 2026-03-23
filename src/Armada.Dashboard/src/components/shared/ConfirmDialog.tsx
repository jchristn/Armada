import { useEffect, useState } from 'react';

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
  const [confirmationText, setConfirmationText] = useState('');

  useEffect(() => {
    if (open) setConfirmationText('');
  }, [open]);

  if (!open) return null;

  return (
    <div className="modal-overlay" style={{ zIndex: 1500 }} onClick={onCancel}>
      <div
        className="modal-box"
        style={width ? { maxWidth: width } : undefined}
        onClick={e => e.stopPropagation()}
      >
        <h3 style={{ marginTop: 0, marginBottom: '1rem' }}>{title}</h3>
        <p style={{ fontSize: '0.9rem', marginBottom: '1.25rem', color: 'var(--text)' }}>
          {message}
        </p>
        {requireDeleteConfirm && (
          <>
            <p style={{ fontSize: '0.9rem', marginBottom: '0.5rem', color: 'var(--text-dim)' }}>
              {resourceName
                ? `Are you sure you wish to delete: ${resourceName}`
                : 'Are you sure you wish to delete this resource?'}
            </p>
            <p style={{ fontSize: '0.9rem', marginBottom: '0.75rem', color: 'var(--text-dim)' }}>
              Type `delete` into the confirmation box to continue.
            </p>
            <input
              autoFocus
              value={confirmationText}
              onChange={e => setConfirmationText(e.target.value)}
              placeholder="delete"
            />
          </>
        )}
        <div className="modal-actions">
          <button className="btn" onClick={onCancel}>
            {cancelLabel}
          </button>
          <button
            className={`btn ${danger ? 'btn-danger' : 'btn-primary'}`}
            onClick={onConfirm}
            disabled={requireDeleteConfirm && confirmationText !== 'delete'}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
