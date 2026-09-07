import { useState, useEffect, useRef, useCallback } from 'react';
import { useNotifications } from '../context/NotificationContext';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { createPromptTemplate, getPromptTemplate, updatePromptTemplate, resetPromptTemplate } from '../api/client';
import type { PromptTemplate } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import PageHeader from '../components/shared/PageHeader';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useAuth } from '../context/AuthContext';
import { canEdit as canEditScoped, type ScopeViewer } from '../lib/scoping';
import { buildPromptTemplateDuplicatePayload } from '../lib/duplicates';

interface ParameterInfo {
  name: string;
  description: string;
}

interface ParameterGroup {
  label: string;
  params: ParameterInfo[];
}

const PARAMETER_GROUPS: ParameterGroup[] = [
  {
    label: 'Mission Context',
    params: [
      { name: '{MissionId}', description: 'Mission identifier' },
      { name: '{MissionTitle}', description: 'Mission title' },
      { name: '{MissionDescription}', description: 'Full mission description' },
      { name: '{MissionPersona}', description: 'Persona assigned to this mission' },
      { name: '{VoyageId}', description: 'Parent voyage identifier' },
      { name: '{BranchName}', description: 'Git branch for this mission' },
    ],
  },
  {
    label: 'Vessel Context',
    params: [
      { name: '{VesselId}', description: 'Vessel identifier' },
      { name: '{VesselName}', description: 'Vessel display name' },
      { name: '{DefaultBranch}', description: 'Default branch (e.g. main)' },
      { name: '{ProjectContext}', description: 'User-supplied project description' },
      { name: '{StyleGuide}', description: 'User-supplied style guide' },
      { name: '{ModelContext}', description: 'Agent-accumulated context' },
      { name: '{FleetId}', description: 'Parent fleet identifier' },
    ],
  },
  {
    label: 'Captain Context',
    params: [
      { name: '{CaptainId}', description: 'Captain identifier' },
      { name: '{CaptainName}', description: 'Captain display name' },
      { name: '{CaptainInstructions}', description: 'User-supplied captain instructions' },
    ],
  },
  {
    label: 'Pipeline Context',
    params: [
      { name: '{PersonaPrompt}', description: 'Resolved persona prompt text' },
      { name: '{PreviousStageDiff}', description: 'Diff from prior pipeline stage' },
      { name: '{ExistingClaudeMd}', description: "Contents of repo's existing CLAUDE.md" },
    ],
  },
  {
    label: 'System',
    params: [
      { name: '{Timestamp}', description: 'Current UTC timestamp' },
    ],
  },
];

const PROMPT_TEMPLATE_CATEGORY_OPTIONS = ['mission', 'persona', 'structure', 'commit', 'landing', 'agent'] as const;

export default function PromptTemplateDetail() {
  const { t, formatDateTime } = useLocale();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { name } = useParams<{ name?: string }>();
  const navigate = useNavigate();
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const createMode = !name;

  const [template, setTemplate] = useState<PromptTemplate | null>(null);
  const canEditThis = createMode ? true : (template ? canEditScoped(viewer, template) : true);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  const { pushToast } = useNotifications();

  // Editable fields
  const [templateName, setTemplateName] = useState('');
  const [category, setCategory] = useState('mission');
  const [content, setContent] = useState('');
  const [description, setDescription] = useState('');
  const [dirty, setDirty] = useState(false);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false, title: '', message: '', onConfirm: () => {},
  });

  const load = useCallback(async () => {
    if (createMode) {
      setTemplate(null);
      setTemplateName('');
      setCategory('mission');
      setContent('');
      setDescription('');
      setDirty(false);
      setError('');
      setLoading(false);
      return;
    }

    if (!name) return;
    try {
      setLoading(true);
      const result = await getPromptTemplate(name);
      setTemplate(result);
      setTemplateName(result.name);
      setCategory(result.category);
      setContent(result.content);
      setDescription(result.description ?? '');
      setDirty(false);
      setError('');
    } catch (err) {
      setTemplate(null);
      setError(err instanceof Error ? err.message : t('Failed to load prompt template.'));
    } finally {
      setLoading(false);
    }
  }, [createMode, name, t]);

  useEffect(() => { void load(); }, [load]);

  function handleTemplateNameChange(value: string) {
    setTemplateName(value);
    setDirty(true);
  }

  function handleCategoryChange(value: string) {
    setCategory(value);
    setDirty(true);
  }

  function handleContentChange(value: string) {
    setContent(value);
    setDirty(true);
  }

  function handleDescriptionChange(value: string) {
    setDescription(value);
    setDirty(true);
  }

  async function handleSave() {
    const normalizedName = templateName.trim();
    const normalizedCategory = category.trim();
    const normalizedDescription = description.trim();

    if (createMode) {
      if (!normalizedName) {
        setError(t('Template name is required.'));
        return;
      }

      if (!normalizedCategory) {
        setError(t('Template category is required.'));
        return;
      }

      if (!content.trim()) {
        setError(t('Template content is required.'));
        return;
      }

      try {
        setSaving(true);
        const result = await createPromptTemplate({
          name: normalizedName,
          category: normalizedCategory,
          content,
          description: normalizedDescription || undefined,
          active: true,
        });
        setTemplate(result);
        setTemplateName(result.name);
        setCategory(result.category);
        setContent(result.content);
        setDescription(result.description ?? '');
        setDirty(false);
        setError('');
        pushToast('success', t('Template "{{name}}" created.', { name: result.name }));
        navigate(`/prompt-templates/${encodeURIComponent(result.name)}`, { replace: true });
      } catch (err) {
        setError(err instanceof Error ? err.message : t('Create failed.'));
      } finally {
        setSaving(false);
      }
      return;
    }

    if (!name || !template) return;

    try {
      setSaving(true);
      const result = await updatePromptTemplate(name, { content, description: normalizedDescription || undefined });
      setTemplate(result);
      setTemplateName(result.name);
      setCategory(result.category);
      setContent(result.content);
      setDescription(result.description ?? '');
      setDirty(false);
      setError('');
      pushToast('success', t('Template saved.'));
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function handleDuplicate() {
    if (!template) return;
    try {
      setSaving(true);
      const created = await createPromptTemplate(buildPromptTemplateDuplicatePayload(template));
      pushToast('success', t('Template "{{name}}" duplicated.', { name: created.name }));
      navigate(`/prompt-templates/${encodeURIComponent(created.name)}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    } finally {
      setSaving(false);
    }
  }

  function handleReset() {
    if (!template || !template.isBuiltIn) return;
    setConfirm({
      open: true,
      title: t('Reset to Default'),
      message: t('Reset "{{name}}" to its built-in default content? Your customizations will be lost.', { name: template.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          const result = await resetPromptTemplate(template.name);
          setTemplate(result);
          setContent(result.content);
          setDescription(result.description ?? '');
          setDirty(false);
          pushToast('success', t('Template reset to default.'));
        } catch {
          setError(t('Reset failed.'));
        }
      },
    });
  }

  function insertParameter(param: string) {
    const textarea = textareaRef.current;
    if (!textarea) return;

    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const newContent = content.substring(0, start) + param + content.substring(end);
    setContent(newContent);
    setDirty(true);

    // Restore cursor position after the inserted text
    requestAnimationFrame(() => {
      textarea.focus();
      textarea.selectionStart = start + param.length;
      textarea.selectionEnd = start + param.length;
    });
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (!createMode && error && !template) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!createMode && !template) return <p className="text-dim">{t('Template not found.')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/prompt-templates">{t('Prompt Templates')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{createMode ? t('Create') : templateName}</span>
          </>
        }
        title={createMode ? t('Create Prompt Template') : templateName}
        actions={
          <>
            {createMode ? (
              <StatusBadge status={category || 'mission'} />
            ) : (
              <>
                <StatusBadge status={template!.category} />
                {template!.isBuiltIn && <StatusBadge status="Built-in" />}
                <ActionMenu id={`template-${template!.name}`} items={[
                  { label: 'Duplicate', onClick: () => void handleDuplicate() },
                  { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Template: {{name}}', { name: template!.name }), data: template }) },
                  ...(template!.isBuiltIn && canEditThis ? [{ label: 'Reset to Default', danger: true as const, onClick: handleReset }] : []),
                ]} />
              </>
            )}
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm(c => ({ ...c, open: false }))}
      />

      <style>{`
        .template-editor-layout {
          display: grid;
          grid-template-columns: 1fr 340px;
          gap: 1.5rem;
          margin-top: 1rem;
        }
        @media (max-width: 900px) {
          .template-editor-layout {
            grid-template-columns: 1fr;
          }
        }
        .template-editor-panel {
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
        }
        .template-editor-textarea {
          width: 100%;
          min-height: 500px;
          font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', Consolas, monospace;
          font-size: 0.875em;
          line-height: 1.5;
          padding: 12px;
          border: 1px solid var(--border);
          border-radius: 6px;
          background: var(--input-bg);
          color: var(--text);
          resize: vertical;
          tab-size: 2;
        }
        .template-editor-textarea:focus {
          outline: none;
          border-color: var(--accent);
          box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
        }
        .template-description-input {
          width: 100%;
          padding: 8px 12px;
          border: 1px solid var(--border);
          border-radius: 6px;
          background: var(--input-bg);
          color: var(--text);
          font-size: 0.9em;
        }
        .template-description-input:focus {
          outline: none;
          border-color: var(--accent);
          box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
        }
        .template-meta-field {
          display: grid;
          gap: 0.35rem;
        }
        .template-meta-label {
          font-size: 0.85em;
          color: var(--text-dim);
        }
        .template-param-panel {
          border: 1px solid var(--border);
          border-radius: 6px;
          background: var(--bg-card);
          padding: 1rem;
          max-height: 700px;
          overflow-y: auto;
        }
        .template-param-panel h4 {
          margin: 0 0 0.75rem 0;
          font-size: 0.95em;
          color: var(--text-dim);
        }
        .template-param-group {
          margin-bottom: 1rem;
        }
        .template-param-group:last-child {
          margin-bottom: 0;
        }
        .template-param-group-label {
          font-size: 0.8em;
          font-weight: 600;
          text-transform: uppercase;
          letter-spacing: 0.05em;
          color: var(--text-dim);
          margin-bottom: 0.4rem;
          padding-bottom: 0.25rem;
          border-bottom: 1px solid var(--border);
        }
        .template-param-item {
          display: flex;
          align-items: baseline;
          gap: 0.5rem;
          padding: 4px 0;
          cursor: pointer;
          border-radius: 3px;
          transition: background 0.15s;
        }
        .template-param-item:hover {
          background: var(--bg-hover);
        }
        .template-param-name {
          font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', Consolas, monospace;
          font-size: 0.8em;
          color: var(--accent);
          white-space: nowrap;
          flex-shrink: 0;
        }
        .template-param-desc {
          font-size: 0.78em;
          color: var(--text-dim);
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }
        .template-editor-actions {
          display: flex;
          gap: 0.5rem;
          align-items: center;
        }
        .template-char-count {
          font-size: 0.8em;
          color: var(--text-dim);
          margin-left: auto;
        }
        .template-dirty-indicator {
          display: inline-block;
          width: 8px;
          height: 8px;
          border-radius: 50%;
          background: #f0a040;
          margin-left: 0.25rem;
        }
      `}</style>

      {/* Template Info */}
      <div className="detail-grid">
        {createMode ? (
          <>
            <label className="detail-field template-meta-field">
              <span className="detail-label">{t('Name')}</span>
              <input
                className="template-description-input"
                value={templateName}
                onChange={e => handleTemplateNameChange(e.target.value)}
                placeholder={t('mission.rules.custom')}
              />
            </label>
            <label className="detail-field template-meta-field">
              <span className="detail-label">{t('Category')}</span>
              <input
                className="template-description-input"
                list="prompt-template-category-options"
                value={category}
                onChange={e => handleCategoryChange(e.target.value)}
                placeholder={t('mission')}
              />
              <datalist id="prompt-template-category-options">
                {PROMPT_TEMPLATE_CATEGORY_OPTIONS.map(option => (
                  <option key={option} value={option} />
                ))}
              </datalist>
            </label>
            <div className="detail-field">
              <span className="detail-label">{t('Type')}</span>
              <span>{t('Custom template')}</span>
            </div>
          </>
        ) : (
          <>
            <div className="detail-field">
              <span className="detail-label">{t('ID')}</span>
              <span className="id-display">
                <span className="mono">{template!.id}</span>
                <CopyButton text={template!.id} />
              </span>
            </div>
            <div className="detail-field"><span className="detail-label">{t('Active')}</span><StatusBadge status={template!.active !== false ? 'Active' : 'Inactive'} /></div>
            <div className="detail-field"><span className="detail-label">{t('Created')}</span><span>{formatDateTime(template!.createdUtc)}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span>{template!.lastUpdateUtc ? formatDateTime(template!.lastUpdateUtc) : '-'}</span></div>
          </>
        )}
      </div>

      <div className="template-editor-layout">
        {/* Left: Editor Panel */}
        <div className="template-editor-panel">
          <label style={{ fontSize: '0.85em', color: 'var(--text-dim)' }}>
            {t('Description')}
            <input
              type="text"
              className="template-description-input"
              value={description}
              onChange={e => handleDescriptionChange(e.target.value)}
              placeholder={t('Template description...')}
            />
          </label>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <label style={{ fontSize: '0.85em', color: 'var(--text-dim)', margin: 0 }}>
              {t('Template Content')}
              {dirty && <span className="template-dirty-indicator" title={t('Unsaved changes')} />}
            </label>
            <span className="template-char-count">{content.length} {t('characters')}</span>
          </div>

          <textarea
            ref={textareaRef}
            className="template-editor-textarea"
            value={content}
            onChange={e => handleContentChange(e.target.value)}
            rows={30}
            spellCheck={false}
          />

          <div className="template-editor-actions">
            <button
              className="btn btn-primary"
              onClick={handleSave}
              disabled={saving || !dirty || !canEditThis || (createMode && (!templateName.trim() || !category.trim() || !content.trim()))}
            >
              {saving ? t('Saving...') : t('Save')}
            </button>
            {template?.isBuiltIn && (
              <button
                className="btn"
                onClick={handleReset}
                disabled={saving}
              >
                {t('Reset to Default')}
              </button>
            )}
            <button
              className="btn"
              onClick={() => navigate('/prompt-templates')}
            >
              {t('Back')}
            </button>
          </div>
        </div>

        {/* Right: Parameter Reference Panel */}
        <div className="template-param-panel">
          <h4>{t('Parameters')}</h4>
          <p style={{ fontSize: '0.78em', color: 'var(--text-dim)', margin: '0 0 0.75rem 0' }}>
            {t('Click a parameter to insert it at the cursor position.')}
          </p>
          {PARAMETER_GROUPS.map(group => (
            <div key={group.label} className="template-param-group">
              <div className="template-param-group-label">{t(group.label)}</div>
              {group.params.map(param => (
                <div
                  key={param.name}
                  className="template-param-item"
                  onClick={() => insertParameter(param.name)}
                  title={t('Insert {{name}} -- {{description}}', { name: param.name, description: t(param.description) })}
                >
                  <span className="template-param-name">{param.name}</span>
                  <span className="template-param-desc">{t(param.description)}</span>
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
