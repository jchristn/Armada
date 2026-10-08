import { Stack, useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { KeyboardAvoidingView, Linking, Platform, RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import {
  createBacklogItem,
  deleteBacklogItem,
  getBacklogItem,
  importObjectiveFromGitHub,
  updateBacklogItem,
} from '@dashboard/api/client';
import type { Objective, WebSocketMessage } from '@dashboard/types/models';
import { emptyObjectiveForm, objectiveFormFrom, objectivePayloadFromForm, replacePrimaryLinkedId, type ObjectiveFormState } from '@dashboard/lib/backlogForm';
import { splitList } from '@dashboard/lib/backlogUtils';
import { buildObjectiveDuplicatePayload } from '@dashboard/lib/duplicates';
import { useAuth } from '../../auth/AuthContext';
import { ActionRow, InfoRow, useActionRunner } from '../../build/fields';
import { useLiveResource } from '../../build/useLiveResource';
import { statusTone } from '../../components/ask/statusTone';
import { AppText, Banner, Button, ConfirmDialog, ErrorState, FormActions, ListRow, LoadingState, Section, StatusBadge, StickyFooter } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useSocket } from '../../socket/SocketContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { ObjectiveForm } from './ObjectiveForm';
import { armadaLinkGroups, backlogItemPath, dispatchHref, historyHref, planningHref, releaseHref } from './objectiveLinks';
import { JsonSheet, Stat, StatGrid, Tags } from './parts';
import { RefinementPanel } from './RefinementPanel';
import { useBacklogReference } from './useBacklogReference';
import { useRefinement } from './useRefinement';

export interface ObjectiveDetailScreenProps {
  /** Backlog item id, or 'new' to create one. */
  id: string;
  /** Shown in a split-view pane (no navigation header of its own). */
  embedded?: boolean;
  /** Prefill for a new item (the dashboard's /backlog/new?vesselId=). */
  prefillVesselId?: string | null;
  /** Transcript to open (?refinementSessionId=). */
  refinementSessionId?: string | null;
  /** After a create; defaults to replacing the route with the new item. */
  onCreated?: (objective: Objective) => void;
  /** After a delete; defaults to going back. */
  onDeleted?: () => void;
  /** Open another backlog item (a duplicate); defaults to pushing its route. */
  onOpenItem?: (id: string) => void;
}

/**
 * A backlog item (the dashboard's ObjectiveDetail at /backlog/:id and /objectives/:id; 'new' creates one): header
 * tags and info tiles, the actions (History, Refresh GitHub, Start Planning, Open In Dispatch, Draft Release,
 * Duplicate, View JSON, Delete with confirmation), the GitHub source, the editor with Save, Armada links, and Backlog
 * Refinement. Live: objective.changed reloads the item (unsaved edits are kept), refinement events update the
 * transcript in place.
 */
export function ObjectiveDetailScreen({ id, embedded = false, prefillVesselId, refinementSessionId, onCreated, onDeleted, onOpenItem }: ObjectiveDetailScreenProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { subscribe } = useSocket();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canManage = isAdmin || isTenantAdmin;
  const createMode = id === 'new';
  const { busy, run } = useActionRunner();
  const reference = useBacklogReference();
  const [form, setForm] = useState<ObjectiveFormState>(emptyObjectiveForm);
  const [dirty, setDirty] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const dirtyRef = useRef(false);
  useEffect(() => { dirtyRef.current = dirty; }, [dirty]);

  const item = useLiveResource(() => getBacklogItem(id), [id], { enabled: !createMode });
  const objective = item.data;
  const setObjective = item.setData;

  const hydrate = useCallback((next: Objective) => {
    setObjective(next);
    setForm(objectiveFormFrom(next));
    setDirty(false);
  }, [setObjective]);

  // A (re)loaded item fills the form unless the user has unsaved edits.
  useEffect(() => {
    if (objective && !dirtyRef.current) setForm(objectiveFormFrom(objective));
  }, [objective]);

  // Create mode: prefill the vessel (and its fleet) once the vessels are known.
  const prefilled = useRef(false);
  useEffect(() => {
    if (!createMode || !prefillVesselId || prefilled.current || reference.vessels.length === 0) return;
    prefilled.current = true;
    const fleetId = reference.vessels.find((v) => v.id === prefillVesselId)?.fleetId ?? '';
    setForm((f) => ({
      ...f,
      vesselIds: f.vesselIds || replacePrimaryLinkedId('', prefillVesselId),
      fleetIds: f.fleetIds || (fleetId ? replacePrimaryLinkedId('', fleetId) : ''),
    }));
  }, [createMode, prefillVesselId, reference.vessels]);

  useEffect(() => {
    if (createMode) return undefined;
    return subscribe((msg: WebSocketMessage) => {
      if (msg.type !== 'objective.changed') return;
      const payload = msg.data as Objective | undefined;
      if (!payload || payload.id !== id) return;
      if (dirtyRef.current) setObjective(payload);
      else hydrate(payload);
    });
  }, [createMode, id, subscribe, hydrate, setObjective]);

  const refinement = useRefinement(createMode ? null : objective?.id ?? null, refinementSessionId ?? '', hydrate);

  const change = (next: ObjectiveFormState) => { setForm(next); setDirty(true); setFormError(null); };

  const openItem = onOpenItem ?? ((itemId: string) => router.push(backlogItemPath(itemId) as Href));

  const save = () => {
    if (!form.title.trim()) {
      setFormError(t('Backlog item title is required.'));
      return;
    }
    void run('save', async () => {
      const payload = objectivePayloadFromForm(form);
      if (createMode) {
        const created = await createBacklogItem(payload);
        setDirty(false);
        if (onCreated) onCreated(created);
        else router.replace(backlogItemPath(created.id) as Href);
        return created;
      }
      const updated = await updateBacklogItem(id, payload);
      hydrate(updated);
      return updated;
    }, createMode ? t('Backlog item "{{title}}" created.', { title: form.title.trim() }) : t('Backlog item "{{title}}" saved.', { title: form.title.trim() }));
  };

  const linkedVesselIds = splitList(form.vesselIds);
  const linkedFleetIds = splitList(form.fleetIds);
  const primaryVesselId = linkedVesselIds[0] || objective?.vesselIds[0] || '';
  const primaryFleetId = linkedFleetIds[0] || objective?.fleetIds[0] || '';
  const primaryVesselName = primaryVesselId ? reference.vesselNames.get(primaryVesselId) ?? primaryVesselId : '';
  const title = createMode ? t('Create Backlog Item') : objective?.title ?? t('Backlog Item');

  if (!createMode && item.loading) return <LoadingState label={t('Loading backlog item...')} />;
  if (!createMode && !objective) {
    return <ErrorState title={t('Could not load')} message={item.error} retryLabel={t('Retry')} onRetry={() => void item.refresh()} />;
  }

  const gitHubNumber = objective?.sourceNumber ?? null;

  return (
    <SafeAreaView edges={embedded ? [] : ['left', 'right']} style={[styles.fill, { backgroundColor: colors.background }]} testID="objective-detail">
      {embedded ? null : <Stack.Screen options={{ title }} />}
      <KeyboardAvoidingView style={styles.fill} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView
          contentContainerStyle={styles.content}
          keyboardShouldPersistTaps="handled"
          refreshControl={createMode ? undefined : <RefreshControl refreshing={item.refreshing} onRefresh={() => { setDirty(false); void item.refresh(); }} tintColor={colors.primary} />}
        >
          <View style={styles.column}>
            {embedded ? <AppText variant="title" accessibilityRole="header" style={styles.heading}>{title}</AppText> : null}
            {objective ? (
              <View style={styles.tags}>
                <StatusBadge label={objective.status} tone={statusTone(objective.status)} />
                <Tags items={[objective.kind, objective.priority, objective.effort, objective.backlogState]} />
              </View>
            ) : null}

            {!canManage ? <Banner tone="warning" title={t('You can view backlog items, but only tenant administrators can create or change them.')} /> : null}

            {objective ? (
              <>
                <ActionRow>
                  <Button label={t('History')} variant="secondary" icon="time-outline" onPress={() => router.push(historyHref(objective) as Href)} testID="objective-history" />
                  {objective.sourceProvider === 'GitHub' && primaryVesselId && gitHubNumber ? (
                    <Button
                      label={busy === 'github' ? t('Refreshing...') : t('Refresh GitHub')}
                      variant="secondary"
                      busy={busy === 'github'}
                      onPress={() => void run('github', async () => {
                        const refreshed = await importObjectiveFromGitHub({
                          objectiveId: objective.id,
                          vesselId: primaryVesselId,
                          sourceType: objective.sourceType === 'PullRequest' ? 'PullRequest' : 'Issue',
                          number: gitHubNumber,
                        });
                        hydrate(refreshed);
                        return refreshed;
                      }, t('Backlog item "{{title}}" refreshed from GitHub.', { title: objective.title }))}
                      testID="objective-github-refresh"
                    />
                  ) : null}
                  <Button label={t('Start Planning')} variant="secondary" disabled={!primaryVesselId} onPress={() => router.push(planningHref(objective, primaryFleetId, primaryVesselId) as Href)} accessibilityHint={t('Open a planning session for this backlog item using the linked vessel and suggested context.')} testID="objective-start-planning" />
                  <Button label={t('Open In Dispatch')} variant="secondary" disabled={!primaryVesselId} onPress={() => router.push(dispatchHref(objective, primaryVesselId, reference.pipelines) as Href)} accessibilityHint={t('Create dispatch-ready implementation work from this backlog item.')} testID="objective-open-dispatch" />
                  <Button label={t('Draft Release')} variant="secondary" disabled={!primaryVesselId} onPress={() => router.push(releaseHref(objective, primaryVesselId) as Href)} accessibilityHint={t('Draft release notes and release metadata from this backlog item.')} testID="objective-draft-release" />
                  {canManage ? (
                    <Button
                      label={t('Duplicate')}
                      variant="secondary"
                      busy={busy === 'duplicate'}
                      onPress={() => void run('duplicate', async () => {
                        const created = await createBacklogItem(buildObjectiveDuplicatePayload(objective));
                        openItem(created.id);
                        return created;
                      }, t('Backlog item "{{title}}" duplicated.', { title: objective.title }))}
                      testID="objective-duplicate"
                    />
                  ) : null}
                  <Button label={t('View JSON')} variant="ghost" onPress={() => setJsonOpen(true)} testID="objective-view-json" />
                  {canManage ? <Button label={t('Delete')} variant="danger" onPress={() => setConfirmDelete(true)} testID="objective-delete" /> : null}
                </ActionRow>

                <StatGrid>
                  <Stat label={t('Rank')} value={objective.rank} />
                  <Stat label={t('Owner')} value={objective.owner || t('Unassigned')} />
                  <Stat label={t('Refinement Sessions')} value={objective.refinementSessionIds.length} />
                  <Stat label={t('Linked Releases')} value={objective.releaseIds.length} />
                  <Stat label={t('Linked Incidents')} value={objective.incidentIds.length} />
                  <Stat label={t('Last Updated')} value={formatRelativeTime(objective.lastUpdateUtc)} />
                </StatGrid>

                {objective.sourceProvider === 'GitHub' ? (
                  <Section title={t('GitHub Source')}>
                    <InfoRow label={t('Provider')} value={objective.sourceProvider} />
                    <InfoRow label={t('Source Type')} value={objective.sourceType || t('Unknown source')} />
                    <InfoRow label={t('Source')} value={objective.sourceId} />
                    <InfoRow label={t('Last Source Update')} value={objective.sourceUpdatedUtc ? formatDateTime(objective.sourceUpdatedUtc) : null} />
                    {objective.sourceUrl ? (
                      <ListRow title={t('Source Link')} subtitle={objective.sourceUrl} icon="open-outline" onPress={() => void Linking.openURL(objective.sourceUrl!)} />
                    ) : <InfoRow label={t('Source Link')} value={null} />}
                  </Section>
                ) : null}

                <Banner tone="info" title={t('Refinement is lighter than planning: it uses a selected captain to sharpen the backlog item, but it does not imply repository mutation, dock provisioning, or dispatch by itself.')} />
                {primaryVesselId
                  ? <Banner tone="info" title={t('Primary vessel {{vessel}} is linked, so planning, dispatch, and release drafting can start from this backlog item.', { vessel: primaryVesselName })} />
                  : <Banner tone="warning" title={t('This backlog item can be refined now, but it still needs a vessel before repository-aware planning or dispatch can start.')} />}
              </>
            ) : null}

            <ObjectiveForm form={form} onChange={change} reference={reference} objective={objective} canManage={canManage} />
            {formError ? <Banner tone="danger" title={formError} /> : null}

            {objective && armadaLinkGroups(objective).length > 0 ? (
              <Section title={t('Armada Links')} footer={t('Armada records these automatically as planning, execution, release, deployment, and incident work references this backlog item.')}>
                {armadaLinkGroups(objective).flatMap((group) => group.ids.map((linkId) => (
                  <ListRow key={`${group.key}-${linkId}`} title={linkId} subtitle={t(group.label)} onPress={() => router.push(group.href(linkId) as Href)} testID={`objective-link-${group.key}-${linkId}`} />
                )))}
              </Section>
            ) : null}

            {objective ? (
              <RefinementPanel objective={objective} refinement={refinement} reference={reference} pipelines={reference.pipelines} canManage={canManage} />
            ) : null}
          </View>
        </ScrollView>
        {/* Save stays reachable below the long form (and above the keyboard), wherever the page is scrolled. */}
        {canManage ? (
          <StickyFooter testID="objective-form-footer">
            <FormActions>
              <Button
                label={busy === 'save' ? t('Saving...') : createMode ? t('Create Backlog Item') : t('Save Changes')}
                busy={busy === 'save'}
                onPress={save}
                testID="objective-form-save"
              />
            </FormActions>
          </StickyFooter>
        ) : null}
      </KeyboardAvoidingView>
      {objective ? (
        <ConfirmDialog
          open={confirmDelete}
          title={t('Delete Backlog Item')}
          message={t('Delete "{{title}}"? This removes the backlog item and its objective snapshot history only.', { title: objective.title })}
          confirmLabel={t('Delete')}
          cancelLabel={t('Cancel')}
          danger
          onConfirm={() => {
            setConfirmDelete(false);
            void run('delete', async () => {
              await deleteBacklogItem(objective.id);
              if (onDeleted) onDeleted();
              else if (router.canGoBack()) router.back();
              else router.replace('/objectives' as Href);
            }, t('Backlog item "{{title}}" deleted.', { title: objective.title }));
          }}
          onCancel={() => setConfirmDelete(false)}
          testID="objective-detail-delete-confirm"
        />
      ) : null}
      <JsonSheet open={jsonOpen} title={title} data={objective} onClose={() => setJsonOpen(false)} />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1 },
  column: { width: '100%', maxWidth: 820, alignSelf: 'center' },
  heading: { marginHorizontal: spacing.lg, marginBottom: spacing.sm },
  tags: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.sm, marginHorizontal: spacing.lg, marginBottom: spacing.md },
});
