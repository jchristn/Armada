import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getMission, listMissionSummaries } from '../../api/client';
import type { Mission, MissionSummary } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import StatusBadge from '../shared/StatusBadge';
import { baseTitle } from './VoyageMissionTree';

interface MissionHierarchyCardProps {
  mission: Pick<Mission, 'id' | 'parentMissionId' | 'lastUpdateUtc'>;
}

const MAX_ANCESTORS = 16;

/**
 * The mission's place in its hierarchy: the chain of parent missions it builds on (oldest first) and the child
 * missions that start once it finishes. Hidden for a mission with neither.
 */
export default function MissionHierarchyCard({ mission }: MissionHierarchyCardProps) {
  const { t } = useLocale();
  const [ancestors, setAncestors] = useState<Mission[]>([]);
  const [children, setChildren] = useState<MissionSummary[]>([]);
  const [childTotal, setChildTotal] = useState(0);

  const load = useCallback(async () => {
    const chain: Mission[] = [];
    const seen = new Set<string>([mission.id]);
    let parentId = mission.parentMissionId;
    while (parentId && !seen.has(parentId) && chain.length < MAX_ANCESTORS) {
      seen.add(parentId);
      const parent = await getMission(parentId).catch(() => null);
      if (!parent) break;
      chain.unshift(parent);
      parentId = parent.parentMissionId;
    }
    setAncestors(chain);
    try {
      const page = await listMissionSummaries({ pageNumber: 1, pageSize: 50, filters: { parentMissionId: mission.id, order: 'CreatedAscending' } });
      setChildren(page.objects || []);
      setChildTotal(page.totalRecords || 0);
    } catch {
      setChildren([]);
    }
  }, [mission.id, mission.parentMissionId]);

  useEffect(() => { void load(); }, [load, mission.lastUpdateUtc]);

  if (ancestors.length === 0 && children.length === 0) return null;

  return (
    <div className="card mission-hierarchy-card" style={{ marginBottom: '1rem' }} data-testid="mission-hierarchy">
      <h3 style={{ marginTop: 0 }} title={t('A child mission starts after its parent\'s last stage finishes and builds on its result.')}>{t('Mission Hierarchy')}</h3>
      {ancestors.length > 0 && (
        <nav className="mission-hierarchy-path" aria-label={t('Parent missions')}>
          {ancestors.map((a, i) => (
            <span key={a.id}>
              {i > 0 && <span className="text-dim" aria-hidden="true"> / </span>}
              <Link to={`/missions/${encodeURIComponent(a.id)}`} title={a.title}>{baseTitle(a.title)}</Link>
            </span>
          ))}
          <span className="text-dim" aria-hidden="true"> / </span>
          <span aria-current="page">{t('this mission')}</span>
        </nav>
      )}
      {children.length > 0 && (
        <>
          <h4 style={{ margin: '0.75rem 0 0.35rem' }}>{t('Children ({{count}})', { count: childTotal })}</h4>
          <ul className="mission-hierarchy-children">
            {children.map(child => (
              <li key={child.id}>
                <StatusBadge status={child.status} />{' '}
                <Link to={`/missions/${encodeURIComponent(child.id)}`} title={child.title}>{baseTitle(child.title)}</Link>
                {child.branchStrategy === 'ContinueParent' && <span className="mission-tree-branch">{t('continues parent')}</span>}
                {child.branchStrategy === 'ForkFromParent' && <span className="mission-tree-branch">{t('forks from parent')}</span>}
              </li>
            ))}
          </ul>
          {childTotal > children.length && <p className="text-dim">{t('Showing the first {{shown}} of {{count}} children.', { shown: children.length, count: childTotal })}</p>}
        </>
      )}
    </div>
  );
}
