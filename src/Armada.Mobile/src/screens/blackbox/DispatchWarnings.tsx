import { useRouter, type Href } from 'expo-router';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { previewMissionOverlap } from '@dashboard/api/client';
import type { MissionOverlapPreview } from '@dashboard/types/blackbox';
import { AppText, BottomSheet, Button, ListRow, StatusBadge } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { canPreview, overlapKindLabel, previewWarnings, warningCount, type DispatchWarnings } from './insightModel';

/** How long typing must pause before the overlap preview runs. */
export const OVERLAP_PREVIEW_DEBOUNCE_MS = 600;

export interface OverlapPreviewState {
  preview: MissionOverlapPreview | null;
  loading: boolean;
  error: string | null;
}

/**
 * A debounced overlap preview (POST /api/v1/vessels/{id}/overlap-preview, which includes the brief lint) of the brief
 * being written; nothing runs without a vessel and some text. Stale responses are dropped.
 */
export function useOverlapPreview(vesselId: string, title: string, description: string, delayMs = OVERLAP_PREVIEW_DEBOUNCE_MS): OverlapPreviewState {
  // The last settled preview and the brief it was for; anything else is still loading (or disabled).
  const [settled, setSettled] = useState<{ key: string; preview: MissionOverlapPreview | null; error: string | null } | null>(null);
  const enabled = canPreview(vesselId, title, description);
  const key = JSON.stringify([vesselId, title, description]);
  useEffect(() => {
    if (!enabled) return undefined;
    let cancelled = false;
    const timer = setTimeout(() => {
      previewMissionOverlap(vesselId, { title, description })
        .then((preview) => { if (!cancelled) setSettled({ key, preview, error: null }); })
        .catch((e: unknown) => { if (!cancelled) setSettled({ key, preview: null, error: errorMessage(e, 'Request failed.') }); });
    }, delayMs);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [enabled, key, vesselId, title, description, delayMs]);
  if (!enabled) return { preview: null, loading: false, error: null };
  if (settled?.key !== key) return { preview: settled?.preview ?? null, loading: true, error: null };
  return { preview: settled.preview, loading: false, error: settled.error };
}

/** Overlap warnings (each linked to the overlapping mission) and brief lint findings, as rows. */
export function DispatchWarningList({ warnings, testID = 'dispatch-warnings' }: { warnings: DispatchWarnings; testID?: string }) {
  const { t } = useLocale();
  const router = useRouter();
  return (
    <View testID={testID}>
      {warnings.overlap.map((w, index) => (
        <ListRow
          key={`overlap-${index}`}
          testID={`${testID}-overlap-${index}`}
          icon="git-merge-outline"
          title={w.message}
          subtitle={[
            t(overlapKindLabel(w.kind)),
            w.overlappingMissionTitle || w.overlappingMissionId || null,
            w.directory || null,
            w.fileCount ? t('{{count}} overlapping file(s)', { count: w.fileCount }) : null,
            w.serialized ? t('Chained after the overlapping mission') : null,
          ].filter(Boolean).join(' - ')}
          onPress={w.overlappingMissionId ? () => router.push(`/missions/${w.overlappingMissionId}` as Href) : undefined}
        />
      ))}
      {warnings.lint.map((f, index) => (
        <ListRow
          key={`lint-${index}`}
          testID={`${testID}-lint-${index}`}
          icon="document-text-outline"
          title={f.message}
          subtitle={[f.ruleId, f.suggestion, f.excerpt ? `"${f.excerpt}"` : null].filter(Boolean).join(' - ')}
        />
      ))}
    </View>
  );
}

/**
 * The live preview under a dispatch form: checking, then the warnings (or that none were found). Warnings never
 * block dispatch.
 */
export function OverlapPreviewPanel({ state, testID = 'overlap-preview' }: { state: OverlapPreviewState; testID?: string }) {
  const { t } = useLocale();
  const warnings = previewWarnings(state.preview);
  const count = warningCount(warnings);
  if (!state.preview && !state.loading && !state.error) return null;
  return (
    <View style={styles.panel} testID={testID} accessibilityLiveRegion="polite">
      <View style={styles.head}>
        <AppText variant="label" style={styles.flex}>{t('Overlap and brief check')}</AppText>
        {state.loading ? <AppText variant="caption" muted testID={`${testID}-loading`}>{t('Checking...')}</AppText> : null}
        {!state.loading && state.preview ? (
          <StatusBadge label={count > 0 ? t('{{count}} warning(s)', { count }) : t('No warnings')} tone={count > 0 ? 'warning' : 'info'} />
        ) : null}
      </View>
      {state.error ? <AppText variant="caption" color="danger" testID={`${testID}-error`}>{t('Preview unavailable: {{message}}', { message: state.error })}</AppText> : null}
      {state.preview && count > 0 ? <DispatchWarningList warnings={warnings} testID={`${testID}-list`} /> : null}
      {state.preview?.suggestedDependsOnMissionId ? (
        <AppText variant="caption" muted testID={`${testID}-suggested`}>
          {state.preview.serializeOverlappingMissions
            ? t('This vessel chains overlapping missions: this one will wait for {{id}}.', { id: state.preview.suggestedDependsOnMissionId })
            : t('Consider making this mission depend on {{id}}.', { id: state.preview.suggestedDependsOnMissionId })}
        </AppText>
      ) : null}
      <AppText variant="caption" muted>{t('Warnings never block dispatch.')}</AppText>
    </View>
  );
}

/**
 * The warnings a create returned (mission or voyage), in a sheet: shown instead of navigating straight to the new
 * item, so they can be read; Continue opens it.
 */
export function DispatchWarningsSheet({ warnings, onContinue, onClose, continueLabel, testID = 'dispatch-warnings-sheet' }: {
  warnings: DispatchWarnings | null;
  onContinue: () => void;
  onClose: () => void;
  continueLabel?: string;
  testID?: string;
}) {
  const { t } = useLocale();
  const open = warningCount(warnings) > 0;
  return (
    <BottomSheet open={open} title={t('Dispatched with warnings')} onClose={onClose} closeLabel={t('Close')} testID={testID}>
      {open && warnings ? (
        <View style={styles.gap}>
          <AppText muted>{t('Armada dispatched the work. Review these warnings; they do not stop it.')}</AppText>
          <DispatchWarningList warnings={warnings} testID={`${testID}-list`} />
          <Button label={continueLabel ?? t('Continue')} onPress={onContinue} testID={`${testID}-continue`} />
        </View>
      ) : null}
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  panel: { gap: spacing.xs, marginBottom: spacing.lg },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  flex: { flex: 1 },
  gap: { gap: spacing.sm },
});
