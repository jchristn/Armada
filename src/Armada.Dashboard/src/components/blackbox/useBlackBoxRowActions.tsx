import { useState, type ReactElement } from 'react';
import { useNavigate } from 'react-router-dom';
import { deleteBlackBox, getBlackBox, getBlackBoxBrief } from '../../api/client';
import type { BlackBoxEntry, BlackBoxEntrySummary } from '../../types/blackbox';
import { useAuth } from '../../context/AuthContext';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import { blackBoxAskHref, blackBoxAskState, blackBoxEntryAskPrompt, blackBoxEntryBriefFileName } from '../../lib/blackbox';
import { useDeleteProgress } from '../../lib/useDeleteProgress';
import type { ActionMenuItem } from '../shared/ActionMenu';
import ConfirmDialog from '../shared/ConfirmDialog';
import JsonViewer from '../shared/JsonViewer';
import BlackBoxResolveDialog from './BlackBoxResolveDialog';
import { downloadBlackBoxMarkdown, reanalyzeBlackBoxEntry, toggleBlackBoxPin } from './blackBoxEntryActions';

/** What {@link useBlackBoxRowActions} returns. */
export interface BlackBoxRowActions {
  /** The row menu items, in order: View, Download Markdown, View JSON, Ask Armada, Resolve, Pin or Unpin, Reanalyze (tenant admins), Delete. */
  items: (row: BlackBoxEntrySummary) => ActionMenuItem[];
  /** The JSON viewer, resolve, confirm and delete progress dialogs; render once next to the table. */
  dialogs: ReactElement;
}

/**
 * Row actions of the black box entries table. Pin, Reanalyze and the Markdown download share their logic and toasts
 * with BlackBoxPanel (blackBoxEntryActions); Resolve opens the panel's BlackBoxResolveDialog on the full entry; Delete
 * confirms first and runs behind the shared delete progress modal. onChanged runs after every change that succeeded.
 */
export function useBlackBoxRowActions(onChanged: () => void): BlackBoxRowActions {
  const { t } = useLocale();
  const { isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const navigate = useNavigate();
  const deletion = useDeleteProgress();
  const [jsonEntry, setJsonEntry] = useState<BlackBoxEntry | null>(null);
  const [resolveEntry, setResolveEntry] = useState<BlackBoxEntry | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<BlackBoxEntrySummary | null>(null);

  function failed(err: unknown) {
    pushToast('error', t('Failed: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
  }

  async function viewJson(row: BlackBoxEntrySummary) {
    try {
      setJsonEntry(await getBlackBox(row.id));
    } catch (err: unknown) {
      failed(err);
    }
  }

  async function openResolve(row: BlackBoxEntrySummary) {
    try {
      setResolveEntry(await getBlackBox(row.id, false));
    } catch (err: unknown) {
      failed(err);
    }
  }

  function askArmada(row: BlackBoxEntrySummary) {
    const prompt = blackBoxEntryAskPrompt(row);
    navigate(blackBoxAskHref(prompt), { state: blackBoxAskState(prompt) });
  }

  async function togglePin(row: BlackBoxEntrySummary) {
    if (await toggleBlackBoxPin(row, t, pushToast)) onChanged();
  }

  async function reanalyze(row: BlackBoxEntrySummary) {
    if (await reanalyzeBlackBoxEntry(row.id, t('Reanalysis started. The table updates when it finishes.'), t, pushToast)) onChanged();
  }

  async function confirmDelete() {
    const row = deleteTarget;
    setDeleteTarget(null);
    if (!row) return;
    const deleted = await deletion.run({
      kind: 'blackBoxEntry',
      action: () => deleteBlackBox(row.id),
      errorMessage: (err) => (err instanceof Error && err.message ? err.message : t('Delete failed.')),
    });
    if (!deleted) return;
    pushToast('warning', t('Black box entry {{id}} deleted.', { id: row.id }));
    onChanged();
  }

  const items = (row: BlackBoxEntrySummary): ActionMenuItem[] => [
    { label: 'View', title: 'Open the black box entry page with every section, the brief and the attempts.', onClick: () => navigate(`/blackbox/${encodeURIComponent(row.id)}`) },
    { label: 'Download Markdown', title: 'Save the FIX THIS brief for a coding agent as a Markdown file.', onClick: () => { void downloadBlackBoxMarkdown(blackBoxEntryBriefFileName(row.id), () => getBlackBoxBrief(row.id), t, pushToast); } },
    { label: 'View JSON', title: 'Show the full black box entry as returned by the API.', onClick: () => { void viewJson(row); } },
    { label: 'Ask Armada', title: 'Open Ask Armada with a prompt about this failure; nothing is sent until you press Send.', onClick: () => askArmada(row) },
    { label: 'Resolve', title: 'Set the resolution of this entry, with a note and a link.', onClick: () => { void openResolve(row); } },
    row.pinned
      ? { label: 'Unpin', title: 'Let garbage collection expire this entry again.', onClick: () => { void togglePin(row); } }
      : { label: 'Pin', title: 'Keep this entry past its retention.', onClick: () => { void togglePin(row); } },
    ...(isTenantAdmin ? [{ label: 'Reanalyze', title: 'Run enrichment and analysis again with the current rules.', onClick: () => { void reanalyze(row); } }] : []),
    isTenantAdmin
      ? { label: 'Delete', title: 'Delete this entry with its stored objects, notes and links. Cannot be undone.', danger: true, onClick: () => setDeleteTarget(row) }
      : { label: 'Delete', title: 'Delete this entry with its stored objects, notes and links. Tenant admins only.', danger: true, disabled: true, onClick: () => {} },
  ];

  const dialogs = (
    <>
      <JsonViewer open={!!jsonEntry} title={t('Black box entry')} id={jsonEntry?.id} data={jsonEntry} onClose={() => setJsonEntry(null)} />
      {resolveEntry && (
        <BlackBoxResolveDialog
          open
          entry={resolveEntry}
          onClose={() => setResolveEntry(null)}
          onResolved={() => {
            setResolveEntry(null);
            pushToast('success', t('Resolution saved.'));
            onChanged();
          }}
        />
      )}
      <ConfirmDialog
        open={!!deleteTarget}
        title="Delete black box entry"
        message={t('Delete black box entry {{id}}? Its stored objects, notes and links are deleted too. This cannot be undone.', { id: deleteTarget?.id ?? '' })}
        confirmLabel="Delete"
        danger
        onConfirm={() => { void confirmDelete(); }}
        onCancel={() => setDeleteTarget(null)}
      />
      {deletion.dialog}
    </>
  );

  return { items, dialogs };
}
