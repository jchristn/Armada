import { useCallback, useEffect, useState } from 'react';
import { createBatteryPack, deleteBatteryPack, deleteBatteryPackFile, getBatteryPack, listBatteryPacks, readBatteryPackFile, updateBatteryPackManifest, validateBatteryPack, writeBatteryPackFile } from '../api/client';
import type { BatteryPackDetail, BatteryPackInfo } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import DialogShell from '../components/shared/DialogShell';
import ErrorModal from '../components/shared/ErrorModal';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { ErrorState } from '../components/shared/StateBlocks';

type PackFile = BatteryPackDetail['files'][number];

const MANIFEST_FILE = 'battery-pack.json';

function message(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

export default function BatteryPacks() {
  const { t } = useLocale();
  const { isAdmin } = useAuth();
  const [packs, setPacks] = useState<BatteryPackInfo[]>([]);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [detail, setDetail] = useState<BatteryPackDetail | null>(null);
  const [manifest, setManifest] = useState('');
  const [path, setPath] = useState('');
  const [content, setContent] = useState('');
  const [fileOpen, setFileOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [newId, setNewId] = useState('');
  const [newManifest, setNewManifest] = useState('{}');
  const [pendingDelete, setPendingDelete] = useState<{ id: string; path?: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await listBatteryPacks(pageSize, pageNumber);
      setPacks(result.objects ?? []);
      setTotalPages(Math.max(1, result.totalPages ?? 1));
      setTotalRecords(result.totalRecords ?? (result.objects ?? []).length);
      setLoadError(null);
    } catch (e) {
      setLoadError(message(e));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize]);
  useEffect(() => { void load(); }, [load]);

  async function perform(action: () => Promise<void>) {
    if (busy) return;
    setBusy(true); setError('');
    try { await action(); } catch (e) { setError(message(e)); }
    finally { setBusy(false); }
  }
  function show(value: BatteryPackDetail) {
    setDetail(value); setManifest(value.manifestJson); setPath(''); setContent(''); setFileOpen(false);
  }
  function closeEditor() {
    if (busy) return;
    setDetail(null); setError('');
  }
  function openPack(id: string) {
    void perform(async () => show(await getBatteryPack(id)));
  }
  async function validate(id: string) {
    const info = await validateBatteryPack(id);
    setPacks(items => items.map(pack => pack.id === id ? { ...info, id } : pack));
    setDetail(current => current?.id === id ? { ...current, info } : current);
  }
  function openFile(file: PackFile) {
    if (!detail) return;
    const id = detail.id;
    void perform(async () => {
      const value = await readBatteryPackFile(id, file.path);
      setPath(value.path); setContent(value.content); setFileOpen(true);
    });
  }

  const columns: DataTableColumn<BatteryPackInfo>[] = [
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      headerTitle: t('Pack ID: the directory name of the pack in the battery catalog.'),
      render: (pack) => (
        <span className="id-display">
          <span className="id-value" title={pack.id}>{pack.id}</span>
          <CopyButton text={pack.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    { key: 'name', label: t('Name'), required: true, cellClassName: 'cell-ident', headerTitle: t('Display name from the pack manifest.'), render: (pack) => pack.name ? <strong>{pack.name}</strong> : <span className="text-dim">-</span> },
    { key: 'domain', label: t('Domain'), headerTitle: t('What the pack exercises, for example code or documentation.'), cellClassName: 'text-dim', render: (pack) => pack.domain || '-' },
    { key: 'version', label: t('Version'), headerTitle: t('Manifest version of the pack.'), cellClassName: 'text-dim', render: (pack) => pack.version },
    { key: 'tasks', label: t('Tasks'), headerTitle: t('Number of tasks the pack defines.'), render: (pack) => pack.taskCount },
    {
      key: 'status', label: t('Status'), headerTitle: t('Result of the last validation. Hover an invalid pack for its errors.'),
      render: (pack) => <StatusBadge status={pack.valid ? 'Valid' : 'Invalid'} title={pack.valid ? undefined : pack.errors.join('\n')} />,
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      headerTitle: t('Open the menu in a row to view, validate or delete that pack.'),
      render: (pack) => (
        <ActionMenu id={`battery-${pack.id}`} items={[
          { label: isAdmin ? 'View/Edit' : 'View', disabled: busy, onClick: () => openPack(pack.id) },
          { label: 'Validate', disabled: busy, onClick: () => void perform(() => validate(pack.id)) },
          ...(isAdmin ? [{ label: 'Delete', disabled: busy, danger: true as const, onClick: () => setPendingDelete({ id: pack.id }) }] : []),
        ]} />
      ),
    },
  ];

  const fileColumns: DataTableColumn<PackFile>[] = [
    { key: 'path', label: t('Path'), required: true, cellClassName: 'mono', render: (file) => file.path },
    { key: 'size', label: t('Size'), required: true, cellClassName: 'text-dim cell-nowrap', render: (file) => `${file.size} B` },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (file) => <button type="button" className="btn btn-sm" disabled={busy} onClick={() => openFile(file)}>{t('Open')}</button>,
    },
  ];

  const editorError = (detail || creating) && error;

  return (
    <div>
      <PageHeader
        title={t('Battery packs')}
        subtitle={t('Fixture packs that Full Evaluate runs against captains. Each pack has a manifest and the fixture files its tasks start from.')}
        actions={isAdmin ? <button type="button" className="btn btn-primary btn-sm" disabled={busy} onClick={() => { setError(''); setCreating(true); }}>+ {t('New pack')}</button> : undefined}
      />

      {loadError !== null && <ErrorState message={loadError || t('Failed to load battery packs.')} onRetry={() => void load()} />}
      <ErrorModal error={editorError ? '' : error} onClose={() => setError('')} />

      <DataTable
        tableKey="battery-packs"
        columns={columns}
        rows={packs}
        rowKey={(pack) => pack.id}
        onRowClick={(pack) => openPack(pack.id)}
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        onRefresh={load}
        refreshTitle={t('Reload the battery pack list now.')}
        busy={busy}
        placeholder={packs.length > 0 ? undefined : <p className="text-dim">{loading ? t('Loading...') : t('No battery packs are installed.')}</p>}
      />

      <DialogShell
        open={detail !== null}
        size="xl"
        className="battery-pack-dialog"
        title={detail ? (detail.info.name || detail.id) : t('Pack editor')}
        subtitle={detail ? <span className="mono">{detail.id}</span> : undefined}
        onClose={closeEditor}
        dismissible={!busy}
        footer={detail && <>
          <button type="button" className="btn" disabled={busy} onClick={() => void perform(() => validate(detail.id))}>{t('Validate')}</button>
          {isAdmin && <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void perform(async () => {
            const updated = await updateBatteryPackManifest(detail.id, manifest);
            setDetail(updated); setManifest(updated.manifestJson); await load();
          })}>{t('Save manifest')}</button>}
          <button type="button" className="btn" disabled={busy} onClick={closeEditor}>{t('Close')}</button>
        </>}
      >
        {detail && <section aria-label={t('Pack editor')} className="form-stack">
          {error && <div className="alert alert-error" role="alert">{error}</div>}
          <div className="form-field" role="status">
            <span className="form-label">{t('Status')}</span>
            <span><StatusBadge status={detail.info.valid ? 'Valid' : 'Invalid'} /></span>
            {!detail.info.valid && detail.info.errors.length > 0 && <div className="alert alert-error"><ul>{detail.info.errors.map((value, i) => <li key={i}>{value}</li>)}</ul></div>}
            <p className="text-dim form-help">{t('Validation checks the saved pack on disk.')}</p>
          </div>
          <label className="form-field">
            <span className="form-label">{t('Manifest JSON')}</span>
            <textarea aria-label={t('Manifest JSON')} className="mono" rows={14} spellCheck={false} value={manifest} readOnly={!isAdmin} disabled={busy} onChange={e => setManifest(e.target.value)} />
          </label>
          <div className="form-field">
            <div className="detail-section-header">
              <h3>{t('Files')}</h3>
              {isAdmin && <button type="button" className="btn btn-sm" disabled={busy} onClick={() => { setPath('fixture/'); setContent(''); setFileOpen(true); }}>+ {t('New file')}</button>}
            </div>
            <DataTable
              tableKey="battery-pack-files"
              columns={fileColumns}
              rows={detail.files}
              rowKey={(file) => file.path}
              onRowClick={openFile}
              busy={busy}
              placeholder={detail.files.length > 0 ? undefined : <p className="text-dim">{t('No files.')}</p>}
            />
          </div>
          {fileOpen && <div className="form-stack">
            <label className="form-field">
              <span className="form-label">{t('File path')}</span>
              <input aria-label={t('File path')} className="mono" value={path} readOnly={!isAdmin} disabled={busy} onChange={e => setPath(e.target.value)} />
            </label>
            <label className="form-field">
              <span className="form-label">{t('File content')}</span>
              <textarea aria-label={t('File content')} className="mono" rows={14} spellCheck={false} value={content} readOnly={!isAdmin || path === MANIFEST_FILE} disabled={busy} onChange={e => setContent(e.target.value)} />
            </label>
            {isAdmin && path !== MANIFEST_FILE && <div className="page-actions">
              <button type="button" className="btn btn-primary btn-sm" disabled={busy} onClick={() => void perform(async () => {
                const updated = await writeBatteryPackFile(detail.id, path, content); setDetail(updated); await load();
              })}>{t('Save file')}</button>
              <button type="button" className="btn btn-danger btn-sm" disabled={busy || !detail.files.some(file => file.path === path)} onClick={() => setPendingDelete({ id: detail.id, path })}>{t('Delete file')}</button>
            </div>}
          </div>}
        </section>}
      </DialogShell>

      <DialogShell open={creating} title={t('New pack')} onClose={() => { if (!busy) { setCreating(false); setError(''); } }} dismissible={!busy} footer={<>
        <button type="button" className="btn btn-primary" disabled={busy || !newId} onClick={() => void perform(async () => {
          const created = await createBatteryPack(newId, newManifest); setCreating(false); show(created); setNewId(''); setNewManifest('{}'); await load();
        })}>{t('Create')}</button>
        <button type="button" className="btn" disabled={busy} onClick={() => { setCreating(false); setError(''); }}>{t('Cancel')}</button>
      </>}>
        <div className="form-stack">
          {error && <div className="alert alert-error" role="alert">{error}</div>}
          <label className="form-field">
            <span className="form-label">{t('Pack ID')}</span>
            <input aria-label={t('Pack ID')} className="mono" value={newId} disabled={busy} onChange={e => setNewId(e.target.value)} placeholder="my-pack" />
          </label>
          <label className="form-field">
            <span className="form-label">{t('Manifest JSON')}</span>
            <textarea aria-label={t('New manifest JSON')} className="mono" rows={12} spellCheck={false} value={newManifest} disabled={busy} onChange={e => setNewManifest(e.target.value)} />
          </label>
        </div>
      </DialogShell>

      <ConfirmDialog open={pendingDelete !== null} danger title="Delete" confirmLabel="Delete" message={t('Delete pack resource {{name}}?', { name: pendingDelete?.path ?? pendingDelete?.id ?? '' })} onCancel={() => { if (!busy) setPendingDelete(null); }} onConfirm={() => void perform(async () => {
        if (!pendingDelete) return;
        if (pendingDelete.path) {
          const updated = await deleteBatteryPackFile(pendingDelete.id, pendingDelete.path); setDetail(updated); setFileOpen(false);
        } else {
          await deleteBatteryPack(pendingDelete.id);
          if (detail?.id === pendingDelete.id) setDetail(null);
        }
        setPendingDelete(null); await load();
      })} />
    </div>
  );
}
