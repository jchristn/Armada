import { useMemo, useState, type ReactNode } from 'react';
import { View } from 'react-native';
import type { Fleet, ScopeEnum, Vessel } from '@dashboard/types/models';
import { canChooseScope, canEdit, resolveCreateScope, scopeLabel, type ScopeViewer } from '@dashboard/lib/scoping';
import { useAuth } from '../../auth/AuthContext';
import type { FormField, FormValues } from '../../components/resource/FormSheet';
import { str } from '../../components/resource/FormSheet';
import { DetailBody } from '../../components/resource/DetailParts';
import { MasterDetail } from '../../components/resource/Hub';
import { BottomSheet } from '../../components/ui/BottomSheet';
import type { StatusTone } from '../../components/ui/StatusBadge';
import type { Translate } from '../../i18n/LocaleContext';
import { useLocale } from '../../i18n/LocaleContext';
import { useLayout } from '../../navigation/useLayout';

/** The signed-in user as the scope rules see them (lib/scoping, the dashboard's ScopeViewer). */
export function useViewer(): ScopeViewer {
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const tenantId = user?.user?.tenantId;
  const userId = user?.user?.id;
  return useMemo(() => ({ isAdmin, isTenantAdmin, tenantId, userId }), [isAdmin, isTenantAdmin, tenantId, userId]);
}

/** Whether the viewer may edit a profile (profiles keep their ownership scope in ownershipScope). */
export function canEditProfile(viewer: ScopeViewer, p: { ownershipScope: ScopeEnum; tenantId: string | null; userId: string | null }): boolean {
  return canEdit(viewer, { scope: p.ownershipScope, tenantId: p.tenantId, userId: p.userId });
}

/** The Visibility badge (the dashboard's ScopeBadge). */
export function scopeBadge(t: Translate, scope: ScopeEnum): { label: string; tone: StatusTone } {
  return { label: t(scopeLabel(scope)), tone: scope === 'UserSpecific' ? 'pending' : 'info' };
}

/**
 * The Visibility field (the dashboard's ScopeSelect): admins choose tenant-wide or personal; regular users are locked
 * to personal and see it as a note.
 */
export function scopeField(t: Translate, viewer: ScopeViewer, key = 'scope'): FormField {
  if (!canChooseScope(viewer)) {
    return { kind: 'note', key, label: `${t('Visibility')}: ${t('Personal (only you can see and edit it)')}` };
  }
  return {
    kind: 'select',
    key,
    label: t('Visibility'),
    options: [
      { value: 'TenantWide', label: t('Tenant-wide (everyone in the tenant can use it)') },
      { value: 'UserSpecific', label: t('Personal (only you can see and edit it)') },
    ],
  };
}

/**
 * The scope a form saves: what an admin chose; for regular users personal on create and the record's own scope on
 * edit (the dashboard's locked ScopeSelect keeps the form value).
 */
export function scopeValue(viewer: ScopeViewer, values: FormValues, existing?: ScopeEnum | null, key = 'scope'): ScopeEnum {
  const chosen = str(values, key);
  const valid = chosen === 'UserSpecific' || chosen === 'TenantWide' ? chosen : null;
  if (existing && !canChooseScope(viewer)) return existing;
  return resolveCreateScope(viewer, valid);
}

export const PROFILE_SCOPES = ['Global', 'Fleet', 'Vessel'] as const;

/** Scope (Global / Fleet / Vessel) with the fleet or vessel picker it needs, as on the profile forms. */
export function profileScopeFields(t: Translate, values: FormValues, fleets: Fleet[], vessels: Vessel[]): FormField[] {
  const scope = str(values, 'scope');
  const out: FormField[] = [
    { kind: 'select', key: 'scope', label: t('Scope'), options: PROFILE_SCOPES.map((s) => ({ value: s, label: t(s) })) },
  ];
  if (scope === 'Fleet') {
    out.push({ kind: 'select', key: 'fleetId', label: t('Fleet'), placeholder: t('Select a fleet...'), options: [{ value: '', label: t('Select a fleet...') }, ...fleets.filter((f) => f.active !== false).map((f) => ({ value: f.id, label: f.name }))] });
  }
  if (scope === 'Vessel') {
    out.push({ kind: 'select', key: 'vesselId', label: t('Vessel'), placeholder: t('Select a vessel...'), options: [{ value: '', label: t('Select a vessel...') }, ...vessels.filter((v) => v.active !== false).map((v) => ({ value: v.id, label: v.name }))] });
  }
  return out;
}

/**
 * Selection for records without their own route (endpoints, harbors, memories): when list and detail fit side by
 * side the record shows beside the list; narrower panes show it in a sheet over the list. `isTablet` is that
 * side-by-side state (it follows the window, so a resize moves an open record between the pane and the sheet).
 */
export function useLocalSelection() {
  const { split } = useLayout();
  const [selected, setSelected] = useState<string | null>(null);
  return { selected, open: setSelected, clear: () => setSelected(null), isTablet: split };
}

/** The body of an in-place detail: the tablet pane scrolls itself; inside the phone sheet the sheet scrolls. */
export function LocalBody({ inSheet, children, testID }: { inSheet: boolean; children: ReactNode; testID?: string }) {
  if (inSheet) return <View testID={testID}>{children}</View>;
  return <DetailBody embedded testID={testID}>{children}</DetailBody>;
}

/**
 * A list with an in-place detail: beside it when the pane fits both, in a bottom sheet otherwise. One tree for both,
 * so resizing the window keeps the list (its scroll and search) and moves the open record between pane and sheet.
 */
export function LocalMasterDetail({ list, detail, title, open, onClose }: { list: ReactNode; detail: ReactNode | null; title: string; open: boolean; onClose: () => void }) {
  const { t } = useLocale();
  const { split } = useLayout();
  return (
    <>
      <MasterDetail list={list} detail={split && open ? detail : null} />
      <BottomSheet open={!split && open} title={title} onClose={onClose} closeLabel={t('Close')} testID="config-detail-sheet">
        {!split && open ? detail : null}
      </BottomSheet>
    </>
  );
}

export function personaPath(name: string): string {
  return `/personas/${encodeURIComponent(name)}`;
}

/** Template name options for persona forms. */
export function templateOptions(t: Translate, names: string[]) {
  return [{ value: '', label: t('Select a template...') }, ...names.map((n) => ({ value: n, label: n }))];
}

export function pipelinePath(name: string): string {
  return `/pipelines/${encodeURIComponent(name)}`;
}

export function promptTemplatePath(name: string): string {
  return `/prompt-templates/${encodeURIComponent(name)}`;
}
