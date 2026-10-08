import { useRouter, type Href } from 'expo-router';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { createVoyage, getVesselReadiness, listCaptains, listPersonas, listPipelines, listVessels } from '@dashboard/api/client';
import {
  ALL_STEPS_PERSONA, buildDispatchVoyageRequest, dispatchPrefillNotice, effectiveStepPersonas, parseDispatchPriority,
  pipelineStepPersonas, seedStepAssignments, type DispatchPrefillState, type StepAssignment,
} from '@dashboard/lib/dispatchRequest';
import { sortByName } from '@dashboard/lib/sortByName';
import type { Captain, Persona, Pipeline, SelectedPlaybook, Vessel, VesselReadinessResult } from '@dashboard/types/models';
import { AppText, Banner, Button, Screen, Section, SelectField, TextField } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import { CaptainPickerField, FallbackTierField } from './w24/CaptainFields';
import { PlaybookPicker } from '../../components/app/PlaybookPicker';
import { ReadinessCard } from './w24/ReadinessCard';

/** How long the success message shows before opening the new voyage (the dashboard waits 1.5 s). */
export const DISPATCH_NAVIGATE_DELAY_MS = 1500;

/**
 * The Dispatch tab (the dashboard's Dispatch page): vessel with its readiness, pipeline, priority, optional voyage
 * title, the description, playbooks, and a captain per pipeline step (or one for every step). Dispatching creates a
 * voyage and opens it. `prefill` is the draft a link carried (from a vessel, planning session, workspace, incident,
 * or backlog item). The form is its own scrolling screen (`header` scrolls above it, for the hub's tab bar) with the
 * Dispatch button and its result in a footer that stays reachable.
 */
export function DispatchForm({ prefill, header, testID = 'dispatch-screen', schedule = (fn, ms) => setTimeout(fn, ms) }: {
  prefill: DispatchPrefillState | null;
  /** Scrolls with the form, above it (the Dispatch hub's tab bar). */
  header?: ReactNode;
  /** The screen's test id. */
  testID?: string;
  /** Injectable for tests (the delay before opening the created voyage). */
  schedule?: (fn: () => void, ms: number) => unknown;
}) {
  const { t } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [personas, setPersonas] = useState<Persona[]>([]);
  const [vesselId, setVesselId] = useState(prefill?.vesselId ?? '');
  const [selectedPipeline, setSelectedPipeline] = useState(prefill?.pipelineName ?? '');
  const [priority, setPriority] = useState(100);
  const [priorityText, setPriorityText] = useState('100');
  const [voyageTitle, setVoyageTitle] = useState(prefill?.voyageTitle ?? '');
  const [prompt, setPrompt] = useState(prefill?.prompt ?? '');
  const [objectiveId] = useState(prefill?.objectiveId ?? '');
  const [selectedPlaybooks, setSelectedPlaybooks] = useState<SelectedPlaybook[]>(prefill?.selectedPlaybooks ?? []);
  const [assignments, setAssignments] = useState<Record<string, StepAssignment>>({});
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [readinessFor, setReadinessFor] = useState('');
  const [dispatching, setDispatching] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; message: string } | null>(null);
  const mounted = useRef(true);
  useEffect(() => () => { mounted.current = false; }, []);

  useEffect(() => {
    let cancelled = false;
    void Promise.all([
      listVessels({ pageSize: 9999 }).catch(() => null),
      listPipelines({ pageSize: 9999 }).catch(() => null),
      listCaptains({ pageSize: 9999 }).catch(() => null),
      listPersonas({ pageSize: 9999 }).catch(() => null),
    ]).then(([v, p, c, pr]) => {
      if (cancelled) return;
      if (v) setVessels(sortByName(v.objects));
      if (p) setPipelines(p.objects ?? []);
      if (c) setCaptains(c.objects ?? []);
      if (pr) setPersonas(pr.objects ?? []);
    });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (!vesselId) return undefined;
    let cancelled = false;
    getVesselReadiness(vesselId)
      .then((value) => { if (!cancelled) { setReadiness(value); setReadinessFor(vesselId); } })
      .catch(() => { if (!cancelled) { setReadiness(null); setReadinessFor(vesselId); } });
    return () => { cancelled = true; };
  }, [vesselId]);

  const pipeline = pipelines.find((p) => p.name === selectedPipeline) ?? null;
  const stepPersonas = useMemo(() => pipelineStepPersonas(pipeline), [pipeline]);
  const steps = useMemo(() => effectiveStepPersonas(stepPersonas), [stepPersonas]);
  // Each step's choice, seeded from the persona's default captain the first time the step appears.
  const effectiveAssignments = useMemo(() => seedStepAssignments(assignments, steps, personas), [assignments, steps, personas]);
  const notice = dispatchPrefillNotice(prefill);

  const dispatch = async () => {
    if (!prompt.trim()) return;
    if (!vesselId) {
      setResult({ ok: false, message: t('Please select a vessel.') });
      return;
    }
    const multiStage = pipeline != null && pipeline.stages.length > 1;
    setDispatching(true);
    setResult(null);
    try {
      const request = buildDispatchVoyageRequest({
        vesselId, prompt, priority, voyageTitle, objectiveId, pipeline: selectedPipeline, selectedPlaybooks,
        stepAssignments: effectiveAssignments, multiTaskTitle: t('Multi-task voyage'),
      });
      const voyage = await createVoyage(request);
      const missionCount = multiStage
        ? t('{{count}} pipeline stages', { count: pipeline!.stages.length })
        : t('{{count}} mission(s)', { count: request.missions.length });
      const message = t('Dispatched voyage with {{missionCount}}', { missionCount });
      setResult({ ok: true, message });
      pushToast('success', message, `/voyages/${voyage.id}`);
      setVoyageTitle('');
      setPrompt('');
      schedule(() => { if (mounted.current) router.push(`/voyages/${voyage.id}` as Href); }, DISPATCH_NAVIGATE_DELAY_MS);
    } catch (e) {
      setResult({ ok: false, message: t('Failed: {{message}}', { message: errorMessage(e, t('Unknown error')) }) });
    } finally {
      if (mounted.current) setDispatching(false);
    }
  };

  const footer = (
    <View style={styles.footer}>
      {result ? (
        <AppText color={result.ok ? 'success' : 'danger'} accessibilityRole="alert" accessibilityLiveRegion="polite" testID="dispatch-result">{result.message}</AppText>
      ) : null}
      <Button
        label={dispatching ? t('Dispatching...') : t('Dispatch')}
        icon="paper-plane-outline"
        busy={dispatching}
        disabled={!vesselId || !prompt.trim()}
        onPress={() => void dispatch()}
        testID="dispatch-submit"
      />
    </View>
  );

  return (
    <Screen testID={testID} footer={footer}>
      {header}
      <View style={styles.wrap} testID="dispatch-form">
        <AppText muted style={styles.intro}>{t('Describe the work you want Armada to dispatch through the selected vessel and pipeline.')}</AppText>
        {notice ? (
          <View>
            <Banner tone="info" title={t(notice)} testID="dispatch-prefill-notice" />
            {objectiveId ? (
              <View style={styles.pad}>
                <Button label={t('Open Backlog Item')} variant="ghost" icon="open-outline" onPress={() => router.push(`/backlog/${objectiveId}` as Href)} testID="dispatch-open-objective" />
              </View>
            ) : null}
          </View>
        ) : null}
        {vesselId ? (
          <ReadinessCard title={t('Vessel Readiness')} readiness={readinessFor === vesselId ? readiness : null} loading={readinessFor !== vesselId}
            emptyMessage={t('Select a vessel to inspect readiness.')} />
        ) : null}
        <View style={styles.pad}>
          <SelectField
            label={t('Vessel')}
            value={vesselId}
            onChange={setVesselId}
            placeholder={t('Select a vessel...')}
            closeLabel={t('Close')}
            searchLabel={t('Search vessels')}
            options={vessels.map((v) => ({ value: v.id, label: v.name }))}
            disabled={dispatching}
            testID="dispatch-vessel"
          />
          <SelectField
            label={t('Pipeline')}
            value={selectedPipeline}
            onChange={setSelectedPipeline}
            allowEmpty
            placeholder={t('Inherit (vessel, then fleet, then WorkerOnly)')}
            closeLabel={t('Close')}
            options={pipelines.map((p) => ({ value: p.name, label: `${p.name} (${p.stages.map((s) => s.personaName).join(' -> ')})` }))}
            disabled={dispatching}
            testID="dispatch-pipeline"
          />
          <Button label={t('Manage pipelines')} variant="ghost" icon="git-branch-outline" onPress={() => router.push('/pipelines' as Href)} testID="dispatch-manage-pipelines" />
          <TextField
            label={t('Priority')}
            hint={t('Higher priority missions are assigned first (default 100)')}
            value={priorityText}
            keyboardType="number-pad"
            onChangeText={(text) => { setPriorityText(text); setPriority(parseDispatchPriority(text)); }}
            onBlur={() => setPriorityText(String(priority))}
            testID="dispatch-priority"
          />
          <TextField label={t('Voyage Title')} value={voyageTitle} onChangeText={setVoyageTitle} placeholder={t('Optional override for the voyage title')} testID="dispatch-voyage-title" />
          <TextField
            label={t('Description')}
            value={prompt}
            onChangeText={setPrompt}
            multiline
            numberOfLines={8}
            textAlignVertical="top"
            placeholder={t('Describe what you need done.\n\nArmada will dispatch this request as a voyage on the selected vessel.')}
            testID="dispatch-prompt"
          />
          <PlaybookPicker value={selectedPlaybooks} onChange={setSelectedPlaybooks} disabled={dispatching} testID="dispatch-playbooks" />
          <Button label={t('Manage playbooks')} variant="ghost" icon="book-outline" onPress={() => router.push('/playbooks' as Href)} testID="dispatch-manage-playbooks" />
        </View>
        <Section
          title={t('Captain Assignments')}
          footer={stepPersonas.length > 0
            ? t('Pick a preferred captain per pipeline step. When it is busy, Armada falls back to an idle captain at or above the fallback tier. Defaults come from each persona.')
            : t('No specific pipeline selected. This captain applies to every step of the mission; leave blank to let Armada auto-assign an idle captain.')}
        >
          {steps.map((persona) => {
            const assignment = effectiveAssignments[persona] ?? { captainId: null, fallbackTier: null };
            const stepLabel = persona === ALL_STEPS_PERSONA ? t('All steps') : persona;
            const key = persona === ALL_STEPS_PERSONA ? 'all' : persona;
            return (
              <View key={persona} style={styles.step} testID={`dispatch-step-${key}`}>
                <AppText variant="label">{stepLabel}</AppText>
                <CaptainPickerField
                  label={t('Preferred Captain')}
                  captains={captains}
                  value={assignment.captainId}
                  onChange={(captainId) => setAssignments(() => ({ ...effectiveAssignments, [persona]: { ...assignment, captainId } }))}
                  disabled={dispatching}
                  testID={`dispatch-captain-${key}`}
                />
                <FallbackTierField
                  label={t('Fallback Tier')}
                  value={assignment.fallbackTier}
                  onChange={(fallbackTier) => setAssignments(() => ({ ...effectiveAssignments, [persona]: { ...assignment, fallbackTier } }))}
                  disabled={dispatching}
                  testID={`dispatch-tier-${key}`}
                />
              </View>
            );
          })}
          <View style={styles.pad}>
            <Button label={t('Manage persona defaults')} variant="ghost" icon="people-outline" onPress={() => router.push('/personas' as Href)} testID="dispatch-manage-personas" />
          </View>
        </Section>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  wrap: { paddingVertical: spacing.md },
  intro: { marginHorizontal: spacing.lg, marginBottom: spacing.lg },
  pad: { paddingHorizontal: spacing.lg },
  step: { paddingHorizontal: spacing.lg, paddingTop: spacing.md },
  footer: { gap: spacing.xs },
});
