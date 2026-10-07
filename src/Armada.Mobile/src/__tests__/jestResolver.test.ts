/**
 * Bare imports from shared dashboard modules resolve from this project (as Metro does), so the suite does not need
 * the dashboard's node_modules (CI installs only src/Armada.Mobile).
 */
import path from 'path';

const resolveRequest = require('../../jest.resolver') as (
  request: string,
  options: { basedir: string; defaultResolver: (request: string, options: { basedir: string }) => string },
) => string;
const sharing = require('../../sharing.config') as { sharedRoots: { dashboardSrc: string } };

const projectRoot = path.resolve(__dirname, '..', '..');
const sharedDir = path.join(sharing.sharedRoots.dashboardSrc, 'api');

function capture(request: string, basedir: string): string {
  let seen = '';
  resolveRequest(request, {
    basedir,
    defaultResolver: (_request, options) => {
      seen = options.basedir;
      return 'resolved';
    },
  });
  return seen;
}

describe('jest resolver', () => {
  it('resolves bare imports from shared dashboard modules from the mobile project', () => {
    expect(capture('@babel/runtime/helpers/interopRequireDefault', sharedDir)).toBe(projectRoot);
    expect(capture('react', sharing.sharedRoots.dashboardSrc)).toBe(projectRoot);
  });

  it('leaves relative imports from shared modules and imports from app modules alone', () => {
    expect(capture('./client', sharedDir)).toBe(sharedDir);
    const appDir = path.join(projectRoot, 'src', 'auth');
    expect(capture('react', appDir)).toBe(appDir);
  });

  it('does not treat a sibling folder with the same prefix as shared', () => {
    const sibling = sharing.sharedRoots.dashboardSrc + '-other';
    expect(capture('react', sibling)).toBe(sibling);
  });
});
