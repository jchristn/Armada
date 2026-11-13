import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import * as client from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import Endpoints from './Endpoints';
import { asPage } from '../test/asPage';

vi.mock('../api/client', () => ({
  createCaptain: vi.fn(), listModelEndpoints: vi.fn(), enumerateModelEndpoints: vi.fn(), duplicateModelEndpoint: vi.fn(), createModelEndpoint: vi.fn(), updateModelEndpoint: vi.fn(), deleteModelEndpoint: vi.fn(),
  validateModelEndpoint: vi.fn(), healthCheckModelEndpoints: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ isAdmin: true, isTenantAdmin: true, user: { user: { id: 'usr_1', tenantId: 'ten_1' } } }),
}));

const STORED = {
  id: 'mep_1', name: 'tunnel-ollama', enabled: true, kind: 'Inference', provider: 'Ollama', model: 'llama3',
  baseUrl: 'https://ollama.example.com', scope: 'TenantWide', tenantId: 'ten_1', userId: 'usr_1',
  healthStatus: 'Healthy', healthHistory: [], lastHealthCheckUtc: null, hasApiKey: true,
  authType: 'Bearer', authUsername: null, authHeaderName: null, authQueryParameter: null,
  headers: [
    { name: 'CF-Access-Client-Id', value: null, hasValue: true },
    { name: 'CF-Access-Client-Secret', value: null, hasValue: true },
  ],
  dimensionality: 0, timeoutMs: 120000,
};

beforeEach(() => {
  localStorage.clear();
  vi.mocked(client.listModelEndpoints).mockResolvedValue([STORED] as never);
  vi.mocked(client.enumerateModelEndpoints).mockImplementation((async () => asPage(await client.listModelEndpoints())) as never);
  vi.mocked(client.updateModelEndpoint).mockResolvedValue(STORED as never);
  vi.mocked(client.createModelEndpoint).mockResolvedValue(STORED as never);
});

afterEach(() => vi.clearAllMocks());

async function openEdit() {
  render(<MemoryRouter><Endpoints /></MemoryRouter>);
  fireEvent.click(await screen.findByText('tunnel-ollama', { selector: 'strong' }));
  return await screen.findByRole('heading', { name: 'Edit Endpoint' });
}

const PROBE = { success: true, baseUrl: STORED.baseUrl, latencyMs: 42, statusCode: 200, error: null, embeddingDimensions: null, sampleText: 'pong', timestampUtc: '2026-10-19T00:00:00Z' };

async function startValidation() {
  render(<MemoryRouter><Endpoints /></MemoryRouter>);
  await screen.findByText('tunnel-ollama', { selector: 'strong' });
  fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
  fireEvent.click(screen.getByRole('menuitem', { name: 'Validate' }));
  return screen.getByRole('dialog', { name: 'Validation Result' });
}

describe('Endpoint validation progress', () => {
  it('opens immediately with progress and replaces it with the result', async () => {
    let resolve!: (result: typeof PROBE) => void;
    vi.mocked(client.validateModelEndpoint).mockImplementation(() => new Promise(done => { resolve = done; }));
    const dialog = await startValidation();
    expect(within(dialog).getByRole('status')).toHaveTextContent('Validating...');
    expect(within(dialog).queryByText('Unhealthy')).not.toBeInTheDocument();
    expect(client.validateModelEndpoint).toHaveBeenCalledWith('mep_1');
    await act(async () => resolve(PROBE));
    expect(within(dialog).queryByText('Validating...')).not.toBeInTheDocument();
    expect(within(dialog).getByText('pong')).toBeInTheDocument();
    expect(within(dialog).getByText('42 ms')).toBeInTheDocument();
  });

  it('shows request errors in the validation dialog', async () => {
    let reject!: (error: Error) => void;
    vi.mocked(client.validateModelEndpoint).mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
    const dialog = await startValidation();
    await act(async () => reject(new Error('Provider timed out')));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Provider timed out');
    expect(within(dialog).queryByText('Validating...')).not.toBeInTheDocument();
  });

  it('keeps the menu label stable and does not reopen a dismissed dialog', async () => {
    let resolve!: (result: typeof PROBE) => void;
    vi.mocked(client.validateModelEndpoint).mockImplementation(() => new Promise(done => { resolve = done; }));
    const dialog = await startValidation();
    fireEvent.click(within(dialog).getAllByRole('button', { name: 'Close' })[0]);
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    expect(screen.getByRole('menuitem', { name: 'Validate' })).toBeDisabled();
    await act(async () => resolve(PROBE));
    expect(screen.queryByRole('dialog', { name: 'Validation Result' })).not.toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Validate' })).toBeEnabled();
  });
});

describe('Endpoints authentication', () => {
  it.each(['OllamaCloud', 'XAI'])('saves %s with bearer credentials and its URL rules', async (provider) => {
    await openEdit();
    fireEvent.change(screen.getByLabelText('Provider'), { target: { value: provider } });
    const auth = screen.getByLabelText('Authentication type');
    expect(Array.from(auth.querySelectorAll('option')).map(o => o.value)).toEqual(['ProviderDefault', 'Bearer']);
    if (provider === 'OllamaCloud') {
      expect(screen.getByLabelText('Base URL')).toBeDisabled();
      expect(screen.getByLabelText('Base URL')).toHaveValue('https://ollama.com:443');
    } else {
      fireEvent.change(screen.getByLabelText('Base URL'), { target: { value: '' } });
      expect(screen.getByLabelText('Base URL')).not.toBeRequired();
    }
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));
    await waitFor(() => expect(client.updateModelEndpoint).toHaveBeenCalled());
    expect(vi.mocked(client.updateModelEndpoint).mock.calls[0][1]).toMatchObject({ provider, authType: 'Bearer', baseUrl: provider === 'OllamaCloud' ? 'https://ollama.com:443' : '' });
    expect(vi.mocked(client.updateModelEndpoint).mock.calls[0][1]).not.toHaveProperty('apiKey');
  });

  it('summarizes the credential in the list', async () => {
    render(<MemoryRouter><Endpoints /></MemoryRouter>);
    expect(await screen.findByText('Bearer token, secret stored, 2 extra headers')).toBeInTheDocument();
  });

  it('shows the stored auth type and headers, and keeps stored values when left blank', async () => {
    await openEdit();
    expect(screen.getByLabelText('Authentication type')).toHaveValue('Bearer');
    expect(screen.getByLabelText('Bearer token')).toHaveAttribute('placeholder', '(unchanged - leave blank to keep stored key)');
    expect(screen.getByText('Sent as: Authorization: Bearer <secret>')).toBeInTheDocument();
    expect(screen.getByLabelText('Header 1 name')).toHaveValue('CF-Access-Client-Id');
    expect(screen.getByLabelText('Header 1 value')).toHaveAttribute('placeholder', '(stored - leave blank to keep stored value)');

    fireEvent.change(screen.getByLabelText('Header 2 value'), { target: { value: 'rotated' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));
    await waitFor(() => expect(client.updateModelEndpoint).toHaveBeenCalled());
    const payload = vi.mocked(client.updateModelEndpoint).mock.calls[0][1] as Record<string, unknown>;
    expect(payload.authType).toBe('Bearer');
    expect(payload).not.toHaveProperty('apiKey');
    expect(payload.headers).toEqual([
      { name: 'CF-Access-Client-Id', value: null },
      { name: 'CF-Access-Client-Secret', value: 'rotated' },
    ]);
  });

  it('switches the fields and the wire format with the auth type; None hides the secret', async () => {
    await openEdit();
    const type = screen.getByLabelText('Authentication type');
    fireEvent.change(type, { target: { value: 'Header' } });
    fireEvent.change(screen.getByLabelText('Header name'), { target: { value: 'X-API-Key' } });
    expect(screen.getByLabelText('Header value')).toBeInTheDocument();
    expect(screen.getByText('Sent as: X-API-Key: <secret>')).toBeInTheDocument();

    fireEvent.change(type, { target: { value: 'Basic' } });
    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'svc' } });
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
    expect(screen.getByText('Sent as: Authorization: Basic base64(svc:<secret>)')).toBeInTheDocument();

    fireEvent.change(type, { target: { value: 'None' } });
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
    expect(screen.getByText('No credential is sent. Extra headers below are still sent.')).toBeInTheDocument();
  });

  it('hides the auth type for signing providers but keeps extra headers', async () => {
    await openEdit();
    fireEvent.change(screen.getByLabelText('Provider'), { target: { value: 'Bedrock' } });
    expect(screen.queryByLabelText('Authentication type')).not.toBeInTheDocument();
    expect(screen.getByLabelText('AWS Secret Access Key')).toBeInTheDocument();
    expect(screen.getByLabelText('Header 1 name')).toBeInTheDocument();
  });

  it('adds and removes header rows', async () => {
    await openEdit();
    fireEvent.click(screen.getByRole('button', { name: '+ Add header' }));
    expect(screen.getByLabelText('Header 3 name')).toHaveValue('');
    fireEvent.click(screen.getAllByRole('button', { name: 'Remove' })[0]);
    expect(screen.getByLabelText('Header 1 name')).toHaveValue('CF-Access-Client-Secret');
    expect(screen.queryByLabelText('Header 3 name')).not.toBeInTheDocument();
  });
});

describe('Endpoint inference policy', () => {
  it('saves explicit tool restrictions, Harbor routing and generation settings', async () => {
    await openEdit();
    fireEvent.click(screen.getByLabelText('Read-only built-in tools'));
    fireEvent.click(screen.getByLabelText('Enable scoped MCP tools'));
    fireEvent.click(screen.getByLabelText('Require Harbor execution'));
    fireEvent.change(screen.getByLabelText('Allowed tool names (one per line; blank allows all)'), { target: { value: 'read_file\ngrep' } });
    fireEvent.change(screen.getByLabelText('Maximum model turns'), { target: { value: '8' } });
    fireEvent.change(screen.getByLabelText('Maximum output tokens'), { target: { value: '2048' } });
    fireEvent.change(screen.getByLabelText('Temperature'), { target: { value: '0' } });
    fireEvent.change(screen.getByLabelText('Context window (Ollama num_ctx)'), { target: { value: '32768' } });
    fireEvent.click(screen.getByLabelText('Stream responses'));
    fireEvent.change(screen.getByLabelText('First-response timeout (ms)'), { target: { value: '300000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));
    await waitFor(() => expect(client.updateModelEndpoint).toHaveBeenCalledWith('mep_1', expect.objectContaining({
      inferenceOptions: { toolsEnabled: true, readOnlyTools: true, mcpToolsEnabled: false, requireHarbor: true,
        allowedTools: ['read_file', 'grep'], maxIterations: 8, maxTokens: 2048, temperature: 0, topP: null,
        contextLength: 32768, maxConcurrentMissions: null, streaming: true, firstResponseTimeoutMs: 300000 },
    })));
  });

  it('preserves an empty tool allowlist when editing unrelated settings', async () => {
    vi.mocked(client.listModelEndpoints).mockResolvedValue([{ ...STORED, inferenceOptions: {
      toolsEnabled: true, readOnlyTools: false, mcpToolsEnabled: true, requireHarbor: false,
      allowedTools: [], maxIterations: 2, maxTokens: null, temperature: null, topP: null,
    } }] as never);
    await openEdit();
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));
    await waitFor(() => expect(client.updateModelEndpoint).toHaveBeenCalledWith('mep_1', expect.objectContaining({ inferenceOptions: expect.objectContaining({ allowedTools: [], maxIterations: 2 }) })));
  });
});


describe('Endpoint duplication', () => {
  async function openCopy() {
    render(<MemoryRouter><Endpoints /></MemoryRouter>);
    await screen.findByText('tunnel-ollama', { selector: 'strong' });
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Duplicate' }));
    await screen.findByRole('heading', { name: 'Duplicate Endpoint' });
  }

  it.each(['Ollama', 'OllamaCloud', 'XAI'])('opens an editable %s draft, preserving write-only credentials by reference until Save Copy', async (provider) => {
    vi.mocked(client.listModelEndpoints).mockResolvedValue([{ ...STORED, provider, baseUrl: provider === 'OllamaCloud' ? 'https://ollama.com:443' : STORED.baseUrl }] as never);
    vi.mocked(client.duplicateModelEndpoint).mockResolvedValue({ ...STORED, id: 'mep_copy' } as never);
    await openCopy();
    expect(client.duplicateModelEndpoint).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Name')).toHaveValue('tunnel-ollama (Copy)');
    expect(screen.getByLabelText('Bearer token')).toHaveValue('');
    expect(screen.getByLabelText('Bearer token')).toHaveAttribute('placeholder', '(unchanged - leave blank to keep stored key)');
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'new cloud model' } });
    fireEvent.change(screen.getByLabelText('Model'), { target: { value: 'new-model' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Copy' }));
    await waitFor(() => expect(client.duplicateModelEndpoint).toHaveBeenCalledWith('mep_1', expect.objectContaining({
      name: 'new cloud model', model: 'new-model', headers: [
        { name: 'CF-Access-Client-Id', value: null }, { name: 'CF-Access-Client-Secret', value: null },
      ],
    })));
    expect(vi.mocked(client.duplicateModelEndpoint).mock.calls[0][1]).not.toHaveProperty('apiKey');
    expect(client.updateModelEndpoint).not.toHaveBeenCalled();
    expect(client.createModelEndpoint).not.toHaveBeenCalled();
  });

  it('cancel leaves no copy behind', async () => {
    await openCopy();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(client.duplicateModelEndpoint).not.toHaveBeenCalled();
    expect(client.createModelEndpoint).not.toHaveBeenCalled();
  });
});


describe('Endpoint linked captain creation', () => {
  it('offers optional captain creation and uses the saved endpoint identity and custom name', async () => {
    await openEdit();
    expect(screen.queryByLabelText('Captain name')).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Create a captain for this endpoint'));
    fireEvent.change(screen.getByLabelText('Captain name'), { target: { value: 'Cloud Captain' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));
    await waitFor(() => expect(client.createCaptain).toHaveBeenCalledWith({ name: 'Cloud Captain', runtime: 'ApiEndpoint', modelEndpointId: STORED.id }));
  });

  it('preserves a newly saved endpoint when captain creation fails, and retries without creating another endpoint', async () => {
    vi.mocked(client.createCaptain).mockRejectedValueOnce(new Error('Capacity reached')).mockResolvedValueOnce({} as never);
    render(<MemoryRouter><Endpoints /></MemoryRouter>);
    await screen.findByText('tunnel-ollama', { selector: 'strong' });
    fireEvent.click(screen.getByRole('button', { name: '+ Endpoint' }));
    fireEvent.change(screen.getByLabelText('Base URL'), { target: { value: 'https://example.com' } });
    fireEvent.click(screen.getByLabelText('Create a captain for this endpoint'));
    fireEvent.click(screen.getByRole('button', { name: 'Create Endpoint' }));
    await screen.findByText(/Endpoint saved, but captain creation failed:/);
    expect(client.createModelEndpoint).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));
    await waitFor(() => expect(client.createCaptain).toHaveBeenCalledTimes(2));
    expect(client.createModelEndpoint).toHaveBeenCalledTimes(1);
    expect(client.createCaptain).toHaveBeenLastCalledWith({ name: STORED.name, runtime: 'ApiEndpoint', modelEndpointId: STORED.id });
  });

  it('hides captain creation for embedding endpoints', async () => {
    await openEdit();
    fireEvent.click(screen.getByLabelText('Create a captain for this endpoint'));
    fireEvent.change(screen.getByLabelText('Kind'), { target: { value: 'Embedding' } });
    expect(screen.queryByLabelText('Create a captain for this endpoint')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));
    await waitFor(() => expect(client.updateModelEndpoint).toHaveBeenCalled());
    expect(client.createCaptain).not.toHaveBeenCalled();
  });

  it('does not create a captain unless requested', async () => {
    await openEdit();
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));
    await waitFor(() => expect(client.updateModelEndpoint).toHaveBeenCalled());
    expect(client.createCaptain).not.toHaveBeenCalled();
  });
});
