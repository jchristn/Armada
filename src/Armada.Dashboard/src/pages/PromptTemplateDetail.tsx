import { useState, useEffect, useRef, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { getPromptTemplate, updatePromptTemplate, resetPromptTemplate } from '../api/client';
import type { PromptTemplate } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import { copyToClipboard } from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';

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

export default function PromptTemplateDetail() {
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const [template, setTemplate] = useState<PromptTemplate | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  const [successMessage, setSuccessMessage] = useState('');

  // Editable fields
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
    if (!name) return;
    try {
      setLoading(true);
      const result = await getPromptTemplate(name);
      setTemplate(result);
      setContent(result.content);
      setDescription(result.description ?? '');
      setDirty(false);
      setError('');
    } catch {
      setError('Failed to load prompt template.');
    } finally {
      setLoading(false);
    }
  }, [name]);

  useEffect(() => { load(); }, [load]);

  function handleContentChange(value: string) {
    setContent(value);
    setDirty(true);
  }

  function handleDescriptionChange(value: string) {
    setDescription(value);
    setDirty(true);
  }

  async function handleSave() {
    if (!name || !template) return;
    try {
      setSaving(true);
      const result = await updatePromptTemplate(name, { content, description });
      setTemplate(result);
      setContent(result.content);
      setDescription(result.description ?? '');
      setDirty(false);
      setSuccessMessage('Template saved.');
      setTimeout(() => setSuccessMessage(''), 3000);
    } catch {
      setError('Save failed.');
    } finally {
      setSaving(false);
    }
  }

  function handleReset() {
    if (!template || !template.isBuiltIn) return;
    setConfirm({
      open: true,
      title: 'Reset to Default',
      message: `Reset "${template.name}" to its built-in default content? Your customizations will be lost.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          const result = await resetPromptTemplate(template.name);
          setTemplate(result);
          setContent(result.content);
          setDescription(result.description ?? '');
          setDirty(false);
          setSuccessMessage('Template reset to default.');
          setTimeout(() => setSuccessMessage(''), 3000);
        } catch {
          setError('Reset failed.');
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

  if (loading) return <p className="text-dim">Loading...</p>;
  if (error && !template) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!template) return <p className="text-dim">Template not found.</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumbs">
        <Link to="/prompt-templates">Prompt Templates</Link> <span className="bc-sep">&gt;</span> <span>{template.name}</span>
      </div>

      <div className="detail-header">
        <h2>{template.name}</h2>
        <div className="inline-actions">
          <StatusBadge status={template.category} />
          {template.isBuiltIn && <StatusBadge status="Built-in" />}
          <ActionMenu id={`template-${template.name}`} items={[
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Template: ${template.name}`, data: template }) },
            ...(template.isBuiltIn ? [{ label: 'Reset to Default', danger: true as const, onClick: handleReset }] : []),
          ]} />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm(c => ({ ...c, open: false }))}
      />

      {successMessage && (
        <div style={{
          padding: '8px 16px',
          marginBottom: '1rem',
          borderRadius: '4px',
          background: 'rgba(80, 200, 120, 0.15)',
          color: '#50c878',
          border: '1px solid rgba(80, 200, 120, 0.3)',
          fontSize: '0.9em',
        }}>
          {successMessage}
        </div>
      )}

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
        <div className="detail-field">
          <span className="detail-label">ID</span>
          <span className="id-display">
            <span className="mono">{template.id}</span>
            <button className="copy-btn" onClick={() => copyToClipboard(template.id)} title="Copy ID" />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">Active</span><StatusBadge status={template.active !== false ? 'Active' : 'Inactive'} /></div>
        <div className="detail-field"><span className="detail-label">Created</span><span>{new Date(template.createdUtc).toLocaleString()}</span></div>
        <div className="detail-field"><span className="detail-label">Last Updated</span><span>{template.lastUpdateUtc ? new Date(template.lastUpdateUtc).toLocaleString() : '-'}</span></div>
      </div>

      <div className="template-editor-layout">
        {/* Left: Editor Panel */}
        <div className="template-editor-panel">
          <label style={{ fontSize: '0.85em', color: 'var(--text-dim)' }}>
            Description
            <input
              type="text"
              className="template-description-input"
              value={description}
              onChange={e => handleDescriptionChange(e.target.value)}
              placeholder="Template description..."
            />
          </label>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <label style={{ fontSize: '0.85em', color: 'var(--text-dim)', margin: 0 }}>
              Template Content
              {dirty && <span className="template-dirty-indicator" title="Unsaved changes" />}
            </label>
            <span className="template-char-count">{content.length} characters</span>
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
              disabled={saving || !dirty}
            >
              {saving ? 'Saving...' : 'Save'}
            </button>
            {template.isBuiltIn && (
              <button
                className="btn"
                onClick={handleReset}
                disabled={saving}
              >
                Reset to Default
              </button>
            )}
            <button
              className="btn"
              onClick={() => navigate('/prompt-templates')}
            >
              Back
            </button>
          </div>
        </div>

        {/* Right: Parameter Reference Panel */}
        <div className="template-param-panel">
          <h4>Parameters</h4>
          <p style={{ fontSize: '0.78em', color: 'var(--text-dim)', margin: '0 0 0.75rem 0' }}>
            Click a parameter to insert it at the cursor position.
          </p>
          {PARAMETER_GROUPS.map(group => (
            <div key={group.label} className="template-param-group">
              <div className="template-param-group-label">{group.label}</div>
              {group.params.map(param => (
                <div
                  key={param.name}
                  className="template-param-item"
                  onClick={() => insertParameter(param.name)}
                  title={`Insert ${param.name} -- ${param.description}`}
                >
                  <span className="template-param-name">{param.name}</span>
                  <span className="template-param-desc">{param.description}</span>
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
