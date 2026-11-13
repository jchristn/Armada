import { useEffect, useState } from 'react';
import { useLocale } from '../../context/LocaleContext';
import { getCaptainTools } from '../../api/client';
import DataTable, { type DataTableColumn, type DataTablePagination } from '../shared/DataTable';
import type { CaptainToolAccessResult, CaptainToolServerSummary, CaptainToolSummary, CaptainToolsQuery, EnumerationResult } from '../../types/models';

interface CaptainToolViewerProps {
  open: boolean;
  captainName: string;
  loading: boolean;
  error: string;
  data: CaptainToolAccessResult | null;
  onClose: () => void;
  /** When set, each tool section reads its own pages from the server (the probe snapshot is reused, so paging does not probe again). */
  captainId?: string;
}

type ToolKind = NonNullable<CaptainToolsQuery['sourceKind']>;

const DEFAULT_TOOL_PAGE_SIZE = 25;

/** One section's page of tools: fetched page by page when a captain id is given, otherwise cut from the loaded page. */
function useToolSection(captainId: string | undefined, data: CaptainToolAccessResult | null, kind: ToolKind) {
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_TOOL_PAGE_SIZE);
  const [page, setPage] = useState<EnumerationResult<CaptainToolSummary> | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setPageNumber(1);
    setPage(null);
    setFailed(false);
  }, [data, captainId]);

  useEffect(() => {
    if (!captainId || !data) return undefined;
    let active = true;
    getCaptainTools(captainId, { sourceKind: kind, pageNumber, pageSize })
      .then((result) => { if (active) { setPage(result?.tools ?? null); setFailed(false); } })
      .catch(() => { if (active) setFailed(true); });
    return () => { active = false; };
  }, [captainId, data, kind, pageNumber, pageSize]);

  if (!captainId) {
    const loaded = (data?.tools?.objects ?? []).filter((tool) => tool.sourceKind === kind);
    return { rows: loaded, pagination: undefined as DataTablePagination | undefined, failed: false };
  }

  const rows = page?.objects ?? [];
  const pagination: DataTablePagination | undefined = page
    ? {
      pageNumber: page.pageNumber,
      pageSize: page.pageSize,
      totalPages: page.totalPages,
      totalRecords: page.totalRecords,
      onPageChange: (next: number) => setPageNumber(next),
      onPageSizeChange: (size: number) => { setPageSize(size); setPageNumber(1); },
    }
    : undefined;
  return { rows, pagination, failed };
}

export default function CaptainToolViewer({ open, captainName, loading, error, data, onClose, captainId }: CaptainToolViewerProps) {
  const { t } = useLocale();
  const internalSection = useToolSection(open ? captainId : undefined, open ? data : null, 'RuntimeBuiltIn');
  const mcpSection = useToolSection(open ? captainId : undefined, open ? data : null, 'McpServer');

  if (!open) return null;

  const configuredMcpServers = data?.servers.filter((server) => server.sourceKind === 'McpServer') ?? [];
  const runtimeSources = data?.servers.filter((server) => server.sourceKind !== 'McpServer') ?? [];
  const runtimeInternalTools = internalSection.rows;
  const mcpTools = mcpSection.rows;
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
      { key: 'source', label: t('Source'), required: true, headerTitle: t('Name of the tool source, usually the MCP server name from the runtime\'s configuration.'), render: (server) => <strong>{server.name}</strong> },
      { key: 'transport', label: t('Transport'), headerTitle: t('How the runtime talks to the source: stdio launches a local command, http or streamable_http connects to a URL.'), render: (server) => server.transport || '-' },
      {
        key: 'endpoint', label: t('Endpoint / Target'), headerTitle: t('The URL (credentials and query removed) or launch command (arguments omitted) the runtime uses for this source.'), cellClassName: 'mono captain-tool-viewer-endpoint-cell',
        // One line; the full endpoint is in the tooltip.
        render: (server) => <span className="cell-one-line" title={formatEndpoint(server)}>{formatEndpoint(server)}</span>,
      },
      {
        key: 'status', label: t('Status'), headerTitle: t('Whether Armada reached the source when this view loaded. Green is reachable, amber is enabled but did not answer, grey is disabled.'), cellClassName: 'cell-nowrap',
        render: (server) => (
          <span className={`tag ${server.reachable ? 'complete' : server.enabled ? 'review' : 'idle'}`} title={server.reachable ? t('Armada reached this source and listed its tools.') : server.enabled ? t('Enabled in the runtime configuration, but it did not respond. It may just be offline right now.') : t('Disabled in the runtime configuration, so the captain does not use it.')}>
            {server.status}
          </span>
        ),
      },
      { key: 'tools', label: t('Tools'), headerTitle: t('Number of tools the source returned, after its allow and deny filters are applied.'), render: (server) => server.toolCount },
      {
        key: 'notes', label: t('Notes'), headerTitle: t('Working directory (WD), timeouts, how many headers, env vars and tool filters are set (values hidden), and any error.'), cellClassName: 'text-dim captain-tool-viewer-notes-cell',
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

  function renderToolTable(tableKey: string, title: string, tools: CaptainToolSummary[], emptyMessage: string, pagination?: DataTablePagination, failed = false) {
    const columns: DataTableColumn<CaptainToolSummary>[] = [
      { key: 'tool', label: t('Tool'), required: true, headerTitle: t('Tool name as the agent sees and calls it.'), cellClassName: 'mono captain-tool-viewer-tool-name', render: (tool) => tool.name },
      {
        key: 'source', label: t('Server / Source'), headerTitle: t('The MCP server that provides the tool, or Internal for a tool built into the runtime.'), cellClassName: 'captain-tool-viewer-source',
        render: (tool) => <span className="tag idle" title={tool.registrationSource ? t('Provided by the {{source}} source.', { source: tool.registrationSource }) : t('Built into the runtime itself, not provided by an MCP server.')}>{tool.registrationSource || t('Internal')}</span>,
      },
      { key: 'description', label: t('Description'), headerTitle: t('What the tool does, as its source describes it.'), render: (tool) => tool.description },
    ];
    return (
      <section className="captain-tool-viewer-section">
        <h4>{title}</h4>
        {failed && <p className="text-dim">{t('Failed to load captain tools.')}</p>}
        {!failed && tools.length === 0 && (!pagination || pagination.totalRecords === 0) ? (
          <p className="text-dim">{emptyMessage}</p>
        ) : !failed && (
          <DataTable
            tableKey={tableKey}
            columns={columns}
            rows={tools}
            rowKey={(tool) => `${tool.registrationSource || 'internal'}:${tool.name}`}
            wrapClassName="captain-tool-viewer-table"
            ariaLabel={title}
            pagination={pagination}
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
          <button type="button" className="btn btn-sm" onClick={onClose} title={t('Close the tool list.')}>
            {t('Close')}
          </button>
        </div>

        {loading && <p className="text-dim">{t('Loading captain tool access...')}</p>}
        {!loading && error && <p className="text-dim">{error}</p>}

        {!loading && !error && data && (
          <>
            <p className="text-dim captain-tool-viewer-summary">{data.summary}</p>
            <div className="captain-tool-viewer-meta">
              <span className={`tag ${data.toolsAccessible ? 'complete' : 'failed'}`} title={data.toolsAccessible ? t('Armada believes this captain can use its tools, including the Armada MCP tools.') : t('Armada believes this captain cannot use its tools right now. See the summary above for why.')}>
                {data.toolsAccessible ? t('Accessible') : t('Unavailable')}
              </span>
              <span className={`tag ${data.availabilityVerified ? 'working' : 'review'}`} title={data.availabilityVerified ? t('Armada checked tool availability live, by contacting the runtime or its sources.') : t('Armada inferred tool availability from configuration without contacting the sources, so it may be out of date.')}>
                {data.availabilityVerified ? t('Verified') : t('Inferred')}
              </span>
              <span className="tag idle" title={t('The agent runtime this captain runs.')}>{data.runtime}</span>
              {data.configuredServerCount > 0 && (
                <span className="tag idle" title={t('Number of tool sources (MCP servers and runtime sources) Armada inspected for this captain.')}>{t('{{count}} sources', { count: data.configuredServerCount })}</span>
              )}
              {data.reachableServerCount > 0 && (
                <span className="tag idle" title={t('Number of those sources that answered when this view loaded.')}>{t('{{count}} reachable', { count: data.reachableServerCount })}</span>
              )}
              {data.endpointName && <span className="tag idle" title={t('The mux endpoint this captain launches with.')}>{data.endpointName}</span>}
              {typeof data.effectiveToolCount === 'number' && (
                <span className="tag idle" title={t('Number of tools the runtime itself reports it can call.')}>{t('{{count}} listed tools', { count: data.effectiveToolCount })}</span>
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
              internalSection.pagination,
              internalSection.failed,
            )}

            {renderToolTable(
              'captain-tools-mcp',
              t('MCP Tools'),
              mcpTools,
              configuredMcpServers.length > 0
                ? t('No named MCP tools were returned from the captain runtime\'s reachable MCP servers.')
                : t('No MCP tool inventory is available for this captain runtime.'),
              mcpSection.pagination,
              mcpSection.failed,
            )}
          </>
        )}
      </div>
    </div>
  );
}
