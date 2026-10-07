import { useState } from 'react';
import type { PlanningSession } from '../../types/models';
import StatusBadge from '../shared/StatusBadge';
import ActionMenu from '../shared/ActionMenu';
import DataTable, { type DataTableColumn } from '../shared/DataTable';

interface PlanningSessionListCardProps {
  t: (value: string, vars?: Record<string, string | number>) => string;
  sessions: PlanningSession[];
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
      key: 'title', label: t('Title'), required: true,
      render: (session) => <div className="planning-session-title">{session.title}</div>,
    },
    {
      // The session ID used to be a second line under the title; it is its own one-line column now.
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (session) => <span className="cell-one-line planning-session-subtitle" title={session.id}>{session.id}</span>,
    },
    { key: 'captain', label: t('Captain'), render: (session) => resolveCaptainName(session.captainId) },
    { key: 'vessel', label: t('Vessel'), render: (session) => resolveVesselName(session.vesselId) },
    { key: 'pipeline', label: t('Pipeline'), render: (session) => resolvePipelineName(session.pipelineId) },
    { key: 'status', label: t('Status'), cellClassName: 'cell-nowrap', render: (session) => <StatusBadge status={session.status} /> },
    { key: 'updated', label: t('Updated'), cellClassName: 'cell-nowrap', render: (session) => formatRelativeTime(session.lastUpdateUtc) },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (session) => {
        const canEndSession = session.status === 'Active' || session.status === 'Responding';
        const ending = endingSessionId === session.id || session.status === 'Stopping';
        const items = [];
        if (canEndSession || ending) {
          items.push({
            label: ending ? t('Ending...') : t('End Session'),
            onClick: () => { if (!ending) onEndSession(session); },
          });
        }
        items.push({
          label: t('Delete'),
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
        >
          <span className="collapsible-caret" aria-hidden="true">{expanded ? '\u25BE' : '\u25B8'}</span>
          <span className="collapsible-title">{t('Recent Sessions')}</span>
          <span className="text-muted" style={{ marginLeft: 'auto' }}>{t('{{count}} total', { count: sessions.length })}</span>
        </button>
        {sessions.length > 0 && (
          <button
            type="button"
            className="btn btn-sm btn-danger"
            onClick={onDeleteAll}
            disabled={deletingAll}
            title={t('Delete all planning sessions')}
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
          </div>
        )
      )}
    </div>
  );
}
