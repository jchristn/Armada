import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listCaptains, createCaptain, updateCaptain, deleteCaptain, stopCaptain, recallCaptain, stopAllCaptains, restartCaptain } from '../api/client';
import type { Captain } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'runtime' | 'state' | 'createdUtc';

function formatTime(utc: string | null): string {
  if (!utc) return '-';
  const d = new Date(utc);
  const now = new Date();
  const diff = now.getTime() - d.getTime();
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
  if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
  return d.toLocaleDateString();
}

export default function Captains() {
  const navigate = useNavigate();
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Captain | null>(null);
  const [form, setForm] = useState({ name: '', runtime: '', systemInstructions: '' });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Sorting
  const [sortField, setSortField] = useState<SortField>('name');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  // Column filters
  const [colFilters, setColFilters] = useState({ name: '', runtime: '', state: '' });

  // Pagination
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listCaptains({ pageSize: 9999 });
      setCaptains(result.objects);
      setError('');
    } catch {
      setError('Failed to load captains.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  // Filtered rows
  const filtered = useMemo(() => {
    return captains.filter(c =>
      (!colFilters.name || c.name.toLowerCase().includes(colFilters.name.toLowerCase())) &&
      (!colFilters.runtime || c.runtime.toLowerCase().includes(colFilters.runtime.toLowerCase())) &&
      (!colFilters.state || (c.state ?? '').toLowerCase().includes(colFilters.state.toLowerCase()))
    );
  }, [captains, colFilters]);

  // Sorted rows
  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string = '';
      let vb: string = '';
      switch (sortField) {
        case 'runtime': va = a.runtime.toLowerCase(); vb = b.runtime.toLowerCase(); break;
        case 'state': va = (a.state ?? '').toLowerCase(); vb = (b.state ?? '').toLowerCase(); break;
        case 'createdUtc': va = a.createdUtc; vb = b.createdUtc; break;
        default: va = a.name.toLowerCase(); vb = b.name.toLowerCase();
      }
      if (va < vb) return sortDir === 'asc' ? -1 : 1;
      if (va > vb) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return arr;
  }, [filtered, sortField, sortDir]);

  // Paginated
  const totalPages = Math.max(1, Math.ceil(sorted.length / pageSize));
  const currentPage = Math.min(pageNumber, totalPages);
  const paginated = useMemo(() => {
    const start = (currentPage - 1) * pageSize;
    return sorted.slice(start, start + pageSize);
  }, [sorted, currentPage, pageSize]);

  function handleSort(field: SortField) {
    if (sortField === field) setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setSortField(field); setSortDir('asc'); }
  }

  function sortIcon(field: SortField) {
    if (sortField !== field) return '';
    return sortDir === 'asc' ? ' \u25B2' : ' \u25BC';
  }

  // Selection
  const allSelected = selected.length > 0 && selected.length === filtered.length;
  function toggleSelect(id: string) {
    setSelected(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]);
  }
  function selectAll() { setSelected(filtered.map(c => c.id)); }
  function clearSelection() { setSelected([]); }

  // CRUD
  function openCreate() { setForm({ name: '', runtime: '', systemInstructions: '' }); setEditing(null); setShowForm(true); }
  function openEdit(c: Captain) { setForm({ name: c.name, runtime: c.runtime, systemInstructions: c.systemInstructions ?? '' }); setEditing(c); setShowForm(true); }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const payload = { ...form } as Record<string, unknown>;
      if (!payload.systemInstructions) delete payload.systemInstructions;
      if (editing) await updateCaptain(editing.id, payload);
      else await createCaptain(payload);
      setShowForm(false);
      load();
    } catch { setError('Save failed.'); }
  }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true,
      title: 'Delete Captain',
      message: `Delete captain "${name}"? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await deleteCaptain(id); load(); } catch { setError('Delete failed.'); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: 'Delete Selected Captains',
      message: `Delete ${selected.length} selected captain(s)? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected];
        setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await deleteCaptain(id); } catch { failed++; }
        }
        if (failed > 0) setError(`Deleted ${ids.length - failed} captains, ${failed} failed.`);
        load();
      },
    });
  }

  function handleStop(id: string, name: string) {
    setConfirm({
      open: true,
      title: 'Stop Captain',
      message: `Stop captain "${name}"? The captain process will be terminated.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await stopCaptain(id); load(); } catch { setError('Stop failed.'); }
      },
    });
  }

  function handleRecall(id: string, name: string) {
    setConfirm({
      open: true,
      title: 'Recall Captain',
      message: `Recall captain "${name}"? The captain will be recalled from its current mission.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await recallCaptain(id); load(); } catch { setError('Recall failed.'); }
      },
    });
  }

  function handleRestart(id: string, name: string) {
    setConfirm({
      open: true,
      title: 'Restart Captain',
      message: `Restart captain "${name}"? The captain will be deleted and recreated with the same name and runtime.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await restartCaptain(id); load(); } catch { setError('Restart failed.'); }
      },
    });
  }

  function handleStopAll() {
    setConfirm({
      open: true,
      title: 'Stop All Captains',
      message: 'Stop ALL captains? All captain processes will be terminated. This cannot be undone.',
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await stopAllCaptains(); load(); } catch { setError('Stop all failed.'); }
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>Captains</h2>
          <p className="text-dim view-subtitle">AI agent processes that execute missions. Monitor heartbeat, state, and manage captain lifecycle.</p>
        </div>
        <div className="view-actions">
          {selected.length > 0 && (
            <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
              Delete Selected ({selected.length})
            </button>
          )}
          <button className="btn btn-sm btn-danger" onClick={handleStopAll} title="Stop all captain processes">Stop All</button>
          <button className="btn btn-primary btn-sm" onClick={openCreate}>+ Captain</button>
          <RefreshButton onRefresh={load} title="Refresh captain data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? 'Edit Captain' : 'Create Captain'}</h3>
            <label>Name<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label title="The AI agent runtime this captain will use">Runtime
              <select value={form.runtime} onChange={e => setForm({ ...form, runtime: e.target.value })} required>
                <option value="">Select runtime...</option>
                <option value="ClaudeCode">Claude Code</option>
                <option value="Codex">Codex</option>
                <option value="Gemini">Gemini</option>
                <option value="Cursor">Cursor</option>
              </select>
            </label>
            <label title="Optional instructions injected into every mission prompt for this captain. Use this to specialize behavior, add guardrails, or provide persistent context.">
              System Instructions
              <textarea value={form.systemInstructions} onChange={e => setForm({ ...form, systemInstructions: e.target.value })} rows={4} placeholder="e.g., You are a testing specialist. Always run tests before committing..." />
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">Save</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>Cancel</button>
            </div>
          </form>
        </div>
      )}

      {/* JSON Viewer */}
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && captains.length === 0 && <p className="text-dim">Loading...</p>}
      {!loading && captains.length === 0 && <p className="text-dim">No captains configured.</p>}

      {captains.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages}
            totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? selectAll() : clearSelection()} title="Select all captains" />
                  </th>
                  <th className="sortable" onClick={() => handleSort('name')} title="Captain name -- click to sort">
                    Name{sortIcon('name')}
                  </th>
                  <th>ID</th>
                  <th className="sortable" onClick={() => handleSort('runtime')} title="Runtime -- click to sort">
                    Runtime{sortIcon('runtime')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('state')} title="State -- click to sort">
                    State{sortIcon('state')}
                  </th>
                  <th>Current Mission</th>
                  <th>Heartbeat</th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')} title="Created date -- click to sort">
                    Created{sortIcon('createdUtc')}
                  </th>
                  <th className="text-right">Actions</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.runtime} onChange={e => { setColFilters(f => ({ ...f, runtime: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td><input type="text" className="col-filter" value={colFilters.state} onChange={e => { setColFilters(f => ({ ...f, state: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(c => (
                  <tr key={c.id} className="clickable" onClick={() => openEdit(c)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={selected.includes(c.id)} onChange={() => toggleSelect(c.id)} title="Select this captain" />
                    </td>
                    <td><strong>{c.name}</strong></td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={c.id}>{c.id}</span>
                        <CopyButton text={c.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td className="text-dim">{c.runtime}</td>
                    <td><StatusBadge status={c.state} /></td>
                    <td className="mono text-dim" onClick={e => e.stopPropagation()}>
                      {c.currentMissionId ? (
                        <a href="#" onClick={e => { e.preventDefault(); navigate(`/missions/${c.currentMissionId}`); }}>
                          {c.currentMissionId.substring(0, 8)}...
                        </a>
                      ) : '-'}
                    </td>
                    <td className="text-dim">{formatTime(c.lastHeartbeatUtc)}</td>
                    <td className="text-dim">{formatTime(c.createdUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`captain-${c.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/captains/${c.id}`) },
                        { label: 'Edit', onClick: () => openEdit(c) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Captain: ${c.name}`, data: c }) },
                        { label: 'Stop', onClick: () => handleStop(c.id, c.name) },
                        { label: 'Recall', onClick: () => handleRecall(c.id, c.name) },
                        { label: 'Restart', onClick: () => handleRestart(c.id, c.name) },
                        { label: 'Delete', danger: true, onClick: () => handleDelete(c.id, c.name) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr><td colSpan={9} className="text-dim">No captains match the current filters.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
