import { useCallback, useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useAuth } from '../../auth/AuthContext';
import { biometricName, useBiometricSupport } from '../../auth/biometrics';
import { useLocale } from '../../i18n/LocaleContext';
import { ProxyError, isSelectableInstance, type ProxyInstance } from '../../proxy/proxyApi';
import { spacing } from '../../theme/typography';
import { AppText, Banner, Button, Icon, ListRow, TextField } from '../ui';
import { BiometricSignInCard, SavePasswordSwitch, useAutoPromptOnce } from './BiometricSignIn';

function ErrorLine({ message }: { message: string }) {
  return (
    <View accessibilityRole="alert" accessibilityLiveRegion="assertive" style={styles.error} testID="proxy-error">
      <Icon name="alert-circle" color="danger" />
      <AppText color="danger" style={styles.flex}>{message}</AppText>
    </View>
  );
}

function proxyErrorText(t: (s: string, v?: Record<string, string | number>) => string, err: unknown): string {
  if (err instanceof ProxyError) {
    switch (err.kind) {
      case 'unauthorized': return t('The proxy password is incorrect.');
      case 'lockedOut':
        return err.retryAfterSeconds
          ? t('Too many failed sign-in attempts. Try again in {{minutes}} minutes.', { minutes: Math.max(1, Math.ceil(err.retryAfterSeconds / 60)) })
          : t('Too many failed sign-in attempts. Wait a few minutes and try again.');
      case 'network': return t('Could not reach the proxy. Check the address and that this device can reach it.');
      case 'notFound': return t('The address did not answer like an Armada.Proxy.');
      case 'conflict': return t('That Admiral is not connected to the proxy right now.');
      default: return t('The proxy could not complete the request.');
    }
  }
  return t('The proxy could not complete the request.');
}

/**
 * Step 1 for a Proxy profile: the Armada.Proxy password (the shared secret set on the proxy), or the password saved
 * for Face ID / Touch ID / fingerprint sign-in.
 */
export function ProxyPortalForm() {
  const { proxySignIn, proxyExpired, activeProfile, signedOutByUser, readSavedProxyPassword, forgetSavedPassword } = useAuth();
  const { t } = useLocale();
  const support = useBiometricSupport();
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const [savePassword, setSavePassword] = useState(!!activeProfile?.proxyPasswordSaved);
  const method = support ? biometricName(support.kind, t) : '';
  const biometricReady = !!support?.canSavePassword && !!activeProfile?.proxyPasswordSaved;

  useAutoPromptOnce(
    biometricReady && !signedOutByUser && activeProfile ? `${activeProfile.id}:portal` : null,
    () => { void biometricSubmit(); },
  );

  async function submit() {
    setError('');
    setNotice('');
    setBusy(true);
    try {
      await proxySignIn(password, {
        password,
        save: savePassword,
        canSave: !!support?.canSavePassword,
        prompt: t('Save your password for {{method}} sign-in', { method }),
      });
      setPassword('');
    } catch (err) {
      setError(proxyErrorText(t, err));
    } finally {
      setBusy(false);
    }
  }

  /** Read the saved proxy password (the OS prompts) and sign in to the proxy with it. */
  async function biometricSubmit() {
    if (!activeProfile || busy) return;
    setError('');
    setNotice('');
    setBusy(true);
    try {
      const read = await readSavedProxyPassword(t('Sign in to {{name}}', { name: activeProfile.name }));
      if (read.status === 'none') {
        setSavePassword(false);
        setNotice(t('Your saved password is no longer available ({{method}} settings changed). Sign in with your password.', { method }));
        return;
      }
      if (read.status === 'failed') {
        setNotice(t('{{method}} sign-in was cancelled. Sign in with your password.', { method }));
        return;
      }
      try {
        await proxySignIn(read.credentials.password);
      } catch (err) {
        if (err instanceof ProxyError && err.kind === 'unauthorized') {
          await forgetSavedPassword(activeProfile.id, 'proxy');
          setSavePassword(false);
          setNotice(t('The saved password was not accepted, so it was removed from this device. Sign in with your password.'));
        } else {
          setError(proxyErrorText(t, err));
        }
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <View testID="proxy-portal">
      {proxyExpired ? (
        <Banner
          tone="warning"
          testID="proxy-expired"
          title={t('Your Armada.Proxy session ended')}
          message={t('Sign in to the proxy again to continue where you left off.')}
        />
      ) : null}
      <View style={styles.pad}>
        <AppText muted style={styles.intro}>
          {t('Sign in to Armada.Proxy, then choose the Admiral to connect to.')}
        </AppText>
        {error ? <ErrorLine message={error} /> : null}
        {notice ? (
          <View accessibilityRole="alert" accessibilityLiveRegion="polite" style={styles.error} testID="proxy-notice">
            <Icon name="information-circle-outline" color="info" />
            <AppText style={styles.flex}>{notice}</AppText>
          </View>
        ) : null}
        {biometricReady && support ? (
          <BiometricSignInCard
            testID="proxy-biometric"
            support={support}
            title={t('Sign in with {{method}}', { method })}
            subtitle={t('Armada.Proxy password')}
            busy={busy}
            onPress={() => void biometricSubmit()}
          />
        ) : null}
        <TextField
          testID="proxy-password"
          label={t('Proxy password')}
          value={password}
          onChangeText={setPassword}
          placeholder={t('Proxy password')}
          secret
          revealLabel={t('Show password')}
          hideLabel={t('Hide password')}
          textContentType="password"
          autoComplete="password"
          returnKeyType="go"
          onSubmitEditing={() => { if (password.trim()) void submit(); }}
        />
        <SavePasswordSwitch testID="proxy-save-password" support={support} value={savePassword} onChange={setSavePassword} />
        <Button testID="proxy-sign-in" label={busy ? t('Signing in...') : t('Sign in to proxy')} onPress={() => void submit()} busy={busy} disabled={!password.trim()} />
      </View>
    </View>
  );
}

/** Step 2: the Admiral instances connected to the proxy. */
export function ProxyInstancePicker() {
  const { proxyListInstances, proxySelectInstance, proxySignOut, activeProfile } = useAuth();
  const { t } = useLocale();
  const [instances, setInstances] = useState<ProxyInstance[] | null>(null);
  const [error, setError] = useState('');
  const [busyId, setBusyId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setInstances(await proxyListInstances());
    } catch (err) {
      setError(proxyErrorText(t, err));
    } finally {
      setLoading(false);
    }
  }, [proxyListInstances, t]);

  // First load on arrival (setState only in the promise callbacks); the Refresh button reloads.
  useEffect(() => {
    let cancelled = false;
    proxyListInstances()
      .then((list) => { if (!cancelled) setInstances(list); })
      .catch((err: unknown) => { if (!cancelled) setError(proxyErrorText(t, err)); });
    return () => { cancelled = true; };
  }, [proxyListInstances, t]);

  async function pick(instance: ProxyInstance) {
    setBusyId(instance.instanceId);
    setError('');
    try {
      await proxySelectInstance(instance.instanceId);
    } catch (err) {
      setError(proxyErrorText(t, err));
    } finally {
      setBusyId(null);
    }
  }

  const stateLabel = (state: string) => (state === 'connected' ? t('Connected') : state === 'stale' ? t('Stale') : t('Offline'));

  return (
    <View testID="proxy-instances">
      <View style={styles.pad}>
        <AppText variant="label" accessibilityRole="header" style={styles.intro}>{t('Choose an Admiral')}</AppText>
        {error ? <ErrorLine message={error} /> : null}
      </View>
      {instances && instances.length === 0 ? (
        <AppText muted style={styles.empty} testID="proxy-instances-empty">
          {t('No Admiral is connected to this proxy. Enable remote control on the Admiral, then refresh.')}
        </AppText>
      ) : null}
      <View style={styles.list}>
        {(instances ?? []).map((instance) => {
          const selectable = isSelectableInstance(instance);
          return (
            <ListRow
              key={instance.instanceId}
              testID={`proxy-instance-${instance.instanceId}`}
              icon="server-outline"
              title={instance.instanceId}
              subtitle={[stateLabel(instance.state), instance.armadaVersion ? `Armada ${instance.armadaVersion}` : null].filter(Boolean).join(' - ')}
              selected={activeProfile?.proxyInstanceId === instance.instanceId}
              accessory={busyId === instance.instanceId ? <AppText muted>{t('Connecting...')}</AppText> : null}
              onPress={selectable && !busyId ? () => void pick(instance) : undefined}
            />
          );
        })}
      </View>
      <View style={styles.pad}>
        <Button testID="proxy-instances-refresh" label={loading ? t('Loading...') : t('Refresh')} variant="secondary" onPress={() => void load()} busy={loading} />
        <Button testID="proxy-sign-out" label={t('Sign out of Armada.Proxy')} variant="ghost" onPress={() => void proxySignOut()} />
      </View>
    </View>
  );
}

/** Step 3 header: which Admiral the sign-in below goes to, with the way back. */
export function ProxyInstanceBar() {
  const { activeProfile, proxyChangeInstance, proxySignOut } = useAuth();
  const { t } = useLocale();
  return (
    <View style={styles.pad} testID="proxy-instance-bar">
      <ListRow
        icon="git-network-outline"
        title={activeProfile?.proxyInstanceId ?? ''}
        subtitle={t('Through Armada.Proxy')}
      />
      <View style={styles.row}>
        <Button testID="proxy-change-instance" label={t('Change Admiral')} variant="ghost" onPress={() => void proxyChangeInstance()} />
        <Button testID="proxy-sign-out" label={t('Sign out of Armada.Proxy')} variant="ghost" onPress={() => void proxySignOut()} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  pad: { paddingHorizontal: spacing.lg, marginBottom: spacing.md },
  intro: { marginBottom: spacing.md },
  error: { flexDirection: 'row', gap: spacing.sm, alignItems: 'center', marginBottom: spacing.md },
  flex: { flex: 1 },
  list: { marginBottom: spacing.md, marginHorizontal: spacing.lg, borderRadius: 10, overflow: 'hidden' },
  empty: { marginHorizontal: spacing.lg, marginBottom: spacing.md },
  row: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
});
