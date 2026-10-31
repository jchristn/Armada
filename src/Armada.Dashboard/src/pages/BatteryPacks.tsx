import { useCallback, useEffect, useState } from 'react';
import { createBatteryPack, deleteBatteryPack, deleteBatteryPackFile, getBatteryPack, listBatteryPacks, readBatteryPackFile, updateBatteryPackManifest, validateBatteryPack, writeBatteryPackFile } from '../api/client';
import type { BatteryPackDetail, BatteryPackInfo } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import DialogShell from '../components/shared/DialogShell';

export default function BatteryPacks() {
  const { t } = useLocale();
  const { isAdmin } = useAuth();
  const [packs, setPacks] = useState<BatteryPackInfo[]>([]);
  const [page, setPage] = useState(1);
  const [pages, setPages] = useState(1);
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
    const result = await listBatteryPacks(50, page);
    setPacks(result.objects);
    setPages(Math.max(1, result.totalPages));
  }, [page]);
  useEffect(() => { void load().catch(e => setError(String(e))); }, [load]);
  async function perform(action: () => Promise<void>) {
    if (busy) return;
    setBusy(true); setError('');
    try { await action(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }
  function show(value: BatteryPackDetail) {
    setDetail(value); setManifest(value.manifestJson); setPath(''); setContent(''); setFileOpen(false);
  }
  async function validate(id: string) {
    const info = await validateBatteryPack(id);
    setPacks(items => items.map(pack => pack.id === id ? { ...info, id } : pack));
    setDetail(current => current?.id === id ? { ...current, info } : current);
  }
  return <div className="page-content">
    <div className="page-header"><h1>{t('Battery packs')}</h1>
      {isAdmin && <button className="btn btn-primary" disabled={busy} onClick={() => setCreating(true)}>{t('New pack')}</button>}
    </div>
    {error && <p role="alert" className="text-danger">{error}</p>}
    <table className="data-table"><thead><tr>
      {['ID', 'Name', 'Language', 'Tasks', 'Status', 'Actions'].map(label => <th key={label}>{t(label)}</th>)}
    </tr></thead><tbody>{packs.map(pack => <tr key={pack.id}>
      <td>{pack.id}</td><td>{pack.name}</td><td>{pack.domain === 'code' ? pack.name ?? pack.id : pack.domain}</td><td>{pack.taskCount}</td>
      <td title={pack.errors.join('\n')}>{t(pack.valid ? 'Valid' : 'Invalid')}</td>
      <td><ActionMenu id={`battery-${pack.id}`} items={[
        { label: isAdmin ? 'View/Edit' : 'View', disabled: busy, onClick: () => void perform(async () => show(await getBatteryPack(pack.id))) },
        { label: 'Validate', disabled: busy, onClick: () => void perform(() => validate(pack.id)) },
        ...(isAdmin ? [{ label: 'Delete', disabled: busy, danger: true, onClick: () => setPendingDelete({ id: pack.id }) }] : []),
      ]} /></td>
    </tr>)}</tbody></table>
    {!packs.length && <p>{t('No battery packs are installed.')}</p>}
    <div className="pagination">
      <button className="btn" disabled={page <= 1 || busy} onClick={() => setPage(p => p - 1)}>{t('Previous')}</button>
      <span>{page} / {pages}</span>
      <button className="btn" disabled={page >= pages || busy} onClick={() => setPage(p => p + 1)}>{t('Next')}</button>
    </div>
    {detail && <section aria-label={t('Pack editor')}>
      <h2>{detail.id}</h2>
      <label>{t('Manifest JSON')}<textarea aria-label={t('Manifest JSON')} rows={18} style={{ width: '100%', fontFamily: 'monospace' }} value={manifest} readOnly={!isAdmin} disabled={busy} onChange={e => setManifest(e.target.value)} /></label>
      <div className="modal-actions">
        {isAdmin && <button className="btn btn-primary" disabled={busy} onClick={() => void perform(async () => {
          const updated = await updateBatteryPackManifest(detail.id, manifest);
          setDetail(updated); setManifest(updated.manifestJson); await load();
        })}>{t('Save manifest')}</button>}
        <button className="btn" disabled={busy} onClick={() => void perform(() => validate(detail.id))}>{t('Validate')}</button>
      </div>
      <p>{t('Validation checks the saved pack on disk.')}</p>
      <div role="status">{detail.info.valid ? t('Valid') : <ul>{detail.info.errors.map((value, i) => <li key={i}>{value}</li>)}</ul>}</div>
      <h3>{t('Files')}</h3>
      <ul>{detail.files.map(file => <li key={file.path}>
        <button className="btn" disabled={busy} onClick={() => void perform(async () => {
          const value = await readBatteryPackFile(detail.id, file.path);
          setPath(value.path); setContent(value.content); setFileOpen(true);
        })}>{file.path}</button> <span>{file.size} B</span>
      </li>)}</ul>
      {isAdmin && <button className="btn" disabled={busy} onClick={() => { setPath('fixture/'); setContent(''); setFileOpen(true); }}>{t('New file')}</button>}
      {fileOpen && <div>
        <label>{t('File path')}<input aria-label={t('File path')} value={path} readOnly={!isAdmin} disabled={busy} onChange={e => setPath(e.target.value)} /></label>
        <textarea aria-label={t('File content')} rows={16} style={{ width: '100%', fontFamily: 'monospace' }} value={content} readOnly={!isAdmin || path === 'battery-pack.json'} disabled={busy} onChange={e => setContent(e.target.value)} />
        {isAdmin && path !== 'battery-pack.json' && <div className="modal-actions">
          <button className="btn btn-primary" disabled={busy} onClick={() => void perform(async () => {
            const updated = await writeBatteryPackFile(detail.id, path, content); setDetail(updated); await load();
          })}>{t('Save file')}</button>
          <button className="btn btn-danger" disabled={busy || !detail.files.some(file => file.path === path)} onClick={() => setPendingDelete({ id: detail.id, path })}>{t('Delete file')}</button>
        </div>}
      </div>}
    </section>}
    <DialogShell open={creating} title={t('New pack')} onClose={() => setCreating(false)} dismissible={!busy} footer={
      <button className="btn btn-primary" disabled={busy || !newId} onClick={() => void perform(async () => {
        const created = await createBatteryPack(newId, newManifest); show(created); setCreating(false); setNewId(''); setNewManifest('{}'); await load();
      })}>{t('Create')}</button>}>
      {error && <p role="alert" className="text-danger">{error}</p>}
      <label>{t('Pack ID')}<input aria-label={t('Pack ID')} value={newId} disabled={busy} onChange={e => setNewId(e.target.value)} placeholder="my-pack" /></label>
      <label>{t('Manifest JSON')}<textarea aria-label={t('New manifest JSON')} rows={12} style={{ width: '100%', fontFamily: 'monospace' }} value={newManifest} disabled={busy} onChange={e => setNewManifest(e.target.value)} /></label>
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
  </div>;
}
