import { useCallback, useEffect, useState } from 'react';
import { listBlackBoxObjects, readBlackBoxObject } from '../../api/client';
import type { BlackBoxObjectSummary } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { BLACKBOX_OBJECT_CHUNK_BYTES, blackBoxErrorCode, blackBoxErrorStatus, formatBlackBoxBytes } from '../../lib/blackbox';
import DialogShell from '../shared/DialogShell';
import LoadingIndicator from '../shared/LoadingIndicator';
import { copyToClipboard } from '../shared/CopyButton';

const OBJECT_PAGE_SIZE = 50;

interface ViewerState {
  object: BlackBoxObjectSummary;
  text: string;
  loaded: number;
  total: number;
  nextOffset: number | null;
  loading: boolean;
  error: string;
  binary: boolean;
}

/**
 * The entry's objects (record.json, the briefs and the evidence files) with a viewer that reads a large object in
 * 64 KB ranges: the first range opens, Load more appends the next one, so a long log never loads whole. A missing
 * object (the store lost it: ObjectMissing) is marked in the list and explained in the viewer.
 */
export default function BlackBoxEvidence({ entryId }: { entryId: string }) {
  const { t, formatDateTime } = useLocale();
  const [objects, setObjects] = useState<BlackBoxObjectSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [viewer, setViewer] = useState<ViewerState | null>(null);

  const load = useCallback(async (pageNumber: number) => {
    setLoading(true);
    try {
      const result = await listBlackBoxObjects(entryId, { pageNumber, pageSize: OBJECT_PAGE_SIZE });
      setObjects(result.objects ?? []);
      setTotal(result.totalRecords ?? (result.objects ?? []).length);
      setTotalPages(Math.max(1, result.totalPages ?? 1));
      setError('');
    } catch (err: unknown) {
      setError(t('The evidence list could not be loaded: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setLoading(false);
    }
  }, [entryId, t]);

  useEffect(() => { void load(page); }, [load, page]);

  const readRange = useCallback(async (object: BlackBoxObjectSummary, offset: number, previous: string) => {
    setViewer((v) => ({
      object, text: previous, loaded: v?.object.name === object.name ? v.loaded : 0, total: v?.object.name === object.name ? v.total : object.uncompressedBytes,
      nextOffset: v?.object.name === object.name ? v.nextOffset : null, loading: true, error: '', binary: false,
    }));
    try {
      const chunk = await readBlackBoxObject(entryId, object.name, { offset, length: BLACKBOX_OBJECT_CHUNK_BYTES });
      const binary = chunk.text === null;
      setViewer({
        object,
        text: binary ? previous : previous + (chunk.text ?? ''),
        loaded: offset + (chunk.body?.size ?? 0),
        total: chunk.totalBytes,
        nextOffset: chunk.nextOffset,
        loading: false,
        error: '',
        binary,
      });
    } catch (err: unknown) {
      const code = blackBoxErrorCode(err);
      const message = code === 'BlackBoxObjectMissing' || (blackBoxErrorStatus(err) === 410)
        ? t('This object is missing from the black box store (ObjectMissing). Its size and checksum are still recorded; the bytes cannot be read.')
        : code === 'BlackBoxObjectCorrupt'
          ? t('This object failed its checksum (BlackBoxObjectCorrupt) and was not shown.')
          : t('The object could not be read: {{message}}', { message: err instanceof Error ? err.message : String(err) });
      setViewer((v) => (v ? { ...v, loading: false, error: message } : v));
    }
  }, [entryId, t]);

  function open(object: BlackBoxObjectSummary) {
    setViewer({ object, text: '', loaded: 0, total: object.uncompressedBytes, nextOffset: null, loading: true, error: '', binary: false });
    void readRange(object, 0, '');
  }

  return (
    <div>
      {loading && objects.length === 0 && <LoadingIndicator label={t('Loading evidence...')} />}
      {error && <div className="blackbox-notice blackbox-notice-error" role="alert">{error} <button type="button" className="btn btn-sm" onClick={() => void load(page)}>{t('Retry')}</button></div>}
      {!loading && !error && objects.length === 0 && <p className="text-dim">{t('This entry has no stored objects.')}</p>}
      {objects.length > 0 && (
        <div className="table-wrap">
          <table className="table-dense" aria-label={t('Evidence objects')}>
            <thead>
              <tr>
                <th>{t('Name')}</th>
                <th>{t('Type')}</th>
                <th>{t('Size')}</th>
                <th>{t('Created')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {objects.map((o) => (
                <tr key={o.name} data-missing={o.isMissing ? 'true' : undefined}>
                  <td className="mono">{o.name}{o.isMissing && <span className="blackbox-chip blackbox-chip-danger" style={{ marginLeft: '0.4rem' }} title={t('The store lost this object; only its record remains.')}>{t('Missing')}</span>}</td>
                  <td className="text-dim">{o.contentType}</td>
                  <td>{formatBlackBoxBytes(o.uncompressedBytes)}</td>
                  <td className="text-dim">{formatDateTime(o.createdUtc)}</td>
                  <td><button type="button" className="btn btn-sm" onClick={() => open(o)} title={t('Open this object in a viewer that loads it in parts.')}>{t('View')}</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {totalPages > 1 && (
        <div className="flex gap-2" style={{ marginTop: '0.4rem', alignItems: 'center' }}>
          <button type="button" className="btn btn-sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>{t('Previous')}</button>
          <span className="text-dim">{t('Page {{page}} of {{pages}} ({{total}} objects)', { page, pages: totalPages, total })}</span>
          <button type="button" className="btn btn-sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>{t('Next')}</button>
        </div>
      )}

      <DialogShell
        open={!!viewer}
        title={viewer?.object.name ?? ''}
        subtitle={viewer ? t('{{loaded}} of {{total}} loaded', { loaded: formatBlackBoxBytes(Math.min(viewer.loaded, viewer.total)), total: formatBlackBoxBytes(viewer.total) }) : undefined}
        onClose={() => setViewer(null)}
        size="xl"
        className="blackbox-dialog"
        footer={viewer && (
          <>
            {viewer.text && <button type="button" className="btn" onClick={() => void copyToClipboard(viewer.text)} title={t('Copy the loaded part of this object to the clipboard.')}>{t('Copy loaded text')}</button>}
            {viewer.nextOffset !== null && !viewer.error && (
              <button type="button" className="btn btn-primary" disabled={viewer.loading} onClick={() => void readRange(viewer.object, viewer.nextOffset as number, viewer.text)} title={t('Read the next 64 KB of this object.')}>
                {t('Load more')}
              </button>
            )}
            <button type="button" className="btn" onClick={() => setViewer(null)}>{t('Close')}</button>
          </>
        )}
      >
        {viewer && (
          <>
            {viewer.error && <div className="blackbox-notice blackbox-notice-error" role="alert">{viewer.error}</div>}
            {viewer.binary && <p className="text-dim">{t('This object is binary; download the .zip to inspect it.')}</p>}
            {viewer.text && <pre className="blackbox-viewer-body" data-i18n-skip="true">{viewer.text}</pre>}
            {viewer.loading && <LoadingIndicator label={t('Loading...')} />}
          </>
        )}
      </DialogShell>
    </div>
  );
}
