import { useState } from 'react';
import DialogShell from './DialogShell';
import { copyToClipboard } from './CopyButton';
import { useLocale } from '../../context/LocaleContext';

interface GeneratedPasswordDialogProps {
  open: boolean;
  /** Already-localized title. */
  title: string;
  /** Already-localized explanation. */
  message: string;
  /** Account the password belongs to. */
  email: string;
  /** The generated password. Held by the caller only while the dialog is open; never persisted. */
  password: string;
  onClose: () => void;
}

/**
 * One-time display of a server-generated password (for example the seeded admin of a new tenant), with a copy
 * button. The password lives only in the caller's component state while this dialog is open; it is never written
 * to browser storage. Backdrop clicks do not close it, so the password is not lost by a stray click.
 */
export default function GeneratedPasswordDialog({ open, title, message, email, password, onClose }: GeneratedPasswordDialogProps) {
  const { t } = useLocale();
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle');

  function close() {
    setCopyState('idle');
    onClose();
  }

  function copy() {
    copyToClipboard(password)
      .then(() => setCopyState('copied'))
      .catch(() => setCopyState('failed'));
  }

  return (
    <DialogShell
      open={open}
      title={title}
      onClose={close}
      dismissible={false}
      footer={<button type="button" className="btn btn-primary" onClick={close}>{t('I have saved it')}</button>}
    >
      <div className="alert alert-warning" role="note">{message}</div>
      <label>
        {t('Admin email')}
        <input type="text" value={email} readOnly aria-label={t('Admin email')} />
      </label>
      <label>
        {t('Password')}
        <span style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
          <input
            type="text"
            className="mono"
            value={password}
            readOnly
            aria-label={t('Generated password')}
            autoComplete="off"
            spellCheck={false}
            onFocus={e => e.currentTarget.select()}
          />
          <button type="button" className="btn btn-sm" onClick={copy}>{copyState === 'copied' ? t('Copied!') : t('Copy password')}</button>
        </span>
      </label>
      {copyState === 'failed' && (
        <p className="text-dim" role="status">{t('Copy failed. Select the password and copy it manually.')}</p>
      )}
    </DialogShell>
  );
}
