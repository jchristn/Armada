import { useState } from 'react';
import { Image, StyleSheet, View } from 'react-native';
import { ApiError, authenticate, lookupTenants } from '@dashboard/api/client';
import type { TenantListEntry } from '@dashboard/types/models';
import { useAuth } from '../auth/AuthContext';
import { InsecureUrlWarning } from '../components/app/InsecureUrlWarning';
import { LocalePicker } from '../components/app/LocalePicker';
import { ProfileForm } from '../components/app/ProfileForm';
import { AppText, BottomSheet, Button, Icon, ListRow, Screen, Section, SegmentedControl, TextField } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { spacing } from '../theme/typography';

type Step = 'email' | 'tenant' | 'password';
type Mode = 'email' | 'apikey';

/**
 * Sign-in, mirroring the dashboard's LoginFlow: email, then tenant lookup (skipped when there is one tenant), then
 * password; or an API key / bearer token. Adds the server profile choice (which Admiral to sign in to).
 */
export function SignInScreen() {
  const { activeProfile, profiles, login, saveProfile, selectProfile } = useAuth();
  const { t } = useLocale();
  const [mode, setMode] = useState<Mode>(activeProfile?.signInMethod === 'token' ? 'apikey' : 'email');
  const [step, setStep] = useState<Step>('email');
  const [email, setEmail] = useState(activeProfile?.lastEmail ?? '');
  const [password, setPassword] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [tenants, setTenants] = useState<TenantListEntry[]>([]);
  const [tenant, setTenant] = useState<TenantListEntry | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [serversOpen, setServersOpen] = useState(false);
  const [addingServer, setAddingServer] = useState(false);

  if (!activeProfile) {
    return (
      <Screen edges={['top', 'bottom', 'left', 'right']} testID="sign-in-add-server">
        <Header />
        <Section title={t('Connect to an Admiral')}>
          <View style={styles.form}>
            <ProfileForm submitLabel={t('Continue')} onSubmit={async (draft) => { await saveProfile(draft); }} />
          </View>
        </Section>
        <LanguageSection />
      </Screen>
    );
  }

  async function submitEmail() {
    setError('');
    setBusy(true);
    try {
      const result = await lookupTenants(email.trim());
      if (result.tenants.length === 0) {
        setError(t('No tenants found for this email.'));
      } else if (result.tenants.length === 1) {
        setTenant(result.tenants[0]);
        setStep('password');
      } else {
        setTenants(result.tenants);
        setTenant(result.tenants.find((x) => x.id === activeProfile?.lastTenantId) ?? null);
        setStep('tenant');
      }
    } catch {
      setError(t('Failed to look up tenants.'));
    } finally {
      setBusy(false);
    }
  }

  async function submitPassword() {
    if (!tenant) return;
    setError('');
    setBusy(true);
    try {
      const result = await authenticate({ email: email.trim(), password, tenantId: tenant.id });
      if (!result.success || !result.token) throw new Error('Authentication failed.');
      await login(result.token, { method: 'password', email: email.trim(), tenantId: tenant.id, tenantName: tenant.name });
    } catch (err) {
      setError(err instanceof ApiError && err.status === 429
        ? t('Too many failed sign-in attempts. Wait a few minutes and try again.')
        : t('Authentication failed.'));
    } finally {
      setBusy(false);
    }
  }

  async function submitApiKey() {
    setError('');
    setBusy(true);
    try {
      await login(apiKey.trim(), { method: 'token' });
    } catch {
      setError(t('API key authentication failed.'));
    } finally {
      setBusy(false);
    }
  }

  function switchMode(next: Mode) {
    setMode(next);
    setError('');
  }

  return (
    <Screen edges={['top', 'bottom', 'left', 'right']} testID="sign-in">
      <Header />
      <Section title={t('Server')}>
        <ListRow
          testID="sign-in-server"
          icon="server-outline"
          title={activeProfile.name}
          subtitle={activeProfile.url}
          onPress={() => setServersOpen(true)}
          accessibilityHint={t('Choose or add a server')}
        />
      </Section>
      <InsecureUrlWarning url={activeProfile.url} />

      <View style={styles.pad}>
        <SegmentedControl
          label={t('Sign-in method')}
          value={mode}
          onChange={switchMode}
          options={[
            { value: 'email', label: t('Email Login'), testID: 'sign-in-mode-email' },
            { value: 'apikey', label: t('API Key Login'), testID: 'sign-in-mode-apikey' },
          ]}
        />

        {error ? (
          <View accessibilityRole="alert" accessibilityLiveRegion="assertive" style={styles.error} testID="sign-in-error">
            <Icon name="alert-circle" color="danger" />
            <AppText color="danger" style={styles.flex}>{error}</AppText>
          </View>
        ) : null}

        {mode === 'apikey' ? (
          <>
            <TextField
              testID="sign-in-apikey"
              label={t('API Key / Bearer Token')}
              value={apiKey}
              onChangeText={setApiKey}
              placeholder={t('Paste your API key')}
              secret
              revealLabel={t('Show API key')}
              hideLabel={t('Hide API key')}
              onSubmitEditing={() => { if (apiKey.trim()) void submitApiKey(); }}
            />
            <Button testID="sign-in-connect" label={busy ? t('Connecting...') : t('Connect')} onPress={() => void submitApiKey()} busy={busy} disabled={!apiKey.trim()} />
          </>
        ) : null}

        {mode === 'email' && step === 'email' ? (
          <>
            <TextField
              testID="sign-in-email"
              label={t('Email')}
              value={email}
              onChangeText={setEmail}
              placeholder={t('you@company.com')}
              keyboardType="email-address"
              autoCapitalize="none"
              autoCorrect={false}
              textContentType="username"
              autoComplete="email"
              returnKeyType="next"
              onSubmitEditing={() => { if (email.trim()) void submitEmail(); }}
            />
            <Button testID="sign-in-continue" label={busy ? t('Looking up...') : t('Continue')} onPress={() => void submitEmail()} busy={busy} disabled={!email.trim()} />
          </>
        ) : null}

        {mode === 'email' && step === 'tenant' ? (
          <>
            <AppText variant="label" style={styles.label}>{t('Tenant')}</AppText>
            <View accessibilityRole="radiogroup" accessibilityLabel={t('Tenant')} style={styles.tenants}>
              {tenants.map((x) => (
                <ListRow
                  key={x.id}
                  testID={`sign-in-tenant-${x.id}`}
                  title={x.name}
                  selected={tenant?.id === x.id}
                  accessory={tenant?.id === x.id ? <Icon name="checkmark-circle" color="primary" /> : null}
                  onPress={() => setTenant(x)}
                />
              ))}
            </View>
            <Button
              testID="sign-in-tenant-continue"
              label={t('Continue')}
              disabled={!tenant}
              onPress={() => {
                if (!tenant) { setError(t('Please select a tenant.')); return; }
                setError('');
                setStep('password');
              }}
            />
            <Button label={t('Back')} variant="ghost" onPress={() => { setTenant(null); setStep('email'); }} />
          </>
        ) : null}

        {mode === 'email' && step === 'password' ? (
          <>
            <AppText muted style={styles.context} testID="sign-in-context">
              {t('Signing in as {{email}} to {{tenant}}', { email: email.trim(), tenant: tenant?.name ?? '' })}
            </AppText>
            <TextField
              testID="sign-in-password"
              label={t('Password')}
              value={password}
              onChangeText={setPassword}
              placeholder={t('Password')}
              secret
              revealLabel={t('Show password')}
              hideLabel={t('Hide password')}
              textContentType="password"
              autoComplete="password"
              returnKeyType="go"
              onSubmitEditing={() => { if (password) void submitPassword(); }}
            />
            <Button testID="sign-in-submit" label={busy ? t('Signing in...') : t('Sign In')} onPress={() => void submitPassword()} busy={busy} disabled={!password} />
            <Button label={t('Back')} variant="ghost" onPress={() => { setStep('email'); setPassword(''); }} />
          </>
        ) : null}
      </View>

      <LanguageSection />
      <AppText variant="caption" muted style={styles.defaults}>
        {t('Default credentials')}: admin@armada / password
      </AppText>

      <BottomSheet open={serversOpen} title={t('Servers')} onClose={() => { setServersOpen(false); setAddingServer(false); }} closeLabel={t('Close')} testID="servers-sheet">
        {addingServer ? (
          <ProfileForm
            submitLabel={t('Add server')}
            onCancel={() => setAddingServer(false)}
            onSubmit={async (draft) => {
              await saveProfile(draft);
              setAddingServer(false);
              setServersOpen(false);
            }}
          />
        ) : (
          <>
            {profiles.map((p) => (
              <ListRow
                key={p.id}
                title={p.name}
                subtitle={p.url}
                selected={p.id === activeProfile.id}
                accessory={p.id === activeProfile.id ? <Icon name="checkmark" color="primary" accessibilityLabel={t('Selected')} /> : null}
                onPress={() => { void selectProfile(p.id); setServersOpen(false); }}
              />
            ))}
            <ListRow testID="servers-add" icon="add-circle-outline" title={t('Add server')} onPress={() => setAddingServer(true)} />
          </>
        )}
      </BottomSheet>
    </Screen>
  );
}

function Header() {
  return (
    <View style={styles.header}>
      <Image source={require('../../assets/images/icon.png')} style={styles.logo} accessibilityIgnoresInvertColors accessible={false} />
      <AppText variant="title" accessibilityRole="header">Armada</AppText>
    </View>
  );
}

function LanguageSection() {
  return (
    <Section>
      <LocalePicker />
    </Section>
  );
}

const styles = StyleSheet.create({
  header: { alignItems: 'center', gap: spacing.sm, marginBottom: spacing.xl },
  logo: { width: 72, height: 72, borderRadius: 16 },
  pad: { paddingHorizontal: spacing.lg, marginBottom: spacing.lg },
  form: { padding: spacing.lg, paddingBottom: spacing.sm },
  error: { flexDirection: 'row', gap: spacing.sm, alignItems: 'center', marginBottom: spacing.md },
  flex: { flex: 1 },
  label: { marginBottom: spacing.xs },
  tenants: { marginBottom: spacing.lg, borderRadius: 10, overflow: 'hidden' },
  context: { marginBottom: spacing.md },
  defaults: { textAlign: 'center', marginTop: spacing.sm },
});
