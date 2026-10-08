import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { listFleets, listVessels } from '@dashboard/api/client';
import type { Fleet, Vessel } from '@dashboard/types/models';
import { MAX_RUN_VESSELS } from '@dashboard/lib/fleetActionForm';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, Banner, BottomSheet, Button, ListRow, LoadingState, SearchField } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';

export interface VesselPickerSheetProps {
  open: boolean;
  title?: string;
  onClose: () => void;
  /** Called with the chosen vessel ids when the operator continues. */
  onPicked: (vesselIds: string[]) => void;
}

/**
 * Choose target vessels for a fleet action run (the dashboard's VesselPickerModal): every vessel, filtered by name or
 * path and by fleet, with select-all-visible and the 500-vessel limit.
 */
export function VesselPickerSheet(props: VesselPickerSheetProps) {
  const { t } = useLocale();
  return (
    <BottomSheet open={props.open} title={props.title ?? t('Choose vessels')} onClose={props.onClose} closeLabel={t('Close')} testID="vessel-picker">
      {props.open ? <VesselPickerContent onClose={props.onClose} onPicked={props.onPicked} /> : null}
    </BottomSheet>
  );
}

async function loadPickerData(): Promise<{ vessels: Vessel[]; fleets: Fleet[] }> {
  const [v, f] = await Promise.all([listVessels({ pageSize: 9999 }), listFleets({ pageSize: 9999 })]);
  return { vessels: v?.objects ?? [], fleets: f?.objects ?? [] };
}

/** The picker itself (inside VesselPickerSheet or RunFlowSheet). */
export function VesselPickerContent({ onClose, onPicked }: Pick<VesselPickerSheetProps, 'onClose' | 'onPicked'>) {
  const { t } = useLocale();
  const data = useLiveResource(loadPickerData, []);
  const vessels = useMemo(() => data.data?.vessels ?? [], [data.data]);
  const fleets = data.data?.fleets ?? [];
  const loading = data.loading;
  const error = data.error;
  const [search, setSearch] = useState('');
  const [fleetId, setFleetId] = useState('');
  const [selected, setSelected] = useState<string[]>([]);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return vessels.filter((v) => (!fleetId || v.fleetId === fleetId) && (!term || v.name.toLowerCase().includes(term) || (v.workingDirectory ?? '').toLowerCase().includes(term)));
  }, [vessels, search, fleetId]);
  const allVisibleSelected = filtered.length > 0 && filtered.every((v) => selected.includes(v.id));
  const tooMany = selected.length > MAX_RUN_VESSELS;
  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));
  const toggleAll = () => {
    if (allVisibleSelected) setSelected((s) => s.filter((id) => !filtered.some((v) => v.id === id)));
    else setSelected((s) => Array.from(new Set([...s, ...filtered.map((v) => v.id)])));
  };
  const fleetOptions: SelectOption<string>[] = [{ value: '', label: t('All Fleets') }, ...fleets.map((f) => ({ value: f.id, label: f.name }))];

  return (
    <View>
      <AppText muted style={styles.count} testID="vessel-picker-count">{t('{count, plural, one {# vessel selected} other {# vessels selected}}', { count: selected.length })}</AppText>
      <View style={styles.search}>
        <SearchField value={search} onChangeText={setSearch} placeholder={t('Vessel name or path contains...')} clearLabel={t('Clear')} testID="vessel-picker-search" />
      </View>
      <SelectField label={t('Filter vessels by fleet')} value={fleetId} options={fleetOptions} onChange={setFleetId} closeLabel={t('Close')} testID="vessel-picker-fleet" />
      {tooMany ? <Banner tone="danger" title={t('A run can target at most 500 vessels.')} /> : null}
      {error ? <Banner tone="danger" title={error} /> : null}
      {error ? <Button label={t('Retry')} variant="secondary" onPress={() => void data.refresh()} /> : null}
      {loading ? <LoadingState label={t('Loading...')} /> : null}
      {!loading && !error && vessels.length === 0 ? <AppText muted>{t('No vessels yet. Import repositories from the Vessels page first.')}</AppText> : null}
      {!loading && !error && vessels.length > 0 ? (
        <View>
          <ListRow
            title={t('Select all visible vessels')}
            icon={allVisibleSelected ? 'checkbox' : 'square-outline'}
            onPress={toggleAll}
            selected={allVisibleSelected}
            testID="vessel-picker-all"
          />
          {filtered.length === 0 ? <AppText muted style={styles.count}>{t('No vessels match the current filters.')}</AppText> : null}
          {filtered.map((v) => {
            const isSelected = selected.includes(v.id);
            const fleetName = v.fleetId ? fleets.find((f) => f.id === v.fleetId)?.name ?? v.fleetId : null;
            return (
              <ListRow
                key={v.id}
                testID={`vessel-picker-row-${v.name}`}
                title={v.name}
                subtitle={[fleetName, v.workingDirectory].filter(Boolean).join(' \u00b7 ') || null}
                icon={isSelected ? 'checkbox' : 'square-outline'}
                selected={isSelected}
                onPress={() => toggle(v.id)}
                accessibilityHint={t('Select {{name}}', { name: v.name })}
              />
            );
          })}
        </View>
      ) : null}
      <View style={styles.actions}>
        <Button label={t('Continue')} disabled={selected.length === 0 || tooMany} onPress={() => onPicked(selected)} testID="vessel-picker-continue" />
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  count: { marginBottom: spacing.sm },
  search: { marginHorizontal: -spacing.md },
  actions: { marginTop: spacing.lg },
});
