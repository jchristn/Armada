import { useState, type FormEvent } from 'react';
import { changePassword } from '../api/client';
import ConfirmDialog from './shared/ConfirmDialog';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';

/**
 * Shown instead of the dashboard while the signed-in account (the seeded admin@armada) still uses the default
 * password. The Admiral reports this through PasswordChangeRequired (advisory on the server; see
 * docs/SECURITY_REVIEW.md). The user can change the password here or skip after confirming the risk; the default
 * credentials banner stays visible until the password changes.
 */
export default function PasswordChangeRequired() {
  const { refresh, logout, skipPasswordChange } = useAuth();
  const { t } = useLocale();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [confirmSkip, setConfirmSkip] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    if (next.length < 8) {
      setError(t('The new password must be at least 8 characters.'));
      return;
    }
    if (next !== confirm) {
      setError(t('The new passwords do not match.'));
      return;
    }
    if (next === 'password' || next === current) {
      setError(t('Choose a password different from the default and the current one.'));
      return;
    }
    setBusy(true);
    try {
      await changePassword({ CurrentPassword: current, NewPassword: next });
      await refresh();
    } catch {
      setError(t('Password change failed. Check the current password and try again.'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login-container">
      <div className="login-card">
        <h1>{t('Change the default password')}</h1>
        <p className="password-change-explainer">
          {t('This account still uses the default password. Choose a new one, or skip for now. Changing it also disables the default bearer token.')}
        </p>
        {error && <div className="login-error" role="alert">{error}</div>}
        <form onSubmit={handleSubmit}>
          <label>
            {t('Current password')}
            <input type="password" autoComplete="current-password" value={current} onChange={e => setCurrent(e.target.value)} required />
          </label>
          <label>
            {t('New password')}
            <input type="password" autoComplete="new-password" value={next} onChange={e => setNext(e.target.value)} required minLength={8} />
          </label>
          <label>
            {t('Confirm new password')}
            <input type="password" autoComplete="new-password" value={confirm} onChange={e => setConfirm(e.target.value)} required minLength={8} />
          </label>
          <button type="submit" disabled={busy}>{busy ? t('Saving...') : t('Change password')}</button>
        </form>
        <div className="login-actions">
          <button type="button" className="link-btn" onClick={() => setConfirmSkip(true)}>{t('Skip for now')}</button>
          <button type="button" className="link-btn" onClick={logout}>{t('Sign out')}</button>
        </div>
        <ConfirmDialog
          open={confirmSkip}
          title={t('Keep the default password?')}
          message={t('Anyone who can reach this server can sign in as admin@armada with the well-known default password, and the default bearer token stays active. Only skip on a server that is reachable from this machine alone (localhost). You can change it later by editing your own account on the Users page.')}
          confirmLabel={t('Skip and continue')}
          cancelLabel={t('Cancel')}
          danger
          onConfirm={() => { setConfirmSkip(false); skipPasswordChange(); }}
          onCancel={() => setConfirmSkip(false)}
        />
      </div>
    </div>
  );
}
