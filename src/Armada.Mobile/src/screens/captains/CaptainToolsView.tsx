import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { getCaptainTools } from '@dashboard/api/client';
import type { CaptainToolAccessResult, CaptainToolServerSummary, CaptainToolSummary } from '@dashboard/types/models';
import { AppText, Button, ListRow, Section, StatusBadge } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';

/**
 * Tool access of a captain (the dashboard's CaptainToolViewer): whether its runtime can reach Armada's tools, the
 * configured MCP servers and runtime sources with their status, and the named tools. The probe can launch the
 * runtime and take a while, so it runs on request.
 */
export function CaptainToolsView({ captainId }: { captainId: string }) {
  const { t } = useLocale();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [data, setData] = useState<CaptainToolAccessResult | null>(null);

  async function load() {
    setLoading(true);
    setError('');
    try {
      setData(await getCaptainTools(captainId));
    } catch {
      setData(null);
      setError(t('Failed to load captain tools.'));
    } finally {
      setLoading(false);
    }
  }

  const servers = data?.servers ?? [];
  const tools = data?.tools ?? [];
  const mcpServers = servers.filter((s) => s.sourceKind === 'McpServer');
  const runtimeSources = servers.filter((s) => s.sourceKind !== 'McpServer');
  const internalTools = tools.filter((tool) => tool.sourceKind === 'RuntimeBuiltIn');
  const mcpTools = tools.filter((tool) => tool.sourceKind === 'McpServer');
  const runtimeReported = runtimeSources.reduce((total, s) => total + Math.max(0, s.toolCount), 0);

  function notes(server: CaptainToolServerSummary): string {
    const parts: string[] = [];
    if (server.workingDirectory) parts.push(t('WD: {{path}}', { path: server.workingDirectory }));
    if (server.startupTimeoutSeconds > 0 || server.toolTimeoutSeconds > 0) {
      parts.push(t('{{startup}}s startup / {{tool}}s tool', { startup: server.startupTimeoutSeconds, tool: server.toolTimeoutSeconds }));
    }
    if (server.headerCount > 0) parts.push(t('{{count}} header(s)', { count: server.headerCount }));
    if (server.environmentVariableCount > 0) parts.push(t('{{count}} env var(s)', { count: server.environmentVariableCount }));
    if (server.enabledToolFilterCount > 0) parts.push(t('{{count}} allow filter(s)', { count: server.enabledToolFilterCount }));
    if (server.disabledToolFilterCount > 0) parts.push(t('{{count}} deny filter(s)', { count: server.disabledToolFilterCount }));
    if (server.errorMessage) parts.push(server.errorMessage);
    return parts.join(' | ');
  }

  function serverList(title: string, list: CaptainToolServerSummary[], empty: string, key: string) {
    return (
      <Section title={title}>
        {list.length === 0 ? <ListRow title={empty} /> : list.map((s) => (
          <ListRow
            key={`${key}:${s.sourceKind}:${s.name}`}
            title={s.name}
            subtitle={[s.transport, s.url || s.command || s.target, t('Tools: {{count}}', { count: s.toolCount }), notes(s)].filter(Boolean).join(' \u00b7 ')}
            accessory={<StatusBadge label={s.status} tone={s.reachable ? 'success' : s.enabled ? 'warning' : 'cancelled'} />}
          />
        ))}
      </Section>
    );
  }

  function toolList(title: string, list: CaptainToolSummary[], empty: string, key: string) {
    return (
      <Section title={title}>
        {list.length === 0 ? <ListRow title={empty} /> : list.map((tool) => (
          <ListRow key={`${key}:${tool.registrationSource || 'internal'}:${tool.name}`} title={tool.name} subtitle={[tool.registrationSource || t('Internal'), tool.description].filter(Boolean).join(' \u00b7 ')} />
        ))}
      </Section>
    );
  }

  return (
    <View testID="captain-tools">
      <View style={styles.head}>
        <Button label={data ? t('Refresh') : t('View Tools')} icon="construct-outline" variant="secondary" onPress={() => void load()} busy={loading} testID="captain-tools-load" />
        {loading ? <AppText muted>{t('Loading captain tool access...')}</AppText> : null}
        {!loading && error ? <AppText color="danger" accessibilityRole="alert">{error}</AppText> : null}
      </View>
      {!loading && !error && data ? (
        <>
          <AppText muted style={styles.pad} testID="captain-tools-summary">{data.summary}</AppText>
          <View style={styles.tags}>
            <StatusBadge label={data.toolsAccessible ? t('Accessible') : t('Unavailable')} tone={data.toolsAccessible ? 'success' : 'failed'} />
            <StatusBadge label={data.availabilityVerified ? t('Verified') : t('Inferred')} tone={data.availabilityVerified ? 'running' : 'warning'} />
            <StatusBadge label={data.runtime} tone="cancelled" />
            {data.configuredServerCount > 0 ? <StatusBadge label={t('{{count}} sources', { count: data.configuredServerCount })} tone="cancelled" /> : null}
            {data.reachableServerCount > 0 ? <StatusBadge label={t('{{count}} reachable', { count: data.reachableServerCount })} tone="cancelled" /> : null}
            {data.endpointName ? <StatusBadge label={data.endpointName} tone="cancelled" /> : null}
            {typeof data.effectiveToolCount === 'number' ? <StatusBadge label={t('{{count}} listed tools', { count: data.effectiveToolCount })} tone="cancelled" /> : null}
          </View>
          {data.configuredServerCount > data.reachableServerCount ? (
            <AppText variant="caption" muted style={styles.pad}>{t('This is a point-in-time snapshot. Configured MCP servers that did not respond may simply be offline right now.')}</AppText>
          ) : null}
          {serverList(t('Configured MCP Servers'), mcpServers, t('No external MCP servers are configured for this captain runtime.'), 'mcp')}
          {runtimeSources.length > 0 ? serverList(t('Runtime Sources'), runtimeSources, t('No runtime-managed sources were reported for this captain runtime.'), 'runtime') : null}
          {toolList(
            t('Runtime Internal Tools'),
            internalTools,
            runtimeReported > 0
              ? t('This runtime reports {{count}} internal tool(s), but it does not expose individual tool names for every internal source.', { count: runtimeReported })
              : t('No named runtime-internal tools are currently exposed for this captain runtime.'),
            'internal',
          )}
          {toolList(
            t('MCP Tools'),
            mcpTools,
            mcpServers.length > 0
              ? t('No named MCP tools were returned from the captain runtime\'s reachable MCP servers.')
              : t('No MCP tool inventory is available for this captain runtime.'),
            'mcptools',
          )}
        </>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  head: { paddingHorizontal: spacing.lg, gap: spacing.xs, marginBottom: spacing.sm },
  pad: { paddingHorizontal: spacing.lg, marginBottom: spacing.sm },
  tags: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, paddingHorizontal: spacing.lg, marginBottom: spacing.md },
});
