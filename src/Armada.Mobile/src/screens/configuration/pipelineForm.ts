import type { PipelineStage } from '@dashboard/types/models';
import { bool, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import type { Translate } from '../../i18n/LocaleContext';

/** A stage as the pipeline forms edit it (no ids or order; order is the list position). */
export interface StageEntry {
  personaName: string;
  isOptional: boolean;
  description: string;
  requiresReview: boolean;
  reviewDenyAction: 'RetryStage' | 'FailPipeline';
}

export function stageEntries(stages: PipelineStage[]): StageEntry[] {
  return [...(stages ?? [])].sort((a, b) => a.order - b.order).map((s) => ({
    personaName: s.personaName,
    isOptional: s.isOptional,
    description: s.description ?? '',
    requiresReview: s.requiresReview,
    reviewDenyAction: s.reviewDenyAction,
  }));
}

/** The stages payload the dashboard sends: blank personas dropped, order renumbered from 1. */
export function stagesPayload(stages: StageEntry[]): Partial<PipelineStage>[] {
  return stages
    .filter((s) => s.personaName.trim() !== '')
    .map((s, i) => ({ personaName: s.personaName.trim(), isOptional: s.isOptional, description: s.description || null, requiresReview: s.requiresReview, reviewDenyAction: s.reviewDenyAction, order: i + 1 }));
}

/** Moves a stage up (-1) or down (+1); out-of-range moves return the list unchanged. */
export function moveStage(stages: StageEntry[], index: number, direction: number): StageEntry[] {
  const target = index + direction;
  if (target < 0 || target >= stages.length) return stages;
  const next = [...stages];
  const temp = next[index];
  next[index] = next[target];
  next[target] = temp;
  return next;
}

export function blankStage(): StageEntry {
  return { personaName: '', isOptional: false, description: '', requiresReview: false, reviewDenyAction: 'RetryStage' };
}

export function stageValues(s: StageEntry): FormValues {
  return { personaName: s.personaName, description: s.description, isOptional: s.isOptional, requiresReview: s.requiresReview, reviewDenyAction: s.reviewDenyAction };
}

export function stageFromValues(v: FormValues): StageEntry {
  return {
    personaName: str(v, 'personaName'),
    description: str(v, 'description'),
    isOptional: bool(v, 'isOptional'),
    requiresReview: bool(v, 'requiresReview'),
    reviewDenyAction: str(v, 'reviewDenyAction') === 'FailPipeline' ? 'FailPipeline' : 'RetryStage',
  };
}

export function stageFields(t: Translate, personaNames: string[], values: FormValues): FormField[] {
  return [
    { kind: 'select', key: 'personaName', label: t('Persona Name'), required: true, placeholder: t('Select persona...'), options: [{ value: '', label: t('Select persona...') }, ...personaNames.map((n) => ({ value: n, label: n }))] },
    { kind: 'multiline', key: 'description', label: t('Description') },
    { kind: 'switch', key: 'isOptional', label: t('Optional') },
    { kind: 'switch', key: 'requiresReview', label: t('Review gate') },
    {
      kind: 'select', key: 'reviewDenyAction', label: t('On Deny'), disabled: !bool(values, 'requiresReview'),
      options: [{ value: 'RetryStage', label: t('Retry stage') }, { value: 'FailPipeline', label: t('Fail pipeline') }],
    },
  ];
}
