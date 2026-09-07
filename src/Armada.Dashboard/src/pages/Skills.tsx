import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createSkill, deleteSkill, listSkills, updateSkill } from '../api/client';
import type { Skill, ScopeEnum } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { canEdit as canEditScoped, resolveCreateScope, type ScopeViewer } from '../lib/scoping';
import ScopeBadge from '../components/shared/ScopeBadge';
import ScopeSelect from '../components/shared/ScopeSelect';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';

export default function Skills() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [skills, setSkills] = useState<Skill[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('all');
  const [colFilters, setColFilters] = useState({ name: '', category: '' });
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false, title: '', message: '', onConfirm: () => {},
  });

  // Create/Edit modal
  const [showCreate, setShowCreate] = useState(false);
  const [editing, setEditing] = useState<Skill | null>(null);
  const [saving, setSaving] = useState(false);
  const [createForm, setCreateForm] = useState<{ name: string; category: string; description: string; content: string; active: boolean; scope: ScopeEnum }>({
    name: 'Untitled Skill', category: '', description: '', content: '', active: true, scope: resolveCreateScope(viewer),
  });

  const canManage = isAdmin || isTenantAdmin;

  function openCreate() {
    setEditing(null);
    setCreateForm({ name: 'Untitled Skill', category: '', description: '', content: '', active: true, scope: resolveCreateScope(viewer) });
    setShowCreate(true);
  }

  function openEdit(skill: Skill) {
    setEditing(skill);
    setCreateForm({
      name: skill.name,
      category: skill.category || '',
      description: skill.description || '',
      content: skill.content || '',
      active: skill.active,
      scope: skill.scope,
    });
    setShowCreate(true);
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    if (saving) return;
    setSaving(true);
    try {
      const payload = {
        name: createForm.name,
        description: createForm.description || null,
        category: createForm.category || null,
        content: createForm.content,
        active: createForm.active,
        scope: createForm.scope,
      };
      if (editing) {
        const updated = await updateSkill(editing.id, payload);
        setShowCreate(false);
        pushToast('success', t('Skill "{{name}}" saved.', { name: updated.name }));
      } else {
        const created = await createSkill(payload);
        setShowCreate(false);
        pushToast('success', t('Skill "{{name}}" created.', { name: created.name }));
      }
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function load() {
    try {
      setLoading(true);
      const result = await listSkills({ pageSize: 9999 });
      setSkills(result.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load skills.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('skills', load);

  const categories = useMemo(() => {
    const set = new Set<string>();
    skills.forEach((s) => { if (s.category) set.add(s.category); });
    return Array.from(set).sort();
  }, [skills]);

  const filtered = useMemo(() => skills.filter((skill) => {
    const matchesSearch = search.trim().length === 0
      || skill.name.toLowerCase().includes(search.toLowerCase())
      || (skill.description || '').toLowerCase().includes(search.toLowerCase())
      || skill.id.toLowerCase().includes(search.toLowerCase());
    const matchesCategory = categoryFilter === 'all' || skill.category === categoryFilter;
    const matchesColumns = (!colFilters.name || (skill.name ?? '').toLowerCase().includes(colFilters.name.toLowerCase()))
      && (!colFilters.category || (skill.category ?? '').toLowerCase().includes(colFilters.category.toLowerCase()));
    return matchesSearch && matchesCategory && matchesColumns;
  }), [skills, search, categoryFilter, colFilters]);

  function handleDelete(skill: Skill) {
    setConfirm({
      open: true,
      title: t('Delete Skill'),
      message: t('Delete "{{name}}"? Project profiles referencing it will simply skip it.', { name: skill.name }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteSkill(skill.id);
          pushToast('warning', t('Skill "{{name}}" deleted.', { name: skill.name }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  return (
    <div>
      <PageHeader
        title={t('Skills')}
        subtitle={t('A directory of reusable capability snippets attached to projects and injected into mission prompts.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh skills')} />
            <button className="btn btn-primary" onClick={openCreate}>+ {t('Skill')}</button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

      {showCreate && (
        <div className="modal-overlay" onClick={() => setShowCreate(false)}>
          <form className="modal modal-large" onClick={(e) => e.stopPropagation()} onSubmit={handleCreate}>
            <h3>{editing ? t('Edit Skill') : t('Create Skill')}</h3>
            <label>{t('Name')}
              <input type="text" value={createForm.name} onChange={(e) => setCreateForm({ ...createForm, name: e.target.value })} required />
            </label>
            <label>{t('Category')}
              <input type="text" value={createForm.category} onChange={(e) => setCreateForm({ ...createForm, category: e.target.value })} placeholder="engineering" />
            </label>
            <label>{t('Description')}
              <input type="text" value={createForm.description} onChange={(e) => setCreateForm({ ...createForm, description: e.target.value })} />
            </label>
            <label>{t('Content')}
              <textarea rows={12} value={createForm.content} onChange={(e) => setCreateForm({ ...createForm, content: e.target.value })} spellCheck={false} placeholder={t('Markdown or plain text injected into mission prompts for projects that attach this skill.')} />
            </label>
            <ScopeSelect viewer={viewer} value={createForm.scope} onChange={(scope) => setCreateForm({ ...createForm, scope })} />
            <label className="checkbox-row">
              <input type="checkbox" checked={createForm.active} onChange={(e) => setCreateForm({ ...createForm, active: e.target.checked })} />
              <span>{t('Active')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Skill')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Skills')}</span>
          <strong>{skills.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Active')}</span>
          <strong>{skills.filter((skill) => skill.active).length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Inactive')}</span>
          <strong>{skills.filter((skill) => !skill.active).length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Categories')}</span>
          <strong>{categories.length}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input type="text" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search by name, description, or ID...')} />
          <select value={categoryFilter} onChange={(e) => setCategoryFilter(e.target.value)}>
            <option value="all">{t('All categories')}</option>
            {categories.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
      </div>

      {loading && skills.length === 0 ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : filtered.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('No skills match the current filters.')}</strong>
          <span>{canManage ? t('Create a skill to capture a reusable habit that can be attached to projects.') : t('Ask a tenant administrator to define skills.')}</span>
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('Skill')}</th>
                <th>{t('Category')}</th>
                <th>{t('Visibility')}</th>
                <th>{t('Status')}</th>
                <th>{t('Last Updated')}</th>
                <th className="text-right">{t('Actions')}</th>
              </tr>
              <tr className="column-filter-row">
                <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => setColFilters(f => ({ ...f, name: e.target.value }))} placeholder={t('Filter...')} /></td>
                <td><input type="text" className="col-filter" value={colFilters.category} onChange={e => setColFilters(f => ({ ...f, category: e.target.value }))} placeholder={t('Filter...')} /></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
              </tr>
            </thead>
            <tbody>
              {filtered.map((skill) => {
                const canEditRow = canEditScoped(viewer, skill);
                return (
                <tr key={skill.id} className="clickable" onClick={() => canEditRow ? openEdit(skill) : navigate(`/skills/${skill.id}`)}>
                  <td>
                    <strong>{skill.name}</strong>
                    <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{skill.id}</div>
                    {skill.description && <div className="text-dim" style={{ marginTop: '0.2rem' }}>{skill.description}</div>}
                  </td>
                  <td className="text-dim">{skill.category || '-'}</td>
                  <td><ScopeBadge scope={skill.scope} /></td>
                  <td><StatusBadge status={skill.active ? 'Active' : 'Inactive'} /></td>
                  <td className="text-dim" title={formatDateTime(skill.lastUpdateUtc)}>{formatRelativeTime(skill.lastUpdateUtc)}</td>
                  <td className="text-right" onClick={(e) => e.stopPropagation()}>
                    <ActionMenu
                      id={`skill-${skill.id}`}
                      items={[
                        { label: 'Open', onClick: () => navigate(`/skills/${skill.id}`) },
                        ...(canEditRow ? [{ label: 'Edit', onClick: () => openEdit(skill) }] : []),
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: skill.name, data: skill }) },
                        ...(canEditRow ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(skill) }] : []),
                      ]}
                    />
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
