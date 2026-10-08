import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import type { Objective, Pipeline } from '@dashboard/types/models';
import { ActionRow, InfoRow, useActionRunner } from '../../build/fields';
import { Markdown } from '../../components/ask/Markdown';
import { statusTone } from '../../components/ask/statusTone';
import { AppText, Banner, Button, ConfirmDialog, ListRow, Section, StatusBadge, TextField } from '../../components/ui';
import { SelectField } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import type { Refinement } from './useRefinement';
import type { BacklogReference } from './useBacklogReference';

export interface RefinementPanelProps {
  objective: Objective;
  refinement: Refinement;
  reference: BacklogReference;
  pipelines: Pipeline[];
  canManage: boolean;
}

/**
 * Backlog Refinement: pick a captain (plus optional vessel and fleet context, title, and kickoff prompt) and start a
 * session; choose a session; read the live transcript (tap a message to select it for summarizing); send follow-ups;
 * summarize, apply the summary back to the item, stop, or delete the session (with confirmation).
 */
export function RefinementPanel({ objective, refinement, reference, pipelines, canManage }: RefinementPanelProps) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const { busy, run } = useActionRunner();
  const [captainId, setCaptainId] = useState('');
  const [vesselId, setVesselId] = useState(objective.vesselIds[0] ?? '');
  const [fleetId, setFleetId] = useState(objective.fleetIds[0] ?? '');
  const [title, setTitle] = useState('');
  const [initialMessage, setInitialMessage] = useState('');
  const [composer, setComposer] = useState('');
  const [confirmDelete, setConfirmDelete] = useState(false);
  const close = t('Close');
  const captain = reference.captains.find((c) => c.id === captainId) ?? null;
  const { detail, summaryDraft, selectedMessageId } = refinement;

  const start = () => void run('start', async () => {
    const created = await refinement.start({
      captainId,
      fleetId: fleetId || undefined,
      vesselId: vesselId || undefined,
      title: title.trim() || undefined,
      initialMessage: initialMessage.trim() || undefined,
    });
    setTitle('');
    setInitialMessage('');
    return created;
  }, t('Refinement session started with {{captain}}.', { captain: captain?.name ?? captainId }));

  const send = () => void run('send', async () => {
    await refinement.send(composer.trim());
    setComposer('');
  });

  return (
    <Section title={t('Backlog Refinement')}>
      <View style={styles.pad} testID="objective-refinement">
        <AppText muted style={styles.note}>{t('Choose the captain explicitly here. Refinement is a backlog workflow, separate from repository-aware planning and dispatch.')}</AppText>
        {canManage ? (
          <>
            <SelectField
              label={t('Captain')}
              value={captainId}
              options={[{ value: '', label: t('Select a captain') }, ...reference.captains.map((c) => ({ value: c.id, label: `${c.name} (${c.state})` }))]}
              onChange={setCaptainId}
              closeLabel={close}
              testID="objective-refine-captain"
            />
            <SelectField
              label={t('Vessel Context')}
              value={vesselId}
              options={[{ value: '', label: t('No vessel context') }, ...reference.vessels.map((v) => ({ value: v.id, label: v.name }))]}
              onChange={setVesselId}
              closeLabel={close}
              testID="objective-refine-vessel"
            />
            <SelectField
              label={t('Fleet Context')}
              value={fleetId}
              options={[{ value: '', label: t('No fleet context') }, ...reference.fleets.map((f) => ({ value: f.id, label: f.name }))]}
              onChange={setFleetId}
              closeLabel={close}
              testID="objective-refine-fleet"
            />
            <TextField label={t('Session Title')} value={title} onChangeText={setTitle} placeholder={t('Optional refinement session title')} testID="objective-refine-title" />
            <TextField label={t('Initial Refinement Prompt')} value={initialMessage} onChangeText={setInitialMessage} multiline textAlignVertical="top" placeholder={t('Optional kickoff prompt for the selected captain')} testID="objective-refine-initial" />
            {captain ? (
              <AppText variant="caption" muted style={styles.note}>
                {t('Selected captain {{captain}} is currently {{state}}. Armada will fail fast if that captain cannot accept a refinement session.', { captain: captain.name, state: captain.state })}
              </AppText>
            ) : null}
            <Button label={busy === 'start' ? t('Starting...') : t('Start Refinement')} busy={busy === 'start'} disabled={!captainId} onPress={start} testID="objective-refine-start" />
          </>
        ) : null}
      </View>

      {refinement.sessions.length === 0 ? (
        <View style={styles.pad}>
          <AppText variant="label">{t('No refinement sessions yet.')}</AppText>
          <AppText muted>{t('Choose a captain and start a lightweight refinement pass from this backlog item.')}</AppText>
        </View>
      ) : refinement.sessions.map((session) => (
        <ListRow
          key={session.id}
          testID={`objective-refine-session-${session.id}`}
          title={session.title}
          subtitle={`${reference.captainNames.get(session.captainId) ?? session.captainId} \u00b7 ${formatRelativeTime(session.lastUpdateUtc)}`}
          selected={session.id === refinement.selectedSessionId}
          onPress={() => refinement.selectSession(session.id)}
          accessory={<StatusBadge label={session.status} tone={statusTone(session.status)} />}
          accessibilityValue={session.status}
        />
      ))}

      {!detail ? (
        <View style={styles.pad}>
          <AppText variant="label">{t('No active refinement transcript selected.')}</AppText>
          <AppText muted>{t('Start a session with a selected captain or choose an existing transcript above.')}</AppText>
        </View>
      ) : (
        <View style={styles.pad} testID="objective-refine-transcript">
          <AppText variant="heading" accessibilityRole="header">{detail.session.title}</AppText>
          <AppText muted>
            {t('Captain {{captain}} {{vesselClause}}', {
              captain: detail.captain?.name ?? detail.session.captainId,
              vesselClause: detail.vessel ? t('with optional vessel context {{vessel}}', { vessel: detail.vessel.name }) : t('without vessel context'),
            })}
          </AppText>
          <View style={styles.badgeRow}><StatusBadge label={detail.session.status} tone={statusTone(detail.session.status)} /></View>
          <View style={[styles.card, { borderColor: colors.border }]}>
            <InfoRow label={t('Selected Captain')} value={detail.captain?.name ?? detail.session.captainId} />
            <InfoRow label={t('Captain State')} value={detail.captain?.state ?? null} />
            <InfoRow label={t('Vessel Context')} value={detail.vessel?.name ?? t('None')} />
            <InfoRow label={t('Updated')} value={formatRelativeTime(detail.session.lastUpdateUtc)} />
          </View>
          {canManage ? (
            <ActionRow>
              <Button label={busy === 'stop' ? t('Stopping...') : t('Stop Session')} variant="secondary" busy={busy === 'stop'} onPress={() => void run('stop', refinement.stop, t('Refinement session is stopping.'))} testID="objective-refine-stop" />
              <Button label={t('Delete Session')} variant="danger" onPress={() => setConfirmDelete(true)} testID="objective-refine-delete" />
            </ActionRow>
          ) : null}

          {detail.messages.map((message) => {
            const selected = message.id === selectedMessageId;
            return (
              <Pressable
                key={message.id}
                testID={`objective-refine-message-${message.sequence}`}
                accessibilityRole="button"
                accessibilityState={{ selected }}
                accessibilityHint={t('Select this transcript message for summarizing or applying refinement back to the backlog item.')}
                onPress={() => refinement.selectMessage(message.id)}
                style={[styles.message, { borderColor: selected ? colors.primary : colors.border, backgroundColor: colors.surface }]}
              >
                <View style={styles.messageHead}>
                  <AppText variant="label">{message.role}</AppText>
                  <AppText variant="caption" muted>{formatRelativeTime(message.lastUpdateUtc)}</AppText>
                </View>
                {message.content ? <Markdown>{message.content}</Markdown> : <AppText muted>{t('Waiting for content...')}</AppText>}
              </Pressable>
            );
          })}

          <TextField
            label={t('Send Refinement Message')}
            value={composer}
            onChangeText={setComposer}
            multiline
            textAlignVertical="top"
            editable={canManage && busy !== 'send'}
            placeholder={t('Ask the selected captain to sharpen scope, acceptance criteria, non-goals, or rollout constraints.')}
            testID="objective-refine-input"
          />
          <ActionRow>
            <Button label={busy === 'send' ? t('Sending...') : t('Send')} busy={busy === 'send'} disabled={!canManage || !composer.trim()} onPress={send} testID="objective-refine-send" />
            <Button label={busy === 'summarize' ? t('Summarizing...') : t('Summarize')} variant="secondary" busy={busy === 'summarize'} disabled={!canManage || !selectedMessageId} onPress={() => void run('summarize', refinement.summarize, t('Refinement summary generated.'))} testID="objective-refine-summarize" />
            <Button label={busy === 'apply' ? t('Applying...') : t('Apply To Backlog Item')} variant="secondary" busy={busy === 'apply'} disabled={!canManage || !selectedMessageId} onPress={() => void run('apply', refinement.apply, t('Applied refinement summary back to the backlog item.'))} testID="objective-refine-apply" />
          </ActionRow>
          {!selectedMessageId ? <AppText variant="caption" muted>{t('Select a transcript message before summarizing or applying refinement back to the backlog item.')}</AppText> : null}
          {detail.session.failureReason ? <Banner tone="danger" title={detail.session.failureReason} /> : null}

          <AppText variant="subheading" muted accessibilityRole="header" style={styles.draftTitle}>{t('Refinement Summary Draft')}</AppText>
          {!summaryDraft ? (
            <AppText muted>{t('Generate a summary from a selected assistant message, then apply it back into the backlog item.')}</AppText>
          ) : (
            <View testID="objective-refine-summary">
              <InfoRow label={t('Summary')} value={summaryDraft.summary} />
              <InfoRow label={t('Suggested Pipeline')} value={pipelines.find((p) => p.id === summaryDraft.suggestedPipelineId)?.name ?? summaryDraft.suggestedPipelineId} />
              <InfoRow label={t('Method')} value={summaryDraft.method} />
              <InfoRow label={t('Acceptance Criteria')} value={summaryDraft.acceptanceCriteria.map((i) => `- ${i}`).join('\n')} />
              <InfoRow label={t('Non-Goals')} value={summaryDraft.nonGoals.map((i) => `- ${i}`).join('\n')} />
              <InfoRow label={t('Rollout Constraints')} value={summaryDraft.rolloutConstraints.map((i) => `- ${i}`).join('\n')} />
            </View>
          )}
        </View>
      )}
      <ConfirmDialog
        open={confirmDelete}
        title={t('Delete Session')}
        message={t('Delete the current refinement session and transcript.')}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { setConfirmDelete(false); void run('delete', refinement.remove, t('Refinement session deleted.')); }}
        onCancel={() => setConfirmDelete(false)}
        testID="objective-refine-delete-confirm"
      />
    </Section>
  );
}

const styles = StyleSheet.create({
  pad: { padding: spacing.lg, gap: spacing.xs },
  note: { marginBottom: spacing.md },
  badgeRow: { marginVertical: spacing.sm },
  card: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, marginBottom: spacing.md, overflow: 'hidden' },
  message: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.sm, gap: spacing.xs },
  messageHead: { flexDirection: 'row', justifyContent: 'space-between' },
  draftTitle: { marginTop: spacing.lg, marginBottom: spacing.sm, textTransform: 'uppercase' },
});
