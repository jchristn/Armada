import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useEffect, useMemo, useRef, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { getClientBaseUrl, getRequestHistoryEntry } from '@dashboard/api/client';
import { buildReplayState } from '@dashboard/lib/requestHistory';
import {
  CODE_TABS,
  RESPONSE_TABS,
  editableHeaderParameter,
  explorerTags,
  filterExplorerOperations,
  getResponseText,
  parameterInitialValue,
  schemaExample,
  type CodeTab,
  type ExplorerOperation,
  type ExplorerResponse,
  type OpenApiParameter,
  type OpenApiSpec,
  type ResponseTab,
} from '@dashboard/lib/apiExplorer';
import { formatBytes } from '@dashboard/lib/format';
import { useAuth } from '../../auth/AuthContext';
import { ActionBar, DetailBody, DetailHeader, DetailPending, FieldCard } from '../../components/resource/DetailParts';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList } from '../../components/resource/ResourceList';
import { ResourceRow } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { TextField } from '../../components/ui/TextField';
import { openExternalUrl } from '../../lib/externalLinks';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param } from '../../resource/links';
import { errorText, useLoad } from '../../resource/useLoad';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { buildExplorerRequest, explorerAuthHeaders, loadExplorerSpec, replayToValues, sendExplorerRequest, serverUrl, type ExplorerValues } from './ApiExplorerLogic';

/** The live OpenAPI document and its operations, loaded with the session token (and proxy headers). */
function useExplorerSpec() {
  const { t } = useLocale();
  const { sessionToken, requestHeaders } = useAuth();
  return useLoad(() => loadExplorerSpec(explorerAuthHeaders(sessionToken, requestHeaders)), [sessionToken, requestHeaders], { fallbackError: t('Failed to load OpenAPI document.') });
}

/**
 * A captured request to replay (?replay=<request history id>, from Request History): the entry is loaded and matched
 * to an operation once the OpenAPI document is in. `error` when it cannot be loaded or no operation matches.
 */
type ReplayResult = { match: { operationId: string; values: ExplorerValues }; error?: undefined } | { error: string; match?: undefined } | null;

function useReplay(replayId: string, operations: ExplorerOperation[] | null): ReplayResult {
  const { t } = useLocale();
  const entry = useLoad(async () => buildReplayState(await getRequestHistoryEntry(replayId)), [replayId], { enabled: !!replayId });
  return useMemo<ReplayResult>(() => {
    if (!replayId) return null;
    if (entry.error) return { error: entry.error };
    if (!entry.data || !operations) return null;
    const match = replayToValues(operations, entry.data);
    return match ? { match } : { error: t('No matching OpenAPI operation was found for the replay request.') };
  }, [replayId, entry.data, entry.error, operations, t]);
}

function methodTone(method: string) {
  switch (method.toLowerCase()) {
    case 'get': return 'info' as const;
    case 'post': return 'success' as const;
    case 'put':
    case 'patch': return 'warning' as const;
    case 'delete': return 'failed' as const;
    default: return 'cancelled' as const;
  }
}

/**
 * /api-explorer: the live Armada API surface by category and filter text. Choosing an operation opens its request
 * builder (beside the list on tablets, its own /api-explorer/:operationId screen on phones).
 */
export function ApiExplorerListRoute() {
  const { t } = useLocale();
  const { colors } = useTheme();
  const { data, loading, refreshing, error, reload, refresh } = useExplorerSpec();
  const operations = useMemo(() => data?.operations ?? [], [data]);
  const [tag, setTag] = useState('All');
  const [filter, setFilter] = useState('');
  const filtered = useMemo(() => filterExplorerOperations(operations, tag, filter), [operations, tag, filter]);
  const selection = useSelection((id) => `/api-explorer/${encodeURIComponent(id)}`);
  const router = useRouter();
  const params = useLocalSearchParams<{ replay?: string }>();
  const replayId = param(params.replay);
  const replay = useReplay(replayId, data?.operations ?? null);
  const replayOperationId = replay?.match?.operationId ?? '';
  // A replay opens the matching operation's builder, prefilled (its own screen keeps ?replay=).
  useEffect(() => {
    if (replayOperationId) router.replace(`/api-explorer/${encodeURIComponent(replayOperationId)}?replay=${encodeURIComponent(replayId)}` as Href);
  }, [replayOperationId, replayId, router]);
  const selectedOperation = selection.selected ? operations.find((o) => o.id === selection.selected) ?? null : null;

  const list = (
    <ResourceList
      testID="api-explorer"
      items={filtered}
      keyOf={(o) => o.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: filter, onChange: setFilter, placeholder: t('Filter by path or summary') }}
      filters={[{ key: 'category', label: t('Category'), value: tag, allValue: 'All', onChange: setTag, options: explorerTags(operations).map((x) => ({ value: x, label: x })) }]}
      header={(
        <>
          <AppText muted style={resourceStyles.pad}>{t('Browse the live OpenAPI document, execute authenticated requests, inspect responses, and replay captured traffic.')}</AppText>
          {replay?.error ? <AppText color="danger" accessibilityRole="alert" style={resourceStyles.pad} testID="api-replay-error">{replay.error}</AppText> : null}
          <ActionBar>
            <Button label={t('OpenAPI JSON')} variant="secondary" icon="open-outline" style={resourceStyles.action} onPress={() => void openExternalUrl(serverUrl('/openapi.json'))} />
            <Button label={t('Swagger')} variant="secondary" icon="open-outline" style={resourceStyles.action} onPress={() => void openExternalUrl(serverUrl('/swagger'))} />
          </ActionBar>
        </>
      )}
      emptyTitle={operations.length > 0 ? t('No operations match the current filter') : t('No operations are available in the current OpenAPI document.')}
      renderItem={(o) => (
        <ResourceRow
          testID={`api-operation-${o.id}`}
          title={`${o.method.toUpperCase()} ${o.path}`}
          subtitle={o.summary !== `${o.method.toUpperCase()} ${o.path}` ? o.summary : null}
          meta={o.tag}
          selected={selection.selected === o.id}
          onPress={() => selection.open(o.id)}
        />
      )}
    />
  );

  return (
    <SafeAreaView edges={['left', 'right']} style={[styles.fill, { backgroundColor: colors.background }]}>
      <Stack.Screen options={{ title: t('API Explorer') }} />
      <MasterDetail
        list={list}
        detail={selectedOperation && data ? <OperationView key={selectedOperation.id} operation={selectedOperation} spec={data.spec} embedded /> : null}
      />
    </SafeAreaView>
  );
}

/** /api-explorer/:operationId: the request builder for one operation (deep-linkable, as on the dashboard). */
export function ApiExplorerOperationRoute() {
  const { t } = useLocale();
  const params = useLocalSearchParams<{ operationId: string; replay?: string }>();
  const operationId = param(params.operationId);
  const replayId = param(params.replay);
  const { data, loading, error, reload } = useExplorerSpec();
  const replay = useReplay(replayId, data?.operations ?? null);
  const operation = data?.operations.find((o) => o.id === operationId) ?? null;
  if (replayId && !replay) return <DetailPending loading error="" onRetry={() => void reload()} />;
  const prefill = replay?.match && replay.match.operationId === operationId ? replay.match.values : null;
  if (!data || !operation) {
    return (
      <>
        <Stack.Screen options={{ title: t('API Explorer') }} />
        <DetailPending loading={loading} error={data ? t('No operations match the current filter') : error} onRetry={() => void reload()} />
      </>
    );
  }
  return (
    <>
      {replay?.error ? <AppText color="danger" accessibilityRole="alert" style={resourceStyles.pad} testID="api-replay-error">{replay.error}</AppText> : null}
      <OperationView key={`${operation.id}:${replayId}`} operation={operation} spec={data.spec} prefill={prefill} />
    </>
  );
}

function initialValues(operation: ExplorerOperation, spec: OpenApiSpec) {
  const path: Record<string, string> = {};
  const query: Record<string, string> = {};
  const header: Record<string, string> = {};
  operation.parameters.forEach((p) => {
    if (p.in === 'path') path[p.name] = parameterInitialValue(p, spec);
    if (p.in === 'query') query[p.name] = parameterInitialValue(p, spec);
    if (editableHeaderParameter(p)) header[p.name] = parameterInitialValue(p, spec);
  });
  const body = operation.requestBody ? JSON.stringify(schemaExample(operation.requestBody.schema, spec), null, 2) : '';
  return { path, query, header, body };
}

function ParameterFields({ title, parameters, values, onChange, testID }: {
  title: string;
  parameters: OpenApiParameter[];
  values: Record<string, string>;
  onChange: (name: string, value: string) => void;
  testID: string;
}) {
  const { t } = useLocale();
  return (
    <FieldCard title={`${title} (${parameters.length})`} testID={testID}>
      <View style={styles.inner}>
        {parameters.length === 0 ? <AppText muted>{t('No parameters')}</AppText> : parameters.map((p) => (
          <TextField
            key={`${p.in}-${p.name}`}
            label={p.required ? `${p.name} *` : p.name}
            value={values[p.name] || ''}
            onChangeText={(v) => onChange(p.name, v)}
            placeholder={p.description || p.name}
            autoCapitalize="none"
            autoCorrect={false}
            testID={`${testID}-${p.name}`}
          />
        ))}
      </View>
    </FieldCard>
  );
}

const TAB_LABELS: Record<ResponseTab, string> = { preview: 'Preview', body: 'Body', headers: 'Headers', code: 'Code' };

/** The request builder and response viewer for one operation. */
function OperationView({ operation, spec, embedded, prefill }: { operation: ExplorerOperation; spec: OpenApiSpec; embedded?: boolean; prefill?: ExplorerValues | null }) {
  const { t } = useLocale();
  const { sessionToken, requestHeaders } = useAuth();
  const { pushToast } = useNotifications();
  const [values, setValues] = useState<ExplorerValues>(() => prefill ?? initialValues(operation, spec));
  const [response, setResponse] = useState<ExplorerResponse | null>(null);
  const [responseTab, setResponseTab] = useState<ResponseTab>('preview');
  const [codeTab, setCodeTab] = useState<CodeTab>('curl');
  const [running, setRunning] = useState(false);
  const [error, setError] = useState('');
  const abortRef = useRef<AbortController | null>(null);

  const preview = buildExplorerRequest(operation, values, getClientBaseUrl(), sessionToken);
  const set = (group: 'path' | 'query' | 'header', name: string, value: string) =>
    setValues((current) => ({ ...current, [group]: { ...current[group], [name]: value } }));

  async function send() {
    const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;
    abortRef.current = controller;
    setRunning(true);
    setError('');
    try {
      const result = await sendExplorerRequest(preview, requestHeaders, controller?.signal);
      setResponse(result);
      setResponseTab('preview');
      pushToast('success', t('Request completed with status {{status}}.', { status: result.status }));
    } catch (err: unknown) {
      if (!(err instanceof Error && err.name === 'AbortError')) setError(errorText(err, t('Request failed.')));
    } finally {
      abortRef.current = null;
      setRunning(false);
    }
  }

  const responseText = response ? getResponseText(response, responseTab, codeTab) : '';

  return (
    <DetailBody embedded={embedded} testID="api-operation">
      {!embedded ? <Stack.Screen options={{ title: t('API Explorer') }} /> : null}
      <DetailHeader
        title={operation.summary || t('Request Builder')}
        subtitle={operation.description || operation.path}
        badges={(
          <>
            <StatusBadge label={operation.method.toUpperCase()} tone={methodTone(operation.method)} />
            <StatusBadge label={operation.tag} tone="cancelled" />
          </>
        )}
      />
      <AppText variant="mono" selectable style={resourceStyles.pad}>{`${operation.method.toUpperCase()} ${operation.path}`}</AppText>
      <ParameterFields title={t('Path Parameters')} parameters={operation.parameters.filter((p) => p.in === 'path')} values={values.path} onChange={(n, v) => set('path', n, v)} testID="api-path" />
      <ParameterFields title={t('Query Parameters')} parameters={operation.parameters.filter((p) => p.in === 'query')} values={values.query} onChange={(n, v) => set('query', n, v)} testID="api-query" />
      <ParameterFields title={t('Headers')} parameters={operation.parameters.filter(editableHeaderParameter)} values={values.header} onChange={(n, v) => set('header', n, v)} testID="api-header" />
      {operation.requestBody ? (
        <FieldCard title={`${t('Request Body')} (${operation.requestBodyContentType || 'application/json'})`}>
          <View style={styles.inner}>
            <TextField
              label={t('Request Body')}
              value={values.body}
              onChangeText={(body) => setValues((current) => ({ ...current, body }))}
              multiline
              autoCapitalize="none"
              autoCorrect={false}
              testID="api-body"
            />
          </View>
        </FieldCard>
      ) : null}
      <FieldCard title={`${t('Request Preview')} (${preview.method.toUpperCase()})`}>
        <AppText variant="mono" selectable style={styles.innerText} testID="api-request-url">{preview.url}</AppText>
      </FieldCard>
      <ActionBar>
        <Button label={running ? t('Running...') : t('Send Request')} icon="send-outline" busy={running} onPress={() => void send()} style={resourceStyles.action} testID="api-send" />
        {running ? <Button label={t('Abort')} variant="secondary" onPress={() => abortRef.current?.abort()} style={resourceStyles.action} /> : null}
      </ActionBar>
      {error ? <AppText color="danger" accessibilityRole="alert" style={resourceStyles.pad}>{error}</AppText> : null}

      <FieldCard title={t('Response')} testID="api-response">
        <View style={styles.inner}>
          {!response ? (
            <AppText muted>{t('Send a request to inspect the live response here.')}</AppText>
          ) : (
            <>
              <View style={resourceStyles.row}>
                <StatusBadge label={`${response.status} ${response.statusText}`.trim()} tone={response.ok ? 'success' : 'failed'} />
                <AppText variant="caption" muted>{response.contentType || 'n/a'}</AppText>
                <AppText variant="caption" muted>{`${response.durationMs.toFixed(2)} ms`}</AppText>
                <AppText variant="caption" muted>{formatBytes(response.sizeBytes)}</AppText>
              </View>
              <View style={styles.tabs}>
                <SegmentedControl
                  label={t('Response')}
                  value={responseTab}
                  onChange={setResponseTab}
                  options={RESPONSE_TABS.map((tab) => ({ value: tab, label: t(TAB_LABELS[tab]), testID: `api-response-tab-${tab}` }))}
                />
                {responseTab === 'code' ? (
                  <SegmentedControl label={t('Code')} value={codeTab} onChange={setCodeTab} options={CODE_TABS.map((tab) => ({ value: tab, label: tab }))} />
                ) : null}
              </View>
              <AppText variant="mono" selectable testID="api-response-text">{responseText || '(empty)'}</AppText>
            </>
          )}
        </View>
      </FieldCard>
    </DetailBody>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  inner: { padding: spacing.md },
  innerText: { padding: spacing.md },
  tabs: { marginTop: spacing.md },
});
