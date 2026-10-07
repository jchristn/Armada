import { fireEvent, render, screen } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import ProtectedRoute from './ProtectedRoute';
import { whoami } from '../api/client';
import { translateTemplate } from '../i18n/runtime';

vi.mock('../api/client', () => ({
  whoami: vi.fn(),
  setAuthToken: vi.fn(),
  setOnUnauthorized: vi.fn(),
  changePassword: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('./LoginFlow', () => ({ default: () => <div>login</div> }));

function renderProtected() {
  return render(
    <AuthProvider>
      <ProtectedRoute><div>dashboard content</div></ProtectedRoute>
    </AuthProvider>,
  );
}

describe('PasswordChangeRequired skip', () => {
  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('armada_session_token', 'tok_session');
    vi.mocked(whoami).mockResolvedValue({
      tenant: null,
      user: { id: 'usr_admin', email: 'admin@armada', isAdmin: true },
      passwordChangeRequired: true,
      defaultCredentialsInUse: true,
    } as never);
  });

  it('skips the default password change only after confirming the risk, and remembers it', async () => {
    const first = renderProtected();
    expect(await screen.findByRole('heading', { name: 'Change the default password' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Skip for now' }));
    expect(screen.getByRole('alertdialog', { name: 'Keep the default password?' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByText('dashboard content')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Skip for now' }));
    fireEvent.click(screen.getByRole('button', { name: 'Skip and continue' }));
    expect(await screen.findByText('dashboard content')).toBeInTheDocument();
    first.unmount();

    renderProtected();
    expect(await screen.findByText('dashboard content')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Change the default password' })).not.toBeInTheDocument();
  });
});
