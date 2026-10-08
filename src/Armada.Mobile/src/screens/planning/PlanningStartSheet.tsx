import { useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { createPlanningSession, getVesselReadiness, TimeoutError } from '@dashboard/api/client';
import type { SelectedPlaybook, VesselReadinessResult } from '@dashboard/types/models';
import { canCaptainStartPlanning } from '@dashboard/lib/captains';
import { errorMessage } from '../../build/useLiveResource';
import { AppText, Banner, BottomSheet, Button, TextField } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import { PlaybookPicker } from './PlaybookPicker';
import { ReadinessSummary } from './ReadinessSummary';
import type { PlanningCatalog } from './usePlanningCatalog';

/** Values another screen can prefill the start form with (the dashboard's PlanningPrefillState, as route params). */
export interface PlanningPrefill {
  title?: string;
  captainId?: string;
  fleetId?: string;
  vesselId?: string;
  pipelineId?: string;
  objectiveId?: string;
  /** Draft first message, placed in the session's composer once it starts. */
  initialPrompt?: string;
}

export interface PlanningStartSheetProps {
  open: boolean;
  onClose: () => void;
  catalog: PlanningCatalog;
  prefill?: PlanningPrefill | null;
  /** The session started; the screen opens it (with the prefilled first message, when there is one). */
  onStarted: (sessionId: string, initialPrompt: string | null) => void;
  /** Starting timed out while the server still provisions: the list should reload. */
  onTimedOut: () => void;
}

/**
 * Start a planning session (the dashboard's PlanningStartCard): title, captain (only idle captains whose runtime
 * supports planning can be chosen), fleet (narrows the vessels), vessel, pipeline, playbooks, and the vessel's
 * readiness. Starting reserves the captain and provisions a dock, which can take minutes the first time.
 */
export function PlanningStartSheet({ open, onClose, catalog, prefill, onStarted, onTimedOut }: PlanningStartSheetProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [title, setTitle] = useState('');
  const [captainId, setCaptainId] = useState('');
  const [fleetId, setFleetId] = useState('');
  const [vesselId, setVesselId] = useState('');
  const [pipelineId, setPipelineId] = useState('');
  const [playbooks, setPlaybooks] = useState<SelectedPlaybook[]>([]);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [readinessLoading, setReadinessLoading] = useState(false);

  // A prefill (from a backlog item, an incident, the workspace, ...) applies each time the sheet opens with one.
  useEffect(() => {
    if (!open || !prefill) return;
    /* eslint-disable react-hooks/set-state-in-effect -- copying a prefill into the form when the sheet opens */
    if (prefill.title) setTitle(prefill.title);
    if (prefill.captainId) setCaptainId(prefill.captainId);
    if (prefill.fleetId) setFleetId(prefill.fleetId);
    if (prefill.vesselId) setVesselId(prefill.vesselId);
    if (prefill.pipelineId) setPipelineId(prefill.pipelineId);
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, prefill]);

  const availableVessels = useMemo(
    () => (fleetId ? catalog.vessels.filter((v) => v.fleetId === fleetId) : catalog.vessels),
    [catalog.vessels, fleetId],
  );

  // Choosing a fleet drops a vessel outside it.
  const chooseFleet = (next: string) => {
    setFleetId(next);
    const vessel = catalog.vessels.find((v) => v.id === vesselId);
    if (next && vessel && vessel.fleetId !== next) setVesselId('');
  };

  useEffect(() => {
    if (!vesselId) {
      /* eslint-disable-next-line react-hooks/set-state-in-effect -- readiness follows the chosen vessel */
      setReadiness(null);
      setReadinessLoading(false);
      return undefined;
    }
    let cancelled = false;
    setReadinessLoading(true);
    getVesselReadiness(vesselId)
      .then((value) => { if (!cancelled) setReadiness(value); })
      .catch(() => { if (!cancelled) setReadiness(null); })
      .finally(() => { if (!cancelled) setReadinessLoading(false); });
    return () => { cancelled = true; };
  }, [vesselId]);

  const selectedCaptain = catalog.captains.find((c) => c.id === captainId) ?? null;
  const canStart = canCaptainStartPlanning(selectedCaptain) && !!vesselId && !creating;

  const captainOptions: SelectOption<string>[] = [
    { value: '', label: t('Select a captain...') },
    ...catalog.captains.map((c) => ({
      value: c.id,
      label: c.name,
      description: `${c.runtime} - ${c.state}${c.supportsPlanningSessions ? '' : ` - ${t('planning unsupported')}`}`,
      disabled: !canCaptainStartPlanning(c),
    })),
  ];
  const fleetOptions: SelectOption<string>[] = [{ value: '', label: t('Any fleet') }, ...catalog.fleets.map((f) => ({ value: f.id, label: f.name }))];
  const vesselOptions: SelectOption<string>[] = [{ value: '', label: t('Select a vessel...') }, ...availableVessels.map((v) => ({ value: v.id, label: v.name }))];
  const pipelineOptions: SelectOption<string>[] = [{ value: '', label: t('Inherit later during dispatch') }, ...catalog.pipelines.map((p) => ({ value: p.id, label: p.name }))];

  const start = async () => {
    if (!canStart) return;
    setCreating(true);
    setError(null);
    try {
      const result = await createPlanningSession({
        title: title.trim() || undefined,
        captainId,
        vesselId,
        fleetId: fleetId || undefined,
        pipelineId: pipelineId || undefined,
        selectedPlaybooks: playbooks,
        objectiveId: prefill?.objectiveId || undefined,
      });
      pushToast('success', t('Planning session started.'));
      setTitle('');
      setPlaybooks([]);
      onStarted(result.session.id, prefill?.initialPrompt?.trim() || null);
    } catch (e) {
      if (e instanceof TimeoutError) {
        onTimedOut();
        setError(t('Starting the planning session is taking longer than expected. Armada is still provisioning the dock and worktree. If setup completes, the session will appear in the list on the left.'));
      } else {
        setError(errorMessage(e));
      }
    } finally {
      setCreating(false);
    }
  };

  return (
    <BottomSheet open={open} title={t('Start Session')} onClose={onClose} closeLabel={t('Close')} testID="planning-new">
      <AppText muted style={styles.gap}>{t('Reserve a captain on a vessel, then use the transcript as the source of truth for a later dispatch.')}</AppText>
      <Banner tone="info" title={t('Planning sessions reserve the selected captain and dock for the duration of the session. The captain can inspect and modify the repository while you plan.')} />
      {error ? <Banner tone="danger" title={error} testID="planning-start-error" /> : null}
      {catalog.loading ? <AppText muted>{t('Loading planning catalog...')}</AppText> : (
        <View>
          <TextField label={t('Title')} value={title} onChangeText={setTitle} placeholder={t('Optional planning session title')} editable={!creating} testID="planning-start-title" />
          <SelectField label={t('Captain')} value={captainId} options={captainOptions} onChange={setCaptainId} closeLabel={t('Close')} disabled={creating} testID="planning-start-captain" />
          {selectedCaptain && !selectedCaptain.supportsPlanningSessions ? (
            <Banner tone="warning" title={selectedCaptain.planningSessionSupportReason || t('This captain runtime is not currently supported for planning sessions.')} />
          ) : null}
          {selectedCaptain?.supportsPlanningSessions ? (
            <AppText variant="caption" muted style={styles.gap}>{t("Planning runs this captain's CLI through transcript-backed turn relaunches.")}</AppText>
          ) : null}
          <SelectField label={t('Fleet')} value={fleetId} options={fleetOptions} onChange={chooseFleet} closeLabel={t('Close')} disabled={creating} testID="planning-start-fleet" />
          <SelectField label={t('Vessel')} value={vesselId} options={vesselOptions} onChange={setVesselId} closeLabel={t('Close')} disabled={creating} testID="planning-start-vessel" />
          {vesselId ? <ReadinessSummary readiness={readiness} loading={readinessLoading} /> : null}
          <SelectField label={t('Pipeline')} value={pipelineId} options={pipelineOptions} onChange={setPipelineId} closeLabel={t('Close')} disabled={creating} testID="planning-start-pipeline" />
          {prefill?.initialPrompt?.trim() ? (
            <Banner tone="info" title={t('Workspace prefilled an initial planning brief. Start the session and review the draft message in the transcript composer before sending it to the captain.')} />
          ) : null}
          <PlaybookPicker value={playbooks} onChange={setPlaybooks} disabled={creating} testID="planning-start-playbooks" />
          {creating ? (
            <View style={styles.creating} accessibilityLiveRegion="polite" accessibilityRole="progressbar">
              <ActivityIndicator />
              <AppText variant="caption" muted style={styles.flex}>
                {t('Armada is reserving the captain, provisioning the dock, and preparing the worktree. First-time repository setup can take a few minutes.')}
              </AppText>
            </View>
          ) : null}
          <Button
            label={creating ? t('Starting Planning Session...') : t('Start Planning Session')}
            onPress={() => void start()}
            disabled={!canStart}
            busy={creating}
            testID="planning-start-submit"
          />
          {catalog.vessels.length === 0 ? <AppText variant="caption" muted>{t('Create a vessel first so Armada has a repository context for planning.')}</AppText> : null}
        </View>
      )}
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  gap: { marginBottom: spacing.md },
  creating: { flexDirection: 'row', gap: spacing.sm, alignItems: 'center', marginBottom: spacing.md },
});
