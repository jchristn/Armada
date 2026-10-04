import { useState, type FormEvent } from 'react';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useTheme } from '../context/ThemeContext';
import { lookupTenants, authenticate, ApiError } from '../api/client';
import type { TenantListEntry } from '../types/models';
import LanguageSelector from './shared/LanguageSelector';

type Step = 'email' | 'tenant' | 'password';
type LoginMode = 'email' | 'apikey';
type RevealField = 'password' | 'apikey';

export default function LoginFlow() {
  const { login } = useAuth();
  const { t } = useLocale();
  const { darkMode, toggleTheme } = useTheme();
  const [mode, setMode] = useState<LoginMode>('email');
  const [step, setStep] = useState<Step>('email');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [tenants, setTenants] = useState<TenantListEntry[]>([]);
  const [selectedTenant, setSelectedTenant] = useState<TenantListEntry | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [revealedField, setRevealedField] = useState<RevealField | null>(null);

  function beginReveal(field: RevealField) {
    setRevealedField(field);
  }

  function endReveal() {
    setRevealedField(null);
  }

  async function handleEmailSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      const result = await lookupTenants(email);
      if (result.tenants.length === 0) {
        setError(t('No tenants found for this email.'));
      } else if (result.tenants.length === 1) {
        setSelectedTenant(result.tenants[0]);
        setStep('password');
      } else {
        setTenants(result.tenants);
        setStep('tenant');
      }
    } catch {
      setError(t('Failed to look up tenants.'));
    } finally {
      setBusy(false);
    }
  }

  function handleTenantSelect(tenantId: string) {
    const tenant = tenants.find(t => t.id === tenantId) ?? null;
    setSelectedTenant(tenant);
  }

  function handleTenantContinue() {
    if (!selectedTenant) {
      setError(t('Please select a tenant.'));
      return;
    }
    setError('');
    setStep('password');
  }

  async function handlePasswordSubmit(e: FormEvent) {
    e.preventDefault();
    if (!selectedTenant) return;
    setError('');
    setBusy(true);
    try {
      const result = await authenticate({ email, password, tenantId: selectedTenant.id });
      if (!result.success || !result.token) throw new Error(t('Authentication failed.'));
      await login(result.token);
    } catch (err) {
      setError(err instanceof ApiError && err.status === 429
        ? t('Too many failed sign-in attempts. Wait a few minutes and try again.')
        : t('Authentication failed.'));
    } finally {
      setBusy(false);
    }
  }

  async function handleApiKeySubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      await login(apiKey);
    } catch {
      setError(t('API key authentication failed.'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login-container">
      <div className="login-card">
        <img src={darkMode ? '/img/logo-light-grey.png' : '/img/logo-dark-grey.png'} alt="Armada" className="login-logo" onError={(e) => { (e.target as HTMLImageElement).style.display = 'none'; }} />
        <h1>Armada</h1>
        <LanguageSelector className="login-language-select" showLabel />

        <div className="login-mode-tabs">
          <button
            className={`login-mode-tab${mode === 'email' ? ' active' : ''}`}
            onClick={() => { setMode('email'); setError(''); }}
            type="button"
          >
            {t('Email Login')}
          </button>
          <button
            className={`login-mode-tab${mode === 'apikey' ? ' active' : ''}`}
            onClick={() => { setMode('apikey'); setError(''); }}
            type="button"
          >
            {t('API Key Login')}
          </button>
        </div>

        {error && <div className="login-error">{error}</div>}

        {mode === 'apikey' && (
          <form onSubmit={handleApiKeySubmit}>
            <label htmlFor="apikey">{t('API Key / Bearer Token')}</label>
            <div className="login-secret-field">
              <input
                id="apikey"
                type={revealedField === 'apikey' ? 'text' : 'password'}
                value={apiKey}
                onChange={e => setApiKey(e.target.value)}
                placeholder={t('Paste your API key')}
                required
                autoFocus
              />
              <button
                type="button"
                className="login-secret-toggle"
                aria-label={revealedField === 'apikey' ? t('Hide API key') : t('Show API key')}
                title={revealedField === 'apikey' ? t('Hide API key') : t('Show API key')}
                onPointerDown={(e) => { e.preventDefault(); beginReveal('apikey'); }}
                onPointerUp={endReveal}
                onPointerLeave={endReveal}
                onPointerCancel={endReveal}
                onBlur={endReveal}
              >
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6-10-6-10-6z" />
                  <circle cx="12" cy="12" r="3" />
                </svg>
              </button>
            </div>
            <button type="submit" disabled={busy}>{busy ? t('Connecting...') : t('Connect')}</button>
          </form>
        )}

        {mode === 'email' && step === 'email' && (
          <form onSubmit={handleEmailSubmit}>
            <label htmlFor="email">{t('Email')}</label>
            <input id="email" type="email" value={email} onChange={e => setEmail(e.target.value)} placeholder={t('you@company.com')} required autoFocus />
            <button type="submit" disabled={busy}>{busy ? t('Looking up...') : t('Continue')}</button>
          </form>
        )}

        {mode === 'email' && step === 'tenant' && (
          <form onSubmit={(e) => { e.preventDefault(); handleTenantContinue(); }}>
            <label htmlFor="tenant-select">{t('Tenant')}</label>
            <select
              className="login-select"
              id="tenant-select"
              value={selectedTenant?.id ?? ''}
              onChange={e => handleTenantSelect(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter' && selectedTenant) { e.preventDefault(); handleTenantContinue(); } }}
              required
              autoFocus
            >
              <option value="">{t('Select a tenant...')}</option>
              {tenants.map(t => (
                <option key={t.id} value={t.id}>{t.name}</option>
              ))}
            </select>
            <div className="login-actions">
              <button type="submit" disabled={!selectedTenant || busy}>
                {t('Continue')}
              </button>
              <button type="button" className="link-btn" onClick={() => { setSelectedTenant(null); setStep('email'); }}>
                {t('Back')}
              </button>
            </div>
          </form>
        )}

        {mode === 'email' && step === 'password' && (
          <form onSubmit={handlePasswordSubmit}>
            <div className="login-context">
              {t('Signing in as {{email}} to {{tenant}}', { email, tenant: selectedTenant?.name ?? '' })}
            </div>
            <label htmlFor="password">{t('Password')}</label>
            <div className="login-secret-field">
              <input
                id="password"
                type={revealedField === 'password' ? 'text' : 'password'}
                value={password}
                onChange={e => setPassword(e.target.value)}
                placeholder={t('Password')}
                required
                autoFocus
              />
              <button
                type="button"
                className="login-secret-toggle"
                aria-label={revealedField === 'password' ? t('Hide password') : t('Show password')}
                title={revealedField === 'password' ? t('Hide password') : t('Show password')}
                onPointerDown={(e) => { e.preventDefault(); beginReveal('password'); }}
                onPointerUp={endReveal}
                onPointerLeave={endReveal}
                onPointerCancel={endReveal}
                onBlur={endReveal}
              >
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6-10-6-10-6z" />
                  <circle cx="12" cy="12" r="3" />
                </svg>
              </button>
            </div>
            <button type="submit" disabled={busy}>{busy ? t('Signing in...') : t('Sign In')}</button>
            <button type="button" className="link-btn" onClick={() => { setStep('email'); setPassword(''); }}>{t('Back')}</button>
          </form>
        )}
        <div className="login-footer">
          <a href="https://github.com/jchristn/armada" target="_blank" rel="noopener noreferrer" className="github-link" title={t('GitHub')}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0 0 24 12c0-6.63-5.37-12-12-12z"/></svg>
          </a>
          <span style={{ fontSize: '0.75em', color: 'var(--text-dim)', opacity: 0.6 }}>v{__APP_VERSION__}</span>
          <button className="login-theme-toggle" onClick={toggleTheme} title={darkMode ? t('Switch to light mode') : t('Switch to dark mode')}>
            {darkMode ? '\u2600' : '\u263E'}
          </button>
        </div>
      </div>
      <p className="login-default-creds">
        {t('Default credentials')}: <code>admin@armada</code> / <code>password</code>
      </p>
    </div>
  );
}
