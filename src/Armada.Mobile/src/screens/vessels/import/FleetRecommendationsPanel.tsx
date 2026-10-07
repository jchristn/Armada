import { useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { apiErrorCode, applyFleetRecommendations, cancelJob, categorizeVesselImport } from '@dashboard/api/client';
import type { FleetRecommendationApplyResult, VesselImportBatch, VesselImportFleetRecommendation, VesselImportItem } from '@dashboard/types/models';
import { categorizationBadge, importErrorLabel } from '@dashboard/lib/vesselImportLabels';
import { buildApplyPayload, draftTotals, isUncategorized, moveVessel, newFleetDraft, toDrafts, validateDrafts, type FleetDraft } from '@dashboard/lib/vesselImport';
import { ActionRow } from '../../../build/fields';
import { errorMessage } from '../../../build/useLiveResource';
import { AppText, Banner, Button, ConfirmDialog, StatusBadge, TextField } from '../../../components/ui';
import { Disclosure } from '../../../components/ui/Disclosure';
import { SelectField } from '../../../components/ui/SelectSheet';
import { useLocale } from '../../../i18n/LocaleContext';
import { useNotifications } from '../../../notifications/NotificationContext';
import { useTheme } from '../../../theme/ThemeContext';
import { radius, spacing } from '../../../theme/typography';

export interface FleetRecommendationsPanelProps {
  batch: VesselImportBatch;
  items: VesselImportItem[];
  recommendations: VesselImportFleetRecommendation[];
  /** A retry, stop, or apply changed the batch: the caller refreshes and resumes polling. */
  onChanged: () => void;
}

function recommendationKey(recommendations: VesselImportFleetRecommendation[]): string {
  return recommendations.map((r) => `${r.id}:${r.vesselIds.join(',')}:${r.appliedFleetId ?? ''}`).join('|');
}

/**
 * Fleet recommendations of an import batch (the dashboard's FleetRecommendationsPanel): the background run with a
 * stop button, the error with a retry, and editable fleet cards once the captain finished (rename, describe, move
 * repositories between fleets, add and remove fleets, apply after a confirmation).
 */
export function FleetRecommendationsPanel({ batch, items, recommendations, onChanged }: FleetRecommendationsPanelProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const status = batch.categorizationStatus ?? 'None';
  const key = `${recommendationKey(recommendations)}#${status}`;
  const [seenKey, setSeenKey] = useState(key);
  const [drafts, setDrafts] = useState<FleetDraft[]>(() => toDrafts(recommendations));
  const [editing, setEditing] = useState(status === 'Completed');
  if (seenKey !== key) {
    // The server-side recommendations changed (new run, applied): reset the editor.
    setSeenKey(key);
    setDrafts(toDrafts(recommendations));
    setEditing(status === 'Completed');
  }
  const [confirmApply, setConfirmApply] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [showErrors, setShowErrors] = useState(false);
  const [applied, setApplied] = useState<FleetRecommendationApplyResult | null>(null);

  const vesselNames = useMemo(() => {
    const map = new Map<string, string>();
    for (const item of items) {
      const id = item.vesselId ?? item.existingVesselId;
      if (id) map.set(id, item.proposedName);
    }
    return map;
  }, [items]);
  const errors = validateDrafts(drafts);
  const hasErrors = Object.keys(errors).length > 0;
  const { assignedCount, fleetCount } = draftTotals(drafts);
  const update = (k: string, patch: Partial<FleetDraft>) => setDrafts((ds) => ds.map((d) => (d.key === k ? { ...d, ...patch } : d)));

  async function run(action: () => Promise<unknown>, fallback: string) {
    setBusy(true);
    setError('');
    try {
      await action();
      onChanged();
    } catch (e) {
      setError(importErrorLabel(t, apiErrorCode(e), errorMessage(e) || fallback));
    } finally {
      setBusy(false);
    }
  }

  async function apply() {
    setConfirmApply(false);
    setBusy(true);
    setError('');
    try {
      const result = await applyFleetRecommendations(batch.id, buildApplyPayload(drafts));
      setApplied(result);
      setEditing(false);
      pushToast('success', t('{count, plural, one {Fleets applied: # vessel assigned.} other {Fleets applied: # vessels assigned.}}', { count: result.assignments.length }));
      onChanged();
    } catch (e) {
      setError(importErrorLabel(t, apiErrorCode(e), errorMessage(e) || t('Applying the fleets failed.')));
    } finally {
      setBusy(false);
    }
  }

  if (status === 'None') return null;
  const badge = categorizationBadge(t, status);

  return (
    <View style={styles.wrap} testID="fleet-recommendations">
      <View style={styles.head}>
        <AppText variant="subheading" muted accessibilityRole="header" style={styles.flex}>{t('Fleet recommendations')}</AppText>
        <StatusBadge label={badge.label} tone={badge.tone} />
      </View>
      {error ? <Banner tone="danger" title={error} /> : null}
      {status === 'Pending' || status === 'Running' ? (
        <View style={[styles.box, { borderColor: colors.info }]} accessibilityLiveRegion="polite">
          <AppText>
            {status === 'Pending'
              ? t('Fleet recommendations will start when the import finishes.')
              : t('A captain is reading the imported repositories and recommending fleets. This runs in the background and can take several minutes.')}
          </AppText>
          <AppText variant="caption" muted>{t('You can leave this screen; you are notified when it finishes. Reopen this batch from the import history to review the fleets.')}</AppText>
          <ActionRow>
            {batch.categorizationJobId ? <Button label={t('Open the Jobs page')} variant="ghost" onPress={() => router.push('/jobs' as Href)} /> : null}
            {batch.categorizationJobId && status === 'Running' ? (
              <Button label={t('Stop the captain')} variant="secondary" disabled={busy} onPress={() => void run(() => cancelJob(batch.categorizationJobId ?? ''), t('Could not stop the captain.'))} />
            ) : null}
          </ActionRow>
        </View>
      ) : null}
      {status === 'Failed' ? (
        <View style={[styles.box, { borderColor: colors.danger }]}>
          <AppText variant="label">{t('The captain could not recommend fleets.')}</AppText>
          {batch.categorizationError ? <AppText variant="caption" selectable>{batch.categorizationError}</AppText> : null}
          <Button
            label={busy ? t('Starting...') : t('Retry categorization')}
            disabled={busy}
            onPress={() => void run(() => categorizeVesselImport(batch.id), t('Could not start fleet categorization.'))}
            testID="fleet-recs-retry"
          />
        </View>
      ) : null}
      {status === 'Completed' || status === 'Applied' ? (
        <View>
          {status === 'Applied' && !editing ? (
            <View style={[styles.box, { borderColor: colors.success }]}>
              <AppText>
                {applied
                  ? t('{count, plural, one {Fleets applied: # vessel assigned.} other {Fleets applied: # vessels assigned.}}', { count: applied.assignments.length })
                  : t('These fleets were applied. Vessels in Uncategorized kept their fleet.')}
              </AppText>
              <Button label={t('Edit and apply again')} variant="secondary" onPress={() => setEditing(true)} />
            </View>
          ) : null}
          {editing ? (
            <AppText variant="caption" muted style={styles.gap}>
              {t('Review the captain\'s fleets. Rename them, move repositories between fleets, add or remove fleets, then apply. Existing fleets with the same name are reused.')}
            </AppText>
          ) : null}
          {drafts.map((d, index) => {
            const uncategorized = isUncategorized(d);
            const fieldError = showErrors ? errors[d.key] : undefined;
            return (
              <View key={d.key} style={[styles.card, { borderColor: colors.border, backgroundColor: colors.surface }]} testID={`fleet-rec-${index}`}>
                {editing ? (
                  <>
                    <TextField label={t('Fleet name')} value={d.name} onChangeText={(name) => update(d.key, { name })} error={fieldError ? t(fieldError) : null} testID={`fleet-rec-name-${index}`} />
                    <TextField label={t('Description')} value={d.description} onChangeText={(description) => update(d.key, { description })} multiline />
                  </>
                ) : (
                  <>
                    <AppText
                      variant="label"
                      color={d.appliedFleetId ? 'primary' : 'text'}
                      accessibilityRole={d.appliedFleetId ? 'link' : 'text'}
                      onPress={d.appliedFleetId ? () => router.push(`/fleets/${encodeURIComponent(d.appliedFleetId ?? '')}` as Href) : undefined}
                    >
                      {d.name}
                    </AppText>
                    {d.description ? <AppText variant="caption" muted>{d.description}</AppText> : null}
                  </>
                )}
                {uncategorized ? <AppText variant="caption" muted>{t('Repositories in Uncategorized are left without a fleet when you apply.')}</AppText> : null}
                {d.rationale ? (
                  <Disclosure title={t('Why the captain grouped these')}>
                    <AppText variant="caption">{d.rationale}</AppText>
                  </Disclosure>
                ) : null}
                <View accessibilityLabel={t('Repositories in this fleet')}>
                  {d.vesselIds.map((vesselId) => {
                    const name = vesselNames.get(vesselId) ?? vesselId;
                    return editing && drafts.length > 1 ? (
                      <SelectField
                        key={vesselId}
                        label={name}
                        value={d.key}
                        options={drafts.map((target) => ({ value: target.key, label: target.name.trim() || t('Unnamed fleet') }))}
                        onChange={(targetKey) => setDrafts((ds) => moveVessel(ds, vesselId, targetKey))}
                        closeLabel={t('Close')}
                        hint={t('Move {{name}} to another fleet', { name })}
                      />
                    ) : <AppText key={vesselId} variant="caption">{`• ${name}`}</AppText>;
                  })}
                  {d.vesselIds.length === 0 ? <AppText variant="caption" muted>{t('No repositories. Move some here or remove this fleet.')}</AppText> : null}
                </View>
                {editing ? (
                  <Button
                    label={t('Remove fleet')}
                    variant="ghost"
                    disabled={d.vesselIds.length > 0}
                    accessibilityHint={d.vesselIds.length > 0 ? t('Move its repositories to other fleets first') : undefined}
                    onPress={() => setDrafts((ds) => ds.filter((x) => x.key !== d.key))}
                  />
                ) : null}
              </View>
            );
          })}
          {editing ? (
            <View>
              <Button label={t('+ Add fleet')} variant="secondary" onPress={() => setDrafts((ds) => [...ds, newFleetDraft()])} />
              <AppText variant="caption" muted style={styles.gap}>
                {`${t('{count, plural, one {# fleet} other {# fleets}}', { count: fleetCount })} - ${t('{count, plural, one {# repository assigned} other {# repositories assigned}}', { count: assignedCount })}`}
              </AppText>
              {showErrors && fleetCount === 0 ? <AppText variant="caption" color="danger" accessibilityRole="alert">{t('Assign at least one repository to a named fleet.')}</AppText> : null}
              <Button
                label={busy ? t('Applying...') : t('Apply fleets')}
                disabled={busy}
                onPress={() => { setShowErrors(true); if (!hasErrors && fleetCount > 0) setConfirmApply(true); }}
                testID="fleet-recs-apply"
              />
            </View>
          ) : null}
        </View>
      ) : null}
      <ConfirmDialog
        open={confirmApply}
        title={t('Apply these fleets?')}
        message={t('{count, plural, one {# repository will be assigned to its recommended fleet. Missing fleets are created; fleets with the same name are reused.} other {# repositories will be assigned to their recommended fleets. Missing fleets are created; fleets with the same name are reused.}}', { count: assignedCount })}
        confirmLabel={t('Apply fleets')}
        cancelLabel={t('Cancel')}
        onConfirm={() => void apply()}
        onCancel={() => setConfirmApply(false)}
        testID="fleet-recs-confirm"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  wrap: { paddingHorizontal: spacing.lg, marginTop: spacing.xl },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginBottom: spacing.sm },
  box: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.md, gap: spacing.xs },
  card: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.md, gap: spacing.sm },
  gap: { marginVertical: spacing.sm },
});
