import { useLocale } from '../../context/LocaleContext';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import type { CaptainToolAccessResult, CaptainToolServerSummary, CaptainToolSummary } from '../../types/models';

interface CaptainToolViewerProps {
  open: boolean;
  captainName: string;
  loading: boolean;
  error: string;
  data: CaptainToolAccessResult | null;
  onClose: () => void;
}

export default function CaptainToolViewer({ open, captainName, loading, error, data, onClose }: CaptainToolViewerProps) {
  const { t } = useLocale();

  if (!open) return null;

  const configuredMcpServers = data?.servers.filter((server) => server.sourceKind === 'McpServer') ?? [];
  const runtimeSources = data?.servers.filter((server) => server.sourceKind !== 'McpServer') ?? [];
  const runtimeInternalTools = data?.tools.filter((tool) => tool.sourceKind === 'RuntimeBuiltIn') ?? [];
  const mcpTools = data?.tools.filter((tool) => tool.sourceKind === 'McpServer') ?? [];
  const runtimeReportedToolCount = runtimeSources.reduce((total, source) => total + Math.max(0, source.toolCount), 0);

  function formatEndpoint(server: CaptainToolServerSummary) {
    return server.url || server.command || server.target || '-';
  }

  function formatServerNotes(server: CaptainToolServerSummary) {
    const notes: string[] = [];
    if (server.workingDirectory) notes.push(t('WD: {{path}}', { path: server.workingDirectory }));
    if (server.startupTimeoutSeconds > 0 || server.toolTimeoutSeconds > 0) {
      notes.push(t('{{startup}}s startup / {{tool}}s tool', { startup: server.startupTimeoutSeconds, tool: server.toolTimeoutSeconds }));
    }
    if (server.headerCount > 0) notes.push(t('{{count}} header(s)', { count: server.headerCount }));
    if (server.environmentVariableCount > 0) notes.push(t('{{count}} env var(s)', { count: server.environmentVariableCount }));
    if (server.enabledToolFilterCount > 0) notes.push(t('{{count}} allow filter(s)', { count: server.enabledToolFilterCount }));
    if (server.disabledToolFilterCount > 0) notes.push(t('{{count}} deny filter(s)', { count: server.disabledToolFilterCount }));
    if (server.errorMessage) notes.push(server.errorMessage);
    return notes.length > 0 ? notes.join(' | ') : '-';
  }

  function renderServerTable(tableKey: string, title: string, servers: CaptainToolServerSummary[], emptyMessage: string) {
    const columns: DataTableColumn<CaptainToolServerSummary>[] = [
      { key: 'source', label: t('Source'), required: true, render: (server) => <strong>{server.name}</strong> },
      { key: 'transport', label: t('Transport'), render: (server) => server.transport || '-' },
      {
        key: 'endpoint', label: t('Endpoint / Target'), cellClassName: 'mono captain-tool-viewer-endpoint-cell',
        // One line; the full endpoint is in the tooltip.
        render: (server) => <span className="cell-one-line" title={formatEndpoint(server)}>{formatEndpoint(server)}</span>,
      },
      {
        key: 'status', label: t('Status'), cellClassName: 'cell-nowrap',
        render: (server) => (
          <span className={`tag ${server.reachable ? 'complete' : server.enabled ? 'review' : 'idle'}`}>
            {server.status}
          </span>
        ),
      },
      { key: 'tools', label: t('Tools'), render: (server) => server.toolCount },
      {
        key: 'notes', label: t('Notes'), cellClassName: 'text-dim captain-tool-viewer-notes-cell',
        cellTitle: (server) => formatServerNotes(server),
        render: (server) => <span className="line-clamp-2">{formatServerNotes(server)}</span>,
      },
    ];
    return (
      <section className="captain-tool-viewer-section">
        <h4>{title}</h4>
        {servers.length === 0 ? (
          <p className="text-dim">{emptyMessage}</p>
        ) : (
          <DataTable
            tableKey={tableKey}
            columns={columns}
            rows={servers}
            rowKey={(server) => `${server.sourceKind}:${server.name}`}
            wrapClassName="captain-tool-viewer-table captain-tool-viewer-server-table"
            ariaLabel={title}
          />
        )}
      </section>
    );
  }

  function renderToolTable(tableKey: string, title: string, tools: CaptainToolSummary[], emptyMessage: string) {
    const columns: DataTableColumn<CaptainToolSummary>[] = [
      { key: 'tool', label: t('Tool'), required: true, cellClassName: 'mono captain-tool-viewer-tool-name', render: (tool) => tool.name },
      {
        key: 'source', label: t('Server / Source'), cellClassName: 'captain-tool-viewer-source',
        render: (tool) => <span className="tag idle">{tool.registrationSource || t('Internal')}</span>,
      },
      { key: 'description', label: t('Description'), render: (tool) => tool.description },
    ];
    return (
      <section className="captain-tool-viewer-section">
        <h4>{title}</h4>
        {tools.length === 0 ? (
          <p className="text-dim">{emptyMessage}</p>
        ) : (
          <DataTable
            tableKey={tableKey}
            columns={columns}
            rows={tools}
            rowKey={(tool) => `${tool.registrationSource || 'internal'}:${tool.name}`}
            wrapClassName="captain-tool-viewer-table"
            ariaLabel={title}
          />
        )}
      </section>
    );
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal captain-tool-viewer" onClick={(event) => event.stopPropagation()}>
        <div className="captain-tool-viewer-header">
          <div>
            <h3>{t('Available Tools')}</h3>
            <p className="text-dim">{captainName}</p>
          </div>
          <button type="button" className="btn btn-sm" onClick={onClose}>
            {t('Close')}
          </button>
        </div>

        {loading && <p className="text-dim">{t('Loading captain tool access...')}</p>}
        {!loading && error && <p className="text-dim">{error}</p>}

        {!loading && !error && data && (
          <>
            <p className="text-dim captain-tool-viewer-summary">{data.summary}</p>
            <div className="captain-tool-viewer-meta">
              <span className={`tag ${data.toolsAccessible ? 'complete' : 'failed'}`}>
                {data.toolsAccessible ? t('Accessible') : t('Unavailable')}
              </span>
              <span className={`tag ${data.availabilityVerified ? 'working' : 'review'}`}>
                {data.availabilityVerified ? t('Verified') : t('Inferred')}
              </span>
              <span className="tag idle">{data.runtime}</span>
              {data.configuredServerCount > 0 && (
                <span className="tag idle">{t('{{count}} sources', { count: data.configuredServerCount })}</span>
              )}
              {data.reachableServerCount > 0 && (
                <span className="tag idle">{t('{{count}} reachable', { count: data.reachableServerCount })}</span>
              )}
              {data.endpointName && <span className="tag idle">{data.endpointName}</span>}
              {typeof data.effectiveToolCount === 'number' && (
                <span className="tag idle">{t('{{count}} listed tools', { count: data.effectiveToolCount })}</span>
              )}
            </div>
            {data.configuredServerCount > data.reachableServerCount && (
              <p className="text-dim captain-tool-viewer-summary">
                {t('This is a point-in-time snapshot. Configured MCP servers that did not respond may simply be offline right now.')}
              </p>
            )}

            {renderServerTable(
              'captain-tools-mcp-servers',
              t('Configured MCP Servers'),
              configuredMcpServers,
              t('No external MCP servers are configured for this captain runtime.'),
            )}

            {runtimeSources.length > 0 &&
              renderServerTable(
                'captain-tools-runtime-sources',
                t('Runtime Sources'),
                runtimeSources,
                t('No runtime-managed sources were reported for this captain runtime.'),
              )}

            {renderToolTable(
              'captain-tools-internal',
              t('Runtime Internal Tools'),
              runtimeInternalTools,
              runtimeReportedToolCount > 0
                ? t('This runtime reports {{count}} internal tool(s), but it does not expose individual tool names for every internal source.', { count: runtimeReportedToolCount })
                : t('No named runtime-internal tools are currently exposed for this captain runtime.'),
            )}

            {renderToolTable(
              'captain-tools-mcp',
              t('MCP Tools'),
              mcpTools,
              configuredMcpServers.length > 0
                ? t('No named MCP tools were returned from the captain runtime\'s reachable MCP servers.')
                : t('No MCP tool inventory is available for this captain runtime.'),
            )}
          </>
        )}
      </div>
    </div>
  );
}
