import { useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import type { Captain, Fleet, Pipeline, VesselImportCandidateStatus, VesselImportHint, VesselImportItem } from '@dashboard/types/models';
import { CANDIDATE_STATUSES, CANDIDATE_STATUS_META, IMPORTABLE_STATUSES, candidateStatusBadge, hintLabel } from '@dashboard/lib/vesselImportLabels';
import { IMPORT_LANDING_MODES, isCaptainAvailable, validateCategorization, type CategorizationOptions } from '@dashboard/lib/vesselImport';
import { ActionRow, SwitchField } from '../../../build/fields';
import { AppText, Banner, Button, Icon, SearchField, StatusBadge, TextField } from '../../../components/ui';
import { SelectField } from '../../../components/ui/SelectSheet';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing, typography } from '../../../theme/typography';
import { ChipGroup } from '../health/ChipGroup';

/** Rows shown before "Show more" (the dashboard pages its table by 50). */
const PAGE = 50;

export interface ImportDefaults {
  fleetId: string;
  pipelineId: string;
  landingMode: string;
}

export interface ImportReviewStepProps {
  candidates: VesselImportItem[];
  truncated: boolean;
  hints: VesselImportHint[];
  selected: string[];
  onSelectedChange: (paths: string[]) => void;
  fleets: Fleet[];
  pipelines: Pipeline[];
  defaults: ImportDefaults;
  onDefaultsChange: (defaults: ImportDefaults) => void;
  categorization: CategorizationOptions;
  onCategorizationChange: (value: CategorizationOptions) => void;
  captains: Captain[];
  captainsLoading: boolean;
  defaultPrompt: string;
  defaultPromptError: string;
  timeoutMinutes?: number;
  showCategorizationErrors: boolean;
}

/**
 * Review step of the import wizard: candidates with status chips, search, and selection (tap to select an importable
 * candidate), the defaults for the new vessels (fleet, pipeline, landing mode), and the optional captain-driven fleet
 * recommendations.
 */
export function ImportReviewStep(props: ImportReviewStepProps) {
  const { candidates, truncated, hints, selected, onSelectedChange, fleets, pipelines, defaults, onDefaultsChange, categorization, onCategorizationChange } = props;
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const [status, setStatus] = useState<VesselImportCandidateStatus | ''>('');
  const [search, setSearch] = useState('');
  const [shown, setShown] = useState(PAGE);

  const counts = useMemo(() => {
    const c: Partial<Record<VesselImportCandidateStatus, number>> = {};
    for (const item of candidates) c[item.candidateStatus] = (c[item.candidateStatus] ?? 0) + 1;
    return c;
  }, [candidates]);
  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return candidates.filter((c) => (!status || c.candidateStatus === status)
      && (!term || c.path.toLowerCase().includes(term) || c.proposedName.toLowerCase().includes(term)));
  }, [candidates, status, search]);
  const selectable = (c: VesselImportItem) => IMPORTABLE_STATUSES.includes(c.candidateStatus);
  const filteredSelectable = filtered.filter(selectable);
  const allFilteredSelected = filteredSelectable.length > 0 && filteredSelectable.every((c) => selected.includes(c.path));
  const toggle = (path: string) => onSelectedChange(selected.includes(path) ? selected.filter((p) => p !== path) : [...selected, path]);

  const chips = [
    { key: 'all', label: t('All'), count: candidates.length },
    ...CANDIDATE_STATUSES.filter((s) => (counts[s] ?? 0) > 0).map((s) => ({ key: s, label: t(CANDIDATE_STATUS_META[s].label), count: counts[s] ?? 0 })),
  ];

  const catErrors = validateCategorization(categorization);
  const availableCount = props.captains.filter(isCaptainAvailable).length;
  const selectedCaptain = props.captains.find((c) => c.id === categorization.captainId) ?? null;

  return (
    <View testID="import-review">
      {hints.length > 0 || truncated ? (
        <Banner
          tone="info"
          title={[...hints.map((h) => hintLabel(t, h.code, h.message)), ...(truncated && !hints.some((h) => h.code === 'CandidateLimitReached') ? [hintLabel(t, 'CandidateLimitReached', '')] : [])].join('\n')}
        />
      ) : null}
      <ChipGroup
        scroll
        chips={chips}
        selected={[status || 'all']}
        onToggle={(key) => { setStatus(key === 'all' ? '' : key as VesselImportCandidateStatus); setShown(PAGE); }}
        label={t('Filter candidates by status')}
        testID="import-status"
      />
      <SearchField value={search} onChangeText={(v) => { setSearch(v); setShown(PAGE); }} placeholder={t('Path or name contains...')} clearLabel={t('Clear')} />
      <ActionRow>
        <Button label={t('Select all new')} variant="secondary" onPress={() => onSelectedChange(candidates.filter((c) => c.candidateStatus === 'New').map((c) => c.path))} />
        <Button
          label={allFilteredSelected ? t('Clear these') : t('Select these')}
          variant="ghost"
          disabled={filteredSelectable.length === 0}
          onPress={() => (allFilteredSelected
            ? onSelectedChange(selected.filter((p) => !filteredSelectable.some((c) => c.path === p)))
            : onSelectedChange(Array.from(new Set([...selected, ...filteredSelectable.map((c) => c.path)]))))}
        />
        <Button label={t('Clear selection')} variant="ghost" disabled={selected.length === 0} onPress={() => onSelectedChange([])} />
      </ActionRow>
      <AppText variant="caption" muted style={styles.pad}>{t('{count, plural, one {# selected} other {# selected}}', { count: selected.length })}</AppText>

      <View style={[styles.list, { borderColor: colors.border }]}>
        {filtered.length === 0 ? <AppText muted style={styles.empty}>{t('No candidates match the current filters.')}</AppText> : null}
        {filtered.slice(0, shown).map((c) => {
          const can = selectable(c);
          const on = selected.includes(c.path);
          const badge = candidateStatusBadge(t, c.candidateStatus);
          const origin = `${c.remoteUrl || t('(no origin)')} - ${c.defaultBranch || '-'}`;
          const existing = c.candidateStatus === 'AlreadyOnboarded' && c.existingVesselId ? c.existingVesselId : null;
          return (
            <View key={c.id || c.path} style={[styles.candidate, { borderBottomColor: colors.border, backgroundColor: on ? colors.surfaceRaised : colors.surface }]}>
            <Pressable
              testID={`import-candidate-${c.proposedName}`}
              accessibilityRole="checkbox"
              accessibilityLabel={`${c.proposedName}, ${badge.label}, ${c.path}, ${origin}`}
              accessibilityState={{ checked: on, disabled: !can }}
              disabled={!can}
              onPress={() => toggle(c.path)}
              style={[styles.item, { opacity: can ? 1 : 0.65 }]}
            >
              <Icon name={on ? 'checkbox' : 'square-outline'} color={on ? 'primary' : 'textMuted'} />
              <View style={styles.flex}>
                <View style={styles.head}>
                  <AppText variant="label" style={styles.flex} numberOfLines={1}>{c.proposedName}</AppText>
                  <StatusBadge label={badge.label} tone={badge.tone} />
                </View>
                <AppText variant="caption" muted style={typography.mono} numberOfLines={2}>{c.path}</AppText>
                <AppText variant="caption" muted numberOfLines={1}>{origin}</AppText>
              </View>
            </Pressable>
            {/* Outside the checkbox: a link inside it would be unreachable for VoiceOver. */}
            {existing ? (
              <AppText
                variant="caption"
                color="primary"
                accessibilityRole="link"
                style={styles.existing}
                onPress={() => router.push(`/vessels/${encodeURIComponent(existing)}` as Href)}
                testID={`import-candidate-${c.proposedName}-existing`}
              >
                {t('Open existing vessel')}
              </AppText>
            ) : null}
            </View>
          );
        })}
      </View>
      {filtered.length > shown ? <Button label={t('Show more')} variant="ghost" onPress={() => setShown((n) => n + PAGE)} /> : null}

      <AppText variant="subheading" muted accessibilityRole="header" style={styles.section}>{t('Defaults for the new vessels')}</AppText>
      <View style={styles.pad}>
        <SelectField
          label={t('Fleet')}
          value={defaults.fleetId}
          options={[{ value: '', label: t('No fleet') }, ...fleets.map((f) => ({ value: f.id, label: f.name }))]}
          onChange={(fleetId) => onDefaultsChange({ ...defaults, fleetId })}
          closeLabel={t('Close')}
          testID="import-default-fleet"
        />
        <SelectField
          label={t('Default Pipeline')}
          value={defaults.pipelineId}
          options={[{ value: '', label: t('None (WorkerOnly)') }, ...pipelines.map((p) => ({ value: p.id, label: p.name }))]}
          onChange={(pipelineId) => onDefaultsChange({ ...defaults, pipelineId })}
          closeLabel={t('Close')}
        />
        <SelectField
          label={t('Landing Mode')}
          value={defaults.landingMode}
          options={IMPORT_LANDING_MODES.map((m) => ({ value: m.value, label: t(m.label) }))}
          onChange={(landingMode) => onDefaultsChange({ ...defaults, landingMode })}
          closeLabel={t('Close')}
          testID="import-default-landing"
        />
        <AppText variant="caption" muted>{t('Imported vessels use the repository origin as the remote and the discovered folder as the working directory. The checkout is never deleted when a vessel is removed.')}</AppText>
      </View>

      <AppText variant="subheading" muted accessibilityRole="header" style={styles.section}>{t('Fleet recommendations')}</AppText>
      <View style={styles.pad}>
        <SwitchField
          label={t('Recommend fleets with a captain')}
          value={categorization.enabled}
          onChange={(enabled) => onCategorizationChange({ ...categorization, enabled, prompt: categorization.prompt || props.defaultPrompt })}
          hint={t('After the vessels are created, a captain reads every imported repository, works out what each one does, and suggests a set of fleets. It runs in the background; you can review and edit the fleets before applying them.')}
          testID="import-categorize"
        />
        {categorization.enabled ? (
          <View>
            <SelectField
              label={t('Captain')}
              value={categorization.captainId ?? ''}
              placeholder={props.captainsLoading ? t('Loading captains...') : t('Select a captain')}
              options={props.captains.map((c) => ({ value: c.id, label: `${c.name} (${c.state})`, disabled: !isCaptainAvailable(c) }))}
              onChange={(captainId) => onCategorizationChange({ ...categorization, captainId: captainId || null })}
              closeLabel={t('Close')}
              error={props.showCategorizationErrors && catErrors.captain ? t(catErrors.captain) : null}
              hint={props.captains.length === 0 && !props.captainsLoading
                ? t('No captains yet. Create one under Captains first.')
                : `${availableCount === 0 ? t('Every captain is busy right now. Only idle captains can be chosen.') : t('Only idle captains can be chosen; busy ones are listed but disabled.')}${selectedCaptain?.model ? ` ${t('Model: {{model}}', { model: selectedCaptain.model })}` : ''}`}
              testID="import-categorize-captain"
            />
            <TextField
              label={t('Instructions for the captain')}
              value={categorization.prompt}
              onChangeText={(prompt) => onCategorizationChange({ ...categorization, prompt })}
              multiline
              error={props.showCategorizationErrors && catErrors.prompt ? t(catErrors.prompt) : null}
              hint={`${props.defaultPromptError
                ? t('The default instructions could not be loaded: {{error}}', { error: props.defaultPromptError })
                : t('Pre-filled from the import.fleet_categorization prompt (Configuration > Prompts). Armada always adds the output format, so editing this text cannot break the result.')}${props.timeoutMinutes ? ` ${t('{count, plural, one {The captain has up to # minute.} other {The captain has up to # minutes.}}', { count: props.timeoutMinutes })}` : ''}`}
            />
            <Button
              label={t('Reset to default')}
              variant="ghost"
              disabled={categorization.prompt === props.defaultPrompt || !props.defaultPrompt}
              onPress={() => onCategorizationChange({ ...categorization, prompt: props.defaultPrompt })}
            />
            <SwitchField
              label={t('Apply recommendations automatically')}
              value={categorization.applyAutomatically}
              onChange={(applyAutomatically) => onCategorizationChange({ ...categorization, applyAutomatically })}
              hint={categorization.applyAutomatically
                ? t('Fleets are created (or reused by name) and vessels assigned as soon as the captain finishes.')
                : t('You review, edit, and apply the recommended fleets yourself when the captain finishes.')}
            />
          </View>
        ) : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  pad: { paddingHorizontal: spacing.lg },
  section: { marginHorizontal: spacing.lg, marginTop: spacing.xl, marginBottom: spacing.sm, textTransform: 'uppercase' },
  list: { borderTopWidth: StyleSheet.hairlineWidth, borderBottomWidth: StyleSheet.hairlineWidth, marginTop: spacing.sm },
  empty: { padding: spacing.lg },
  candidate: { borderBottomWidth: StyleSheet.hairlineWidth, borderRadius: radius.sm },
  item: { flexDirection: 'row', gap: spacing.md, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, minHeight: MIN_TOUCH, alignItems: 'flex-start' },
  // Lines up with the candidate's text (after the checkbox icon) and keeps a full-size touch target.
  existing: { marginLeft: spacing.lg + 22 + spacing.md, minHeight: MIN_TOUCH, textAlignVertical: 'center', paddingVertical: spacing.sm },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
});
