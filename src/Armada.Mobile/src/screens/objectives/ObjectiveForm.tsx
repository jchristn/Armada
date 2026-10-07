import { useRouter, type Href } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import type { Objective } from '@dashboard/types/models';
import {
  OBJECTIVE_BACKLOG_STATES,
  OBJECTIVE_EFFORTS,
  OBJECTIVE_KINDS,
  OBJECTIVE_PRIORITIES,
  OBJECTIVE_STATUSES,
  splitList,
} from '@dashboard/lib/backlogUtils';
import { createEmptyTagEntry, replacePrimaryLinkedId, type ObjectiveFormState, type TagEntry } from '@dashboard/lib/backlogForm';
import { AppText, IconButton, Section, TextField } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { MultiSelectField } from './parts';
import type { BacklogReference } from './useBacklogReference';

export interface ObjectiveFormProps {
  form: ObjectiveFormState;
  onChange: (next: ObjectiveFormState) => void;
  reference: BacklogReference;
  /** The item being edited (null while creating): enables the linked-record links. */
  objective: Objective | null;
  /** Tenant administrators edit; everyone else sees the fields read-only. */
  canManage: boolean;
}

/** The primary vessel changes: it becomes the first linked vessel and its fleet the first linked fleet (dashboard rule). */
export function withPrimaryVessel(form: ObjectiveFormState, vesselId: string, fleetOfVessel: string | null | undefined): ObjectiveFormState {
  if (!vesselId) return { ...form, vesselIds: '', fleetIds: '' };
  return {
    ...form,
    vesselIds: replacePrimaryLinkedId(form.vesselIds, vesselId),
    fleetIds: fleetOfVessel ? replacePrimaryLinkedId(form.fleetIds, fleetOfVessel) : '',
  };
}

function options<T extends string>(values: readonly T[]): SelectOption<T>[] {
  return values.map((v) => ({ value: v, label: v }));
}

/**
 * The backlog item editor (the dashboard's ObjectiveDetail form): Backlog Item, Scope, and Workflow Metadata
 * sections with every field, the tag rows, the parent / blocked-by / pipeline pickers, and links to the linked
 * fleets and vessels. Lists are one entry per line.
 */
export function ObjectiveForm({ form, onChange, reference, objective, canManage }: ObjectiveFormProps) {
  const { t } = useLocale();
  const router = useRouter();
  const close = t('Close');
  const set = <K extends keyof ObjectiveFormState>(key: K, value: ObjectiveFormState[K]) => onChange({ ...form, [key]: value });
  const ro = !canManage;
  const linkedVessels = splitList(form.vesselIds);
  const linkedFleets = splitList(form.fleetIds);
  const primaryVesselId = linkedVessels[0] ?? '';
  const currentId = objective?.id ?? '';
  const selectable = reference.objectives
    .filter((o) => o.id !== currentId)
    .slice()
    .sort((a, b) => (a.rank !== b.rank ? a.rank - b.rank : a.title.localeCompare(b.title)));
  const objectiveLabel = (o: Objective) => {
    const vessel = o.vesselIds[0] ? reference.vesselNames.get(o.vesselIds[0]) ?? o.vesselIds[0] : '';
    return vessel ? `${o.title} - ${vessel} (${o.id})` : `${o.title} (${o.id})`;
  };
  const objectiveOptions = selectable.map((o) => ({ value: o.id, label: objectiveLabel(o) }));
  const known = new Set(selectable.map((o) => o.id));
  const parentOptions: SelectOption<string>[] = [{ value: '', label: t('No parent objective') }];
  if (form.parentObjectiveId && !known.has(form.parentObjectiveId)) parentOptions.push({ value: form.parentObjectiveId, label: t('Unavailable backlog item ({{id}})', { id: form.parentObjectiveId }) });
  parentOptions.push(...objectiveOptions);
  const blockedOptions = [
    ...form.blockedByObjectiveIds.filter((id) => !known.has(id)).map((id) => ({ value: id, label: t('Unavailable backlog item ({{id}})', { id }) })),
    ...objectiveOptions,
  ];

  const setTag = (index: number, field: keyof TagEntry, value: string) =>
    set('tagEntries', form.tagEntries.map((entry, i) => (i === index ? { ...entry, [field]: value } : entry)));
  const removeTag = (index: number) => {
    const next = form.tagEntries.filter((_e, i) => i !== index);
    set('tagEntries', next.length > 0 ? next : [createEmptyTagEntry()]);
  };

  const multiline = { multiline: true, textAlignVertical: 'top' as const };

  return (
    <View>
      <Section title={t('Backlog Item')}>
        <View style={styles.pad}>
          <TextField label={t('Title')} value={form.title} onChangeText={(v) => set('title', v)} editable={!ro} placeholder={t('Add feature to improve login')} hint={t('Short backlog headline. Armada reuses this in backlog lists, planning, dispatch, and release drafting.')} testID="objective-form-title" />
          <SelectField
            label={t('Vessel')}
            value={primaryVesselId}
            options={[{ value: '', label: t('No vessel selected') }, ...reference.vessels.map((v) => ({ value: v.id, label: v.fleetId ? `${v.name} (${reference.fleetNames.get(v.fleetId) ?? v.fleetId})` : v.name }))]}
            onChange={(id) => onChange(withPrimaryVessel(form, id, reference.vessels.find((v) => v.id === id)?.fleetId))}
            closeLabel={close}
            disabled={ro}
            hint={t('Primary vessel this backlog item targets. Armada uses the selected vessel to infer fleet context and unlock repository-aware planning and dispatch.')}
            testID="objective-form-vessel"
          />
          <SelectField label={t('Status')} value={form.status} options={options(OBJECTIVE_STATUSES)} onChange={(v) => set('status', v)} closeLabel={close} disabled={ro} testID="objective-form-status" />
          <TextField label={t('Owner')} value={form.owner} onChangeText={(v) => set('owner', v)} editable={!ro} autoCapitalize="none" testID="objective-form-owner" />
          <TextField label={t('Description')} value={form.description} onChangeText={(v) => set('description', v)} editable={!ro} {...multiline} numberOfLines={5} testID="objective-form-description" />
          <AppText variant="label">{t('Tags')}</AppText>
          {form.tagEntries.map((entry, index) => (
            <View key={`tag-${index}`} style={styles.tagRow}>
              <View style={styles.flex}>
                <TextField label={t('Key')} value={entry.key} onChangeText={(v) => setTag(index, 'key', v)} editable={!ro} autoCapitalize="none" testID={`objective-form-tag-key-${index}`} />
              </View>
              <View style={styles.flex}>
                <TextField label={t('Value')} value={entry.value} onChangeText={(v) => setTag(index, 'value', v)} editable={!ro} autoCapitalize="none" testID={`objective-form-tag-value-${index}`} />
              </View>
              {canManage ? <IconButton icon="trash-outline" label={t('Delete tag')} onPress={() => removeTag(index)} color="textMuted" /> : null}
            </View>
          ))}
          {canManage ? <IconButton icon="add-circle-outline" label={t('Add tag')} onPress={() => set('tagEntries', [...form.tagEntries, createEmptyTagEntry()])} testID="objective-form-tag-add" /> : null}
        </View>
      </Section>

      <Section title={t('Scope')} footer={t('Attach a fleet or vessel above to unlock repository-aware planning, dispatch, and release drafting from this backlog item. Armada adds downstream planning, mission, release, deployment, and incident links later.')}>
        <View style={styles.pad}>
          {objective ? (
            <View style={styles.links}>
              <AppText variant="label">{t('Linked Fleets')}</AppText>
              {linkedFleets.length === 0 ? <AppText muted>{t('None')}</AppText> : linkedFleets.map((id) => (
                <AppText key={`f-${id}`} color="primary" accessibilityRole="link" onPress={() => router.push(`/fleets/${encodeURIComponent(id)}` as Href)}>{reference.fleetNames.get(id) ?? id}</AppText>
              ))}
              <AppText variant="label">{t('Linked Vessels')}</AppText>
              {linkedVessels.length === 0 ? <AppText muted>{t('None')}</AppText> : linkedVessels.map((id) => (
                <AppText key={`v-${id}`} color="primary" accessibilityRole="link" onPress={() => router.push(`/vessels/${encodeURIComponent(id)}` as Href)}>{reference.vesselNames.get(id) ?? id}</AppText>
              ))}
            </View>
          ) : null}
          <TextField label={t('Refinement Summary')} value={form.refinementSummary} onChangeText={(v) => set('refinementSummary', v)} editable={!ro} {...multiline} testID="objective-form-refinement-summary" />
          <TextField label={t('Acceptance Criteria')} value={form.acceptanceCriteria} onChangeText={(v) => set('acceptanceCriteria', v)} editable={!ro} {...multiline} hint={t('Concrete conditions that must be true for the backlog item to count as done.')} testID="objective-form-acceptance" />
          <TextField label={t('Non-Goals')} value={form.nonGoals} onChangeText={(v) => set('nonGoals', v)} editable={!ro} {...multiline} testID="objective-form-non-goals" />
          <TextField label={t('Rollout Constraints')} value={form.rolloutConstraints} onChangeText={(v) => set('rolloutConstraints', v)} editable={!ro} {...multiline} testID="objective-form-rollout" />
          <TextField label={t('Evidence Links')} value={form.evidenceLinks} onChangeText={(v) => set('evidenceLinks', v)} editable={!ro} {...multiline} autoCapitalize="none" testID="objective-form-evidence" />
        </View>
      </Section>

      <Section title={t('Workflow Metadata')}>
        <View style={styles.pad}>
          <SelectField label={t('Kind')} value={form.kind} options={options(OBJECTIVE_KINDS)} onChange={(v) => set('kind', v)} closeLabel={close} disabled={ro} testID="objective-form-kind" />
          <TextField label={t('Category')} value={form.category} onChangeText={(v) => set('category', v)} editable={!ro} testID="objective-form-category" />
          <SelectField label={t('Priority')} value={form.priority} options={options(OBJECTIVE_PRIORITIES)} onChange={(v) => set('priority', v)} closeLabel={close} disabled={ro} testID="objective-form-priority" />
          <SelectField label={t('Backlog State')} value={form.backlogState} options={options(OBJECTIVE_BACKLOG_STATES)} onChange={(v) => set('backlogState', v)} closeLabel={close} disabled={ro} testID="objective-form-backlog-state" />
          <SelectField label={t('Effort')} value={form.effort} options={options(OBJECTIVE_EFFORTS)} onChange={(v) => set('effort', v)} closeLabel={close} disabled={ro} testID="objective-form-effort" />
          <TextField label={t('Rank')} value={form.rank} onChangeText={(v) => set('rank', v)} editable={!ro} keyboardType="number-pad" hint={t('Manual backlog ordering value. Lower ranks appear earlier in the backlog.')} testID="objective-form-rank" />
          <TextField label={t('Target Version')} value={form.targetVersion} onChangeText={(v) => set('targetVersion', v)} editable={!ro} autoCapitalize="none" testID="objective-form-target-version" />
          <TextField label={t('Due UTC')} value={form.dueUtc} onChangeText={(v) => set('dueUtc', v)} editable={!ro} autoCapitalize="none" placeholder="YYYY-MM-DDTHH:mm" hint={t('Optional due date used for urgency tracking and delivery planning.')} testID="objective-form-due" />
          <SelectField label={t('Parent Objective')} value={form.parentObjectiveId} options={parentOptions} onChange={(v) => set('parentObjectiveId', v)} closeLabel={close} disabled={ro} testID="objective-form-parent" />
          <SelectField
            label={t('Suggested Pipeline')}
            value={form.suggestedPipelineId}
            options={[{ value: '', label: t('None') }, ...reference.pipelines.map((p) => ({ value: p.id, label: p.name }))]}
            onChange={(v) => set('suggestedPipelineId', v)}
            closeLabel={close}
            disabled={ro}
            testID="objective-form-pipeline"
          />
          <MultiSelectField
            label={t('Blocked By Objectives')}
            values={form.blockedByObjectiveIds}
            options={blockedOptions}
            onChange={(v) => set('blockedByObjectiveIds', v)}
            disabled={ro || blockedOptions.length === 0}
            hint={t('Other backlog items that must be resolved before this work can move forward.')}
            testID="objective-form-blocked-by"
          />
          <TextField label={t('Suggested Playbooks')} value={form.suggestedPlaybooks} onChangeText={(v) => set('suggestedPlaybooks', v)} editable={!ro} {...multiline} autoCapitalize="none" placeholder="playbook-id:InlineFullContent" hint={t('Playbooks to carry into planning or dispatch. Use playbook-id:delivery-mode entries.')} testID="objective-form-playbooks" />
        </View>
      </Section>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  pad: { padding: spacing.lg, paddingBottom: 0 },
  tagRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  links: { gap: spacing.xs, marginBottom: spacing.lg },
});
