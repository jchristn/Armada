import { useEffect, useState } from 'react';
import { listUsers } from '@dashboard/api/client';
import type { Fleet, UserMaster, Vessel } from '@dashboard/types/models';
import {
  DEFAULT_BACKLOG_FILTERS,
  OBJECTIVE_BACKLOG_STATES,
  OBJECTIVE_EFFORTS,
  OBJECTIVE_KINDS,
  OBJECTIVE_PRIORITIES,
  OBJECTIVE_STATUSES,
  type BacklogFilters,
  type BacklogSortKey,
} from '@dashboard/lib/backlogUtils';
import { BottomSheet, Button, TextField } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useAuth } from '../../auth/AuthContext';
import { useLocale } from '../../i18n/LocaleContext';

export interface ObjectiveFilterSheetProps {
  open: boolean;
  onClose: () => void;
  filters: BacklogFilters;
  onChange: (next: BacklogFilters) => void;
  /** Admin "view records for a specific user" scope ('' = all users). */
  userScope: string;
  onUserScopeChange: (userId: string) => void;
  fleets: Fleet[];
  vessels: Vessel[];
}

function allOption<T extends string>(label: string, values: readonly T[]): SelectOption<'all' | T>[] {
  return [{ value: 'all', label }, ...values.map((v) => ({ value: v, label: v }))];
}

/** The Backlog page's filter card as a sheet: every dashboard filter, the sort, and the admin user scope. */
export function ObjectiveFilterSheet({ open, onClose, filters, onChange, userScope, onUserScopeChange, fleets, vessels }: ObjectiveFilterSheetProps) {
  const { t } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canScope = isAdmin || isTenantAdmin;
  const [users, setUsers] = useState<UserMaster[]>([]);
  const set = <K extends keyof BacklogFilters>(key: K, value: BacklogFilters[K]) => onChange({ ...filters, [key]: value });

  useEffect(() => {
    if (!open || !canScope) return undefined;
    let mounted = true;
    listUsers().then((r) => { if (mounted) setUsers(r?.objects ?? []); }).catch(() => { /* non-fatal: the scope stays on All users */ });
    return () => { mounted = false; };
  }, [open, canScope]);

  const close = t('Close');
  const sortOptions: SelectOption<BacklogSortKey>[] = [
    { value: 'rank', label: t('Sort by rank') },
    { value: 'priority', label: t('Sort by priority') },
    { value: 'updated', label: t('Sort by last updated') },
    { value: 'due', label: t('Sort by due date') },
  ];

  return (
    <BottomSheet open={open} title={t('Filters')} onClose={onClose} closeLabel={t('Done')} testID="objective-filters">
      {canScope ? (
        <SelectField
          label={t('View records for a specific user')}
          value={userScope}
          options={[{ value: '', label: t('All users') }, ...users.map((u) => {
            const name = [u.firstName, u.lastName].filter(Boolean).join(' ').trim();
            return { value: u.id, label: name ? `${name} (${u.email})` : u.email };
          })]}
          onChange={onUserScopeChange}
          closeLabel={close}
          testID="objective-filter-user"
        />
      ) : null}
      <SelectField label={t('Sort')} value={filters.sortBy} options={sortOptions} onChange={(v) => set('sortBy', v)} closeLabel={close} testID="objective-filter-sort" />
      <SelectField label={t('Kind')} value={filters.kind} options={allOption(t('All kinds'), OBJECTIVE_KINDS)} onChange={(v) => set('kind', v)} closeLabel={close} testID="objective-filter-kind" />
      <SelectField label={t('Priority')} value={filters.priority} options={allOption(t('All priorities'), OBJECTIVE_PRIORITIES)} onChange={(v) => set('priority', v)} closeLabel={close} testID="objective-filter-priority" />
      <SelectField label={t('Backlog State')} value={filters.backlogState} options={allOption(t('All backlog states'), OBJECTIVE_BACKLOG_STATES)} onChange={(v) => set('backlogState', v)} closeLabel={close} testID="objective-filter-backlog-state" />
      <SelectField label={t('Effort')} value={filters.effort} options={allOption(t('All effort sizes'), OBJECTIVE_EFFORTS)} onChange={(v) => set('effort', v)} closeLabel={close} testID="objective-filter-effort" />
      <SelectField label={t('Status')} value={filters.status} options={allOption(t('All lifecycle statuses'), OBJECTIVE_STATUSES)} onChange={(v) => set('status', v)} closeLabel={close} testID="objective-filter-status" />
      <SelectField
        label={t('Fleet')}
        value={filters.fleetId}
        options={[{ value: 'all', label: t('All fleets') }, ...fleets.map((f) => ({ value: f.id, label: f.name }))]}
        onChange={(v) => set('fleetId', v)}
        closeLabel={close}
        testID="objective-filter-fleet"
      />
      <SelectField
        label={t('Vessel')}
        value={filters.vesselId}
        options={[{ value: 'all', label: t('All vessels') }, ...vessels.map((v) => ({ value: v.id, label: v.name }))]}
        onChange={(v) => set('vesselId', v)}
        closeLabel={close}
        testID="objective-filter-vessel"
      />
      <TextField label={t('Owner filter')} value={filters.owner} onChangeText={(v) => set('owner', v)} autoCapitalize="none" testID="objective-filter-owner" />
      <TextField label={t('Target version filter')} value={filters.targetVersion} onChangeText={(v) => set('targetVersion', v)} autoCapitalize="none" testID="objective-filter-version" />
      <Button
        label={t('Clear Filters')}
        variant="secondary"
        onPress={() => onChange(DEFAULT_BACKLOG_FILTERS)}
        testID="objective-filters-clear"
      />
    </BottomSheet>
  );
}
