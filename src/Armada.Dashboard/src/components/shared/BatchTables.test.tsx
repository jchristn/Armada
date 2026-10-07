import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CaptainToolViewer from '../captains/CaptainToolViewer';
import WorkflowCommandPreview from './WorkflowCommandPreview';
import TemplateVariableHelp from '../fleetActions/TemplateVariableHelp';
import VesselPickerModal from '../fleetActions/VesselPickerModal';
import VesselHealthDetailModal from '../vessels/health/VesselHealthDetailModal';
import { getVesselHealth, listFleets, listVessels } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { CaptainToolAccessResult } from '../../types/models';

vi.mock('../../api/client', () => ({
  listVessels: vi.fn(),
  listFleets: vi.fn(),
  getVesselHealth: vi.fn(),
  setVesselHealthOverride: vi.fn(),
  deleteVesselHealthOverride: vi.fn(),
}));
// Stable across renders (components keep `t` in effect dependencies).
const localeValue = {
  t: (text: string, params?: Record<string, string | number>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));

async function openChooser() {
  await act(async () => { fireEvent.click(screen.getAllByRole('button', { name: /^Columns/ })[0]); });
}

describe('CaptainToolViewer tables', () => {
  beforeEach(() => localStorage.clear());

  const data = {
    summary: 'ok', toolsAccessible: true, availabilityVerified: true, runtime: 'ClaudeCode', configuredServerCount: 1, reachableServerCount: 1,
    endpointName: null, effectiveToolCount: 1,
    servers: [{
      name: 'docs', sourceKind: 'McpServer', transport: 'http', url: 'https://mcp.example.com/a/very/long/endpoint/path/that/should/not/wrap',
      command: null, target: null, reachable: true, enabled: true, status: 'Reachable', toolCount: 3, workingDirectory: null,
      startupTimeoutSeconds: 0, toolTimeoutSeconds: 0, headerCount: 0, environmentVariableCount: 0, enabledToolFilterCount: 0, disabledToolFilterCount: 0, errorMessage: null,
    }],
    tools: [{ name: 'search', sourceKind: 'McpServer', registrationSource: 'docs', description: 'Search the docs' }],
  } as unknown as CaptainToolAccessResult;

  it('shows the endpoint on one line with the full value in its title, and locks Source and Tool', async () => {
    render(<CaptainToolViewer open captainName="cap" loading={false} error="" data={data} onClose={() => undefined} />);
    const endpoint = screen.getByText('https://mcp.example.com/a/very/long/endpoint/path/that/should/not/wrap');
    expect(endpoint).toHaveClass('cell-one-line');
    expect(endpoint).toHaveAttribute('title', 'https://mcp.example.com/a/very/long/endpoint/path/that/should/not/wrap');
    await openChooser();
    expect(screen.getByRole('menuitemcheckbox', { name: /Source/ })).toHaveAttribute('aria-disabled', 'true');
  });

  it('keeps separate column choices for each table', () => {
    localStorage.setItem('armada_columns_captain-tools-mcp-servers', JSON.stringify({ hidden: ['notes'], version: 0 }));
    render(<CaptainToolViewer open captainName="cap" loading={false} error="" data={data} onClose={() => undefined} />);
    expect(screen.queryByRole('columnheader', { name: 'Notes' })).not.toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Description' })).toBeInTheDocument();
  });
});

describe('WorkflowCommandPreview', () => {
  beforeEach(() => localStorage.clear());

  it('renders through the shared table with Check Type and Command locked and no record bar', async () => {
    const { container } = render(<WorkflowCommandPreview commands={[{ checkType: 'Build', environmentName: null, command: 'dotnet build' }] as never} />);
    expect(container.querySelector('.pagination-records')).toBeNull();
    expect(screen.getByText('Base')).toBeInTheDocument();
    await openChooser();
    expect(screen.getByRole('menuitemcheckbox', { name: /Check Type/ })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('menuitemcheckbox', { name: /Command/ })).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('TemplateVariableHelp', () => {
  it('renders the variables through the shared table without a chooser (both columns are identity)', () => {
    const onInsert = vi.fn();
    render(<TemplateVariableHelp defaultOpen onInsert={onInsert} />);
    expect(screen.getByRole('columnheader', { name: 'Variable' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Columns/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: /^Insert/ })[0]);
    expect(onInsert).toHaveBeenCalledTimes(1);
  });
});

describe('VesselPickerModal', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(listVessels).mockResolvedValue({ objects: [{ id: 'vsl_1', name: 'gateway', fleetId: 'flt_1', workingDirectory: '/src/gateway' }] } as never);
    vi.mocked(listFleets).mockResolvedValue({ objects: [{ id: 'flt_1', name: 'Core' }] } as never);
  });

  it('selects through the shared selection column and locks Name', async () => {
    const onPicked = vi.fn();
    render(<VesselPickerModal open onClose={() => undefined} onPicked={onPicked} />);
    fireEvent.click(await screen.findByLabelText('Select gateway'));
    expect(document.querySelector('td[data-col="fleet"]')).toHaveTextContent('Core');
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(onPicked).toHaveBeenCalledWith(['vsl_1']);
    await openChooser();
    expect(screen.getByRole('menuitemcheckbox', { name: /Name/ })).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('VesselHealthDetailModal tables', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(getVesselHealth).mockResolvedValue({
      health: { vesselId: 'vsl_1', vesselName: 'gateway', overallStatus: 'Warn' },
      findings: [{ vesselId: 'vsl_1', criterion: 'Readme', status: 'Fail' }],
      dependencies: [{ vesselId: 'vsl_1', ecosystem: 'NuGet', projectPath: 'src/App/App.csproj', packageName: 'Newtonsoft.Json', currentVersion: '12.0.1', latestVersion: '13.0.3', drift: 'Major' }],
      overrides: [{ vesselId: 'vsl_1', criterion: 'Readme', status: 'Pass', note: 'Docs live in the wiki' }],
    } as never);
  });

  it('puts the override note in the details tooltip instead of a second line', async () => {
    render(<MemoryRouter><VesselHealthDetailModal vesselId="vsl_1" canAdmin={false} onClose={() => undefined} initialSection="findings" /></MemoryRouter>);
    const row = (await screen.findByRole('columnheader', { name: 'Criterion' })).closest('table')!.querySelectorAll('tbody tr')[0];
    const details = row.querySelector('td[data-col="details"]') as HTMLElement;
    expect(details).toHaveAttribute('title', 'Note: Docs live in the wiki');
    expect(details.querySelectorAll('.vh-note')).toHaveLength(0);
  });

  it('truncates the dependency project path on one line and locks Package', async () => {
    render(<MemoryRouter><VesselHealthDetailModal vesselId="vsl_1" canAdmin={false} onClose={() => undefined} initialSection="dependencies" /></MemoryRouter>);
    const path = await screen.findByText('src/App/App.csproj');
    expect(path).toHaveClass('cell-one-line');
    expect(path).toHaveAttribute('title', 'src/App/App.csproj');
    await openChooser();
    expect(screen.getByRole('menuitemcheckbox', { name: /Package/ })).toHaveAttribute('aria-disabled', 'true');
  });
});
