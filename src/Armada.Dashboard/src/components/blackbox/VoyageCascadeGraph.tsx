import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { getVoyageBlackBoxCascade } from '../../api/client';
import type { BlackBoxCascadeEdgeKind, BlackBoxCascadeGraph } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { CASCADE_NODE_HEIGHT, CASCADE_NODE_WIDTH, cascadeStatusClass, layoutCascade } from '../../lib/cascadeLayout';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';
import './BlackBox.css';

const EDGE_KINDS: BlackBoxCascadeEdgeKind[] = ['DependsOn', 'CancelledBecause', 'BlockedBy', 'Sibling'];

const EDGE_LABEL: Record<BlackBoxCascadeEdgeKind, string> = {
  DependsOn: 'Depends on',
  CancelledBecause: 'Cancelled because',
  BlockedBy: 'Blocked by',
  Sibling: 'Sibling failure',
  Unknown: 'Unknown',
};

const EDGE_TOOLTIP: Record<BlackBoxCascadeEdgeKind, string> = {
  DependsOn: 'The mission waits for the one it depends on.',
  CancelledBecause: 'The dependent mission stopped because its dependency failed.',
  BlockedBy: 'The dependent mission is held behind a failed dependency.',
  Sibling: 'Another independent failure in the voyage, linked from the cascade root.',
  Unknown: 'Unknown edge kind.',
};

function truncate(text: string, max: number): string {
  return text.length > max ? `${text.slice(0, max - 1)}…` : text;
}

/**
 * Cascade graph of a voyage: its missions (status, failure kind, black box entry) and the edges between them
 * (DependsOn, CancelledBecause, BlockedBy, Sibling), laid out left to right by dependency in SVG. The root (the
 * earliest failure with no failed dependency) is outlined; each node opens its black box entry or mission. A list of
 * the same nodes and edges follows for keyboard and screen reader use.
 */
export default function VoyageCascadeGraph({ voyageId }: { voyageId: string }) {
  const { t } = useLocale();
  const navigate = useNavigate();
  const markerBase = useId().replace(/:/g, '');
  const [graph, setGraph] = useState<BlackBoxCascadeGraph | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setGraph(await getVoyageBlackBoxCascade(voyageId));
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [voyageId]);
  useEffect(() => { void load(); }, [load]);

  const layout = useMemo(() => graph ? layoutCascade(graph) : null, [graph]);
  const titleOf = useMemo(() => new Map((graph?.nodes ?? []).map((n) => [n.missionId, n.title || n.missionId])), [graph]);

  if (loading && !graph) return <LoadingState />;
  if (error) return <ErrorState message={t('The cascade graph could not be loaded: {{message}}', { message: error })} onRetry={() => void load()} />;
  if (!graph || !layout || graph.nodes.length === 0) return <EmptyState title={t('No missions in this voyage')}>{t('The cascade graph shows the voyage\'s missions once it has some.')}</EmptyState>;

  const nodeHref = (entryId?: string | null, missionId?: string) => entryId ? `/blackbox/${encodeURIComponent(entryId)}` : `/missions/${encodeURIComponent(missionId ?? '')}`;

  return (
    <div data-testid="voyage-cascade">
      <div className="cascade-legend" aria-label={t('Edge legend')}>
        {EDGE_KINDS.map((k) => (
          <span key={k} title={t(EDGE_TOOLTIP[k])}>
            <svg width="28" height="10" aria-hidden="true"><line x1="0" y1="5" x2="28" y2="5" className={`cascade-edge cascade-edge-${k}`} /></svg>
            {t(EDGE_LABEL[k])}
          </span>
        ))}
        {graph.rootMissionId && <span>{t('Thick outline: cascade root')}</span>}
      </div>
      <div className="cascade-graph">
        <svg width={layout.width} height={layout.height} viewBox={`0 0 ${layout.width} ${layout.height}`} role="img" aria-label={t('Cascade graph of {{count}} missions and {{edges}} edges', { count: graph.nodes.length, edges: graph.edges.length })}>
          <defs>
            {EDGE_KINDS.map((k) => (
              <marker key={k} id={`${markerBase}-${k}`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
                <path d="M 0 0 L 10 5 L 0 10 z" className={`cascade-edge cascade-edge-${k}`} style={{ strokeDasharray: 'none' }} />
              </marker>
            ))}
          </defs>
          {layout.edges.map(({ edge, d }) => (
            <path key={`${edge.fromMissionId}|${edge.toMissionId}|${edge.kind}`} d={d} className={`cascade-edge cascade-edge-${edge.kind}`} markerEnd={`url(#${markerBase}-${edge.kind})`} data-edge-kind={edge.kind}>
              <title>{`${titleOf.get(edge.fromMissionId)} -> ${titleOf.get(edge.toMissionId)}: ${t(EDGE_LABEL[edge.kind] ?? edge.kind)}`}</title>
            </path>
          ))}
          {layout.nodes.map(({ node, x, y }) => {
            const label = `${node.title || node.missionId}, ${t(node.status)}${node.failureKind ? `, ${t(node.failureKind)}` : ''}${node.isRoot ? `, ${t('cascade root')}` : ''}`;
            const href = nodeHref(node.entryId, node.missionId);
            return (
              <g key={node.missionId} className={`cascade-node cascade-node-${cascadeStatusClass(node.status)}${node.isRoot ? ' cascade-node-root' : ''}`} data-mission-id={node.missionId}>
                <a href={href} aria-label={label} onClick={(e) => { e.preventDefault(); navigate(href); }}>
                  <title>{label}</title>
                  <rect x={x} y={y} width={CASCADE_NODE_WIDTH} height={CASCADE_NODE_HEIGHT} rx={8} />
                  <text x={x + 10} y={y + 22}>{truncate(node.title || node.missionId, 28)}</text>
                  <text x={x + 10} y={y + 42} className="cascade-node-sub">{truncate(`${t(node.status)}${node.failureKind ? ` · ${t(node.failureKind)}` : ''}`, 32)}</text>
                </a>
              </g>
            );
          })}
        </svg>
      </div>
      {graph.truncated && <p className="text-dim" style={{ fontSize: '0.8rem' }}>{t('The graph was cut: only the first {{count}} missions are shown.', { count: graph.nodes.length })}</p>}
      <details style={{ marginTop: '0.5rem' }}>
        <summary>{t('Missions and edges as a list')}</summary>
        <ul className="blackbox-list" aria-label={t('Cascade missions')}>
          {graph.nodes.map((n) => (
            <li key={n.missionId}>
              <Link to={`/missions/${encodeURIComponent(n.missionId)}`}>{n.title || n.missionId}</Link> <span className="text-dim">· {t(n.status)}{n.failureKind ? ` · ${t(n.failureKind)}` : ''}</span>
              {n.isRoot && <> <span className="blackbox-chip blackbox-chip-danger">{t('Cascade root')}</span></>}
              {n.entryId && <> · <Link to={`/blackbox/${encodeURIComponent(n.entryId)}`} className="mono">{n.entryId}</Link></>}
              {graph.edges.filter((e) => e.toMissionId === n.missionId).map((e) => (
                <div key={`${e.fromMissionId}|${e.kind}`} className="text-dim" style={{ fontSize: '0.8rem' }}>{t(EDGE_LABEL[e.kind] ?? e.kind)}: {titleOf.get(e.fromMissionId)}</div>
              ))}
            </li>
          ))}
        </ul>
      </details>
    </div>
  );
}
