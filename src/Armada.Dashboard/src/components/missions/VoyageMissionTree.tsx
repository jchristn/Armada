import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getVoyageTree } from '../../api/client';
import type { MissionTreeNode, VoyageTree } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useLiveRefresh } from '../../lib/useLiveRefresh';
import { baseTitle, countRunning, hasPipeline, orderedNodes as ordered, stageLabel, treeIsInformative } from '../../lib/missionTree';
import StatusBadge from '../shared/StatusBadge';

interface VoyageMissionTreeProps {
  voyageId: string;
  /** The vessel's MaxParallelMissionsPerVoyage (0 is unlimited), or undefined when not known. */
  missionLimit?: number;
}

export { baseTitle, countRunning, treeIsInformative } from '../../lib/missionTree';

/**
 * The voyage's missions as logical missions: each row is a mission with its pipeline stages as pills and how it
 * branches from its parent; children are indented under their parent. Refreshes on mission and voyage events.
 */
export default function VoyageMissionTree({ voyageId, missionLimit }: VoyageMissionTreeProps) {
  const { t } = useLocale();
  const [tree, setTree] = useState<VoyageTree | null>(null);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    try {
      setTree(await getVoyageTree(voyageId));
      setError('');
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [voyageId]);

  useEffect(() => { void load(); }, [load]);
  useLiveRefresh(['mission.', 'voyage.'], load);

  if (error && !tree) return <p className="text-dim" role="status">{t('Mission tree unavailable: {{message}}', { message: error })}</p>;
  if (!treeIsInformative(tree)) return null;
  const running = countRunning(tree!.roots);

  const renderNode = (node: MissionTreeNode) => {
    const strategy = node.mission.branchStrategy;
    const isChild = !!node.mission.parentMissionId;
    return (
      <li key={node.mission.id} className="mission-tree-node" data-mission-id={node.mission.id}>
        <div className="mission-tree-row">
          <StatusBadge status={node.mission.status} />
          <Link to={`/missions/${encodeURIComponent(node.mission.id)}`} className="mission-tree-title" title={node.mission.title}>{baseTitle(node.mission.title)}</Link>
          {hasPipeline(node) && (
            <span className="mission-tree-stages" aria-label={t('Pipeline stages')}>
              {[node.mission, ...node.stages].map((stage, index) => (
                <span key={stage.id} className="mission-tree-stage-step">
                  {index > 0 && <span className="text-dim" aria-hidden="true">&rarr;</span>}
                  <Link to={`/missions/${encodeURIComponent(stage.id)}`} className={`tag ${stage.status.toLowerCase()} mission-tree-stage`}
                    title={t('{{stage}} stage: {{status}}', { stage: stageLabel(stage), status: t(stage.status) })}>
                    {t(stageLabel(stage))}
                  </Link>
                </span>
              ))}
            </span>
          )}
          {isChild && strategy === 'ContinueParent' && (
            <span className="mission-tree-branch" title={t('Works on its parent mission\'s branch, after the parent finished.')}>{t('continues parent')}</span>
          )}
          {isChild && strategy === 'ForkFromParent' && (
            <span className="mission-tree-branch" title={t('Starts a new branch from the head of its parent mission\'s branch.')}>{t('forks from parent')}</span>
          )}
          {node.mission.waitForVoyageWorkers && (
            <span className="mission-tree-branch" title={t('Starts after every other mission in the voyage has settled.')}>{t('integration')}</span>
          )}
        </div>
        {node.children.length > 0 && <ul className="mission-tree-children">{ordered(node.children).map(renderNode)}</ul>}
      </li>
    );
  };

  return (
    <div className="card mission-tree-card" style={{ marginBottom: 20 }} data-testid="voyage-mission-tree">
      <div className="readiness-panel-header">
        <div>
          <h3 title={t('Logical missions with their pipeline stages. A child starts after its parent\'s last stage finishes and builds on its result; missions at the same level run in parallel.')}>{t('Mission Tree')}</h3>
          <div className="readiness-panel-meta">
            {t('{{count}} missions, {{depth}} levels deep', { count: tree!.totalMissions, depth: tree!.maxDepth + 1 })}
          </div>
        </div>
        <span className="readiness-pill ready" title={t('Logical missions of this voyage running now, against the vessel\'s limit on missions running at once per voyage.')}>
          {missionLimit === undefined
            ? t('{{running}} running', { running })
            : missionLimit === 0
              ? t('{{running}} running (no limit)', { running })
              : t('{{running}} / {{limit}} running', { running, limit: missionLimit })}
        </span>
      </div>
      {tree!.truncated && (
        <p className="text-dim" role="status">{t('Showing the oldest 1000 of {{count}} missions; the table below lists them all.', { count: tree!.totalMissions })}</p>
      )}
      <ul className="mission-tree">{ordered(tree!.roots).map(renderNode)}</ul>
    </div>
  );
}
