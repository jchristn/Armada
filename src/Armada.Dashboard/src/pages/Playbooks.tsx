import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { deletePlaybook, listPlaybooks } from '../api/client';
import type { Playbook } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import RefreshButton from '../components/shared/RefreshButton';
import StatusBadge from '../components/shared/StatusBadge';

export default function Playbooks() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [playbooks, setPlaybooks] = useState<Playbook[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'inactive'>('all');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  const canManage = isAdmin || isTenantAdmin;

  async function load() {
    try {
      setLoading(true);
      const result = await listPlaybooks({ pageSize: 9999 });
      setPlaybooks(result.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load playbooks.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  const filtered = playbooks.filter((playbook) => {
    const matchesSearch = search.trim().length === 0
      || playbook.fileName.toLowerCase().includes(search.toLowerCase())
      || (playbook.description || '').toLowerCase().includes(search.toLowerCase())
      || playbook.id.toLowerCase().includes(search.toLowerCase());

    const matchesStatus = statusFilter === 'all'
      || (statusFilter === 'active' && playbook.active)
      || (statusFilter === 'inactive' && !playbook.active);

    return matchesSearch && matchesStatus;
  });

  const activeCount = playbooks.filter((playbook) => playbook.active).length;
  const inactiveCount = playbooks.length - activeCount;
  const totalChars = playbooks.reduce((total, playbook) => total + playbook.content.length, 0);

  function handleDelete(playbook: Playbook) {
    setConfirm({
      open: true,
      title: t('Delete Playbook'),
      message: t('Delete "{{name}}"? Existing mission snapshots will remain, but this playbook will no longer be selectable.', { name: playbook.fileName }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deletePlaybook(playbook.id);
          pushToast('warning', t('Playbook "{{name}}" deleted.', { name: playbook.fileName }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>{t('Playbooks')}</h2>
          <p className="text-dim view-subtitle">
            {t('Tenant-scoped markdown playbooks that can be attached to voyages and missions. Use them for durable engineering rules, architecture standards, or execution checklists.')}
          </p>
        </div>
        <div className="view-actions">
          <RefreshButton onRefresh={load} title={t('Refresh playbooks')} />
          {canManage && (
            <button className="btn btn-primary" onClick={() => navigate('/playbooks/new')}>
              + {t('Playbook')}
            </button>
          )}
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Playbooks')}</span>
          <strong>{playbooks.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Active')}</span>
          <strong>{activeCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Inactive')}</span>
          <strong>{inactiveCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Stored Markdown')}</span>
          <strong>{totalChars.toLocaleString()} {t('chars')}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('Search by filename, description, or ID...')}
          />
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as 'all' | 'active' | 'inactive')}>
            <option value="all">{t('All statuses')}</option>
            <option value="active">{t('Active only')}</option>
            <option value="inactive">{t('Inactive only')}</option>
          </select>
        </div>
      </div>

      {loading && playbooks.length === 0 ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : filtered.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('No playbooks match the current filters.')}</strong>
          <span>{canManage ? t('Create a playbook to start standardizing dispatch behavior.') : t('Ask a tenant administrator to create playbooks for shared guidance.')}</span>
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('File')}</th>
                <th>{t('Description')}</th>
                <th>{t('Status')}</th>
                <th>{t('Content')}</th>
                <th>{t('Last Updated')}</th>
                <th className="text-right">{t('Actions')}</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((playbook) => (
                <tr key={playbook.id} className="clickable" onClick={() => navigate(`/playbooks/${playbook.id}`)}>
                  <td>
                    <strong>{playbook.fileName}</strong>
                    <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{playbook.id}</div>
                  </td>
                  <td className="text-dim">{playbook.description || '-'}</td>
                  <td>
                    <StatusBadge status={playbook.active ? 'Active' : 'Inactive'} />
                  </td>
                  <td className="text-dim">
                    {playbook.content.length.toLocaleString()} {t('chars')}
                  </td>
                  <td className="text-dim" title={formatDateTime(playbook.lastUpdateUtc)}>
                    {formatRelativeTime(playbook.lastUpdateUtc)}
                  </td>
                  <td className="text-right" onClick={(event) => event.stopPropagation()}>
                    <ActionMenu
                      id={`playbook-${playbook.id}`}
                      items={[
                        { label: 'Open', onClick: () => navigate(`/playbooks/${playbook.id}`) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: playbook.fileName, data: playbook }) },
                        ...(canManage ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(playbook) }] : []),
                      ]}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
