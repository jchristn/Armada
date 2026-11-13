import { useMemo, useState } from 'react';
import { ActivityIndicator } from 'react-native';
import { BottomSheet } from '../../components/ui/BottomSheet';
import {
  createCaptain, createModelEndpoint, duplicateModelEndpoint, deleteModelEndpoint, getModelEndpoint, healthCheckModelEndpoints, enumerateModelEndpoints, updateModelEndpoint, validateModelEndpoint,
} from '@dashboard/api/client';
import type { ModelEndpoint, ModelEndpointProbeResult } from '@dashboard/types/models';
import { formatHealthSpan, MODEL_ENDPOINT_KINDS } from '@dashboard/lib/configuration';
import { canEdit } from '@dashboard/lib/scoping';
import { useAuth } from '../../auth/AuthContext';
import { ActionBar, DetailHeader, DetailPending, Field, FieldCard, JsonSheet } from '../../components/resource/DetailParts';
import { FormSheet } from '../../components/resource/FormSheet';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { TruncationNote } from '../../components/resource/TruncationNote';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import type { StatusTone } from '../../components/ui/StatusBadge';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { LocalBody, LocalMasterDetail, scopeBadge, useLocalSelection, useViewer } from './common';
import { authTypeLabel, endpointFields, endpointPayload, endpointSecretLabel, endpointValidate, endpointValues, type AuthModelEndpoint } from './endpointForm';

function healthTone(e: ModelEndpoint): StatusTone {
  return e.healthStatus === 'Healthy' ? 'success' : e.healthStatus === 'Unhealthy' ? 'failed' : 'cancelled';
}

/** "OK OK FAIL ..." strip of the latest probes (the dashboard draws a histogram). */
function historyText(endpoint: ModelEndpoint): string {
  return endpoint.healthHistory.slice(-24).map((r) => (r.success ? '\u25A0' : '\u25A1')).join('');
}

interface EndpointDetailProps {
  id: string;
  inSheet: boolean;
  onEdit: (e: ModelEndpoint) => void;
  onDuplicate: (e: ModelEndpoint) => void;
  onDelete: (e: ModelEndpoint) => void;
  onChanged: () => void;
}

/** One endpoint's health (the dashboard's health modal) plus Validate, Edit, View JSON, and Delete. */
function EndpointDetail({ id, inSheet, onEdit, onDuplicate, onDelete, onChanged }: EndpointDetailProps) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const { isAdmin, isTenantAdmin } = useAuth();
  const viewer = useViewer();
  const [probe, setProbe] = useState<ModelEndpointProbeResult | null>(null);
  const [validating, setValidating] = useState(false);
  const [validationOpen, setValidationOpen] = useState(false);
  const [validationError, setValidationError] = useState('');
  const [jsonOpen, setJsonOpen] = useState(false);
  const { data: endpoint, loading, error, reload } = useLoad(() => getModelEndpoint(id), [id], { fallbackError: t('Failed to load endpoints.') });

  if (!endpoint) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const e: ModelEndpoint = endpoint;
  const auth = e as AuthModelEndpoint;
  const editable = canEdit(viewer, e);
  const canManage = isAdmin || isTenantAdmin;

  async function validate() {
    setValidationOpen(true);
    setProbe(null);
    setValidationError('');
    setValidating(true);
    try {
      const result = await validateModelEndpoint(e.id);
      setProbe(result);
      if (result.success) pushToast('success', t('Endpoint "{{name}}" validated.', { name: e.name }));
      else pushToast('error', t('Endpoint "{{name}}" validation failed.', { name: e.name }));
      await reload();
      onChanged();
    } catch (err: unknown) {
      const message = errorText(err, t('Validation failed.'));
      setValidationError(message);
      pushToast('error', message);
    } finally {
      setValidating(false);
    }
  }

  return (
    <LocalBody inSheet={inSheet} testID="endpoint-detail">
      <DetailHeader title={e.name} subtitle={e.id} testID="endpoint-title" badges={(
        <>
          <StatusBadge label={t(e.healthStatus)} tone={healthTone(e)} />
          {!e.enabled ? <StatusBadge label={t('disabled')} tone="cancelled" /> : null}
          <StatusBadge {...scopeBadge(t, e.scope)} />
        </>
      )} />
      <ActionBar>
        {canManage || editable ? <Button label={t('Validate Now')} busy={validating} style={resourceStyles.action} onPress={() => void validate()} testID="endpoint-validate" /> : null}
        {editable ? <Button label={t('Edit')} variant="secondary" style={resourceStyles.action} onPress={() => onEdit(e)} /> : null}
        {editable ? <Button label={t('Duplicate')} variant="secondary" style={resourceStyles.action} onPress={() => onDuplicate(e)} /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {editable ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={() => onDelete(e)} /> : null}
      </ActionBar>
      <BottomSheet open={validationOpen} title={t('Validation Result')} closeLabel={t('Close')} onClose={() => setValidationOpen(false)} testID="endpoint-validation-modal">
        {validating ? <><ActivityIndicator accessibilityLabel={t('Validating...')} /><AppText accessibilityLiveRegion="polite">{t('Validating...')}</AppText></> : null}
        {validationError ? <AppText color="danger" accessibilityRole="alert">{validationError}</AppText> : null}
      {probe ? (
        <FieldCard title={t('Validation Result')} testID="endpoint-probe">
          <Field label={t('Result')} value={probe.success ? t('Success') : t('Failed')} />
          <Field label={t('Latency')} value={`${probe.latencyMs} ms`} />
          {probe.statusCode != null ? <Field label={t('Status Code')} value={probe.statusCode} /> : null}
          {probe.embeddingDimensions != null ? <Field label={t('Dimensions')} value={probe.embeddingDimensions} /> : null}
          {probe.sampleText ? <Field label={t('Sample')} value={probe.sampleText} mono /> : null}
          {probe.error ? <Field label={t('Error')} value={probe.error} /> : null}
        </FieldCard>
      ) : null}
      </BottomSheet>
      <FieldCard>
        <Field label={t('Kind')} value={e.kind} />
        <Field label={t('Provider')} value={e.provider} />
        <Field label={t('Base URL')} value={e.baseUrl} mono />
        <Field label={t('Model')} value={e.model} />
        {e.region ? <Field label={t('Region')} value={e.region} /> : null}
        {e.project ? <Field label={t('Project')} value={e.project} /> : null}
        {e.apiVersion ? <Field label={t('API Version')} value={e.apiVersion} /> : null}
        {e.accessKeyId ? <Field label={t('Access Key ID')} value={e.accessKeyId} /> : null}
        <Field label={t('Authentication')} value={authTypeLabel(t, auth.authType)} />
        {auth.authType === 'Basic' && auth.authUsername ? <Field label={t('Username')} value={auth.authUsername} /> : null}
        {auth.authType === 'Header' && auth.authHeaderName ? <Field label={t('Header name')} value={auth.authHeaderName} mono /> : null}
        {auth.authType === 'QueryParameter' && auth.authQueryParameter ? <Field label={t('Query parameter name')} value={auth.authQueryParameter} mono /> : null}
        {auth.authType !== 'None' ? <Field label={endpointSecretLabel(t, auth.authType, e.provider)} value={e.hasApiKey ? t('Stored') : t('None')} /> : null}
        {(auth.headers ?? []).length > 0 ? (
          <Field label={t('Extra headers')} value={(auth.headers ?? []).map((h) => `${h.name}: ${h.hasValue ? '******' : ''}`).join('\n')} mono />
        ) : null}
        <Field label={t('Dimensionality')} value={e.dimensionality} />
        <Field label={t('Timeout (ms)')} value={e.timeoutMs} />
      </FieldCard>
      <FieldCard title={t('Health')} testID="endpoint-health">
        <Field label={t('Status')} value={t(e.healthStatus)} />
        <Field label={t('Uptime')} value={`${e.uptimePercentage.toFixed(1)}%`} />
        <Field label={t('History Span')} value={formatHealthSpan(e.firstHealthCheckUtc)} />
        <Field label={t('Consecutive OK')} value={e.consecutiveSuccesses} />
        <Field label={t('Consecutive Fail')} value={e.consecutiveFailures} />
        {e.lastLatencyMs != null ? <Field label={t('Latency')} value={`${e.lastLatencyMs} ms`} /> : null}
        {e.lastHealthError ? <Field label={t('Last Error')} value={e.lastHealthError} /> : null}
        {e.healthHistory.length > 0 ? <Field label={t('Health History')} value={historyText(e)} mono /> : null}
        <Field label={t('First check')} value={e.firstHealthCheckUtc ? formatDateTime(e.firstHealthCheckUtc) : '-'} />
        <Field label={t('Last check')} value={e.lastHealthCheckUtc ? formatDateTime(e.lastHealthCheckUtc) : '-'} />
        <Field label={t('Last healthy')} value={e.lastHealthyUtc ? formatDateTime(e.lastHealthyUtc) : '-'} />
        <Field label={t('Last unhealthy')} value={e.lastUnhealthyUtc ? formatDateTime(e.lastUnhealthyUtc) : '-'} />
        <AppText variant="caption" muted style={resourceStyles.pad}>{t('Health checks are deduplicated by base URL: endpoints sharing a base URL are probed once per sweep.')}</AppText>
      </FieldCard>
      <JsonSheet open={jsonOpen} title={e.name} data={e} onClose={() => setJsonOpen(false)} />
    </LocalBody>
  );
}

/**
 * Configuration > Endpoints: managed embedding and inference model endpoints with health (deduplicated by base URL),
 * Run Health Sweep, Validate, and create / edit / delete for tenant admins (rows follow the scope rules). Credentials
 * are write-only: never shown, sent only when typed.
 */
export function EndpointsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const viewer = useViewer();
  const canManage = isAdmin || isTenantAdmin;
  const { confirm, dialog } = useConfirm('endpoint-confirm');
  const selection = useLocalSelection();
  // One bounded page of the largest size; the note in the header says when more endpoints exist.
  const [total, setTotal] = useState(0);
  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => {
    const page = await enumerateModelEndpoints();
    setTotal(page?.totalRecords ?? 0);
    return page?.objects ?? [];
  }, [], { fallbackError: t('Failed to load endpoints.') });
  useReloadOnFocus(reload);
  const endpoints = useMemo(() => data ?? [], [data]);
  const [search, setSearch] = useState('');
  const [kind, setKind] = useState('all');
  const [editing, setEditing] = useState<ModelEndpoint | 'new' | null>(null);
  const [duplicateSource, setDuplicateSource] = useState<ModelEndpoint | null>(null);
  const [sweeping, setSweeping] = useState(false);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return endpoints.filter((e) => (!term || [e.name, e.baseUrl, e.model, e.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (kind === 'all' || e.kind === kind));
  }, [endpoints, search, kind]);

  async function sweep() {
    setSweeping(true);
    try {
      const result = await healthCheckModelEndpoints();
      pushToast('success', t('Health sweep probed {{count}} base URL(s).', { count: result.distinctBaseUrlsProbed }));
      await reload();
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Health sweep failed.')));
    } finally {
      setSweeping(false);
    }
  }

  function remove(e: ModelEndpoint) {
    confirm({
      title: t('Delete Endpoint'),
      message: t('Delete "{{name}}"? This cannot be undone.', { name: e.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteModelEndpoint(e.id);
          pushToast('warning', t('Endpoint "{{name}}" deleted.', { name: e.name }));
          if (selection.selected === e.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  function duplicate(e: ModelEndpoint) {
    setDuplicateSource(e);
    setEditing('new');
  }

  const existing = editing && editing !== 'new' ? editing : null;
  const formSource = duplicateSource ?? existing;
  const selected = endpoints.find((e) => e.id === selection.selected);
  const list = (
    <ResourceList
      testID="endpoints"
      items={filtered}
      keyOf={(e) => e.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by name, base URL, model, or ID...') }}
      filters={[{ key: 'kind', label: t('Kind'), value: kind, onChange: setKind, options: [{ value: 'all', label: t('All kinds') }, ...MODEL_ENDPOINT_KINDS.map((k) => ({ value: k, label: k }))] }]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Endpoints'), value: Math.max(total, endpoints.length) },
            { label: t('Healthy'), value: endpoints.filter((e) => e.healthStatus === 'Healthy').length, tone: 'success' },
            { label: t('Unhealthy'), value: endpoints.filter((e) => e.healthStatus === 'Unhealthy').length, tone: 'danger' },
            { label: t('Enabled'), value: endpoints.filter((e) => e.enabled).length },
          ]} />
          <TruncationNote shown={endpoints.length} total={total} />
          {canManage ? <Button label={sweeping ? t('Sweeping...') : t('Run Health Sweep')} variant="secondary" busy={sweeping} onPress={() => void sweep()} style={resourceStyles.create} testID="endpoints-sweep" /> : null}
          {canManage ? <Button label={t('Endpoint')} icon="add" onPress={() => { setDuplicateSource(null); setEditing('new'); }} style={resourceStyles.create} testID="endpoints-create" /> : null}
        </>
      )}
      emptyTitle={t('No endpoints match the current filters.')}
      emptyMessage={canManage ? t('Add an embedding or inference endpoint to manage and monitor it.') : t('Ask a tenant administrator to configure model endpoints.')}
      renderItem={(e) => {
        const editable = canEdit(viewer, e);
        return (
          <ResourceRow
            testID={`endpoint-row-${e.id}`}
            title={e.enabled ? e.name : `${e.name} (${t('disabled')})`}
            subtitle={[`${e.kind} \u2022 ${e.provider}`, authTypeLabel(t, (e as AuthModelEndpoint).authType), e.model, e.baseUrl].filter(Boolean).join(' \u2022 ')}
            badge={{ label: t(e.healthStatus), tone: healthTone(e) }}
            meta={e.lastHealthCheckUtc ? formatRelativeTime(e.lastHealthCheckUtc) : t('Never')}
            selected={selection.selected === e.id}
            onPress={() => selection.open(e.id)}
            actions={editable ? [
              { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => { setDuplicateSource(null); setEditing(e); } },
              { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline', onPress: () => duplicate(e) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => remove(e) },
            ] : []}
          />
        );
      }}
    />
  );

  return (
    <>
      <LocalMasterDetail
        list={list}
        title={selected ? `${t('Health')}: ${selected.name}` : t('Health')}
        open={!!selection.selected}
        onClose={selection.clear}
        detail={selection.selected ? (
          <EndpointDetail key={selection.selected} id={selection.selected} inSheet={!selection.isTablet}
            onEdit={(e) => { setDuplicateSource(null); setEditing(e); }} onDuplicate={duplicate} onDelete={(e) => remove(e)} onChanged={() => void reload()} />
        ) : null}
      />
      <FormSheet
        testID="endpoint-form"
        open={editing !== null}
        title={duplicateSource ? t('Duplicate Endpoint') : existing ? t('Edit Endpoint') : t('Create Endpoint')}
        initial={{ ...endpointValues(viewer, formSource), ...(duplicateSource ? { name: `${duplicateSource.name} (copy)` } : {}) }}
        fields={(v) => endpointFields(t, viewer, v, formSource)}
        validate={(v) => endpointValidate(t, v, formSource)}
        submitLabel={existing ? t('Save Changes') : t('Create Endpoint')}
        onClose={() => { setEditing(null); setDuplicateSource(null); }}
        onSubmit={async (v) => {
          if (existing) {
            const updated = await updateModelEndpoint(existing.id, endpointPayload(viewer, v, existing));
            pushToast('success', t('Endpoint "{{name}}" saved.', { name: updated.name }));
          } else {
            const payload = endpointPayload(viewer, v, formSource);
            const created = duplicateSource ? await duplicateModelEndpoint(duplicateSource.id, payload) : await createModelEndpoint(payload);
            pushToast('success', t('Endpoint "{{name}}" created.', { name: created.name }));
            if (created.kind === 'Inference') {
              confirm({
                title: t('Create a captain for this endpoint'),
                message: t('Create an API Endpoint captain when saving, ready to select in Ask Armada and persona priorities.'),
                confirmLabel: t('Create Captain'),
                onConfirm: async () => {
                  try {
                    await createCaptain({ name: created.name, runtime: 'ApiEndpoint', modelEndpointId: created.id });
                    pushToast('success', t('Captain created.'));
                  } catch (err: unknown) {
                    pushToast('error', errorText(err, t('Endpoint saved, but captain creation failed.')));
                  }
                },
              });
            }
          }
          setEditing(null);
          setDuplicateSource(null);
          await reload();
        }}
      />
      {dialog}
    </>
  );
}
