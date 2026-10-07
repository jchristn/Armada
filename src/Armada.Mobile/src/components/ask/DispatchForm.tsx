import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { listPipelines, listVessels } from '@dashboard/api/client';
import type { Pipeline, Vessel } from '@dashboard/types/models';
import { buildDispatchArguments, validateDispatch, type DispatchDraft } from '@dashboard/lib/askQuickActions';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Button } from '../ui/Button';
import { IconButton } from '../ui/IconButton';
import { SelectField } from '../ui/SelectSheet';
import { TextField } from '../ui/TextField';

const MAX_MISSIONS = 25;

export interface QuickActionFormProps {
  busy: boolean;
  onSubmit: (args: Record<string, unknown>) => void;
  onCancel: () => void;
}

/**
 * The `/dispatch` form (the dashboard's AskDispatchForm): a vessel, one or more missions (title and optional
 * description), an optional voyage title, and an optional pipeline. Submitting runs the MCP `dispatch` tool through
 * the thread's actions endpoint; the arguments and validation are the shared lib/askQuickActions.
 */
export function DispatchForm({ busy, onSubmit, onCancel }: QuickActionFormProps) {
  const { t } = useLocale();
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [loadError, setLoadError] = useState('');
  const [loading, setLoading] = useState(true);
  const [draft, setDraft] = useState<DispatchDraft>({ vesselId: '', title: '', missions: [{ title: '', description: '' }], pipelineId: '' });
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    let active = true;
    Promise.all([listVessels({ pageSize: 9999 }), listPipelines({ pageSize: 500 }).catch(() => null)])
      .then(([v, p]) => {
        if (!active) return;
        const list = (v?.objects || []).slice().sort((a, b) => a.name.localeCompare(b.name));
        setVessels(list);
        setPipelines(p?.objects || []);
        if (list.length === 1) setDraft((d) => ({ ...d, vesselId: list[0].id }));
      })
      .catch((err: unknown) => { if (active) setLoadError(err instanceof Error ? err.message : t('Failed to load vessels.')); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [t]);

  function updateMission(index: number, field: 'title' | 'description', value: string) {
    setDraft((d) => ({ ...d, missions: d.missions.map((m, i) => (i === index ? { ...m, [field]: value } : m)) }));
  }

  function submit() {
    const found = validateDispatch(draft);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    onSubmit(buildDispatchArguments(draft));
  }

  return (
    <View testID="ask-dispatch-form">
      {loadError ? <AppText color="danger" accessibilityRole="alert" style={styles.gap}>{loadError}</AppText> : null}
      <SelectField
        label={t('Vessel')}
        value={draft.vesselId}
        options={[{ value: '', label: loading ? t('Loading...') : t('Choose a vessel') }, ...vessels.map((v) => ({ value: v.id, label: v.name }))]}
        onChange={(vesselId) => setDraft((d) => ({ ...d, vesselId }))}
        closeLabel={t('Close')}
        disabled={loading || busy}
        error={errors.vessel ? t(errors.vessel) : null}
        testID="dispatch-vessel"
      />
      <SelectField
        label={t('Pipeline (optional)')}
        value={draft.pipelineId}
        options={[{ value: '', label: t('Vessel default') }, ...pipelines.map((p) => ({ value: p.id, label: p.name }))]}
        onChange={(pipelineId) => setDraft((d) => ({ ...d, pipelineId }))}
        closeLabel={t('Close')}
        disabled={busy}
        testID="dispatch-pipeline"
      />
      <TextField
        label={t('Voyage title (optional)')}
        value={draft.title}
        maxLength={200}
        placeholder={t('Defaults to the first mission title')}
        onChangeText={(title) => setDraft((d) => ({ ...d, title }))}
        editable={!busy}
        testID="dispatch-title"
      />
      <AppText variant="subheading" muted accessibilityRole="header" style={styles.gap}>{t('Missions')}</AppText>
      {draft.missions.map((mission, index) => (
        <View key={index} style={styles.mission}>
          <View style={styles.missionFields}>
            <TextField
              label={t('Mission {{number}} title', { number: index + 1 })}
              value={mission.title}
              maxLength={200}
              placeholder={t('Mission title')}
              onChangeText={(value) => updateMission(index, 'title', value)}
              editable={!busy}
              testID={`dispatch-mission-${index}-title`}
            />
            <TextField
              label={t('Mission {{number}} description', { number: index + 1 })}
              value={mission.description}
              multiline
              placeholder={t('What should the captain do? (optional)')}
              onChangeText={(value) => updateMission(index, 'description', value)}
              editable={!busy}
              testID={`dispatch-mission-${index}-description`}
            />
          </View>
          {draft.missions.length > 1 ? (
            <IconButton
              icon="close"
              color="textMuted"
              label={t('Remove mission {{number}}', { number: index + 1 })}
              onPress={() => setDraft((d) => ({ ...d, missions: d.missions.filter((_, i) => i !== index) }))}
            />
          ) : null}
        </View>
      ))}
      {errors.missions ? <AppText variant="caption" color="danger" accessibilityRole="alert" style={styles.gap}>{t(errors.missions)}</AppText> : null}
      <Button
        label={`+ ${t('Add mission')}`}
        variant="secondary"
        onPress={() => setDraft((d) => ({ ...d, missions: [...d.missions, { title: '', description: '' }] }))}
        disabled={busy || draft.missions.length >= MAX_MISSIONS}
        testID="dispatch-add-mission"
      />
      <AppText variant="caption" muted style={styles.gap}>{t('Submitting this form is the confirmation; the voyage starts right away.')}</AppText>
      <Button label={busy ? t('Dispatching...') : t('Dispatch')} onPress={submit} disabled={busy || loading} busy={busy} testID="dispatch-submit" />
      <Button label={t('Cancel')} variant="ghost" onPress={onCancel} disabled={busy} />
    </View>
  );
}

const styles = StyleSheet.create({
  gap: { marginBottom: spacing.sm },
  mission: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.xs },
  missionFields: { flex: 1 },
});
