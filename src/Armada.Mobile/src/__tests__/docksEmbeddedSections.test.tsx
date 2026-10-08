import { fireEvent, screen } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import { CaptainsHubScreen } from '../screens/captains/CaptainsHubScreen';
import { EMBEDDED_SECTIONS, embeddedSectionFor } from '../screens/embeddedSections';
import { RoutePlaceholder } from '../screens/RoutePlaceholder';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => require('../test/operationsClient').operationsClientMockFactory());
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const mockWindow = { width: 390, height: 844 };
jest.mock('react-native/Libraries/Utilities/useWindowDimensions', () => ({
  __esModule: true,
  default: () => ({ width: mockWindow.width, height: mockWindow.height, scale: 2, fontScale: 1 }),
}));

const api = client as jest.Mocked<typeof client>;
const realError = console.error;
beforeAll(() => {
  jest.spyOn(console, 'error').mockImplementation((...args: unknown[]) => {
    if (typeof args[0] === 'string' && args[0].includes('[react-native-gesture-handler]')) return;
    realError(...args);
  });
});
afterAll(() => { (console.error as jest.Mock).mockRestore(); });

beforeEach(() => {
  mockWindow.width = 390;
  setMockParams({});
  api.listDocks.mockResolvedValue(page([{ id: 'dck_1', vesselId: 'vsl_1', captainId: null, branchName: 'armada/x', worktreePath: '/w', active: true, createdUtc: '2026-10-07T10:00:00Z' }]) as never);
  api.listSignals.mockResolvedValue(page([{ id: 'sig_1', type: 'Mail', payload: 'hi', fromCaptainId: null, toCaptainId: null, read: false, createdUtc: '2026-10-07T10:00:00Z' }]) as never);
  api.listEvents.mockResolvedValue(page([]) as never);
  api.listVessels.mockResolvedValue(page([]) as never);
  api.listCaptains.mockResolvedValue(page([]) as never);
  api.getSignal.mockResolvedValue({ id: 'sig_1', type: 'Mail', payload: 'hi', fromCaptainId: null, toCaptainId: null, read: false, createdUtc: '2026-10-07T10:00:00Z', tenantId: null });
});

describe('hubs serve the sections already built', () => {
  it('maps hub queries to sections', () => {
    // The Captains hub is a real screen now; its Docks tab embeds the list itself.
    expect(embeddedSectionFor('/captains', { tab: 'docks' })).toBeNull();
    // The Activity hub (W4.3) is a real screen too; it embeds the Events and Signals lists itself.
    expect(embeddedSectionFor('/activity', { source: ['signals'] })).toBeNull();
    expect(embeddedSectionFor('/vessels', { tab: 'docks' })).toBeNull();
    expect(EMBEDDED_SECTIONS).toEqual({});
  });

  it('/captains?tab=docks shows the Docks list in the Captains hub; a row pushes the dock on phones', async () => {
    setMockParams({ tab: 'docks' });
    await renderScreen(<CaptainsHubScreen />);
    expect(await screen.findByTestId('docks-list')).toBeTruthy();
    await fireEvent.press(await screen.findByTestId('dock-row-dck_1'));
    expect(mockRouter.push).toHaveBeenCalledWith('/docks/dck_1');
  });

  it('other placeholders are unchanged', async () => {
    setMockParams({ tab: 'docks' });
    await renderScreen(<RoutePlaceholder pattern="/vessels" />);
    expect(await screen.findByTestId('route-placeholder-workstream')).toHaveTextContent('Coming in W3.1');
    expect(screen.queryByTestId('route-placeholder-section-docks')).toBeNull();
  });
});
