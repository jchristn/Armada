import { useEffect, useState } from 'react';
import { StyleSheet, Switch, View } from 'react-native';
import { biometricsAvailable } from '../../auth/biometrics';
import { useLocale } from '../../i18n/LocaleContext';
import { normalizeServerUrl, type ServerUrlError } from '../../profiles/serverUrl';
import type { ServerProfile, ServerProfileDraft, ServerProfileKind } from '../../profiles/types';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { AppText, Button, SegmentedControl, TextField } from '../ui';
import { InsecureUrlWarning } from './InsecureUrlWarning';

export interface ProfileFormProps {
  profile?: ServerProfile | null;
  onSubmit: (draft: ServerProfileDraft) => Promise<void>;
  onCancel?: () => void;
  submitLabel: string;
}

function urlErrorText(t: (s: string) => string, error: ServerUrlError): string {
  switch (error) {
    case 'empty': return t('Enter the server address.');
    case 'scheme': return t('The address must start with http:// or https://.');
    case 'credentials': return t('Do not put a user name or password in the address.');
    case 'queryOrFragment': return t('Remove the query string or fragment from the address.');
    default: return t('That does not look like a server address.');
  }
}

/** The unauthenticated health endpoint of an Admiral, or of an Armada.Proxy. */
export function healthPath(kind: ServerProfileKind): string {
  return kind === 'Proxy' ? '/proxy-api/v1/status/health' : '/api/v1/status/health';
}

/** True when an Admiral (or, for `Proxy`, an Armada.Proxy) answers its health endpoint at `baseUrl` within the timeout. */
export async function probeServer(baseUrl: string, timeoutMs = 8000, kind: ServerProfileKind = 'Direct'): Promise<boolean> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const res = await fetch(`${baseUrl}${healthPath(kind)}`, { signal: controller.signal });
    return res.ok;
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Add or edit a server profile: connection kind (an Admiral directly, or through Armada.Proxy), name, URL (with the
 * plain-HTTP warning), connection test, biometric unlock.
 */
export function ProfileForm({ profile, onSubmit, onCancel, submitLabel }: ProfileFormProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [name, setName] = useState(profile?.name ?? '');
  const [url, setUrl] = useState(profile?.url ?? '');
  const [kind, setKind] = useState<ServerProfileKind>(profile?.kind ?? 'Direct');
  const [biometric, setBiometric] = useState(profile?.biometricUnlock ?? false);
  const [canBiometric, setCanBiometric] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void biometricsAvailable().then((ok) => { if (!cancelled) setCanBiometric(ok); });
    return () => { cancelled = true; };
  }, []);

  const normalized = normalizeServerUrl(url);

  async function testConnection() {
    setTestResult(null);
    if (!normalized.url) {
      setError(urlErrorText(t, normalized.error ?? 'host'));
      return;
    }
    setBusy(true);
    try {
      // The health endpoint needs no sign-in; a plain fetch leaves the shared client's configuration alone.
      if (!(await probeServer(normalized.url, 8000, kind))) throw new Error('unreachable');
      setTestResult(t('Connected to the server.'));
      setError(null);
    } catch {
      setError(t('Could not reach the server. Check the address and that this device can reach it.'));
    } finally {
      setBusy(false);
    }
  }

  async function submit() {
    if (!normalized.url) {
      setError(urlErrorText(t, normalized.error ?? 'host'));
      return;
    }
    setError(null);
    setBusy(true);
    try {
      await onSubmit({ name: name.trim() || normalized.url, url: normalized.url, kind, biometricUnlock: biometric });
    } catch {
      setError(t('The server could not be saved.'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View>
      <View style={styles.kind}>
        <SegmentedControl
          label={t('Connection')}
          value={kind}
          onChange={(next) => { setKind(next); setTestResult(null); setError(null); }}
          options={[
            { value: 'Direct', label: t('Admiral'), testID: 'profile-kind-direct' },
            { value: 'Proxy', label: t('Armada.Proxy'), testID: 'profile-kind-proxy' },
          ]}
        />
      </View>
      <TextField
        testID="profile-url"
        label={t('Server address')}
        value={url}
        onChangeText={(v) => { setUrl(v); setError(null); setTestResult(null); }}
        placeholder="https://armada.example.com"
        autoCapitalize="none"
        autoCorrect={false}
        keyboardType="url"
        textContentType="URL"
        error={error}
        hint={testResult ?? (kind === 'Proxy'
          ? t('The Armada.Proxy URL, for example https://proxy.example.com; you pick the Admiral after signing in')
          : t('The Admiral URL, for example https://armada.example.com or http://192.168.1.20:7890'))}
      />
      {normalized.url ? <InsecureUrlWarning url={normalized.url} /> : null}
      <TextField
        testID="profile-name"
        label={t('Name (optional)')}
        value={name}
        onChangeText={setName}
        placeholder={t('My Admiral')}
      />
      {canBiometric ? (
        <View style={styles.switchRow}>
          <AppText style={styles.switchLabel}>{t('Require Face ID, Touch ID, or fingerprint to unlock')}</AppText>
          <Switch
            testID="profile-biometric"
            value={biometric}
            onValueChange={setBiometric}
            accessibilityLabel={t('Require Face ID, Touch ID, or fingerprint to unlock')}
            trackColor={{ true: colors.primary, false: colors.border }}
          />
        </View>
      ) : null}
      <View style={styles.actions}>
        <Button label={t('Test connection')} variant="secondary" onPress={() => void testConnection()} disabled={busy} testID="profile-test" />
        <Button label={submitLabel} onPress={() => void submit()} busy={busy} testID="profile-save" />
        {onCancel ? <Button label={t('Cancel')} variant="ghost" onPress={onCancel} /> : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  kind: { marginBottom: spacing.md },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, marginBottom: spacing.lg },
  switchLabel: { flex: 1 },
  actions: { gap: spacing.sm },
});
