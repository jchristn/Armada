import { describe, expect, it } from 'vitest';
import { baseTitle, branchLabel, countRunning, stageLabel, treeIsInformative, treeRows } from './missionTree';
import type { MissionSummary, MissionTreeNode } from '../types/models';

const m = (id: string, title: string, status: string, extra: Partial<MissionSummary> = {}) => ({ id, title, status, parentMissionId: null, persona: null, ...extra }) as MissionSummary;
const n = (mission: MissionSummary, stages: MissionSummary[] = [], children: MissionTreeNode[] = []): MissionTreeNode => ({ mission, stages, children });

describe('mission tree helpers', () => {
  const tree = [n(m('i', 'Docs [Worker]', 'Pending', { waitForVoyageWorkers: true })),
    n(m('p', 'Schema [Worker]', 'Complete'), [m('pj', 'Schema [Judge]', 'InProgress')], [n(m('c', 'API', 'Pending', { parentMissionId: 'p', branchStrategy: 'ForkFromParent' }))])];

  it('flattens depth first with the integration mission last', () => {
    expect(treeRows(tree).map(r => `${r.node.mission.id}@${r.depth}`)).toEqual(['p@0', 'c@1', 'i@0']);
  });

  it('labels titles, stages and branches', () => {
    expect(baseTitle('Schema [Worker]')).toBe('Schema');
    expect(baseTitle('[Judge]')).toBe('[Judge]');
    expect(stageLabel(m('x', 'Schema [Judge]', 'Pending'))).toBe('Judge');
    expect(stageLabel(m('x', 'Plain', 'Pending', { persona: 'Worker' }))).toBe('Worker');
    expect(branchLabel(tree[1].children[0].mission)).toBe('forks from parent');
    expect(branchLabel(tree[0].mission)).toBe('integration');
    expect(branchLabel(tree[1].mission)).toBeNull();
  });

  it('counts running logical missions and decides when to show the tree', () => {
    expect(countRunning(tree)).toBe(1);
    expect(treeIsInformative({ voyageId: 'v', title: 't', totalMissions: 1, truncated: false, maxDepth: 0, roots: [n(m('a', 'A', 'Pending'))] })).toBe(false);
    expect(treeIsInformative(null)).toBe(false);
  });
});
