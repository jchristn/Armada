import { useCallback, useEffect, useMemo, useState } from 'react';
import { listMuxEndpoints } from '../../api/client';
import { isMuxRuntime, type MuxCaptainFormFields } from '../../lib/mux';
import type { MuxEndpointInfo } from '../../types/models';

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

      setEndpoints(result.endpoints ?? []);
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

  const endpointHint = useMemo(() => {
    if (!isMuxRuntime(runtime)) return '';
    if (loading) return t('Loading saved Mux endpoints...');
    if (loadError) return loadError;
    if (endpoints.length === 0) return t('No saved Mux endpoints were found for this config directory.');
    return t('{{count}} saved Mux endpoint(s) available.', { count: endpoints.length });
  }, [endpoints.length, loadError, loading, runtime, t]);

  if (!isMuxRuntime(runtime)) return null;

  return (
    <div className="mux-runtime-fields" style={{ marginTop: compact ? '0.5rem' : '0.75rem' }}>
      <div className="wizard-form-grid">
        <div className="form-group">
          <label title={t('Optional mux config directory override. Leave blank to use mux defaults.')}>{t('Mux Config Directory')}</label>
          <input aria-label={t('Mux Config Directory')}
            value={form.muxConfigDirectory}
            onChange={(event) => onChange({ muxConfigDirectory: event.target.value })}
            placeholder={t('Optional path, e.g. C:\\Users\\you\\.mux')}
          />
        </div>
        <div className="form-group">
          <label title={t('Named mux endpoint to validate and launch for this captain.')}>{t('Mux Endpoint')}</label>
          <input aria-label={t('Mux Endpoint')}
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
        <button type="button" className="btn btn-sm" onClick={() => void loadEndpoints()} disabled={loading}>
          {loading ? t('Refreshing...') : t('Refresh Mux Endpoints')}
        </button>
        <span className={`text-dim${loadError ? ' text-danger' : ''}`}>{endpointHint}</span>
      </div>

      <details>
        <summary>{t('Advanced Mux Overrides')}</summary>
        <div className="wizard-form-grid" style={{ marginTop: '0.75rem' }}>
          <div className="form-group">
            <label>{t('Mux Base URL')}</label>
            <input aria-label={t('Mux Base URL')}
              value={form.muxBaseUrl}
              onChange={(event) => onChange({ muxBaseUrl: event.target.value })}
              placeholder={t('Optional override')}
            />
          </div>
          <div className="form-group">
            <label>{t('Mux Adapter Type')}</label>
            <input aria-label={t('Mux Adapter Type')}
              value={form.muxAdapterType}
              onChange={(event) => onChange({ muxAdapterType: event.target.value })}
              placeholder={t('Optional override')}
            />
          </div>
          <div className="form-group">
            <label>{t('Mux Temperature')}</label>
            <input aria-label={t('Mux Temperature')}
              value={form.muxTemperature}
              onChange={(event) => onChange({ muxTemperature: event.target.value })}
              placeholder={t('Optional number')}
            />
          </div>
          <div className="form-group">
            <label>{t('Mux Max Tokens')}</label>
            <input aria-label={t('Mux Max Tokens')}
              value={form.muxMaxTokens}
              onChange={(event) => onChange({ muxMaxTokens: event.target.value })}
              placeholder={t('Optional integer')}
            />
          </div>
          <div className="form-group">
            <label>{t('Mux System Prompt Path')}</label>
            <input aria-label={t('Mux System Prompt Path')}
              value={form.muxSystemPromptPath}
              onChange={(event) => onChange({ muxSystemPromptPath: event.target.value })}
              placeholder={t('Optional path')}
            />
          </div>
          <div className="form-group">
            <label>{t('Mux Approval Policy')}</label>
            <select aria-label={t('Mux Approval Policy')}
              value={form.muxApprovalPolicy}
              onChange={(event) => onChange({ muxApprovalPolicy: event.target.value })}
            >
              <option value="">{t('Default (auto)')}</option>
              <option value="auto">auto</option>
              <option value="autoapprove">autoapprove</option>
              <option value="deny">deny</option>
              <option value="ask">ask</option>
            </select>
          </div>
        </div>
      </details>
    </div>
  );
}
