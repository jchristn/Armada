import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import VoyageCascadeGraph from './VoyageCascadeGraph';
import { getVoyageBlackBoxCascade } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import { CASCADE_NODE_WIDTH, cascadeStatusClass, layoutCascade } from '../../lib/cascadeLayout';
import type { BlackBoxCascadeGraph } from '../../types/blackbox';

vi.mock('../../api/client', () => ({ getVoyageBlackBoxCascade: vi.fn() }));
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

const GRAPH: BlackBoxCascadeGraph = {
  voyageId: 'vyg_1', voyageTitle: 'Auth cleanup', voyageStatus: 'Failed', entryId: 'bbx_v', rootMissionId: 'msn_1', rootEntryId: 'bbx_1', missionCount: 4, truncated: false,
  nodes: [
    { missionId: 'msn_1', title: 'Schema change', status: 'Failed', failureKind: 'LandingConflict', entryId: 'bbx_1', isRoot: true },
    { missionId: 'msn_2', title: 'API update', status: 'Cancelled', dependsOnMissionId: 'msn_1', entryId: 'bbx_2', isRoot: false },
    { missionId: 'msn_3', title: 'Docs', status: 'Pending', dependsOnMissionId: 'msn_2', isRoot: false },
    { missionId: 'msn_4', title: 'Unrelated failure', status: 'Failed', failureKind: 'NoOp', entryId: 'bbx_4', isRoot: false },
  ],
  edges: [
    { fromMissionId: 'msn_1', toMissionId: 'msn_2', kind: 'CancelledBecause' },
    { fromMissionId: 'msn_2', toMissionId: 'msn_3', kind: 'BlockedBy' },
    { fromMissionId: 'msn_1', toMissionId: 'msn_4', kind: 'Sibling' },
  ],
};

function Where() {
  return <div data-testid="where">{useLocation().pathname}</div>;
}

function renderGraph() {
  return render(
    <MemoryRouter initialEntries={['/voyages/vyg_1']}>
      <Routes>
        <Route path="/voyages/:id" element={<VoyageCascadeGraph voyageId="vyg_1" />} />
        <Route path="*" element={<Where />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('layoutCascade', () => {
  it('puts dependents one column right of their dependency and draws every edge', () => {
    const layout = layoutCascade(GRAPH);
    const x = Object.fromEntries(layout.nodes.map((n) => [n.node.missionId, n.x]));
    expect(x.msn_2).toBeGreaterThan(x.msn_1);
    expect(x.msn_3).toBeGreaterThan(x.msn_2);
    expect(x.msn_4).toBe(x.msn_1);
    expect(layout.edges).toHaveLength(3);
    // The sibling edge stays in column 0, so it bends out to the right of the boxes.
    const sibling = layout.edges.find((e) => e.edge.kind === 'Sibling')!;
    expect(sibling.d).toContain(`M ${x.msn_1 + CASCADE_NODE_WIDTH}`);
    expect(layout.width).toBeGreaterThan(3 * CASCADE_NODE_WIDTH);
  });

  it('ignores edges to missing nodes and survives a dependency cycle', () => {
    const layout = layoutCascade({
      nodes: [{ missionId: 'a', status: 'Failed', dependsOnMissionId: 'b', isRoot: false }, { missionId: 'b', status: 'Failed', dependsOnMissionId: 'a', isRoot: false }],
      edges: [{ fromMissionId: 'a', toMissionId: 'zzz', kind: 'DependsOn' }],
    });
    expect(layout.nodes).toHaveLength(2);
    expect(layout.edges).toHaveLength(0);
  });

  it('classifies statuses', () => {
    expect(cascadeStatusClass('LandingFailed')).toBe('failed');
    expect(cascadeStatusClass('Blocked')).toBe('blocked');
    expect(cascadeStatusClass('Complete')).toBe('complete');
    expect(cascadeStatusClass('InProgress')).toBe('other');
  });
});

describe('VoyageCascadeGraph', () => {
  beforeEach(() => vi.mocked(getVoyageBlackBoxCascade).mockReset());

  it('renders a node per mission and an edge per link, with accessible labels and the root outlined', async () => {
    vi.mocked(getVoyageBlackBoxCascade).mockResolvedValue(GRAPH);
    const { container } = renderGraph();
    expect(await screen.findByRole('img', { name: 'Cascade graph of 4 missions and 3 edges' })).toBeInTheDocument();
    expect(container.querySelectorAll('g.cascade-node')).toHaveLength(4);
    expect(container.querySelectorAll('path[data-edge-kind]')).toHaveLength(3);
    expect(container.querySelector('path[data-edge-kind="CancelledBecause"]')).toHaveClass('cascade-edge-CancelledBecause');
    expect(container.querySelector('g[data-mission-id="msn_1"]')).toHaveClass('cascade-node-root', 'cascade-node-failed');
    expect(container.querySelector('g[data-mission-id="msn_3"]')).toHaveClass('cascade-node-blocked');
    expect(screen.getByLabelText('Schema change, Failed, LandingConflict, cascade root')).toHaveAttribute('href', '/blackbox/bbx_1');
    expect(screen.getByLabelText('Docs, Pending')).toHaveAttribute('href', '/missions/msn_3');
    expect(screen.getByText('Sibling failure')).toBeInTheDocument();
    // The list view names each incoming edge.
    expect(screen.getByText('Cancelled because: Schema change')).toBeInTheDocument();
    expect(screen.getByText('Blocked by: API update')).toBeInTheDocument();
  });

  it('opens the entry when a node is clicked', async () => {
    vi.mocked(getVoyageBlackBoxCascade).mockResolvedValue(GRAPH);
    renderGraph();
    fireEvent.click(await screen.findByLabelText('API update, Cancelled'));
    expect(screen.getByTestId('where')).toHaveTextContent('/blackbox/bbx_2');
  });

  it('notes a truncated graph', async () => {
    vi.mocked(getVoyageBlackBoxCascade).mockResolvedValue({ ...GRAPH, truncated: true });
    renderGraph();
    expect(await screen.findByText('The graph was cut: only the first 4 missions are shown.')).toBeInTheDocument();
  });

  it('shows the empty and error states', async () => {
    vi.mocked(getVoyageBlackBoxCascade).mockResolvedValueOnce({ ...GRAPH, nodes: [], edges: [] });
    const view = renderGraph();
    expect(await screen.findByText('No missions in this voyage')).toBeInTheDocument();
    view.unmount();
    vi.mocked(getVoyageBlackBoxCascade).mockRejectedValueOnce(new Error('Voyage not found'));
    renderGraph();
    expect(await screen.findByText('The cascade graph could not be loaded: Voyage not found')).toBeInTheDocument();
  });
});
