import { useState } from 'react';
import { updateSettings } from '@dashboard/api/client';
import type { Vessel } from '@dashboard/types/models';
import { mergeServerSettings, type ServerSettings } from '@dashboard/lib/serverSettings';
import { DEFAULT_GLOBAL_LANDING_MODE, findLandingMode, getGlobalLandingModes } from '@dashboard/lib/vesselForm';
import { SelectField } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { errorText } from '../../resource/useLoad';
import { intOr, useDraft, type MobileServerSettings } from './settingsModel';
import { NumberField, SaveRow, SettingsSection } from './settingsParts';

export interface SectionProps {
  settings: MobileServerSettings;
  /** Remote proxy mode: local settings are read-only (the dashboard's fieldset disabled). */
  locked: boolean;
  /** The settings the server returned after a save. */
  onSaved: (updated: MobileServerSettings) => void;
}

/** Saves a partial settings update with the dashboard's toasts; resolves to false on failure. */
export function useSettingsSave(onSaved: (updated: MobileServerSettings) => void) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [saving, setSaving] = useState(false);
  async function save(payload: Record<string, unknown>, successMessage: string): Promise<boolean> {
    setSaving(true);
    try {
      const updated = await updateSettings(payload);
      onSaved((mergeServerSettings(updated as unknown as ServerSettings) ?? updated) as MobileServerSettings);
      pushToast('success', successMessage);
      return true;
    } catch (e: unknown) {
      pushToast('error', t('Failed: {{message}}', { message: errorText(e, t('Unknown error')) }));
      return false;
    } finally {
      setSaving(false);
    }
  }
  return { save, saving };
}

/** Server Configuration: Admiral port, MCP port, max captains. */
export function ServerConfigSection({ settings, locked, onSaved }: SectionProps) {
  const { t } = useLocale();
  const draft = useDraft({ admiralPort: String(settings.admiralPort), mcpPort: String(settings.mcpPort), maxCaptains: String(settings.maxCaptains) });
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  return (
    <SettingsSection title={t('Server Configuration')} testID="settings-server-config">
      <NumberField label={t('Admiral Port')} hint={t('REST API port (1-65535)')} value={v.admiralPort} onChange={(x) => draft.set({ admiralPort: x })} disabled={locked} testID="settings-admiral-port" />
      <NumberField label={t('MCP Port')} hint={t('MCP server port (1-65535)')} value={v.mcpPort} onChange={(x) => draft.set({ mcpPort: x })} disabled={locked} />
      <NumberField label={t('Max Captains')} hint={t('Maximum captains (0 = unlimited)')} value={v.maxCaptains} onChange={(x) => draft.set({ maxCaptains: x })} disabled={locked} />
      <SaveRow
        label={t('Save Server Config')}
        saving={saving}
        disabled={locked}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        onSave={async () => {
          if (await save({ admiralPort: intOr(v.admiralPort, 0), mcpPort: intOr(v.mcpPort, 0), maxCaptains: intOr(v.maxCaptains, 0) }, t('Server configuration saved'))) draft.reset();
        }}
      />
    </SettingsSection>
  );
}

/** Rebuild Armada settings: the self vessel and how many build slots to keep. */
export function RebuildSettingsSection({ settings, locked, onSaved, vessels }: SectionProps & { vessels: Vessel[] }) {
  const { t } = useLocale();
  const draft = useDraft({ selfVesselId: settings.selfVesselId ?? '', rebuildSlotRetentionCount: String(settings.rebuildSlotRetentionCount ?? 3) });
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  return (
    <SettingsSection title={t('Rebuild Armada')} description={t('Designate the vessel that holds Armada\'s own source so the "Rebuild Armada" button knows what to build. Rebuilding forces a server restart; if you are running Harbor, restart it manually afterward.')}>
      <SelectField
        label={t('Self Vessel ID')}
        hint={t('The vessel holding Armada source. Select none to disable rebuild.')}
        value={v.selfVesselId}
        options={[{ value: '', label: t('-- none --') }, ...vessels.map((vessel) => ({ value: vessel.id, label: `${vessel.name} (${vessel.id})` }))]}
        onChange={(x) => draft.set({ selfVesselId: x })}
        closeLabel={t('Close')}
        disabled={locked}
        testID="settings-self-vessel"
      />
      <NumberField label={t('Slot Retention')} hint={t('Number of build slots to retain (minimum 1)')} value={v.rebuildSlotRetentionCount} onChange={(x) => draft.set({ rebuildSlotRetentionCount: x })} disabled={locked} />
      <SaveRow
        label={t('Save Rebuild Settings')}
        saving={saving}
        disabled={locked}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        onSave={async () => {
          if (await save({ selfVesselId: v.selfVesselId, rebuildSlotRetentionCount: intOr(v.rebuildSlotRetentionCount, 1) }, t('Rebuild settings saved'))) draft.reset();
        }}
      />
    </SettingsSection>
  );
}

/** Agent Settings: heartbeat, stall threshold, idle captain timeout, and the Default Landing Mode. */
export function AgentSettingsSection({ settings, locked, onSaved }: SectionProps) {
  const { t } = useLocale();
  const draft = useDraft({
    heartbeatIntervalSeconds: String(settings.heartbeatIntervalSeconds),
    stallThresholdMinutes: String(settings.stallThresholdMinutes),
    idleCaptainTimeoutSeconds: String(settings.idleCaptainTimeoutSeconds),
    landingMode: settings.landingMode || DEFAULT_GLOBAL_LANDING_MODE,
  });
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  const modes = getGlobalLandingModes(t);
  const help = t('How finished missions land when neither the vessel nor the voyage sets a landing mode (default Merge and Push).');
  return (
    <SettingsSection title={t('Agent Settings')} description={t('Settings that control captain monitoring, stalling, cleanup, and how finished missions land.')} testID="settings-agent">
      <NumberField label={t('Heartbeat Interval (seconds)')} hint={t('Health check interval, minimum 5 seconds')} value={v.heartbeatIntervalSeconds} onChange={(x) => draft.set({ heartbeatIntervalSeconds: x })} disabled={locked} />
      <NumberField label={t('Stall Threshold (minutes)')} hint={t('Minutes before a captain is considered stalled')} value={v.stallThresholdMinutes} onChange={(x) => draft.set({ stallThresholdMinutes: x })} disabled={locked} />
      <NumberField label={t('Idle Captain Timeout (seconds)')} hint={t('Auto-remove idle captains after this many seconds (0 = disabled)')} value={v.idleCaptainTimeoutSeconds} onChange={(x) => draft.set({ idleCaptainTimeoutSeconds: x })} disabled={locked} />
      <SelectField
        label={t('Default Landing Mode')}
        hint={`${findLandingMode(modes, v.landingMode).description} ${help}`}
        value={v.landingMode}
        options={modes.map((m) => ({ value: m.value, label: m.label, description: m.description }))}
        onChange={(x) => draft.set({ landingMode: x })}
        closeLabel={t('Close')}
        disabled={locked}
        testID="settings-landing-mode"
      />
      <SaveRow
        label={t('Save Agent Settings')}
        saving={saving}
        disabled={locked}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        testID="settings-agent-save"
        onSave={async () => {
          const ok = await save({
            heartbeatIntervalSeconds: intOr(v.heartbeatIntervalSeconds, 5),
            stallThresholdMinutes: intOr(v.stallThresholdMinutes, 1),
            idleCaptainTimeoutSeconds: intOr(v.idleCaptainTimeoutSeconds, 0),
            landingMode: v.landingMode || DEFAULT_GLOBAL_LANDING_MODE,
          }, t('Agent settings saved'));
          if (ok) draft.reset();
        }}
      />
    </SettingsSection>
  );
}

/** Planning Session Settings: idle and abandonment timeouts, transcript retention. */
export function PlanningSessionSection({ settings, locked, onSaved }: SectionProps) {
  const { t } = useLocale();
  const draft = useDraft({
    planningSessionInactivityTimeoutMinutes: String(settings.planningSessionInactivityTimeoutMinutes),
    planningSessionAbandonmentTimeoutMinutes: String(settings.planningSessionAbandonmentTimeoutMinutes),
    planningSessionRetentionDays: String(settings.planningSessionRetentionDays),
  });
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  return (
    <SettingsSection title={t('Planning Session Settings')} description={t('Idle planning sessions are only auto-ended when there is no running planning process. Set a value to 0 to disable the corresponding cleanup rule.')}>
      <NumberField label={t('Idle Session Timeout (minutes)')} hint={t('Minutes before an idle planning session is automatically ended (0 = disabled)')} value={v.planningSessionInactivityTimeoutMinutes} onChange={(x) => draft.set({ planningSessionInactivityTimeoutMinutes: x })} disabled={locked} />
      <NumberField label={t('Abandonment Timeout (minutes)')} hint={t('Minutes before a stale planning session is force-ended (0 = disabled)')} value={v.planningSessionAbandonmentTimeoutMinutes} onChange={(x) => draft.set({ planningSessionAbandonmentTimeoutMinutes: x })} disabled={locked} />
      <NumberField label={t('Transcript Retention (days)')} hint={t('Days to keep stopped or failed planning sessions before deleting them (0 = disabled)')} value={v.planningSessionRetentionDays} onChange={(x) => draft.set({ planningSessionRetentionDays: x })} disabled={locked} />
      <SaveRow
        label={t('Save Planning Session Settings')}
        saving={saving}
        disabled={locked}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        onSave={async () => {
          const ok = await save({
            planningSessionInactivityTimeoutMinutes: intOr(v.planningSessionInactivityTimeoutMinutes, 0),
            planningSessionAbandonmentTimeoutMinutes: intOr(v.planningSessionAbandonmentTimeoutMinutes, 0),
            planningSessionRetentionDays: intOr(v.planningSessionRetentionDays, 0),
          }, t('Planning session settings saved'));
          if (ok) draft.reset();
        }}
      />
    </SettingsSection>
  );
}
