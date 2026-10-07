import { createVoyage, getSettings, listPipelines, listVessels } from '@dashboard/api/client';
import { sortByName } from '@dashboard/lib/sortByName';
import { findLandingMode, getVoyageLandingModes } from '@dashboard/lib/vesselForm';
import {
  buildVoyageCreateRequest,
  emptyVoyageForm,
  newVoyageMissionRow,
  validateVoyageForm,
  type VoyageFormState,
  type VoyageMissionRow,
} from '@dashboard/lib/voyageForm';
import type { Pipeline, Vessel } from '@dashboard/types/models';
import { Stack, useRouter, type Href } from 'expo-router';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { AppText, Banner, Button, IconButton, Screen, Section, TextField } from '../../components/ui';
import { SelectField } from '../../components/ui/SelectField';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import { PlaybookPicker } from '../../components/app/PlaybookPicker';
import { effectiveDefaultLandingMode, globalLandingModeFrom } from './voyage/landingDefault';

/**
 * Create Voyage (the dashboard's /voyages/create): title, description, vessel, pipeline, landing mode, playbooks,
 * and one or more missions. The landing mode defaults to "Default" (inherit) exactly as on the dashboard; the hint
 * names what that resolves to (the vessel's mode, else the server's Default Landing Mode setting).
 */
export function VoyageCreateScreen() {
  const { t } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const landingModes = getVoyageLandingModes(t);
  const [form, setForm] = useState<VoyageFormState>(emptyVoyageForm);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [globalLanding, setGlobalLanding] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    let mounted = true;
    listVessels({ pageSize: 1000 }).then((r) => { if (mounted) setVessels(sortByName(r?.objects)); }).catch(() => undefined);
    listPipelines({ pageSize: 1000 }).then((r) => { if (mounted) setPipelines(r?.objects ?? []); }).catch(() => undefined);
    getSettings().then((s) => { if (mounted) setGlobalLanding(globalLandingModeFrom(s)); }).catch(() => undefined);
    return () => { mounted = false; };
  }, []);

  const set = (patch: Partial<VoyageFormState>) => setForm((current) => ({ ...current, ...patch }));
  const updateMission = (index: number, patch: Partial<VoyageMissionRow>) =>
    setForm((current) => ({ ...current, missions: current.missions.map((m, i) => (i === index ? { ...m, ...patch } : m)) }));

  const vessel = vessels.find((v) => v.id === form.vesselId);
  const selectedMode = findLandingMode(landingModes, form.landingMode);
  const resolvedDefault = findLandingMode(landingModes, effectiveDefaultLandingMode(vessel?.landingMode, globalLanding));
  const landingHint = form.landingMode
    ? selectedMode.description
    : `${selectedMode.description} ${t('Currently: {{mode}}', { mode: resolvedDefault.label })}`;

  async function submit() {
    setError('');
    const problem = validateVoyageForm(form);
    if (problem) {
      setError(t(problem));
      return;
    }
    setSubmitting(true);
    try {
      const voyage = await createVoyage(buildVoyageCreateRequest(form));
      pushToast('success', t('Voyage "{{title}}" created.', { title: form.title.trim() }), `/voyages/${voyage.id}`);
      router.replace(`/voyages/${voyage.id}` as Href);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('Failed to create voyage.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Screen testID="voyage-create">
      <Stack.Screen options={{ title: t('Create Voyage') }} />
      {error ? <Banner tone="danger" title={error} testID="voyage-create-error" /> : null}

      <Section title={t('Voyage Details')}>
        <View style={styles.body}>
          <TextField
            testID="voyage-title"
            label={t('Title')}
            value={form.title}
            onChangeText={(title) => set({ title })}
            placeholder={t('Name for this batch of missions')}
            returnKeyType="next"
          />
          <TextField
            testID="voyage-description"
            label={t('Description')}
            value={form.description}
            onChangeText={(description) => set({ description })}
            placeholder={t('Optional description for the voyage...')}
            multiline
          />
          <SelectField
            testID="voyage-vessel"
            label={t('Vessel')}
            value={form.vesselId}
            onChange={(vesselId) => set({ vesselId })}
            placeholder={t('Select a vessel...')}
            searchLabel={t('Search...')}
            closeLabel={t('Close')}
            options={vessels.map((v) => ({ value: v.id, label: `${v.name} (${v.id})` }))}
          />
          <SelectField
            testID="voyage-pipeline"
            label={t('Pipeline')}
            value={form.pipeline}
            onChange={(pipeline) => set({ pipeline })}
            allowEmpty
            placeholder={t('Inherit (vessel, then fleet, then WorkerOnly)')}
            closeLabel={t('Close')}
            options={pipelines.map((p) => ({ value: p.name, label: `${p.name} (${p.stages.map((s) => s.personaName).join(' -> ')})` }))}
          />
          <SelectField
            testID="voyage-landing-mode"
            label={t('Landing Mode')}
            value={form.landingMode}
            onChange={(landingMode) => set({ landingMode })}
            placeholder={landingModes[0].label}
            closeLabel={t('Close')}
            options={landingModes.map((m) => ({ value: m.value, label: m.label, description: m.description }))}
            hint={landingHint}
          />
        </View>
      </Section>

      <PlaybookPicker value={form.selectedPlaybooks} onChange={(selectedPlaybooks) => set({ selectedPlaybooks })} disabled={submitting} />

      <Section title={`${t('Missions')} (${form.missions.length})`}>
        <View style={styles.body}>
          {form.missions.map((m, i) => (
            <View key={i} style={styles.mission} testID={`voyage-mission-${i}`}>
              <View style={styles.missionHead}>
                <AppText variant="label" style={styles.flex}>{t('Mission {{index}}', { index: i + 1 })}</AppText>
                {form.missions.length > 1 ? (
                  <IconButton
                    icon="trash-outline"
                    color="danger"
                    label={t('Remove')}
                    onPress={() => set({ missions: form.missions.filter((_, j) => j !== i) })}
                    testID={`voyage-mission-remove-${i}`}
                  />
                ) : null}
              </View>
              <TextField
                testID={`voyage-mission-title-${i}`}
                label={t('Title')}
                value={m.title}
                onChangeText={(title) => updateMission(i, { title })}
                placeholder={t('What needs to be done?')}
              />
              <TextField
                testID={`voyage-mission-priority-${i}`}
                label={t('Priority')}
                value={String(m.priority)}
                keyboardType="number-pad"
                onChangeText={(text) => updateMission(i, { priority: Number(text.replace(/[^0-9]/g, '')) })}
              />
              <TextField
                testID={`voyage-mission-description-${i}`}
                label={t('Description')}
                value={m.description}
                onChangeText={(description) => updateMission(i, { description })}
                placeholder={t('Detailed instructions for the AI captain...')}
                multiline
              />
            </View>
          ))}
          {form.missions.length === 0 ? (
            <AppText muted style={styles.center}>{t('No missions added. Click "+ Add Mission" to add one.')}</AppText>
          ) : null}
          <Button
            testID="voyage-add-mission"
            label={`+ ${t('Add Mission')}`}
            variant="secondary"
            onPress={() => set({ missions: [...form.missions, newVoyageMissionRow()] })}
          />
        </View>
      </Section>

      <View style={styles.actions}>
        <Button testID="voyage-create-cancel" label={t('Cancel')} variant="ghost" onPress={() => router.back()} />
        <Button testID="voyage-create-submit" label={submitting ? t('Creating...') : t('Create Voyage')} busy={submitting} onPress={() => void submit()} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  body: { padding: spacing.md },
  mission: { gap: spacing.xs, marginBottom: spacing.md, paddingBottom: spacing.sm },
  missionHead: { flexDirection: 'row', alignItems: 'center' },
  flex: { flex: 1 },
  center: { textAlign: 'center', padding: spacing.lg },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm, paddingHorizontal: spacing.md, paddingBottom: spacing.xl },
});
