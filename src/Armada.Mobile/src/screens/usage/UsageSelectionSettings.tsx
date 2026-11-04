import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { apiErrorMessage, getSettings, isApiStatus, updateSettings } from '@dashboard/api/client';
import { USAGE_SELECTION_NUMERIC_FIELDS, USAGE_SELECTION_STRATEGY_KEYS, usageSelectionFieldValid, usageSelectionModeDescriptionKey, usageSelectionReadOnly, usageSelectionValid,
  type UsageSelectionNumericField } from '@dashboard/lib/usageSelectionForm';
import type { CaptainSelectionStrategy, SettingsData, UsageSelectionSettings as Policy } from '@dashboard/types/models';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, Button, Section, TextField } from '../../components/ui';
import { Disclosure } from '../../components/ui/Disclosure';
import { SelectField } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';

type Draft = Omit<Policy, UsageSelectionNumericField> & { values: Record<UsageSelectionNumericField, string> };

function draftFrom(policy: Policy): Draft {
  const values = {} as Record<UsageSelectionNumericField, string>;
  for (const key of USAGE_SELECTION_NUMERIC_FIELDS) values[key] = String(policy[key]);
  return { mode: policy.mode, strategy: policy.strategy, unknownUsageBehavior: policy.unknownUsageBehavior, values };
}

function policyFrom(draft: Draft): Policy {
  const numbers = {} as Record<UsageSelectionNumericField, number>;
  for (const key of USAGE_SELECTION_NUMERIC_FIELDS) numbers[key] = draft.values[key].trim() === '' ? NaN : Number(draft.values[key]);
  return { mode: draft.mode, strategy: draft.strategy, unknownUsageBehavior: draft.unknownUsageBehavior, ...numbers };
}

/**
 * The dashboard's Captain selection settings panel for global administrators: mode, strategy, unknown-usage policy and
 * the evidence and accounting limits, saved live with the optimistic revision. Environment overrides are read-only.
 */
export function UsageSelectionSettings() {
  const { t } = useLocale();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [settings, setSettings] = useState<SettingsData | null>(null);
  const [message, setMessage] = useState<{ error: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const resource = useLiveResource(async () => {
    const result = await getSettings();
    setSettings(result); setDraft(result.usageSelection ? draftFrom(result.usageSelection) : null);
    return result;
  }, []);
  const capability = settings?.usageSelectionCapabilities;
  const readOnly = usageSelectionReadOnly(capability);
  const policy = draft ? policyFrom(draft) : null;
  const valid = !!policy && usageSelectionValid(policy);
  async function save() {
    if (!policy || !valid || readOnly) return;
    setBusy(true); setMessage(null);
    try {
      const result = await updateSettings({ usageSelection: policy, expectedUsageSelectionRevision: settings?.usageSelectionRevision });
      setSettings(result); if (result.usageSelection) setDraft(draftFrom(result.usageSelection));
      setMessage({ error: false, text: t('usageSelection.saved') });
    } catch (error) {
      setMessage({ error: true, text: isApiStatus(error, 409) ? t('usageSelection.conflict') : apiErrorMessage(error, t('usageSelection.saveFailed')) });
    } finally { setBusy(false); }
  }
  const field = (key: UsageSelectionNumericField) => {
    const text = draft?.values[key] ?? '';
    const bad = text.trim() !== '' && !usageSelectionFieldValid(key, Number(text));
    return <TextField key={key} label={t(`usageSelection.${key}`)} value={text} keyboardType="decimal-pad" editable={!readOnly && !busy}
      error={bad ? t('usageSelection.invalid') : null} testID={`usage-selection-${key}`}
      onChangeText={(value) => draft && setDraft({ ...draft, values: { ...draft.values, [key]: value } })} />;
  };
  return <Section title={t('usageSelection.title')}>
    <View style={styles.content}>
      {resource.error ? <><AppText color="danger" accessibilityRole="alert">{t('usageSelection.loadFailed')}</AppText>
        <Button label={t('Retry')} variant="secondary" onPress={() => void resource.reload()} /></> : null}
      {draft ? <>
        <AppText muted>{t(usageSelectionModeDescriptionKey(draft.mode))}</AppText>
        {readOnly ? <AppText muted>{t('usageSelection.environment')}</AppText> : null}
        <SelectField closeLabel={t('Close')} label={t('usageSelection.mode')} value={draft.mode} disabled={readOnly || busy} testID="usage-selection-mode"
          hint={capability && !capability.enforcementAvailable ? t('usageSelection.enforcementUnavailable') : undefined}
          options={[
            { value: 'Disabled', label: t('usageSelection.disabled') }, { value: 'Observe', label: t('usageSelection.observe') },
            ...(capability?.enforcementAvailable !== false || draft.mode === 'Enforce' ? [{ value: 'Enforce' as const, label: t('usageSelection.enforce') }] : []),
          ]} onChange={(mode) => setDraft({ ...draft, mode })} />
        <SelectField closeLabel={t('Close')} label={t('usageSelection.strategy')} value={draft.strategy} disabled={readOnly || busy} hint={t('usageSelection.strategyHint')} testID="usage-selection-strategy"
          options={(capability?.strategies ?? Object.keys(USAGE_SELECTION_STRATEGY_KEYS) as CaptainSelectionStrategy[]).map(strategy => ({ value: strategy, label: t(USAGE_SELECTION_STRATEGY_KEYS[strategy]) }))}
          onChange={(strategy) => setDraft({ ...draft, strategy })} />
        <SelectField closeLabel={t('Close')} label={t('usageSelection.unknown')} value={draft.unknownUsageBehavior} disabled={readOnly || busy} testID="usage-selection-unknown"
          options={[{ value: 'AllowDeprioritized', label: t('usageSelection.allowUnknown') }, { value: 'Wait', label: t('usageSelection.waitUnknown') }]}
          onChange={(unknownUsageBehavior) => setDraft({ ...draft, unknownUsageBehavior })} />
        <Disclosure title={t('usageSelection.advanced')}>
          <View style={styles.content}>{USAGE_SELECTION_NUMERIC_FIELDS.map(field)}</View>
        </Disclosure>
        {policy && !valid ? <AppText color="danger" accessibilityRole="alert">{t('usageSelection.invalid')}</AppText> : null}
        <Button label={t(busy ? 'Saving...' : 'usageSelection.save')} disabled={readOnly || busy || !valid} onPress={() => void save()} testID="usage-selection-save" />
        {message ? <AppText color={message.error ? 'danger' : undefined} accessibilityRole={message.error ? 'alert' : undefined}>{message.text}</AppText> : null}
      </> : null}
    </View>
  </Section>;
}

const styles = StyleSheet.create({ content: { gap: spacing.md } });
