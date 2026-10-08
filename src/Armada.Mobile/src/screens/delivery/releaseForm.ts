import type { Release, ReleaseStatus, ReleaseUpsertRequest, Vessel, WorkflowProfile } from '@dashboard/types/models';
import { joinList, RELEASE_STATUSES, splitList } from '@dashboard/lib/deliveryForms';
import { str, strOrNull, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { recordOptions, valueOptions } from '../../resource/lookups';
import type { Translate } from '../../i18n/LocaleContext';

/** Prefill a link can carry into /releases/new (ids are comma separated). */
export interface ReleasePrefill {
  vesselId?: string;
  workflowProfileId?: string;
  title?: string;
  version?: string;
  tagName?: string;
  summary?: string;
  notes?: string;
  status?: string;
  voyageIds?: string;
  missionIds?: string;
  checkRunIds?: string;
}

export function newReleaseValues(p: ReleasePrefill = {}): FormValues {
  const status = p.status && (RELEASE_STATUSES as string[]).includes(p.status) ? p.status : 'Draft';
  return {
    vesselId: p.vesselId ?? '', workflowProfileId: p.workflowProfileId ?? '', title: p.title || 'Draft Release', version: p.version ?? '', tagName: p.tagName ?? '',
    summary: p.summary ?? '', notes: p.notes ?? '', status,
    voyageIds: joinList(splitList(p.voyageIds ?? '')), missionIds: joinList(splitList(p.missionIds ?? '')), checkRunIds: joinList(splitList(p.checkRunIds ?? '')),
  };
}

export function releaseValues(r: Release): FormValues {
  return {
    vesselId: r.vesselId || '', workflowProfileId: r.workflowProfileId || '', title: r.title, version: r.version || '', tagName: r.tagName || '',
    summary: r.summary || '', notes: r.notes || '', status: r.status,
    voyageIds: joinList(r.voyageIds), missionIds: joinList(r.missionIds), checkRunIds: joinList(r.checkRunIds),
  };
}

export function releasePayload(v: FormValues, objectiveIds?: string[]): ReleaseUpsertRequest {
  const payload: ReleaseUpsertRequest = {
    vesselId: str(v, 'vesselId') || null,
    workflowProfileId: str(v, 'workflowProfileId') || null,
    title: strOrNull(v, 'title'),
    version: strOrNull(v, 'version'),
    tagName: strOrNull(v, 'tagName'),
    summary: strOrNull(v, 'summary'),
    notes: strOrNull(v, 'notes'),
    status: str(v, 'status') as ReleaseStatus,
    voyageIds: splitList(str(v, 'voyageIds')),
    missionIds: splitList(str(v, 'missionIds')),
    checkRunIds: splitList(str(v, 'checkRunIds')),
  };
  if (objectiveIds) payload.objectiveIds = objectiveIds;
  return payload;
}

export function releaseFields(t: Translate, vessels: Vessel[], profiles: WorkflowProfile[]): FormField[] {
  return [
    { kind: 'text', key: 'title', label: t('Title') },
    { kind: 'select', key: 'status', label: t('Status'), options: valueOptions(RELEASE_STATUSES, t) },
    { kind: 'select', key: 'vesselId', label: t('Vessel'), options: recordOptions(vessels, t('Resolve from linked work or select a vessel...')) },
    { kind: 'select', key: 'workflowProfileId', label: t('Workflow Profile'), options: recordOptions(profiles, t('Resolved default')) },
    { kind: 'text', key: 'version', label: t('Version'), placeholder: '1.2.3' },
    { kind: 'text', key: 'tagName', label: t('Tag Name'), placeholder: 'v1.2.3' },
    { kind: 'multiline', key: 'summary', label: t('Summary') },
    { kind: 'multiline', key: 'notes', label: t('Notes') },
    { kind: 'multiline', key: 'voyageIds', label: t('Voyage IDs') },
    { kind: 'multiline', key: 'missionIds', label: t('Mission IDs') },
    { kind: 'multiline', key: 'checkRunIds', label: t('Check Run IDs') },
  ];
}
