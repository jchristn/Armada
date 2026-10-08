import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { biometricName, useBiometricSupport } from '../../auth/biometrics';
import { useLocale } from '../../i18n/LocaleContext';
import { normalizeServerUrl, type ServerUrlError } from '../../profiles/serverUrl';
import type { ServerProfile, ServerProfileDraft, ServerProfileKind } from '../../profiles/types';
import { spacing } from '../../theme/typography';
import { Button, SegmentedControl, SwitchRow, TextField } from '../ui';
import { InsecureUrlWarning } from './InsecureUrlWarning';
import { SavedPasswordRow } from './SignInSecurity';

export interface ProfileFormProps {
  profile?: ServerProfile | null;
  onSubmit: (draft: ServerProfileDraft) => Promise<void>;
  onCancel?: () => void;
  submitLabel: string;
  /** Editing a saved profile: forget its saved password (shown with the saved-password status). */
  onForgetSavedPassword?: () => Promise<void>;
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
 * plain-HTTP warning), connection test, the biometric app lock, and (editing) the saved password for biometric sign-in.
 */
export function ProfileForm({ profile, onSubmit, onCancel, submitLabel, onForgetSavedPassword }: ProfileFormProps) {
  const { t } = useLocale();
  const support = useBiometricSupport();
  const [name, setName] = useState(profile?.name ?? '');
  const [url, setUrl] = useState(profile?.url ?? '');
  const [kind, setKind] = useState<ServerProfileKind>(profile?.kind ?? 'Direct');
  const [biometric, setBiometric] = useState(profile?.biometricUnlock ?? false);
  const [error, setError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

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
      {support?.enrolled ? (
        <SwitchRow
          testID="profile-biometric"
          label={t('Unlock with {{method}}', { method: biometricName(support.kind, t) })}
          hint={t('Asks for {{method}} when Armada opens and after 5 minutes in the background.', { method: biometricName(support.kind, t) })}
          value={biometric}
          onChange={setBiometric}
        />
      ) : null}
      {profile && onForgetSavedPassword && support && (support.canSavePassword || profile.savedSignIn || profile.proxyPasswordSaved) ? (
        <SavedPasswordRow
          testID="profile-saved-password"
          profile={profile}
          method={biometricName(support.kind, t)}
          onForget={onForgetSavedPassword}
        />
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
  actions: { gap: spacing.sm },
});
