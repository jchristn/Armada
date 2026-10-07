import { useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { enumerateFleetActions, listVessels } from '@dashboard/api/client';
import type { FleetAction, Vessel } from '@dashboard/types/models';
import { buildFleetActionArguments, validateFleetAction, type FleetActionDraft } from '@dashboard/lib/askQuickActions';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Button } from '../ui/Button';
import { Icon } from '../ui/Icon';
import { SearchField } from '../ui/SearchField';
import { SelectField } from '../ui/SelectSheet';
import type { QuickActionFormProps } from './DispatchForm';

/**
 * The `/fleet-action` form (the dashboard's AskFleetActionForm): pick a saved fleet action and the vessels to run
 * it on. Submitting runs the MCP `run_fleet_action` tool through the thread's actions endpoint.
 */
export function FleetActionForm({ busy, onSubmit, onCancel }: QuickActionFormProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [actions, setActions] = useState<FleetAction[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [filter, setFilter] = useState('');
  const [draft, setDraft] = useState<FleetActionDraft>({ actionId: '', vesselIds: [] });
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    let active = true;
    Promise.all([enumerateFleetActions({ pageSize: 500 }), listVessels({ pageSize: 9999 })])
      .then(([a, v]) => {
        if (!active) return;
        setActions(a?.objects || []);
        setVessels((v?.objects || []).slice().sort((x, y) => x.name.localeCompare(y.name)));
      })
      .catch((err: unknown) => { if (active) setLoadError(err instanceof Error ? err.message : t('Failed to load fleet actions.')); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [t]);

  const visible = useMemo(() => {
    const term = filter.trim().toLowerCase();
    return term ? vessels.filter((v) => v.name.toLowerCase().includes(term)) : vessels;
  }, [vessels, filter]);
  const allVisibleSelected = visible.length > 0 && visible.every((v) => draft.vesselIds.includes(v.id));

  function toggle(id: string) {
    setDraft((d) => ({ ...d, vesselIds: d.vesselIds.includes(id) ? d.vesselIds.filter((x) => x !== id) : [...d.vesselIds, id] }));
  }

  function toggleAllVisible() {
    setDraft((d) => {
      const ids = new Set(d.vesselIds);
      if (allVisibleSelected) visible.forEach((v) => ids.delete(v.id));
      else visible.forEach((v) => ids.add(v.id));
      return { ...d, vesselIds: [...ids] };
    });
  }

  function submit() {
    const found = validateFleetAction(draft);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    onSubmit(buildFleetActionArguments(draft));
  }

  return (
    <View testID="ask-fleet-action-form">
      {loadError ? <AppText color="danger" accessibilityRole="alert" style={styles.gap}>{loadError}</AppText> : null}
      <SelectField
        label={t('Action')}
        value={draft.actionId}
        options={[{ value: '', label: loading ? t('Loading...') : t('Choose an action') }, ...actions.map((a) => ({ value: a.id, label: a.name }))]}
        onChange={(actionId) => setDraft((d) => ({ ...d, actionId }))}
        closeLabel={t('Close')}
        disabled={loading || busy}
        error={errors.action ? t(errors.action) : null}
        testID="fleet-action-action"
      />
      <AppText variant="label" style={styles.gap}>
        {t('Vessels')} <AppText variant="caption" muted>({t('{{count}} selected', { count: draft.vesselIds.length })})</AppText>
      </AppText>
      <SearchField value={filter} onChangeText={setFilter} placeholder={t('Filter vessels')} clearLabel={t('Clear')} />
      <Button
        label={allVisibleSelected ? t('Clear visible') : t('Select visible')}
        variant="ghost"
        onPress={toggleAllVisible}
        disabled={visible.length === 0 || busy}
      />
      <View accessibilityRole="list">
        {visible.map((v) => {
          const checked = draft.vesselIds.includes(v.id);
          return (
            <Pressable
              key={v.id}
              accessibilityRole="checkbox"
              accessibilityLabel={v.name}
              accessibilityState={{ checked, disabled: busy }}
              disabled={busy}
              onPress={() => toggle(v.id)}
              style={[styles.vessel, { borderBottomColor: colors.border }]}
            >
              <Icon name={checked ? 'checkbox' : 'square-outline'} color={checked ? 'primary' : 'textMuted'} />
              <AppText style={styles.flex}>{v.name}</AppText>
            </Pressable>
          );
        })}
        {!loading && visible.length === 0 ? <AppText muted>{t('No vessels match.')}</AppText> : null}
      </View>
      {errors.vessels ? <AppText variant="caption" color="danger" accessibilityRole="alert" style={styles.gap}>{t(errors.vessels)}</AppText> : null}
      <AppText variant="caption" muted style={styles.gap}>{t('Submitting this form is the confirmation; the run starts right away.')}</AppText>
      <Button label={busy ? t('Starting...') : t('Run action')} onPress={submit} disabled={busy || loading} busy={busy} testID="fleet-action-submit" />
      <Button label={t('Cancel')} variant="ghost" onPress={onCancel} disabled={busy} />
    </View>
  );
}

const styles = StyleSheet.create({
  gap: { marginBottom: spacing.sm },
  vessel: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: MIN_TOUCH, borderBottomWidth: StyleSheet.hairlineWidth },
  flex: { flex: 1 },
});
