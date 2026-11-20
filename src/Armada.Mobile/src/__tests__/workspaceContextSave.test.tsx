import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { configureClient } from '@dashboard/api/client';
import type { Vessel } from '@dashboard/types/models';
import { WorkspaceContextSheet } from '../screens/workspace/WorkspaceContextSheet';
import { BuildProviders } from '../test/buildFixtures';

const vessel = {
  id: 'vsl_ctx',
  name: 'kept-name',
  repoUrl: 'https://example.test/kept',
  projectContext: 'old project',
  styleGuide: 'old style',
  modelContext: 'old model',
} as Vessel;

describe('workspace context save', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
    configureClient({ baseUrl: '' });
  });

  it('saves the three context fields through the context patch', async () => {
    configureClient({ baseUrl: 'http://example.test' });
    const fetchMock = jest.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ({
        id: 'vsl_ctx',
        name: 'kept-name',
        repoUrl: 'https://example.test/kept',
        projectContext: 'old project',
        styleGuide: 'old style',
        modelContext: 'old model',
      }),
      text: async () => '',
    }));
    global.fetch = fetchMock as unknown as typeof fetch;

    const onSaved = jest.fn();
    await render(
      <BuildProviders>
        <WorkspaceContextSheet
          vessel={vessel}
          canAppend={false}
          buildSnippet={async () => null}
          onSaved={onSaved}
          onClose={jest.fn()}
        />
      </BuildProviders>,
    );

    fireEvent.press(screen.getByTestId('workspace-context-save'));
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());

    const url = String(fetchMock.mock.calls[0][0]);
    const init = fetchMock.mock.calls[0][1] as { method?: string; body?: string };
    expect(url).toBe('/api/v1/vessels/vsl_ctx/context');
    expect(init.method).toBe('PATCH');
    expect(JSON.parse(init.body ?? '{}')).toEqual({
      projectContext: 'old project',
      styleGuide: 'old style',
      modelContext: 'old model',
    });

    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(onSaved.mock.calls[0][0].name).toBe('kept-name');
    expect(onSaved.mock.calls[0][0].repoUrl).toBe('https://example.test/kept');
  });
});
