import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiErrorMessage, listModelProfiles } from '../api/client';
import type { ModelProfile } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import LoadingIndicator from '../components/shared/LoadingIndicator';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useResourceTable } from '../lib/useResourceTable';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import { capabilityLabel, servedContextLabel } from '../lib/modelProfiles';

/**
 * Model profiles: the models as served (runtime, provider, model, served context) shared by every captain that runs
 * them. A Captains hub tab; a row opens the profile page, where admins edit the tier, rendering, recovery and timeouts.
 */
export default function ModelProfiles() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const [profiles, setProfiles] = useState<ModelProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = usePersistedPageSize('model-profiles', 25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [modelFilter, setModelFilter] = useState('');

  const table = useResourceTable({
    rows: profiles,
    getId: (p) => p.id,
    columnValues: {
      runtime: (p) => p.identity.runtime.toLowerCase(),
      provider: (p) => (p.identity.provider ?? '').toLowerCase(),
      context: (p) => p.identity.servedContextTokens ?? 0,
      tier: (p) => p.tiering.tier ?? '',
      lastUpdateUtc: (p) => p.lastUpdateUtc,
    },
    initialSortField: 'lastUpdateUtc',
    initialSortDir: 'desc',
  });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listModelProfiles({ pageNumber, pageSize, model: modelFilter.trim() || undefined });
      setProfiles(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      setError('');
    } catch (err: unknown) {
      setError(apiErrorMessage(err, t('Failed to load model profiles.')));
    } finally {
      setLoading(false);
      setLoaded(true);
    }
  }, [pageNumber, pageSize, modelFilter, t]);

  useEffect(() => { void load(); }, [load]);

  const columns: DataTableColumn<ModelProfile>[] = [
    {
      key: 'model', label: t('Model'), required: true, cellClassName: 'cell-ident',
      headerTitle: t('Model name as served. Type in the box below to search every page.'),
      clearFilter: () => { setModelFilter(''); setPageNumber(1); },
      filter: <input type="text" className="col-filter" aria-label={t('Model')} value={modelFilter} onChange={e => { setModelFilter(e.target.value); setPageNumber(1); }} placeholder={t('Filter...')} />,
      render: (p) => <strong>{p.identity.model || t('(runtime default)')}</strong>,
    },
    {
      key: 'runtime', label: t('Runtime'), sortKey: 'runtime', cellClassName: 'cell-nowrap',
      headerTitle: t('Agent runtime that runs the model, for example ClaudeCode, Codex or ApiEndpoint.'),
      render: (p) => p.identity.runtime,
    },
    {
      key: 'provider', label: t('Provider'), sortKey: 'provider', cellClassName: 'cell-nowrap',
      headerTitle: t('Inference provider, or a dash when the runtime does not say (vendor CLIs).'),
      render: (p) => p.identity.provider || '-',
    },
    {
      key: 'context', label: t('Served context'), sortKey: 'context', cellClassName: 'cell-nowrap',
      headerTitle: t('Context window the model is served with, and whether it was configured, discovered from the model server, or measured.'),
      render: (p) => servedContextLabel(p.identity, t),
    },
    {
      key: 'tier', label: t('Tier'), sortKey: 'tier', cellClassName: 'cell-nowrap',
      headerTitle: t('Tier an administrator set for routing. A suggested tier from outcome data is shown next to it and changes nothing until applied.'),
      render: (p) => (
        <span>
          {p.tiering.tier ? t(p.tiering.tier) : <span className="text-dim" title={t('No tier set: captains on this profile are classified from their model name.')}>{t('From model name')}</span>}
          {p.tiering.suggestedTier && p.tiering.suggestedTier !== p.tiering.tier && (
            <span className="tag info" style={{ marginLeft: '0.35rem' }} title={p.tiering.suggestedReason || ''}>{t('Suggested: {{tier}}', { tier: t(p.tiering.suggestedTier) })}</span>
          )}
        </span>
      ),
    },
    {
      key: 'tools', label: t('Tools'), cellClassName: 'cell-nowrap',
      headerTitle: t('Whether the model can call tools as served: the administrator value, else the probe result.'),
      render: (p) => capabilityLabel(p.capabilities.toolsAvailable, p.capabilities.probedToolsAvailable, t),
    },
    {
      key: 'updated', label: t('Last Updated'), sortKey: 'lastUpdateUtc', cellClassName: 'text-dim cell-nowrap',
      headerTitle: t('When the profile last changed. Hover a cell for the exact time.'),
      cellTitle: (p) => formatDateTime(p.lastUpdateUtc),
      render: (p) => formatRelativeTime(p.lastUpdateUtc),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Model Profiles')}
        subtitle={t('The models as served, shared by every captain that runs them. Open one to see its recovery policy, timeouts and capabilities.')}
      />
      <ErrorModal error={error} onClose={() => setError('')} />
      <DataTable
        tableKey="model-profiles"
        columns={columns}
        rows={table.sorted}
        rowKey={(p) => p.id}
        onRowClick={(p) => navigate(`/model-profiles/${encodeURIComponent(p.id)}`)}
        sort={table.sortState}
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        onRefresh={load}
        refreshTitle={t('Reload the model profile list now.')}
        loading={loading}
        emptyMessage={t('No model profiles match the current filters.')}
        placeholder={profiles.length > 0 ? undefined : !loaded ? <LoadingIndicator compact label={t('Loading...')} /> : <p className="text-dim">{t('No model profiles yet. A profile is created the first time a captain runs a model.')}</p>}
      />
    </div>
  );
}
