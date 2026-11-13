import { useCallback, useEffect, useMemo, useState } from 'react';
import { getHarbor, listMuxEndpoints } from '../../api/client';
import { MUX_APPROVAL_ASK_WARNING, MUX_APPROVAL_POLICIES, isMuxRuntime, isUnsupportedMuxApprovalPolicy, muxFieldErrorMessage, muxMaxTokensError, muxTemperatureError, type MuxCaptainFormFields } from '../../lib/mux';
import type { MuxEndpointInfo } from '../../types/models';

/** English tooltip for the Mux Endpoint field: this, not Model, is where the endpoint name goes. */
const MUX_ENDPOINT_TOOLTIP = 'Required: name of a saved mux endpoint (as shown by mux endpoint list), passed to mux as --endpoint, for example local-ollama. Pick one from the suggestions. Model only overrides its model.';
const MUX_CONFIG_DIRECTORY_TOOLTIP = 'Optional mux config directory, passed as --config-dir: a path on the machine that runs mux (your Harbor), for example ~/.mux. It decides which saved endpoints and MCP servers mux sees. Blank uses MUX_CONFIG_DIR, then ~/.mux.';
const MUX_BASE_URL_TOOLTIP = 'Optional URL passed as --base-url, replacing the saved endpoint\'s server address, for example http://localhost:11434/v1. Blank keeps the endpoint\'s own URL.';
const MUX_ADAPTER_TYPE_TOOLTIP = 'Optional mux adapter type passed as --adapter-type, replacing the API protocol the saved endpoint uses, for example openai. Blank keeps the endpoint\'s adapter.';
const MUX_TEMPERATURE_TOOLTIP = 'Optional sampling temperature passed as --temperature, for example 0.2. Lower is more predictable, higher is more varied. Blank uses the endpoint\'s setting.';
const MUX_MAX_TOKENS_TOOLTIP = 'Optional cap on tokens per model response, passed as --max-tokens, for example 8192. A whole number. Blank uses the endpoint\'s setting.';
const MUX_SYSTEM_PROMPT_PATH_TOOLTIP = 'Optional path to a system prompt file on the machine that runs mux, passed as --system-prompt, for example ~/.mux/prompts/captain.md. Blank uses mux\'s default prompt.';
const MUX_APPROVAL_POLICY_TOOLTIP = 'How mux handles tool calls that need approval. Captains run mux print, which cannot prompt, so only these work: Default, auto and autoapprove run with --yolo (no prompts), unless auto-approve is off, which makes Default deny. deny refuses them.';

interface MuxRuntimeFieldsProps {
  runtime: string;
  form: MuxCaptainFormFields;
  onChange: (patch: Partial<MuxCaptainFormFields>) => void;
  t: (key: string, vars?: Record<string, string | number | null | undefined>) => string;
  compact?: boolean;
}

export default function MuxRuntimeFields({ runtime, form, onChange, t, compact = false }: MuxRuntimeFieldsProps) {
  const [endpoints, setEndpoints] = useState<MuxEndpointInfo[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState('');
  const [harborId, setHarborId] = useState<string | null>(null);
  const [harborLabel, setHarborLabel] = useState<{ id: string; name: string } | null>(null);
  const configDirectory = form.muxConfigDirectory.trim();

  const loadEndpoints = useCallback(async () => {
    if (!isMuxRuntime(runtime)) {
      setEndpoints([]);
      setLoadError('');
      return;
    }

    try {
      setLoading(true);
      const result = await listMuxEndpoints(configDirectory || undefined);
      if (!result.success) {
        throw new Error(result.errorMessage || result.errorCode || t('Mux endpoint discovery failed.'));
      }

      setEndpoints(result.objects ?? []);
      setHarborId(result.harborId ?? null);
      setLoadError('');
    } catch (error) {
      setEndpoints([]);
      setLoadError(error instanceof Error ? error.message : t('Mux endpoint discovery failed.'));
    } finally {
      setLoading(false);
    }
  }, [configDirectory, runtime, t]);

  useEffect(() => {
    if (!isMuxRuntime(runtime)) return;
    void loadEndpoints();
  }, [loadEndpoints, runtime]);

  // Show the Harbor that answered by name, not by its hbr_ id.
  useEffect(() => {
    if (!harborId) return undefined;
    let cancelled = false;
    getHarbor(harborId)
      .then(harbor => { if (!cancelled && harbor?.name) setHarborLabel({ id: harborId, name: harbor.name }); })
      .catch(() => { /* keep showing the id */ });
    return () => { cancelled = true; };
  }, [harborId]);
  const harborName = harborId && harborLabel?.id === harborId ? harborLabel.name : null;

  const temperatureError = muxTemperatureError(form.muxTemperature);
  const maxTokensError = muxMaxTokensError(form.muxMaxTokens);
  const askPolicy = isUnsupportedMuxApprovalPolicy(form.muxApprovalPolicy);

  const endpointHint = useMemo(() => {
    if (!isMuxRuntime(runtime)) return '';
    if (loading) return t('Loading saved Mux endpoints...');
    if (loadError) return loadError;
    // Mux runs on the user's Harbor (where Mux captains run); say which machine answered.
    const source = harborId ? t('Read from the mux on Harbor {{harbor}}.', { harbor: harborName || harborId }) : t('Read from the mux on the Admiral host.');
    if (endpoints.length === 0) return t('No saved Mux endpoints were found for this config directory.') + ' ' + source;
    return t('{{count}} saved Mux endpoint(s) available.', { count: endpoints.length }) + ' ' + source;
  }, [endpoints.length, harborId, harborName, loadError, loading, runtime, t]);

  if (!isMuxRuntime(runtime)) return null;

  return (
    <div className="mux-runtime-fields" style={{ marginTop: compact ? '0.5rem' : '0.75rem' }}>
      <div className="wizard-form-grid">
        <div className="form-group">
          <label title={t(MUX_CONFIG_DIRECTORY_TOOLTIP)}>{t('Mux Config Directory')}</label>
          <input aria-label={t('Mux Config Directory')}
            title={t(MUX_CONFIG_DIRECTORY_TOOLTIP)}
            value={form.muxConfigDirectory}
            onChange={(event) => onChange({ muxConfigDirectory: event.target.value })}
            placeholder={t('Optional path, e.g. ~/.mux')}
          />
        </div>
        <div className="form-group">
          <label title={t(MUX_ENDPOINT_TOOLTIP)}>{t('Mux Endpoint')}</label>
          <input aria-label={t('Mux Endpoint')}
            title={t(MUX_ENDPOINT_TOOLTIP)}
            list="mux-endpoint-options"
            value={form.muxEndpoint}
            onChange={(event) => onChange({ muxEndpoint: event.target.value })}
            placeholder={t('Required endpoint name')}
            required
          />
          <datalist id="mux-endpoint-options">
            {endpoints.map((endpoint) => (
              <option key={endpoint.name} value={endpoint.name}>
                {endpoint.adapterType} {endpoint.model ? `(${endpoint.model})` : ''}
              </option>
            ))}
          </datalist>
        </div>
      </div>

      <div className="wizard-inline-actions" style={{ marginBottom: '0.75rem' }}>
        <button type="button" className="btn btn-sm" onClick={() => void loadEndpoints()} disabled={loading} title={t('Read the saved endpoints again from mux (on your Harbor, or the Admiral host) using the config directory above, to refresh the Mux Endpoint suggestions.')}>
          {loading ? t('Refreshing...') : t('Refresh Mux Endpoints')}
        </button>
        <span className={`text-dim${loadError ? ' text-danger' : ''}`}>{endpointHint}</span>
      </div>

      <details>
        <summary title={t('Optional settings that replace values from the saved mux endpoint for this captain only. Leave them blank to use the endpoint as saved.')}>{t('Advanced Mux Overrides')}</summary>
        <div className="wizard-form-grid" style={{ marginTop: '0.75rem' }}>
          <div className="form-group">
            <label title={t(MUX_BASE_URL_TOOLTIP)}>{t('Mux Base URL')}</label>
            <input aria-label={t('Mux Base URL')}
              title={t(MUX_BASE_URL_TOOLTIP)}
              value={form.muxBaseUrl}
              onChange={(event) => onChange({ muxBaseUrl: event.target.value })}
              placeholder={t('Optional override')}
            />
          </div>
          <div className="form-group">
            <label title={t(MUX_ADAPTER_TYPE_TOOLTIP)}>{t('Mux Adapter Type')}</label>
            <input aria-label={t('Mux Adapter Type')}
              title={t(MUX_ADAPTER_TYPE_TOOLTIP)}
              value={form.muxAdapterType}
              onChange={(event) => onChange({ muxAdapterType: event.target.value })}
              placeholder={t('Optional override')}
            />
          </div>
          <div className="form-group">
            <label title={t(MUX_TEMPERATURE_TOOLTIP)}>{t('Mux Temperature')}</label>
            <input aria-label={t('Mux Temperature')}
              title={t(MUX_TEMPERATURE_TOOLTIP)}
              value={form.muxTemperature}
              onChange={(event) => onChange({ muxTemperature: event.target.value })}
              placeholder={t('Optional number')}
              inputMode="decimal"
              aria-invalid={temperatureError ? true : undefined}
            />
            {temperatureError && <small className="text-danger" role="alert">{t(muxFieldErrorMessage(temperatureError))}</small>}
          </div>
          <div className="form-group">
            <label title={t(MUX_MAX_TOKENS_TOOLTIP)}>{t('Mux Max Tokens')}</label>
            <input aria-label={t('Mux Max Tokens')}
              title={t(MUX_MAX_TOKENS_TOOLTIP)}
              value={form.muxMaxTokens}
              onChange={(event) => onChange({ muxMaxTokens: event.target.value })}
              placeholder={t('Optional integer')}
              inputMode="numeric"
              aria-invalid={maxTokensError ? true : undefined}
            />
            {maxTokensError && <small className="text-danger" role="alert">{t(muxFieldErrorMessage(maxTokensError))}</small>}
          </div>
          <div className="form-group">
            <label title={t(MUX_SYSTEM_PROMPT_PATH_TOOLTIP)}>{t('Mux System Prompt Path')}</label>
            <input aria-label={t('Mux System Prompt Path')}
              title={t(MUX_SYSTEM_PROMPT_PATH_TOOLTIP)}
              value={form.muxSystemPromptPath}
              onChange={(event) => onChange({ muxSystemPromptPath: event.target.value })}
              placeholder={t('Optional path')}
            />
          </div>
          <div className="form-group">
            <label title={t(MUX_APPROVAL_POLICY_TOOLTIP)}>{t('Mux Approval Policy')}</label>
            <select aria-label={t('Mux Approval Policy')}
              title={t(MUX_APPROVAL_POLICY_TOOLTIP)}
              value={form.muxApprovalPolicy}
              onChange={(event) => onChange({ muxApprovalPolicy: event.target.value })}
            >
              <option value="">{t('Default (auto)')}</option>
              {MUX_APPROVAL_POLICIES.map(policy => <option key={policy} value={policy}>{policy}</option>)}
              {askPolicy && <option value={form.muxApprovalPolicy}>{t('ask (not supported, saved as deny)')}</option>}
            </select>
            {askPolicy && <small className="text-danger" role="alert">{t(MUX_APPROVAL_ASK_WARNING)}</small>}
          </div>
        </div>
      </details>
    </div>
  );
}
