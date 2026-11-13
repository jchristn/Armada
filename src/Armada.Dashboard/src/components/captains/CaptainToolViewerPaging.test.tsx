import { describe, expect, it, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import CaptainToolViewer from './CaptainToolViewer';
import { getCaptainTools } from '../../api/client';
import type { CaptainToolAccessResult, CaptainToolSummary } from '../../types/models';

vi.mock('../../api/client', () => ({ getCaptainTools: vi.fn() }));
import { translateTemplate } from '../../i18n/runtime';

// Stable across renders (components keep `t` in effect dependencies).
const localeValue = {
  t: (text: string, params?: Record<string, string | number>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

function tool(name: string, sourceKind: CaptainToolSummary['sourceKind']): CaptainToolSummary {
  return { name, sourceKind, registrationSource: sourceKind === 'McpServer' ? 'docs' : '', description: 'Tool description', inputSchemaJson: null } as CaptainToolSummary;
}

function result(objects: CaptainToolSummary[], pageNumber: number, totalRecords: number, pageSize = 25): CaptainToolAccessResult {
  return {
    captainId: 'cpt_1', captainName: 'cap', runtime: 'ClaudeCode', toolsAccessible: true, availabilityVerified: true, availabilitySource: 'probe',
    summary: 'ok', endpointName: null, toolsEnabled: null, effectiveToolCount: totalRecords, armadaToolCount: 0, configuredServerCount: 1, reachableServerCount: 1,
    servers: [], toolCount: totalRecords, mcpToolCount: totalRecords, runtimeBuiltInToolCount: 0, snapshotUtc: '2026-01-01T00:00:00Z',
    tools: { success: true, pageNumber, pageSize, totalPages: Math.ceil(totalRecords / pageSize), totalRecords, totalMs: 0, objects },
  } as CaptainToolAccessResult;
}

describe('CaptainToolViewer tool paging', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(getCaptainTools).mockReset();
  });

  it('reads each tool section a page at a time with the true total, and pages on Next without refreshing', async () => {
    vi.mocked(getCaptainTools).mockImplementation(async (_id, query) => {
      if (query?.sourceKind === 'RuntimeBuiltIn') return result([], 1, 0);
      const page = query?.pageNumber ?? 1;
      return result([tool(`mcp_tool_page_${page}`, 'McpServer')], page, 60);
    });
    const summary = result([], 1, 60, 1);
    render(<CaptainToolViewer open captainName="cap" captainId="cpt_1" loading={false} error="" data={summary} onClose={() => undefined} />);

    expect(await screen.findByText('mcp_tool_page_1')).toBeInTheDocument();
    expect(getCaptainTools).toHaveBeenCalledWith('cpt_1', { sourceKind: 'McpServer', pageNumber: 1, pageSize: 25 });
    expect(getCaptainTools).toHaveBeenCalledWith('cpt_1', { sourceKind: 'RuntimeBuiltIn', pageNumber: 1, pageSize: 25 });
    expect(screen.getByText(/60 records/)).toBeInTheDocument();

    await act(async () => { fireEvent.click(screen.getAllByRole('button', { name: /Next/ })[0]); });
    await waitFor(() => expect(screen.getByText('mcp_tool_page_2')).toBeInTheDocument());
    expect(getCaptainTools).toHaveBeenCalledWith('cpt_1', { sourceKind: 'McpServer', pageNumber: 2, pageSize: 25 });
    for (const call of vi.mocked(getCaptainTools).mock.calls) expect(call[1]?.refresh).toBeFalsy();
  });

  it('says so when a section page fails to load', async () => {
    vi.mocked(getCaptainTools).mockRejectedValue(new Error('down'));
    render(<CaptainToolViewer open captainName="cap" captainId="cpt_1" loading={false} error="" data={result([], 1, 5, 1)} onClose={() => undefined} />);
    await waitFor(() => expect(screen.getAllByText('Failed to load captain tools.').length).toBe(2));
  });
});
