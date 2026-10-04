import {
  buildDispatchArguments,
  buildFleetActionArguments,
  DEFAULT_QUICK_ACTIONS,
  filterQuickActions,
  mergeQuickActions,
  quickActionForm,
  validateDispatch,
  validateFleetAction,
} from './askQuickActions';

describe('quick action catalog', () => {
  it('keeps the server order, fills commands, and appends missing built-ins', () => {
    const merged = mergeQuickActions([{ name: 'status', toolName: 'status' }, { name: 'deploy', command: 'deploy', toolName: 'create_deployment' }]);
    expect(merged.map((a) => a.command)).toEqual(['/status', '/deploy', '/dispatch', '/fleet-action', '/health', '/import']);
    expect(mergeQuickActions(null)).toEqual(DEFAULT_QUICK_ACTIONS);
  });

  it('filters by the typed command and closes once arguments are typed', () => {
    expect(filterQuickActions(DEFAULT_QUICK_ACTIONS, '/').length).toBe(5);
    expect(filterQuickActions(DEFAULT_QUICK_ACTIONS, '/he').map((a) => a.name)).toEqual(['health']);
    expect(filterQuickActions(DEFAULT_QUICK_ACTIONS, '/dispatch now')).toEqual([]);
    expect(filterQuickActions(DEFAULT_QUICK_ACTIONS, 'hello')).toEqual([]);
  });

  it('maps actions to their inline form', () => {
    expect(DEFAULT_QUICK_ACTIONS.map(quickActionForm)).toEqual(['dispatch', 'fleet-action', 'none', 'none', 'import']);
  });
});

describe('dispatch form', () => {
  it('validates and builds MCP dispatch arguments', () => {
    expect(validateDispatch({ vesselId: '', title: '', missions: [{ title: '', description: '' }], pipelineId: '' })).toEqual({ vessel: 'Choose a vessel.', missions: 'Add at least one mission.' });
    expect(validateDispatch({ vesselId: 'v', title: '', missions: [{ title: '', description: 'x' }], pipelineId: '' })).toEqual({ missions: 'Every mission needs a title.' });
    expect(buildDispatchArguments({ vesselId: 'vsl_1', title: ' ', missions: [{ title: ' A ', description: '' }, { title: '', description: '' }], pipelineId: '' }))
      .toEqual({ title: 'A', vesselId: 'vsl_1', missions: [{ title: 'A', description: 'A' }] });
  });
});

describe('fleet action form', () => {
  it('validates and builds MCP run_fleet_action arguments', () => {
    expect(validateFleetAction({ actionId: '', vesselIds: [] })).toEqual({ action: 'Choose an action.', vessels: 'Choose at least one vessel.' });
    expect(buildFleetActionArguments({ actionId: 'fac_1', vesselIds: ['vsl_1', 'vsl_2'] })).toEqual({ actionId: 'fac_1', vesselIds: ['vsl_1', 'vsl_2'] });
  });
});
