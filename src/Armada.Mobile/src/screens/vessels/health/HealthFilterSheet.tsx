import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import type { Fleet, VesselHealthSortField, VesselHealthStatus } from '@dashboard/types/models';
import { DEFAULT_HEALTH_FILTERS, DIVERGENCE_FILTERS, HEALTH_SORT_FIELDS, type HealthFilters, type TriState } from '@dashboard/lib/health/healthFilters';
import { HEALTH_STATUSES, statusLabel } from '@dashboard/lib/health/healthText';
import { ActionRow, SwitchField } from '../../../build/fields';
import { AppText, BottomSheet, Button, SegmentedControl, TextField } from '../../../components/ui';
import { SelectField } from '../../../components/ui/SelectSheet';
import { useLocale } from '../../../i18n/LocaleContext';
import { spacing } from '../../../theme/typography';
import { ChipGroup } from './ChipGroup';

const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

/** English labels of the sort fields (the dashboard's column headers). */
const SORT_LABELS: Record<VesselHealthSortField, string> = {
  VesselName: 'Vessel', FleetName: 'Fleet', OverallStatus: 'Overall', Divergence: 'Divergence', AheadOfDefault: 'Ahead',
  BehindDefault: 'Behind', IsDirty: 'Dirty', BranchCount: 'Branches', StaleBranchCount: 'Stale branches', OutdatedCount: 'Dependencies',
  OutdatedMajorCount: 'Major drift', VulnerableCount: 'Vulnerabilities', DependencyStatus: 'Dependency status', TestInfraStatus: 'Tests',
  CiStatus: 'CI', LastCommitUtc: 'Last commit', EvaluatedUtc: 'Evaluated',
};

function toggle(list: VesselHealthStatus[], value: VesselHealthStatus): VesselHealthStatus[] {
  const next = list.includes(value) ? list.filter((s) => s !== value) : [...list, value];
  return HEALTH_STATUSES.filter((s) => next.includes(s));
}

/** Number of filters (not sort) that differ from the defaults, for the filter button badge. */
export function activeFilterCount(f: HealthFilters): number {
  return [f.fleetId, f.overall.length, f.deps.length, f.tests.length, f.dirty, f.ci, f.divergence, f.minBranches, f.maxBranches, f.commitAfter, f.commitBefore]
    .filter(Boolean).length;
}

export interface HealthFilterSheetProps {
  open: boolean;
  filters: HealthFilters;
  fleets: Fleet[];
  onApply: (next: HealthFilters) => void;
  onClose: () => void;
}

/**
 * The Health tab's filters and sort in a bottom sheet (the dashboard's filter bar and sortable headers): fleet,
 * overall / dependency / test status, dirty, CI, divergence, branch count range, last commit date range, and sort.
 * Changes apply together with Apply and return to the first page.
 */
export function HealthFilterSheet({ open, filters, fleets, onApply, onClose }: HealthFilterSheetProps) {
  const { t } = useLocale();
  const [draft, setDraft] = useState<HealthFilters>(filters);
  const [seen, setSeen] = useState(filters);
  if (seen !== filters) {
    // Opening again after the filters changed elsewhere (summary tiles, search) starts from the current filters.
    setSeen(filters);
    setDraft(filters);
  }
  const patch = (p: Partial<HealthFilters>) => setDraft((d) => ({ ...d, ...p }));
  const statusChips = HEALTH_STATUSES.map((s) => ({ key: s, label: statusLabel(t, s) }));
  const intOk = (v: string) => /^\d*$/.test(v.trim());
  const dateOk = (v: string) => v.trim() === '' || DATE_RE.test(v.trim());
  const valid = intOk(draft.minBranches) && intOk(draft.maxBranches) && dateOk(draft.commitAfter) && dateOk(draft.commitBefore);
  const tri = (value: TriState, set: (v: TriState) => void, yes: string, no: string, label: string) => (
    <View>
      <AppText variant="label" style={styles.label}>{label}</AppText>
      <SegmentedControl<'any' | 'yes' | 'no'>
        label={label}
        value={value || 'any'}
        onChange={(v) => set(v === 'any' ? '' : v)}
        options={[{ value: 'any', label: t('Any') }, { value: 'yes', label: yes }, { value: 'no', label: no }]}
      />
    </View>
  );

  return (
    <BottomSheet open={open} title={t('Health filters')} onClose={onClose} closeLabel={t('Close')} testID="health-filters">
      <SelectField
        label={t('Fleet')}
        value={draft.fleetId}
        options={[{ value: '', label: t('All fleets') }, ...[...fleets].sort((a, b) => a.name.localeCompare(b.name)).map((f) => ({ value: f.id, label: f.name }))]}
        onChange={(fleetId) => patch({ fleetId })}
        closeLabel={t('Close')}
        testID="health-filter-fleet"
      />
      <AppText variant="label" style={styles.label}>{t('Overall')}</AppText>
      <ChipGroup chips={statusChips} selected={draft.overall} onToggle={(s) => patch({ overall: toggle(draft.overall, s) })} label={t('Overall status filter')} testID="health-filter-overall" />
      <AppText variant="label" style={styles.label}>{t('Dependencies')}</AppText>
      <ChipGroup chips={statusChips} selected={draft.deps} onToggle={(s) => patch({ deps: toggle(draft.deps, s) })} label={t('Dependency status filter')} />
      <AppText variant="label" style={styles.label}>{t('Tests')}</AppText>
      <ChipGroup chips={statusChips} selected={draft.tests} onToggle={(s) => patch({ tests: toggle(draft.tests, s) })} label={t('Test status filter')} />
      {tri(draft.dirty, (dirty) => patch({ dirty }), t('Yes'), t('No'), t('Dirty'))}
      {tri(draft.ci, (ci) => patch({ ci }), t('Has CI'), t('No CI'), t('CI'))}
      <SelectField
        label={t('Divergence')}
        value={draft.divergence}
        options={[
          { value: '', label: t('Any') },
          ...DIVERGENCE_FILTERS.map((d) => ({ value: d, label: t(d === 'Ahead' ? 'Ahead only' : d === 'Behind' ? 'Behind only' : d === 'Diverged' ? 'Diverged' : 'Even') })),
        ]}
        onChange={(divergence) => patch({ divergence })}
        closeLabel={t('Close')}
      />
      <View style={styles.pair}>
        <View style={styles.flex}>
          <TextField label={t('Minimum branch count')} value={draft.minBranches} onChangeText={(minBranches) => patch({ minBranches })} keyboardType="number-pad" error={intOk(draft.minBranches) ? null : t('Whole numbers only')} />
        </View>
        <View style={styles.flex}>
          <TextField label={t('Maximum branch count')} value={draft.maxBranches} onChangeText={(maxBranches) => patch({ maxBranches })} keyboardType="number-pad" error={intOk(draft.maxBranches) ? null : t('Whole numbers only')} />
        </View>
      </View>
      <View style={styles.pair}>
        <View style={styles.flex}>
          <TextField label={t('Last commit on or after')} value={draft.commitAfter} onChangeText={(commitAfter) => patch({ commitAfter })} placeholder="YYYY-MM-DD" autoCapitalize="none" error={dateOk(draft.commitAfter) ? null : t('Use YYYY-MM-DD')} />
        </View>
        <View style={styles.flex}>
          <TextField label={t('Last commit on or before')} value={draft.commitBefore} onChangeText={(commitBefore) => patch({ commitBefore })} placeholder="YYYY-MM-DD" autoCapitalize="none" error={dateOk(draft.commitBefore) ? null : t('Use YYYY-MM-DD')} />
        </View>
      </View>
      <SelectField
        label={t('Sort by')}
        value={draft.sortBy}
        options={HEALTH_SORT_FIELDS.map((f) => ({ value: f, label: t(SORT_LABELS[f]) }))}
        onChange={(sortBy) => patch({ sortBy })}
        closeLabel={t('Close')}
        testID="health-filter-sort"
      />
      <SwitchField label={t('Descending')} value={draft.sortDesc} onChange={(sortDesc) => patch({ sortDesc })} />
      <ActionRow>
        <Button
          label={t('Clear filters')}
          variant="ghost"
          onPress={() => setDraft({ ...DEFAULT_HEALTH_FILTERS, name: draft.name, sortBy: draft.sortBy, sortDesc: draft.sortDesc })}
          testID="health-filters-clear"
        />
        <Button
          label={t('Apply')}
          disabled={!valid}
          onPress={() => onApply({ ...draft, minBranches: draft.minBranches.trim(), maxBranches: draft.maxBranches.trim(), commitAfter: draft.commitAfter.trim(), commitBefore: draft.commitBefore.trim(), page: 1 })}
          testID="health-filters-apply"
        />
      </ActionRow>
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  label: { marginBottom: spacing.xs },
  pair: { flexDirection: 'row', gap: spacing.md },
});
