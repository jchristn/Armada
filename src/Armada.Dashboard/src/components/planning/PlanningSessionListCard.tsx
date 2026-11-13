import { useState } from 'react';
import type { PlanningSession } from '../../types/models';
import StatusBadge from '../shared/StatusBadge';
import ActionMenu from '../shared/ActionMenu';
import DataTable, { type DataTableColumn } from '../shared/DataTable';

interface PlanningSessionListCardProps {
  t: (value: string, vars?: Record<string, string | number>) => string;
  sessions: PlanningSession[];
  /** Number of sessions on the server (every page); defaults to the loaded count. */
  totalCount?: number;
  /** Loads the next page of sessions; shown while fewer than totalCount are loaded. */
  onLoadMore?: () => void;
  loadingMore?: boolean;
  activeSessionId?: string;
  endingSessionId?: string | null;
  formatRelativeTime: (value: string) => string;
  resolveCaptainName: (captainId: string) => string;
  resolveVesselName: (vesselId: string) => string;
  resolvePipelineName: (pipelineId: string | null) => string;
  onSelect: (sessionId: string) => void;
  onEndSession: (session: PlanningSession) => void;
  onDeleteSession: (session: PlanningSession) => void;
  onDeleteAll: () => void;
  deletingAll?: boolean;
}

export default function PlanningSessionListCard(props: PlanningSessionListCardProps) {
  const {
    t,
    sessions,
    totalCount,
    onLoadMore,
    loadingMore,
    activeSessionId,
    endingSessionId,
    formatRelativeTime,
    resolveCaptainName,
    resolveVesselName,
    resolvePipelineName,
    onSelect,
    onEndSession,
    onDeleteSession,
    onDeleteAll,
    deletingAll,
  } = props;

  // Recent Sessions is a supporting list, not the primary workspace, so it starts collapsed.
  const [expanded, setExpanded] = useState(false);

  const columns: DataTableColumn<PlanningSession>[] = [
    {
      // The session ID used to be a second line under the title; it is its own one-line column now.
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique planning session ID.'), cellClassName: 'mono text-dim table-id-cell',
      render: (session) => <span className="cell-one-line planning-session-subtitle" title={session.id}>{session.id}</span>,
    },
    {
      key: 'title', label: t('Title'), required: true, headerTitle: t('Name of the planning session. Click a row to open its transcript.'),
      render: (session) => <div className="planning-session-title">{session.title}</div>,
    },
    { key: 'captain', label: t('Captain'), headerTitle: t('Captain reserved for this session. It takes no missions until the session ends.'), render: (session) => resolveCaptainName(session.captainId) },
    { key: 'vessel', label: t('Vessel'), headerTitle: t('Repository the session plans against.'), render: (session) => resolveVesselName(session.vesselId) },
    { key: 'pipeline', label: t('Pipeline'), headerTitle: t('Pipeline used when dispatching from this session. - means it is inherited from the vessel or fleet at dispatch time.'), render: (session) => resolvePipelineName(session.pipelineId) },
    { key: 'status', label: t('Status'), headerTitle: t('Active is ready for your next message, Responding is the captain replying, Stopping and Stopped mean it has ended, Failed means setup or a turn failed.'), cellClassName: 'cell-nowrap', render: (session) => <StatusBadge status={session.status} /> },
    { key: 'updated', label: t('Updated'), headerTitle: t('When the session last changed, such as a new message.'), cellClassName: 'cell-nowrap', render: (session) => formatRelativeTime(session.lastUpdateUtc) },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('End or delete a planning session.'), fixed: true, interactive: true, className: 'text-right',
      render: (session) => {
        const canEndSession = session.status === 'Active' || session.status === 'Responding';
        const ending = endingSessionId === session.id || session.status === 'Stopping';
        const items = [];
        if (canEndSession || ending) {
          items.push({
            label: ending ? t('Ending...') : t('End Session'),
            title: 'Stop the session, release the captain and reclaim its dock. The transcript is kept.',
            onClick: () => { if (!ending) onEndSession(session); },
          });
        }
        items.push({
          label: t('Delete'),
          title: 'Delete the session and its transcript. An active session is stopped first. Cannot be undone.',
          danger: true,
          onClick: () => onDeleteSession(session),
        });
        return (
          <div className="planning-session-actions" style={{ justifyContent: 'flex-end' }}>
            <ActionMenu id={`planning-session-${session.id}`} items={items} />
          </div>
        );
      },
    },
  ];

  return (
    <div className="card" style={{ padding: '1rem' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
        <button
          type="button"
          className="collapsible-header"
          style={{ flex: 1 }}
          onClick={() => setExpanded((prev) => !prev)}
          aria-expanded={expanded}
          title={expanded ? t('Hide the list of planning sessions.') : t('Show the list of planning sessions. Click one to open its transcript.')}
        >
          <span className="collapsible-caret" aria-hidden="true">{expanded ? '\u25BE' : '\u25B8'}</span>
          <span className="collapsible-title">{t('Recent Sessions')}</span>
          <span className="text-muted" style={{ marginLeft: 'auto' }}>{t('{{count}} total', { count: totalCount ?? sessions.length })}</span>
        </button>
        {(totalCount ?? sessions.length) > 0 && (
          <button
            type="button"
            className="btn btn-sm btn-danger"
            onClick={onDeleteAll}
            disabled={deletingAll}
            title={t('Delete every planning session and its transcript, stopping active ones first. You confirm first; cannot be undone.')}
          >
            {deletingAll ? t('Deleting...') : t('Delete All')}
          </button>
        )}
      </div>

      {expanded && (
        sessions.length === 0 ? (
          <p className="text-muted" style={{ marginTop: '0.75rem' }}>{t('No planning sessions yet.')}</p>
        ) : (
          <div style={{ marginTop: '0.75rem' }}>
            <DataTable
              tableKey="planning-sessions"
              className="planning-session-table"
              recordCount={null}
              rows={sessions}
              rowKey={(session) => session.id}
              onRowClick={(session) => onSelect(session.id)}
              rowClassName={(session) => `planning-session-row${activeSessionId === session.id ? ' is-active' : ''}`}
              columns={columns}
            />
            {onLoadMore && (totalCount ?? 0) > sessions.length && (
              <button
                type="button"
                className="btn btn-sm"
                style={{ marginTop: '0.5rem' }}
                onClick={onLoadMore}
                disabled={loadingMore}
                title={t('Load the next page of planning sessions.')}
              >
                {loadingMore ? t('Loading...') : t('Load more ({{shown}} of {{total}})', { shown: sessions.length, total: totalCount ?? sessions.length })}
              </button>
            )}
          </div>
        )
      )}
    </div>
  );
}
