import { useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Linking, StyleSheet, View } from 'react-native';
import { deleteVesselHealthOverride, getVesselHealth, setVesselHealthOverride } from '@dashboard/api/client';
import type { VesselHealthCriterion, VesselHealthDetail, VesselHealthOverride, VesselHealthStatus } from '@dashboard/types/models';
import {
  HEALTH_CRITERIA,
  HEALTH_STATUSES,
  OVERRIDE_CRITERIA,
  criterionLabel,
  describeFinding,
  driftLabel,
  formatCount,
  severityLabel,
  statusLabel,
} from '@dashboard/lib/health/healthText';
import { ActionRow, InfoRow } from '../../../build/fields';
import { errorMessage } from '../../../build/useLiveResource';
import { HubTabBar } from '../../../build/HubTabs';
import { AppText, Banner, BottomSheet, Button, ConfirmDialog, LoadingState, TextField } from '../../../components/ui';
import { SelectField } from '../../../components/ui/SelectSheet';
import { useLocale } from '../../../i18n/LocaleContext';
import { useNotifications } from '../../../notifications/NotificationContext';
import { useTheme } from '../../../theme/ThemeContext';
import { radius, spacing, typography } from '../../../theme/typography';
import { HealthBadge } from './HealthBadge';

export type HealthDetailSection = 'summary' | 'findings' | 'dependencies' | 'overrides' | 'json';

export interface HealthDetailSheetProps {
  vesselId: string;
  vesselName?: string | null;
  /** Default branch named as the divergence base. */
  defaultBranch?: string | null;
  initialSection?: HealthDetailSection;
  /** Tenant admin: re-evaluate and edit overrides (the server enforces this too). */
  canAdmin: boolean;
  onReevaluate?: (vesselId: string) => void;
  evaluationRunning?: boolean;
  /** Increment to reload (an evaluation finished). */
  refreshToken?: number;
  /** An override changed; the caller refreshes its list. */
  onChanged?: () => void;
  onClose: () => void;
}

const LAST_RUN_LABELS: Record<string, string> = { Passed: 'Passed', Failed: 'Failed', Canceled: 'Canceled', Cancelled: 'Cancelled' };

function normalize(detail: VesselHealthDetail): VesselHealthDetail {
  return { health: detail.health, findings: detail.findings ?? [], dependencies: detail.dependencies ?? [], overrides: detail.overrides ?? [] };
}

/**
 * The vessel health inspector (the dashboard's VesselHealthDetailModal) as a bottom sheet: summary numbers, every
 * criterion's finding as a sentence, outdated and vulnerable dependencies, manual overrides (tenant admins add,
 * edit, and remove them), and the raw JSON.
 */
export function HealthDetailSheet(props: HealthDetailSheetProps) {
  const { vesselId, vesselName, defaultBranch, initialSection = 'summary', canAdmin, onReevaluate, evaluationRunning = false, refreshToken = 0, onChanged, onClose } = props;
  const { t, locale, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { colors } = useTheme();
  const router = useRouter();
  const [detail, setDetail] = useState<VesselHealthDetail | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [section, setSection] = useState<HealthDetailSection>(initialSection);
  const [form, setForm] = useState<{ criterion: VesselHealthCriterion; status: VesselHealthStatus; note: string }>({ criterion: 'Overall', status: 'Pass', note: '' });
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);
  const [confirmRemove, setConfirmRemove] = useState<VesselHealthOverride | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setDetail(normalize(await getVesselHealth(vesselId)));
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setLoading(false);
    }
  }, [vesselId]);

  // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch keyed on the vessel and refresh token; it sets loading state first
  useEffect(() => { void load(); }, [load, refreshToken]);

  const overrides = useMemo(() => {
    const map = new Map<string, VesselHealthOverride>();
    for (const o of detail?.overrides ?? []) map.set(o.criterion, o);
    return map;
  }, [detail]);
  const findings = useMemo(() => {
    const order = new Map(HEALTH_CRITERIA.map((c, i) => [c, i]));
    return [...(detail?.findings ?? [])].sort((a, b) => (order.get(a.criterion) ?? 99) - (order.get(b.criterion) ?? 99));
  }, [detail]);

  const health = detail?.health;
  const name = health?.vesselName || vesselName || vesselId;
  const base = defaultBranch || t('the default branch');
  const overall = overrides.get('Overall');

  function startOverride(criterion: VesselHealthCriterion) {
    const existing = overrides.get(criterion);
    setForm({ criterion, status: existing?.status ?? 'Pass', note: existing?.note ?? '' });
    setFormError('');
    setSection('overrides');
  }

  async function saveOverride() {
    if (form.note.length > 4000) {
      setFormError(t('The note can be at most {{count}} characters.', { count: formatCount(locale, 4000) }));
      return;
    }
    setSaving(true);
    setFormError('');
    try {
      const updated = await setVesselHealthOverride(vesselId, form.criterion, form.status, form.note.trim() || null);
      setDetail(normalize(updated));
      pushToast('success', t('Override saved for {{criterion}}.', { criterion: criterionLabel(t, form.criterion) }));
      setForm((f) => ({ ...f, note: '' }));
      onChanged?.();
    } catch (e) {
      setFormError(errorMessage(e));
    } finally {
      setSaving(false);
    }
  }

  async function removeOverride(o: VesselHealthOverride) {
    setConfirmRemove(null);
    try {
      setDetail(normalize(await deleteVesselHealthOverride(vesselId, o.criterion)));
      pushToast('success', t('Override removed for {{criterion}}.', { criterion: criterionLabel(t, o.criterion) }));
      onChanged?.();
    } catch (e) {
      pushToast('error', t('Could not remove the override: {{message}}', { message: errorMessage(e) }));
    }
  }

  const yesNo = (v: boolean | null | undefined) => (v === null || v === undefined ? '-' : v ? t('Yes') : t('No'));
  const aheadBehind = (a: number | null | undefined, b: number | null | undefined) => (
    (a === null || a === undefined) && (b === null || b === undefined)
      ? '-'
      : t('{{ahead}} ahead, {{behind}} behind', { ahead: formatCount(locale, a ?? 0), behind: formatCount(locale, b ?? 0) })
  );

  const sections: { key: HealthDetailSection; label: string }[] = [
    { key: 'summary', label: t('Summary') },
    { key: 'findings', label: t('Findings') },
    { key: 'dependencies', label: t('Dependencies') },
    { key: 'overrides', label: t('Overrides') },
    { key: 'json', label: t('JSON') },
  ];

  return (
    <BottomSheet open title={name} onClose={onClose} closeLabel={t('Close')} testID="health-detail">
      <View style={styles.head}>
        {health ? <HealthBadge status={health.overallStatus} overridden={!!overall} /> : null}
        <AppText variant="caption" muted selectable>{vesselId}</AppText>
        <AppText variant="caption" muted>
          {health?.evaluatedUtc ? t('Evaluated {{when}}', { when: formatRelativeTime(health.evaluatedUtc) }) : t('Never evaluated')}
        </AppText>
      </View>
      <ActionRow>
        <Button label={t('Open vessel')} variant="secondary" onPress={() => { onClose(); router.push(`/vessels/${encodeURIComponent(vesselId)}` as Href); }} />
        {onReevaluate && canAdmin ? (
          <Button
            label={evaluationRunning ? t('Evaluating...') : t('Re-evaluate')}
            disabled={evaluationRunning}
            onPress={() => onReevaluate(vesselId)}
            testID="health-detail-reevaluate"
          />
        ) : null}
      </ActionRow>
      <View style={styles.tabs}><HubTabBar tabs={sections} value={section} onChange={setSection} label={t('Health detail sections')} /></View>

      {loading && !detail ? <LoadingState label={t('Loading health...')} /> : null}
      {error ? <Banner tone="danger" title={t('Could not load vessel health: {{message}}', { message: error })} /> : null}
      {error ? <Button label={t('Retry')} variant="secondary" onPress={() => void load()} /> : null}

      {detail && health && section === 'summary' ? (
        <View>
          {!health.id ? (
            <View style={styles.block}>
              <AppText>{t('This vessel has not been evaluated yet.')}</AppText>
              {onReevaluate && canAdmin ? <Button label={t('Evaluate now')} onPress={() => onReevaluate(vesselId)} disabled={evaluationRunning} /> : null}
            </View>
          ) : null}
          {health.errorCode ? <Banner tone="warning" title={describeFinding({ detailCode: health.errorCode }, t, locale)} /> : null}
          <View style={[styles.card, { borderColor: colors.border }]}>
            <InfoRow label={t('Fleet')} value={health.fleetName ?? health.fleetId} />
            <InfoRow label={t('Current branch')} value={health.currentBranch} mono />
            <InfoRow label={t('Versus {{base}}', { base })} value={aheadBehind(health.aheadOfDefault, health.behindDefault)} />
            <InfoRow label={t('Versus upstream')} value={aheadBehind(health.aheadOfUpstream, health.behindUpstream)} />
            <InfoRow label={t('Dirty')} value={yesNo(health.isDirty)} />
            <InfoRow label={t('Untracked files')} value={formatCount(locale, health.untrackedCount)} />
            <InfoRow
              label={t('Branches')}
              value={health.branchCount === null || health.branchCount === undefined ? '-' : t('{{count}} ({{stale}} stale, {{armada}} armada/*)', {
                count: formatCount(locale, health.branchCount),
                stale: formatCount(locale, health.staleBranchCount ?? 0),
                armada: formatCount(locale, health.armadaBranchCount ?? 0),
              })}
            />
            <InfoRow label={t('Primary language')} value={health.primaryLanguage} />
            <InfoRow label={t('Projects')} value={formatCount(locale, health.projectCount)} />
            <InfoRow
              label={t('Outdated packages')}
              value={health.outdatedCount === null || health.outdatedCount === undefined ? '-' : t('{{count}} ({{major}} major)', { count: formatCount(locale, health.outdatedCount), major: formatCount(locale, health.outdatedMajorCount ?? 0) })}
            />
            <InfoRow
              label={t('Vulnerable packages')}
              value={health.vulnerableCount === null || health.vulnerableCount === undefined ? '-' : t('{{count}} (max {{severity}})', { count: formatCount(locale, health.vulnerableCount), severity: severityLabel(t, health.maxVulnerabilitySeverity) })}
            />
            <InfoRow label={t('Last test run')} value={health.lastCheckRunStatus ? t(LAST_RUN_LABELS[health.lastCheckRunStatus] ?? health.lastCheckRunStatus) : '-'} />
            <InfoRow label={t('CI configuration')} value={yesNo(health.hasCiConfig)} />
            <InfoRow label={t('License')} value={yesNo(health.hasLicense)} />
            <InfoRow label={t('Readme')} value={yesNo(health.hasReadme)} />
            <InfoRow label={t('Readiness errors')} value={formatCount(locale, health.readinessErrorCount)} />
            <InfoRow label={t('Recent failed missions')} value={formatCount(locale, health.recentMissionFailureCount)} />
            <InfoRow label={t('Last commit')} value={health.lastCommitUtc ? `${formatRelativeTime(health.lastCommitUtc)} (${formatDateTime(health.lastCommitUtc)})` : '-'} />
            <InfoRow label={t('Evaluated')} value={health.evaluatedUtc ? formatDateTime(health.evaluatedUtc) : t('Never')} />
            <InfoRow label={t('Evaluation time')} value={health.evaluationDurationMs === null || health.evaluationDurationMs === undefined ? '-' : t('{{count}} ms', { count: formatCount(locale, health.evaluationDurationMs) })} />
            <InfoRow label={t('Dependencies checked')} value={health.dependenciesEvaluatedUtc ? formatDateTime(health.dependenciesEvaluatedUtc) : '-'} />
            <InfoRow label={t('Evaluated path')} value={health.evaluatedPath} mono />
          </View>
          <Button label={t('View findings')} variant="ghost" onPress={() => setSection('findings')} />
        </View>
      ) : null}

      {detail && section === 'findings' ? (
        findings.length === 0 ? <AppText muted>{t('No findings yet. Findings appear after the first evaluation.')}</AppText> : (
          <View testID="health-findings">
            {findings.map((f) => {
              const o = overrides.get(f.criterion);
              return (
                <View key={f.criterion} style={[styles.item, { borderColor: colors.border }]}>
                  <View style={styles.itemHead}>
                    <AppText variant="label" style={styles.flex}>{criterionLabel(t, f.criterion)}</AppText>
                    <HealthBadge status={f.status} />
                  </View>
                  {o ? (
                    <View style={styles.itemHead}>
                      <AppText variant="caption" muted>{t('Overridden to')}</AppText>
                      <HealthBadge status={o.status} overridden />
                    </View>
                  ) : null}
                  <AppText variant="caption">{describeFinding(f, t, locale)}</AppText>
                  {o?.note ? <AppText variant="caption" muted>{t('Note: {{note}}', { note: o.note })}</AppText> : null}
                  {canAdmin ? (
                    <Button label={o ? t('Edit override') : t('Override...')} variant="ghost" onPress={() => startOverride(f.criterion)} style={styles.start} />
                  ) : null}
                </View>
              );
            })}
          </View>
        )
      ) : null}

      {detail && section === 'dependencies' ? (
        detail.dependencies.length === 0 ? <AppText muted>{t('No outdated or vulnerable dependencies were found.')}</AppText> : (
          <View>
            {detail.dependencies.map((d) => (
              <View key={d.id ?? `${d.projectPath}-${d.packageName}-${d.currentVersion}`} style={[styles.item, { borderColor: colors.border }]}>
                <AppText variant="label" style={typography.mono}>{d.packageName}</AppText>
                <AppText variant="caption" muted>{`${d.ecosystem}${d.projectPath ? ` - ${d.projectPath}` : ''}`}</AppText>
                <AppText variant="caption" style={typography.mono}>{`${d.currentVersion || '-'}${d.latestVersion ? ` \u2192 ${d.latestVersion}` : ''}`}</AppText>
                <View style={styles.itemHead}>
                  {d.drift && d.drift !== 'None' ? <AppText variant="caption" color="warning">{driftLabel(t, d.drift)}</AppText> : null}
                  {d.isVulnerable ? <AppText variant="caption" color="danger">{severityLabel(t, d.severity)}</AppText> : null}
                  {d.advisoryUrl ? (
                    <AppText variant="caption" color="primary" accessibilityRole="link" onPress={() => { if (d.advisoryUrl) void Linking.openURL(d.advisoryUrl); }}>{t('Advisory')}</AppText>
                  ) : null}
                </View>
              </View>
            ))}
          </View>
        )
      ) : null}

      {detail && section === 'overrides' ? (
        <View>
          {detail.overrides.length === 0 ? (
            <AppText muted style={styles.gap}>{t('No overrides. An override sets a manual status for one criterion or for Overall.')}</AppText>
          ) : detail.overrides.map((o) => (
            <View key={o.criterion} style={[styles.item, { borderColor: colors.border }]} testID={`health-override-${o.criterion}`}>
              <View style={styles.itemHead}>
                <AppText variant="label" style={styles.flex}>{criterionLabel(t, o.criterion)}</AppText>
                <HealthBadge status={o.status} overridden />
              </View>
              {o.lastUpdateUtc ? <AppText variant="caption" muted>{formatRelativeTime(o.lastUpdateUtc)}</AppText> : null}
              {o.note ? <AppText variant="caption">{o.note}</AppText> : null}
              {canAdmin ? (
                <View style={styles.itemHead}>
                  <Button label={t('Edit')} variant="ghost" onPress={() => startOverride(o.criterion)} />
                  <Button label={t('Remove')} variant="danger" onPress={() => setConfirmRemove(o)} testID={`health-override-remove-${o.criterion}`} />
                </View>
              ) : null}
            </View>
          ))}
          {canAdmin ? (
            <View style={styles.form}>
              <AppText variant="subheading" muted accessibilityRole="header" style={styles.gap}>
                {overrides.has(form.criterion) ? t('Edit override') : t('Add override')}
              </AppText>
              {formError ? <Banner tone="danger" title={formError} /> : null}
              <SelectField
                label={t('Criterion')}
                value={form.criterion}
                options={OVERRIDE_CRITERIA.map((c) => ({ value: c, label: criterionLabel(t, c) }))}
                onChange={(criterion) => setForm({ ...form, criterion })}
                closeLabel={t('Close')}
                testID="health-override-criterion"
              />
              <SelectField
                label={t('Status')}
                value={form.status}
                options={HEALTH_STATUSES.map((s) => ({ value: s, label: statusLabel(t, s) }))}
                onChange={(status) => setForm({ ...form, status })}
                closeLabel={t('Close')}
                testID="health-override-status"
              />
              <TextField
                label={t('Note')}
                value={form.note}
                onChangeText={(note) => setForm({ ...form, note })}
                placeholder={t('Why this status is set by hand (optional)')}
                multiline
                maxLength={4000}
                testID="health-override-note"
              />
              <Button label={saving ? t('Saving...') : t('Save override')} busy={saving} onPress={() => void saveOverride()} testID="health-override-save" />
            </View>
          ) : <AppText muted>{t('Only tenant administrators can change overrides.')}</AppText>}
        </View>
      ) : null}

      {detail && section === 'json' ? (
        <View style={[styles.code, { backgroundColor: colors.background, borderColor: colors.border }]}>
          <AppText selectable style={typography.mono}>{JSON.stringify(detail, null, 2)}</AppText>
        </View>
      ) : null}

      <ConfirmDialog
        open={confirmRemove !== null}
        title={t('Remove override')}
        message={confirmRemove ? t('Remove the {{criterion}} override? The evaluated status applies again.', { criterion: criterionLabel(t, confirmRemove.criterion) }) : ''}
        confirmLabel={t('Remove')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { if (confirmRemove) void removeOverride(confirmRemove); }}
        onCancel={() => setConfirmRemove(null)}
        testID="health-override-remove-confirm"
      />
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  head: { gap: spacing.xs, marginBottom: spacing.md },
  block: { gap: spacing.sm, marginBottom: spacing.md },
  card: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, overflow: 'hidden', marginBottom: spacing.md },
  item: { borderBottomWidth: StyleSheet.hairlineWidth, paddingVertical: spacing.sm, gap: spacing.xs },
  itemHead: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: spacing.sm },
  start: { alignSelf: 'flex-start', marginBottom: 0 },
  gap: { marginVertical: spacing.sm },
  form: { marginTop: spacing.lg },
  tabs: { marginBottom: spacing.md },
  code: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md },
});
