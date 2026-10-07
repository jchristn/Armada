import { useState } from 'react';
import { DEFAULT_REMOTE_TUNNEL_URL, getRemoteTunnelIndicator, type HealthInfo } from '@dashboard/lib/serverSettings';
import { Banner } from '../../components/ui/Banner';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { SwitchField } from '../../components/ui/SwitchField';
import { TextField } from '../../components/ui/TextField';
import { useLocale } from '../../i18n/LocaleContext';
import { CATEGORY_TEXT } from '../../push/categoryLabels';
import { PUSH_CATEGORIES } from '../../push/types';
import { useSettingsSave, type SectionProps } from './ServerConfigSections';
import { hasStoredSecret, intOr, mergePush, pushPayload, remoteControlPayload, secretDraft, useDraft, type SecretDraft } from './settingsModel';
import { NumberField, SaveRow, SettingsSection } from './settingsParts';

/**
 * A secret field that never shows the stored value: empty with a hint saying whether a value is stored. Typing
 * replaces it; the Clear switch removes it; leaving it untouched keeps it (the redacted value goes back).
 */
function SecretField({ label, hint, draft, onChange, clearLabel, disabled, testID }: {
  label: string;
  hint: string;
  draft: SecretDraft;
  onChange: (next: SecretDraft) => void;
  /** Offer removing the stored secret (not for secrets the server requires). */
  clearLabel?: string;
  disabled?: boolean;
  testID?: string;
}) {
  const { t } = useLocale();
  const stored = hasStoredSecret(draft.stored);
  const state = stored
    ? t('A value is stored and is never shown. Leave this blank to keep it, or enter a new value to replace it.')
    : t('No value is stored.');
  return (
    <>
      <TextField
        label={label}
        hint={`${hint} ${state}`}
        value={draft.typed}
        onChangeText={(typed) => onChange({ ...draft, typed })}
        secret
        revealLabel={t('Show')}
        hideLabel={t('Hide')}
        placeholder={stored ? '\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022' : undefined}
        editable={!disabled && !draft.clear}
        testID={testID}
      />
      {stored && clearLabel ? <SwitchField label={clearLabel} value={draft.clear} onChange={(clear) => onChange({ ...draft, clear, typed: clear ? '' : draft.typed })} disabled={disabled} testID={testID ? `${testID}-clear` : undefined} /> : null}
    </>
  );
}

/** Remote Control (`RemoteControl`): the outbound tunnel to Armada.Proxy. Enabling it asks for confirmation. */
export function RemoteControlSection({ settings, locked, onSaved, health, onHealthRefresh }: SectionProps & { health: HealthInfo | null; onHealthRefresh: () => void }) {
  const { t } = useLocale();
  const rc = settings.remoteControl;
  const draft = useDraft({
    enabled: rc.enabled,
    tunnelUrl: rc.tunnelUrl ?? '',
    instanceId: rc.instanceId ?? '',
    connectTimeoutSeconds: String(rc.connectTimeoutSeconds),
    heartbeatIntervalSeconds: String(rc.heartbeatIntervalSeconds),
    reconnectBaseDelaySeconds: String(rc.reconnectBaseDelaySeconds),
    reconnectMaxDelaySeconds: String(rc.reconnectMaxDelaySeconds),
    allowInvalidCertificates: rc.allowInvalidCertificates,
    enrollmentToken: secretDraft(rc.enrollmentToken),
    password: secretDraft(rc.password),
  });
  const [confirmEnable, setConfirmEnable] = useState(false);
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  const indicator = getRemoteTunnelIndicator(v.enabled, health?.remoteTunnel?.state);
  const tone = indicator.dotClass === 'connected' ? 'success' : indicator.dotClass === 'warning' ? 'warning' : 'cancelled';

  return (
    <SettingsSection title={t('Remote Control')} description={t('Outbound tunnel settings for connecting this Armada server to Armada.Proxy.')} testID="settings-remote">
      <SwitchField
        label={t('Enable Remote Tunnel')}
        hint={t('Open an outbound remote-management tunnel from this Armada instance to Armada.Proxy.')}
        value={v.enabled}
        onChange={(on) => { if (on) setConfirmEnable(true); else draft.set({ enabled: false }); }}
        disabled={locked}
        testID="settings-remote-enabled"
      />
      <TextField label={t('Tunnel URL')} hint={t('Proxy base URL or tunnel endpoint. http/https will be normalized to ws/wss and /tunnel will be added automatically when needed.')} value={v.tunnelUrl} onChangeText={(x) => draft.set({ tunnelUrl: x })} placeholder={DEFAULT_REMOTE_TUNNEL_URL} autoCapitalize="none" autoCorrect={false} editable={!locked} />
      <TextField label={t('Instance ID Override')} hint={t('Optional stable deployment identifier advertised to Armada.Proxy. Leave blank to let Armada derive one automatically.')} value={v.instanceId} onChangeText={(x) => draft.set({ instanceId: x })} placeholder={t('Leave blank for auto-generated')} autoCapitalize="none" autoCorrect={false} editable={!locked} />
      <SecretField
        label={t('Instance Enrollment Token')}
        hint={t('Optional extra admission token used only when Armada.Proxy requires instance enrollment tokens.')}
        draft={v.enrollmentToken}
        onChange={(x) => draft.set({ enrollmentToken: x })}
        clearLabel={t('Remove the stored enrollment token')}
        disabled={locked}
        testID="settings-remote-enrollment"
      />
      <SecretField
        label={t('Proxy Shared Password')}
        hint={t('Shared secret used to authenticate this Armada instance to Armada.Proxy and to unlock Armada.Proxy browser access.')}
        draft={v.password}
        onChange={(x) => draft.set({ password: x })}
        disabled={locked}
        testID="settings-remote-password"
      />
      <NumberField label={t('Connect Timeout (seconds)')} hint={t('How long Armada waits for the proxy tunnel connection to open before treating the attempt as failed.')} value={v.connectTimeoutSeconds} onChange={(x) => draft.set({ connectTimeoutSeconds: x })} disabled={locked} />
      <NumberField label={t('Heartbeat Interval (seconds)')} hint={t('How often Armada sends tunnel heartbeats to keep the connection alive and measure latency.')} value={v.heartbeatIntervalSeconds} onChange={(x) => draft.set({ heartbeatIntervalSeconds: x })} disabled={locked} />
      <NumberField label={t('Reconnect Base Delay (seconds)')} hint={t('Initial reconnect backoff after a tunnel failure. Later retries grow from this base delay.')} value={v.reconnectBaseDelaySeconds} onChange={(x) => draft.set({ reconnectBaseDelaySeconds: x })} disabled={locked} />
      <NumberField label={t('Reconnect Max Delay (seconds)')} hint={t('Maximum reconnect backoff between tunnel retry attempts.')} value={v.reconnectMaxDelaySeconds} onChange={(x) => draft.set({ reconnectMaxDelaySeconds: x })} disabled={locked} />
      <SwitchField label={t('Allow Invalid Certificates')} hint={t('Allow self-signed or otherwise invalid TLS certificates for https/wss tunnel endpoints. Use only in trusted environments.')} value={v.allowInvalidCertificates} onChange={(x) => draft.set({ allowInvalidCertificates: x })} disabled={locked} />
      {v.enabled ? <StatusBadge label={`${t('Tunnel Status')}: ${t(indicator.labelKey)}`} tone={tone} /> : null}
      {health?.remoteTunnel?.lastError ? <Banner tone="danger" title={health.remoteTunnel.lastError} /> : null}
      <SaveRow
        label={t('Save Remote Control Settings')}
        saving={saving}
        disabled={locked}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        testID="settings-remote-save"
        onSave={async () => {
          const ok = await save({
            remoteControl: remoteControlPayload({
              ...rc,
              enabled: v.enabled,
              tunnelUrl: v.tunnelUrl || null,
              instanceId: v.instanceId || null,
              connectTimeoutSeconds: intOr(v.connectTimeoutSeconds, 15),
              heartbeatIntervalSeconds: intOr(v.heartbeatIntervalSeconds, 30),
              reconnectBaseDelaySeconds: intOr(v.reconnectBaseDelaySeconds, 5),
              reconnectMaxDelaySeconds: intOr(v.reconnectMaxDelaySeconds, 60),
              allowInvalidCertificates: v.allowInvalidCertificates,
            }, v.enrollmentToken, v.password),
          }, t('Remote control settings saved'));
          if (ok) {
            draft.reset();
            onHealthRefresh();
          }
        }}
      />
      <ConfirmDialog
        open={confirmEnable}
        title={t('Enable Remote Tunnel')}
        message={t('Enabling remote tunnel will enable remote connectivity to this Armada instance. Are you sure?')}
        confirmLabel={t('Yes')}
        cancelLabel={t('Cancel')}
        onConfirm={() => { setConfirmEnable(false); draft.set({ enabled: true }); }}
        onCancel={() => setConfirmEnable(false)}
        testID="settings-remote-confirm"
      />
    </SettingsSection>
  );
}

/**
 * Push Notifications (`Push`; a mobile extension, the dashboard has no push UI): delivery on/off, the categories a
 * new device gets, rate limit, deduplication window, and the optional Expo access token (a secret, never shown).
 */
export function PushSettingsSection({ settings, locked, onSaved }: SectionProps) {
  const { t } = useLocale();
  const push = mergePush(settings.push);
  const draft = useDraft({
    enabled: push.enabled,
    categories: push.categories,
    maxPerUserPerMinute: String(push.maxPerUserPerMinute),
    dedupeWindowSeconds: String(push.dedupeWindowSeconds),
    token: secretDraft(push.expoAccessToken),
  });
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  const maxValid = /^\d+$/.test(v.maxPerUserPerMinute.trim()) && Number(v.maxPerUserPerMinute) >= 1 && Number(v.maxPerUserPerMinute) <= 600;
  const dedupeValid = /^\d+$/.test(v.dedupeWindowSeconds.trim()) && Number(v.dedupeWindowSeconds) <= 86400;
  return (
    <SettingsSection title={t('Push Notifications')} description={t('Pushes to the Armada mobile apps for things that need you, delivered through the Expo Push Service. Changes apply immediately.')} testID="settings-push">
      <SwitchField label={t('Send push notifications')} value={v.enabled} onChange={(x) => draft.set({ enabled: x })} disabled={locked} testID="settings-push-enabled" />
      <NumberField
        label={t('Max pushes per user per minute')}
        hint={t('Further pushes in the same minute are dropped (1-600, default 20).')}
        error={maxValid ? null : t('Must be a whole number from {{min}} to {{max}}.', { min: 1, max: 600 })}
        value={v.maxPerUserPerMinute}
        onChange={(x) => draft.set({ maxPerUserPerMinute: x })}
        disabled={locked}
      />
      <NumberField
        label={t('Deduplication window (seconds)')}
        hint={t('A repeat push about the same item within this window is suppressed; 0 turns it off (0-86400, default 300).')}
        error={dedupeValid ? null : t('Must be a whole number from {{min}} to {{max}}.', { min: 0, max: (86400).toLocaleString() })}
        value={v.dedupeWindowSeconds}
        onChange={(x) => draft.set({ dedupeWindowSeconds: x })}
        disabled={locked}
      />
      <SecretField
        label={t('Expo access token')}
        hint={t('Optional; needed only when the Expo project has enhanced push security enabled.')}
        draft={v.token}
        onChange={(x) => draft.set({ token: x })}
        clearLabel={t('Remove the stored access token')}
        disabled={locked}
        testID="settings-push-token"
      />
      {PUSH_CATEGORIES.map((c) => (
        <SwitchField
          key={c}
          label={t(CATEGORY_TEXT[c].label)}
          hint={t(CATEGORY_TEXT[c].description)}
          value={v.categories.includes(c)}
          disabled={locked}
          onChange={(on) => {
            const next = on ? [...v.categories, c] : v.categories.filter((x) => x !== c);
            draft.set({ categories: PUSH_CATEGORIES.filter((x) => next.includes(x)) });
          }}
        />
      ))}
      <SaveRow
        label={t('Save Push Settings')}
        saving={saving}
        disabled={locked || !maxValid || !dedupeValid}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        testID="settings-push-save"
        onSave={async () => {
          const ok = await save({
            push: pushPayload({
              enabled: v.enabled,
              categories: v.categories,
              maxPerUserPerMinute: Number(v.maxPerUserPerMinute),
              dedupeWindowSeconds: Number(v.dedupeWindowSeconds),
              expoAccessToken: push.expoAccessToken,
            }, v.token),
          }, t('Push settings saved'));
          if (ok) draft.reset();
        }}
      />
    </SettingsSection>
  );
}
