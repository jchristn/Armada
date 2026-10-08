import type { ModelEndpoint, ModelEndpointKind, ModelProvider } from '@dashboard/types/models';
import { MODEL_ENDPOINT_KINDS, MODEL_PROVIDERS, unsupportedEndpointReason } from '@dashboard/lib/configuration';
import { resolveCreateScope, type ScopeViewer } from '@dashboard/lib/scoping';
import { bool, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import type { Translate } from '../../i18n/LocaleContext';
import { scopeField, scopeValue } from './common';

export function endpointValues(viewer: ScopeViewer, e: ModelEndpoint | null): FormValues {
  if (!e) {
    return {
      name: 'New Endpoint', kind: 'Inference', provider: 'OpenAI', baseUrl: '', model: '', apiKey: '', region: '', project: '', apiVersion: '', accessKeyId: '',
      dimensionality: '0', timeoutMs: '120000', enabled: true, scope: resolveCreateScope(viewer),
    };
  }
  return {
    name: e.name, kind: e.kind, provider: e.provider, baseUrl: e.baseUrl, model: e.model || '', apiKey: '', region: e.region || '', project: e.project || '',
    apiVersion: e.apiVersion || '', accessKeyId: e.accessKeyId || '', dimensionality: String(e.dimensionality ?? 0), timeoutMs: String(e.timeoutMs ?? 120000),
    enabled: e.enabled, scope: e.scope,
  };
}

/** The provider/kind guard message (translated), or null when the combination is valid. */
export function endpointReason(t: Translate, v: FormValues): string | null {
  const reason = unsupportedEndpointReason(str(v, 'provider') as ModelProvider, str(v, 'kind') as ModelEndpointKind);
  return reason ? t(reason) : null;
}

/**
 * The payload the dashboard saves. The credential is a secret: it is never shown, and it is sent only when typed, so
 * an unchanged edit keeps the stored key (the dashboard's apiKeyTouched).
 */
export function endpointPayload(viewer: ScopeViewer, v: FormValues, existing: ModelEndpoint | null): Partial<ModelEndpoint> & { apiKey?: string | null } {
  const payload: Partial<ModelEndpoint> & { apiKey?: string | null } = {
    name: str(v, 'name'),
    kind: str(v, 'kind') as ModelEndpointKind,
    provider: str(v, 'provider') as ModelProvider,
    baseUrl: str(v, 'baseUrl'),
    model: str(v, 'model').trim() || null,
    region: str(v, 'region').trim() || null,
    project: str(v, 'project').trim() || null,
    apiVersion: str(v, 'apiVersion').trim() || null,
    accessKeyId: str(v, 'accessKeyId').trim() || null,
    dimensionality: Number.parseInt(str(v, 'dimensionality'), 10) || 0,
    timeoutMs: Number.parseInt(str(v, 'timeoutMs'), 10) || 120000,
    enabled: bool(v, 'enabled'),
    scope: scopeValue(viewer, v, existing?.scope ?? null),
  };
  if (str(v, 'apiKey')) payload.apiKey = str(v, 'apiKey');
  return payload;
}

export function endpointFields(t: Translate, viewer: ScopeViewer, v: FormValues, existing: ModelEndpoint | null): FormField[] {
  const provider = str(v, 'provider');
  const isAzure = provider === 'AzureOpenAI';
  const isVertex = provider === 'VertexAI';
  const isBedrock = provider === 'Bedrock';
  const reason = endpointReason(t, v);
  const keepNote = existing?.hasApiKey ? (isVertex ? t('(unchanged - leave blank to keep stored credential)') : t('(unchanged - leave blank to keep stored key)')) : null;
  return [
    { kind: 'text', key: 'name', label: t('Name'), required: true },
    { kind: 'select', key: 'kind', label: t('Kind'), options: MODEL_ENDPOINT_KINDS.map((k) => ({ value: k, label: k })) },
    { kind: 'select', key: 'provider', label: t('Provider'), options: MODEL_PROVIDERS.map((p) => ({ value: p, label: p })), hint: reason },
    ...(isVertex || isBedrock ? [{ kind: 'text' as const, key: 'region', label: t('Region'), required: true, placeholder: isBedrock ? 'us-east-1' : 'us-central1' }] : []),
    ...(isVertex ? [{ kind: 'text' as const, key: 'project', label: t('Project') }] : []),
    ...(isBedrock ? [{ kind: 'text' as const, key: 'accessKeyId', label: t('Access Key ID') }] : []),
    {
      kind: 'text', key: 'baseUrl', required: !isVertex && !isBedrock,
      label: isVertex || isBedrock ? t('Base URL (optional override)') : isAzure ? t('Base URL (resource endpoint)') : t('Base URL'),
      placeholder: isAzure ? 'https://my-resource.openai.azure.com' : 'https://api.openai.com',
    },
    {
      kind: 'text', key: 'model', required: isAzure,
      label: isAzure ? t('Deployment name') : isBedrock ? t('Bedrock model id') : t('Model'),
      placeholder: isAzure ? 'gpt-4o' : isBedrock ? 'anthropic.claude-3-5-sonnet-20240620-v1:0' : str(v, 'kind') === 'Embedding' ? 'text-embedding-3-small' : 'gpt-4o-mini',
    },
    ...(isAzure ? [{ kind: 'text' as const, key: 'apiVersion', label: t('API Version'), placeholder: '2024-10-21 (default)' }] : []),
    {
      kind: isVertex ? 'multiline' : 'secret', key: 'apiKey',
      label: isVertex ? t('Service Account JSON') : isBedrock ? t('AWS Secret Access Key') : t('API Key'),
      placeholder: keepNote ?? (isVertex ? t('Paste the service-account JSON') : t('Optional')),
      hint: keepNote,
    },
    { kind: 'integer', key: 'dimensionality', label: t('Dimensionality') },
    { kind: 'integer', key: 'timeoutMs', label: t('Timeout (ms)') },
    scopeField(t, viewer),
    { kind: 'switch', key: 'enabled', label: t('Enabled') },
  ];
}
