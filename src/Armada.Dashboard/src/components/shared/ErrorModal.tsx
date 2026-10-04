import { useId, useRef } from 'react';
import { useDialog } from '../../lib/dialogA11y';
import { useLocale } from '../../context/LocaleContext';

interface ErrorModalProps {
  error: string;
  onClose: () => void;
}

export default function ErrorModal({ error, onClose }: ErrorModalProps) {
  const { t } = useLocale();

  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const messageId = useId();
  useDialog(panelRef, !!error, onClose);

  if (!error) return null;

  return (
    <div className="modal-overlay" style={{ zIndex: 1500 }} onClick={onClose}>
      <div ref={panelRef} role="alertdialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={messageId} tabIndex={-1} className="modal error-modal" onClick={e => e.stopPropagation()}>
        <div className="error-modal-header">
          <span className="error-modal-icon" aria-hidden="true">!</span>
          <h3 id={titleId}>{t('Error')}</h3>
        </div>
        <p id={messageId} className="error-modal-message">{t(error)}</p>
        <div className="modal-actions">
          <button type="button" className="btn btn-primary" onClick={onClose}>{t('Dismiss')}</button>
        </div>
      </div>
    </div>
  );
}
