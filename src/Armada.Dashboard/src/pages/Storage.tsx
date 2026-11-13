import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getStorage, listAllStorageHosts, refreshStorage } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import PageHeader from '../components/shared/PageHeader';
import CopyButton from '../components/shared/CopyButton';
import StorageTable from '../components/storage/StorageTable';
import type { StorageHostReport, StorageSnapshot } from '../types/storage';
import { formatStorageBytes } from '../lib/storageFormatting';
import './Storage.css';

export default function Storage() {
  const { isAdmin } = useAuth();
  const { t, locale, formatDateTime } = useLocale();
  const [snapshot, setSnapshot] = useState<StorageSnapshot | null>(null);
  // Host reports are not in the snapshot; they are read in explicit bounded pages (100 hosts per page).
  const [hostReports, setHostReports] = useState<StorageHostReport[]>([]);
  const [error, setError] = useState(false);
  const [requesting, setRequesting] = useState(false);
  const [tab, setTab] = useState('locations');
  const [trendScope, setTrendScope] = useState<'categories' | 'hosts' | 'tables'>('categories');
  const load = useCallback(async () => {
    try {
      const [result, hosts] = await Promise.all([getStorage(), listAllStorageHosts()]);
      setSnapshot(result); setHostReports(hosts); setError(false);
    } catch { setError(true); }
  }, []);
  useEffect(() => {
    if (!isAdmin) return;
    let active = true;
    const poll = async () => {
      try {
        const [result, hosts] = await Promise.all([getStorage(), listAllStorageHosts()]);
        if (active) { setSnapshot(result); setHostReports(hosts); setError(false); }
      }
      catch { if (active) setError(true); }
    };
    void poll();
    const timer = window.setInterval(() => void poll(), 5000);
    return () => { active = false; window.clearInterval(timer); };
  }, [isAdmin]);
  const collect = async (exact: boolean) => {
    setRequesting(true);
    try {
      const result = await refreshStorage(exact);
      setSnapshot(result); setHostReports(await listAllStorageHosts()); setError(false);
    } catch { setError(true); }
    finally { setRequesting(false); }
  };
  const number = (n: number | null | undefined) => n == null ? t('storage.unavailable') : new Intl.NumberFormat(locale).format(n);
  const bytes = (n: number | null | undefined) => formatStorageBytes(n, locale, t('storage.unavailable'));
  const date = (value: string | null | undefined) => value && !value.startsWith('0001-') ? formatDateTime(value) : t('storage.unavailable');
  const state = (value: string) => t(`storage.${value}`);
  const category = (value: string) => t(`storage.category.${value}`);
  const path = (value: string) => <span className="storage-path"><code title={value}>{value}</code><CopyButton text={value} /></span>;
  if (!isAdmin) return <p role="alert">{t('storage.denied')}</p>;
  const hosts = hostReports;
  const locations = hosts.flatMap(host => host.locations.map(l => ({ ...l, host: host.name, hostId: host.hostId, measured: host.measuredUtc, stale: host.stale })));
  const sum = snapshot?.observedFileBytes ?? locations.reduce((n, l) => n + (l.bytes ?? 0), 0);
  const logs = locations.filter(l => l.category.startsWith('Logs/')).reduce((n, l) => n + (l.bytes ?? 0), 0);
  const fresh = snapshot?.freshHostCount ?? hosts.filter(h => !h.stale && h.status === 'Complete').length;
  const hostCount = snapshot?.hostCount ?? hosts.length;
  const files = locations.flatMap(l => l.largestFiles.map(f => ({ ...f, host: l.host, category: l.category })));
  const db = snapshot?.database;
  const history = snapshot?.history ?? [];
  const latest = history[history.length - 1];
  const previous = history[history.length - 2];
  const growth = latest && previous ? latest.observedFileBytes - previous.observedFileBytes : null;
  const trendNames = Array.from(new Set([...Object.keys(latest?.[trendScope] ?? {}), ...Object.keys(previous?.[trendScope] ?? {})]));
  const trends = trendNames.map(name => ({ name, current: latest?.[trendScope][name] ?? null, previous: previous?.[trendScope][name] ?? null, change: latest?.[trendScope][name] != null && previous?.[trendScope][name] != null ? latest[trendScope][name] - previous[trendScope][name] : null }));
  const historyMax = Math.max(1, ...history.map(h => h.observedFileBytes));
  const busy = requesting || snapshot?.collecting === true;
  return <div className="storage-page">
    <PageHeader title={t('storage.title')} subtitle={t('storage.subtitle')} actions={<>
      <button className="btn btn-sm" title={t('storage.reloadHelp')} onClick={() => void load()}>{t('storage.reload')}</button>
      <button className="btn btn-sm btn-primary" disabled={busy} title={t('storage.collectHelp')} onClick={() => void collect(false)}>{t(busy ? 'storage.collecting' : 'storage.collect')}</button>
      <button className="btn btn-sm" disabled={busy} title={t('storage.exactHelp')} onClick={() => void collect(true)}>{t('storage.exact')}</button>
    </>} />
    {error && <div role="alert" className="error-banner">{t('storage.error')} <button className="btn btn-sm" onClick={() => void load()}>{t('storage.reload')}</button></div>}
    {snapshot?.lastError && <p role="alert">{state(snapshot.lastError)}</p>}
    {!snapshot ? <p role="status">{t('storage.loading')}</p> : <>
      <p className="text-dim">{t('storage.measured', { time: date(snapshot.measuredUtc), seconds: snapshot.intervalSeconds })}</p>
      {!snapshot.measuredUtc && <p role="status">{t('storage.pending')}</p>}
      <div className="storage-kpis">
        {[['filesKpi', bytes(sum)], ['databaseKpi', bytes(db?.allocatedBytes)], ['logsKpi', bytes(logs)], ['coverageKpi', `${number(fresh)} / ${number(hostCount)}`]].map(([key, value]) =>
          <div className="stat-card" key={key}><div className="stat-label">{t(`storage.${key}`)}</div><div className="stat-value">{value}</div></div>)}
      </div>
      <p className="text-dim">{t('storage.accounting')}</p>
      <div className="storage-tabs" role="tablist" aria-label={t('storage.sections')} onKeyDown={e => {
        const names = ['locations', 'database', 'hosts', 'volumes', 'files', 'history', 'retention'];
        let index = names.indexOf(tab);
        if (e.key === 'ArrowRight') index = (index + 1) % names.length;
        else if (e.key === 'ArrowLeft') index = (index - 1 + names.length) % names.length;
        else if (e.key === 'Home') index = 0;
        else if (e.key === 'End') index = names.length - 1;
        else return;
        e.preventDefault(); setTab(names[index]); document.getElementById(`storage-tab-${names[index]}`)?.focus();
      }}>
        {['locations', 'database', 'hosts', 'volumes', 'files', 'history', 'retention'].map(name => <button key={name} role="tab" id={`storage-tab-${name}`} aria-controls="storage-panel" aria-selected={tab === name} tabIndex={tab === name ? 0 : -1} className={`btn btn-sm ${tab === name ? 'btn-primary' : ''}`} onClick={() => setTab(name)}>{t(`storage.${name}`)}</button>)}
      </div>
      <section role="tabpanel" id="storage-panel" aria-labelledby={`storage-tab-${tab}`}>
        {tab === 'locations' && <><p className="text-dim">{t('storage.filterHelp')}</p><StorageTable name="locations" rows={locations} columns={[
          { key: 'host', label: t('storage.host'), render: l => l.host, required: true },
          { key: 'category', label: t('storage.category'), render: l => category(l.category) },
          { key: 'path', label: t('storage.path'), render: l => path(l.path), interactive: true },
          { key: 'bytes', label: t('storage.bytes'), render: l => bytes(l.bytes) },
          { key: 'fileCount', label: t('storage.fileCount'), render: l => number(l.fileCount) },
          { key: 'entityId', label: t('storage.entity'), render: l => l.entityId ?? '-' },
          { key: 'activity', label: t('storage.activity'), render: l => l.activity ? t(l.activity) : '-' },
          { key: 'status', label: t('storage.status'), render: l => <span>{state(l.status)}{l.stale ? ` (${t('storage.Stale')})` : ''}{l.warnings.map(w => <small key={w} className="storage-warning">{state(w)}</small>)}</span> },
          { key: 'oldestWriteUtc', label: t('storage.oldest'), render: l => date(l.oldestWriteUtc), defaultHidden: true },
          { key: 'newestWriteUtc', label: t('storage.newest'), render: l => date(l.newestWriteUtc), defaultHidden: true },
        ]} /></>}
        {tab === 'database' && <>
          <p>{db?.provider || t('storage.unavailable')} · {state(db?.status ?? 'Pending')} · {date(db?.measuredUtc)}</p>
          <p className="text-dim">{t('storage.databaseHelp', { unused: bytes(db?.unusedBytes) })}</p>
          {db?.warnings.map(w => <p key={w} role="status">{state(w)}</p>)}
          <StorageTable name="database" rows={db?.tables ?? []} columns={[
            { key: 'schema', label: t('storage.schema'), render: r => r.schema },
            { key: 'name', label: t('storage.table'), required: true, render: r => r.name },
            { key: 'rows', label: t('storage.records'), render: r => number(r.rows) },
            { key: 'countKind', label: t('storage.accuracy'), render: r => state(r.countKind) },
            { key: 'logicalBytes', label: t('storage.logicalBytes'), render: r => bytes(r.logicalBytes) },
            { key: 'dataBytes', label: t('storage.dataBytes'), render: r => bytes(r.dataBytes) },
            { key: 'indexBytes', label: t('storage.indexBytes'), render: r => bytes(r.indexBytes) },
            { key: 'warning', label: t('storage.warning'), render: r => r.warning ? state(r.warning) : '-' },
          ]} />
        </>}
        {tab === 'hosts' && <StorageTable name="hosts" rows={hosts} columns={[
          { key: 'name', label: t('storage.host'), required: true, render: h => h.name },
          { key: 'hostId', label: t('storage.id'), render: h => h.hostId },
          { key: 'measuredUtc', label: t('storage.lastSample'), render: h => date(h.measuredUtc) },
          { key: 'status', label: t('storage.status'), render: h => <>{state(h.status)} {h.stale && t('storage.Stale')}</> },
          { key: 'warnings', label: t('storage.warning'), render: h => h.warnings.map(w => state(w)).join('; ') || '-' },
        ]} />}
        {tab === 'volumes' && <><p className="text-dim">{t('storage.volumeHelp')}</p><StorageTable name="volumes" rows={hosts.flatMap(h => h.volumes.map(v => ({ ...v, host: h.name, utilization: v.totalBytes && v.availableBytes != null ? (v.totalBytes - v.availableBytes) / v.totalBytes : null })))} columns={[
          { key: 'host', label: t('storage.host'), required: true, render: v => v.host },
          { key: 'path', label: t('storage.path'), render: v => path(v.path), interactive: true },
          { key: 'totalBytes', label: t('storage.capacity'), render: v => bytes(v.totalBytes) },
          { key: 'availableBytes', label: t('storage.available'), render: v => bytes(v.availableBytes) },
          { key: 'status', label: t('storage.status'), render: v => state(v.status) },
          { key: 'utilization', label: t('storage.utilization'), render: v => v.utilization != null ? new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(v.utilization) : t('storage.unavailable') },
        ]} /></>}
        {tab === 'files' && <><p className="text-dim">{t('storage.filesHelp')}</p><StorageTable name="files" rows={files} columns={[
          { key: 'host', label: t('storage.host'), required: true, render: f => f.host },
          { key: 'path', label: t('storage.path'), render: f => path(f.path), interactive: true },
          { key: 'category', label: t('storage.category'), render: f => category(f.category) },
          { key: 'bytes', label: t('storage.bytes'), render: f => bytes(f.bytes) },
          { key: 'lastWriteUtc', label: t('storage.newest'), render: f => date(f.lastWriteUtc) },
        ]} /></>}
        {tab === 'history' && <>
          <p className="text-dim">{t('storage.historyHelp', { count: snapshot.historyCapacity })}</p>
          <p>{t('storage.change', { value: growth == null ? t('storage.unavailable') : `${growth < 0 ? '-' : '+'}${bytes(Math.abs(growth))}` })}</p>
          <div className="storage-chart" role="img" aria-label={t('storage.chartHelp')}>
            {history.slice(-60).map(h => <div key={h.measuredUtc} title={`${date(h.measuredUtc)}: ${bytes(h.observedFileBytes)}`} style={{ height: `${Math.max(2, h.observedFileBytes / historyMax * 100)}%` }} />)}
          </div>
          <StorageTable name="history" rows={history.map((h, i) => ({ ...h, change: i ? h.observedFileBytes - history[i - 1].observedFileBytes : null }))} columns={[
            { key: 'measuredUtc', label: t('storage.lastSample'), required: true, render: h => date(h.measuredUtc) },
            { key: 'observedFileBytes', label: t('storage.filesKpi'), render: h => bytes(h.observedFileBytes) },
            { key: 'databaseBytes', label: t('storage.databaseKpi'), render: h => bytes(h.databaseBytes) },
            { key: 'change', label: t('storage.growth'), render: h => h.change == null ? '-' : `${h.change < 0 ? '-' : '+'}${bytes(Math.abs(h.change))}` },
          ]} />
          <label>{t('storage.trendScope')} <select aria-label={t('storage.trendScope')} value={trendScope} onChange={e => setTrendScope(e.target.value as typeof trendScope)}>
            {(['categories', 'hosts', 'tables'] as const).map(scope => <option key={scope} value={scope}>{t(`storage.${scope}`)}</option>)}
          </select></label>
          <StorageTable name="trends" rows={trends} columns={[
            { key: 'name', label: t(`storage.${trendScope}`), required: true, render: r => trendScope === 'categories' ? category(r.name) : r.name },
            { key: 'current', label: t('storage.current'), render: r => bytes(r.current) },
            { key: 'previous', label: t('storage.previous'), render: r => bytes(r.previous) },
            { key: 'change', label: t('storage.changeBytes'), render: r => r.change == null ? '-' : `${r.change < 0 ? '-' : '+'}${bytes(Math.abs(r.change))}` },
          ]} />
        </>}
        {tab === 'retention' && <><p className="text-dim">{t('storage.retentionHelp')} <Link to="/server">{t('storage.settings')}</Link></p><StorageTable name="retention" rows={Object.entries(snapshot.retention).map(([policy, value]) => ({ policy, value }))} columns={[
          { key: 'policy', label: t('storage.policy'), required: true, render: r => t(`storage.retention.${r.policy}`) },
          { key: 'value', label: t('storage.value'), render: r => r.policy === 'BuildSlots' ? number(r.value) : r.value === 0 ? t('storage.never') : t('storage.days', { count: number(r.value) }) },
        ]} /></>}
      </section>
    </>}
  </div>;
}
