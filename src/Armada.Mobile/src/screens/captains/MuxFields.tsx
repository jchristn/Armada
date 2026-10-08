import { useCallback, useEffect, useState } from 'react';
import { listMuxEndpoints } from '@dashboard/api/client';
import { isMuxRuntime, type MuxCaptainFormFields } from '@dashboard/lib/mux';
import type { MuxEndpointInfo } from '@dashboard/types/models';
import { errorMessage } from '../../build/useLiveResource';
import { Button, TextField } from '../../components/ui';
import { Disclosure } from '../../components/ui/Disclosure';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';

interface MuxFieldsProps {
  runtime: string;
  form: MuxCaptainFormFields;
  onChange: (patch: Partial<MuxCaptainFormFields>) => void;
}

/**
 * Mux runtime options of a captain (the dashboard's MuxRuntimeFields): config directory, the named endpoint (picked
 * from the saved endpoints the server discovers, or typed), and the advanced overrides. Renders nothing for other
 * runtimes.
 */
export function MuxFields({ runtime, form, onChange }: MuxFieldsProps) {
  const { t } = useLocale();
  const [endpoints, setEndpoints] = useState<MuxEndpointInfo[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState('');
  const mux = isMuxRuntime(runtime);
  const configDirectory = form.muxConfigDirectory.trim();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await listMuxEndpoints(configDirectory || undefined);
      if (!result.success) throw new Error(result.errorMessage || result.errorCode || t('Mux endpoint discovery failed.'));
      setEndpoints(result.endpoints ?? []);
      setLoadError('');
    } catch (e) {
      setEndpoints([]);
      setLoadError(errorMessage(e) || t('Mux endpoint discovery failed.'));
    } finally {
      setLoading(false);
    }
  }, [configDirectory, t]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- discovery is a fetch keyed on the config directory
    if (mux) void load();
  }, [mux, load]);

  if (!mux) return null;

  const hint = loading
    ? t('Loading saved Mux endpoints...')
    : loadError || (endpoints.length === 0
      ? t('No saved Mux endpoints were found for this config directory.')
      : t('{{count}} saved Mux endpoint(s) available.', { count: endpoints.length }));

  const approvalOptions: SelectOption<string>[] = [
    { value: '', label: t('Default (auto)') },
    { value: 'auto', label: 'auto' },
    { value: 'autoapprove', label: 'autoapprove' },
    { value: 'deny', label: 'deny' },
    { value: 'ask', label: 'ask' },
  ];

  return (
    <>
      <TextField
        label={t('Mux Config Directory')}
        value={form.muxConfigDirectory}
        onChangeText={(v) => onChange({ muxConfigDirectory: v })}
        placeholder={t('Optional path, e.g. C:\\Users\\you\\.mux')}
        autoCapitalize="none"
        autoCorrect={false}
        testID="captain-form-muxConfigDirectory"
      />
      <TextField
        label={t('Mux Endpoint')}
        value={form.muxEndpoint}
        onChangeText={(v) => onChange({ muxEndpoint: v })}
        placeholder={t('Required endpoint name')}
        autoCapitalize="none"
        autoCorrect={false}
        hint={hint}
        testID="captain-form-muxEndpoint"
      />
      {endpoints.length > 0 ? (
        <SelectField
          label={t('Saved Mux endpoints')}
          value={endpoints.some((e) => e.name === form.muxEndpoint) ? form.muxEndpoint : ''}
          options={endpoints.map((e) => ({ value: e.name, label: e.name, description: `${e.adapterType}${e.model ? ` (${e.model})` : ''}` }))}
          onChange={(v) => onChange({ muxEndpoint: v })}
          closeLabel={t('Close')}
          placeholder={t('Choose a saved endpoint')}
          testID="captain-form-muxEndpointPicker"
        />
      ) : null}
      <Button label={loading ? t('Refreshing...') : t('Refresh Mux Endpoints')} variant="ghost" icon="refresh" onPress={() => void load()} disabled={loading} />
      <Disclosure title={t('Advanced Mux Overrides')} testID="captain-form-muxAdvanced">
        <TextField label={t('Mux Base URL')} value={form.muxBaseUrl} onChangeText={(v) => onChange({ muxBaseUrl: v })} placeholder={t('Optional override')} autoCapitalize="none" autoCorrect={false} />
        <TextField label={t('Mux Adapter Type')} value={form.muxAdapterType} onChangeText={(v) => onChange({ muxAdapterType: v })} placeholder={t('Optional override')} autoCapitalize="none" autoCorrect={false} />
        <TextField label={t('Mux Temperature')} value={form.muxTemperature} onChangeText={(v) => onChange({ muxTemperature: v })} placeholder={t('Optional number')} keyboardType="decimal-pad" />
        <TextField label={t('Mux Max Tokens')} value={form.muxMaxTokens} onChangeText={(v) => onChange({ muxMaxTokens: v })} placeholder={t('Optional integer')} keyboardType="number-pad" />
        <TextField label={t('Mux System Prompt Path')} value={form.muxSystemPromptPath} onChangeText={(v) => onChange({ muxSystemPromptPath: v })} placeholder={t('Optional path')} autoCapitalize="none" autoCorrect={false} />
        <SelectField label={t('Mux Approval Policy')} value={form.muxApprovalPolicy} options={approvalOptions} onChange={(v) => onChange({ muxApprovalPolicy: v })} closeLabel={t('Close')} />
      </Disclosure>
    </>
  );
}
