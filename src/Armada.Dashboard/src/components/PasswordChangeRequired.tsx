import { useState, type FormEvent } from 'react';
import { changePassword } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';

/**
 * Shown instead of the dashboard while the signed-in account (the seeded admin@armada) still uses the default
 * password. The Admiral reports this through PasswordChangeRequired; the dashboard and TUI hold the session here until
 * the password is changed (the flag is advisory on the server; see docs/SECURITY_REVIEW.md).
 */
export default function PasswordChangeRequired() {
  const { refresh, logout } = useAuth();
  const { t } = useLocale();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

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
          {t('This account still uses the default password. Choose a new one to continue. Changing it also disables the default bearer token.')}
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
          <button type="button" className="link-btn" onClick={logout}>{t('Sign out')}</button>
        </div>
      </div>
    </div>
  );
}
