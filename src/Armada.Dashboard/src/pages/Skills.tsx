import { duplicateDisplayName } from '../lib/duplicates';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createSkill, deleteSkill, listSkills, updateSkill } from '../api/client';
import { listAll } from '../api/paging';
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
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { useDeleteProgress } from '../lib/useDeleteProgress';

const SKILL_NAME_TIP = 'Unique skill name, for example Conventional Commits. Project profiles list skills by this name, so renaming one can break those references. Required.';
const SKILL_CATEGORY_TIP = 'Optional group for browsing the skill directory, for example engineering or testing. It does not change how the skill is used.';
const SKILL_DESCRIPTION_TIP = 'Optional one-line summary shown in the skill list. It is not sent to the captain.';
const SKILL_CONTENT_TIP = 'The instruction text, in markdown or plain text. It is added under a Skills heading to the prompt of every mission on a vessel whose project profile lists this skill.';
const SKILL_ACTIVE_TIP = 'On: the skill is added to mission prompts where a project profile lists it. Off: it is skipped, but stays listed on those profiles.';

export default function Skills() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const deletion = useDeleteProgress();
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

  function openDuplicate(skill: Skill) {
    openEdit(skill);
    setEditing(null);
    setCreateForm(current => ({ ...current, name: duplicateDisplayName(skill.name), scope: resolveCreateScope(viewer) }));
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
      const result = await listAll(listSkills);
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
        if (!(await deletion.run({ kind: 'skill', action: () => deleteSkill(skill.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Skill "{{name}}" deleted.', { name: skill.name }));
        await load();
      },
    });
  }

  const emptyState = (
    <div className="playbook-empty-state">
      <strong>{t('No skills match the current filters.')}</strong>
      <span>{canManage ? t('Create a skill to capture a reusable habit that can be attached to projects.') : t('Ask a tenant administrator to define skills.')}</span>
    </div>
  );

  // Name, ID and description used to stack in one cell; each is its own one-line column now.
  const columns: DataTableColumn<Skill>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique skill ID (skl_ prefix). Project profiles can refer to a skill by this ID or by its name.'), cellClassName: 'mono text-dim table-id-cell',
      render: (skill) => (
        <span className="id-display">
          <span className="id-value" title={skill.id}>{skill.id}</span>
          <CopyButton text={skill.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Skill'), required: true, headerTitle: t('Skill name. Project profiles list skills by this name (or ID) to add them to their missions.'),
      clearFilter: () => setColFilters(f => ({ ...f, name: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Skill')} title={t('Show only skills whose name contains this text.')} value={colFilters.name} onChange={e => setColFilters(f => ({ ...f, name: e.target.value }))} placeholder={t('Filter...')} />,
      render: (skill) => <strong>{skill.name}</strong>,
    },
    {
      key: 'description', label: t('Description'), headerTitle: t('Short summary of the skill. Hover a value for the full text.'), cellClassName: 'text-dim truncate-cell',
      render: (skill) => skill.description ? <span className="truncate-text" title={skill.description}>{skill.description}</span> : '-',
    },
    {
      key: 'category', label: t('Category'), headerTitle: t('Free-form group for browsing the skill directory, for example engineering or testing.'), cellClassName: 'text-dim',
      clearFilter: () => setColFilters(f => ({ ...f, category: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Category')} title={t('Show only skills whose category contains this text.')} value={colFilters.category} onChange={e => setColFilters(f => ({ ...f, category: e.target.value }))} placeholder={t('Filter...')} />,
      render: (skill) => skill.category || '-',
    },
    { key: 'visibility', label: t('Visibility'), headerTitle: t('Tenant-wide skills are usable by everyone in the tenant and edited by admins; personal ones only by their owner.'), cellClassName: 'cell-nowrap', render: (skill) => <ScopeBadge scope={skill.scope} /> },
    { key: 'status', label: t('Status'), headerTitle: t('Only active skills are added to mission prompts. Inactive skills stay listed on project profiles but are skipped.'), cellClassName: 'cell-nowrap', render: (skill) => <StatusBadge status={skill.active ? 'Active' : 'Inactive'} /> },
    {
      key: 'lastUpdated', label: t('Last Updated'), headerTitle: t('When this skill was last changed. Hover a value for the exact time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (skill) => formatDateTime(skill.lastUpdateUtc),
      render: (skill) => formatRelativeTime(skill.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open the menu in a row to open, edit or delete that skill.'), fixed: true, interactive: true, className: 'text-right',
      render: (skill) => {
        const canEditRow = canEditScoped(viewer, skill);
        return (
          <ActionMenu
            id={`skill-${skill.id}`}
            items={[
              { label: 'Open', title: 'Open this skill\'s page to read or edit its content.', onClick: () => navigate(`/skills/${skill.id}`) },
              ...(canEditRow ? [{ label: 'Duplicate', title: 'Edit a new copy before saving.', onClick: () => openDuplicate(skill) }] : []),
              ...(canEditRow ? [{ label: 'Edit', title: 'Change this skill\'s name, category, description, content, visibility and active state.', onClick: () => openEdit(skill) }] : []),
              { label: 'View JSON', title: 'Show this skill\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: skill.name, data: skill }) },
              ...(canEditRow ? [{ label: 'Delete', danger: true as const, title: 'Permanently delete this skill. Project profiles that list it simply skip it. You confirm first.', onClick: () => handleDelete(skill) }] : []),
            ]}
          />
        );
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Skills')}
        subtitle={t('A directory of reusable capability snippets attached to projects and injected into mission prompts.')}
        actions={(
          <>
            <button className="btn btn-primary" onClick={openCreate} title={t('Create a skill: a short reusable instruction, such as write tests first, that project profiles can add to mission prompts.')}>+ {t('Skill')}</button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {deletion.dialog}
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
            <label title={t(SKILL_NAME_TIP)}>{t('Name')}
              <input type="text" title={t(SKILL_NAME_TIP)} value={createForm.name} onChange={(e) => setCreateForm({ ...createForm, name: e.target.value })} required />
            </label>
            <label title={t(SKILL_CATEGORY_TIP)}>{t('Category')}
              <input type="text" title={t(SKILL_CATEGORY_TIP)} value={createForm.category} onChange={(e) => setCreateForm({ ...createForm, category: e.target.value })} placeholder="engineering" />
            </label>
            <label title={t(SKILL_DESCRIPTION_TIP)}>{t('Description')}
              <input type="text" title={t(SKILL_DESCRIPTION_TIP)} value={createForm.description} onChange={(e) => setCreateForm({ ...createForm, description: e.target.value })} />
            </label>
            <label title={t(SKILL_CONTENT_TIP)}>{t('Content')}
              <textarea rows={12} title={t(SKILL_CONTENT_TIP)} value={createForm.content} onChange={(e) => setCreateForm({ ...createForm, content: e.target.value })} spellCheck={false} placeholder={t('Markdown or plain text injected into mission prompts for projects that attach this skill.')} />
            </label>
            <ScopeSelect viewer={viewer} value={createForm.scope} onChange={(scope) => setCreateForm({ ...createForm, scope })} />
            <label className="checkbox-row" title={t(SKILL_ACTIVE_TIP)}>
              <input type="checkbox" title={t(SKILL_ACTIVE_TIP)} checked={createForm.active} onChange={(e) => setCreateForm({ ...createForm, active: e.target.checked })} />
              <span>{t('Active')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={editing ? t('Save changes to this skill. Missions started after saving get the new text.') : t('Create this skill. Add it to a project profile to use it.')}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Skill')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All skills you can see, active and inactive.')}>
          <span>{t('Total Skills')}</span>
          <strong>{skills.length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Skills that are added to mission prompts when a project profile lists them.')}>
          <span>{t('Active')}</span>
          <strong>{skills.filter((skill) => skill.active).length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Skills that are skipped even when a project profile lists them.')}>
          <span>{t('Inactive')}</span>
          <strong>{skills.filter((skill) => !skill.active).length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Number of distinct categories used by skills.')}>
          <span>{t('Categories')}</span>
          <strong>{categories.length}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input type="text" value={search} onChange={(e) => setSearch(e.target.value)} title={t('Show only skills whose name, description or ID contains this text.')} placeholder={t('Search by name, description, or ID...')} />
          <select aria-label={t('All categories')} title={t('Show skills from every category, or only one.')} value={categoryFilter} onChange={(e) => setCategoryFilter(e.target.value)}>
            <option value="all">{t('All categories')}</option>
            {categories.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
      </div>

      <DataTable
        tableKey="skills"
        columns={columns}
        rows={filtered}
        rowKey={(skill) => skill.id}
        onRowClick={(skill) => (canEditScoped(viewer, skill) ? openEdit(skill) : navigate(`/skills/${skill.id}`))}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh skills')}
        emptyMessage={emptyState}
        placeholder={skills.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : emptyState}
      />
    </div>
  );
}
