import { matchPath } from 'react-router-dom';
import { dashboardBreadcrumbRoutes, dashboardBreadcrumbs, dashboardCollections, dashboardHubs } from './dashboardNavigation';
import { flattenNavCommands } from './navModel';

const raw = import.meta.glob<string>('/src/**/*.{ts,tsx}', { eager: true, query: '?raw', import: 'default' });
const sources = Object.fromEntries(Object.entries(raw).filter(([file]) => !file.includes('.test.') && !file.includes('/test/')));
const app = sources['/src/App.tsx'];
const routes = [...app.matchAll(/<Route path="([^"]+)" element=\{<(\w+)/g)].map(match => ({ path: `/${match[1]}`, component: match[2] }));
const explicitRoutes = ['/', ...routes.filter(route => route.path !== '/*').map(route => route.path)];
const matches = (to: string) => explicitRoutes.some(path => matchPath({ path, end: true }, to.split(/[?#]/)[0]));

function resolve(source: string, reference: string): string | undefined {
  const segments = `${source.slice(0, source.lastIndexOf('/'))}/${reference}`.split('/');
  const normalized: string[] = [];
  for (const part of segments) {
    if (part === '..') normalized.pop();
    else if (part && part !== '.') normalized.push(part);
  }
  const base = '/' + normalized.join('/');
  return [base, `${base}.tsx`, `${base}.ts`, `${base}/index.tsx`, `${base}/index.ts`].find(path => sources[path] !== undefined);
}

/** These are structural regressions: new routes/pages/links must join the actual navigation tree. */
describe('Complete dashboard navigation inventory', () => {
  it('covers every actual rendered route with global breadcrumb metadata', () => {
    const active = routes.filter(route => !route.component.endsWith('Redirect') && route.path !== '/*').map(route => route.path);
    expect([...dashboardBreadcrumbRoutes].sort()).toEqual(active.sort());
    expect(app).toContain('<Route path="*" element={<NotFound />} />');
    const layout = sources['/src/components/Layout.tsx'];
    expect(layout).toMatch(/<DashboardBreadcrumbProvider>[\s\S]*<DashboardBreadcrumbs\s*\/>[\s\S]*<Suspense[\s\S]*<Outlet\s*\/>[\s\S]*<\/DashboardBreadcrumbProvider>/);
    expect(app).not.toContain('<Suspense');
  });

  it('has no unused App page loaders hiding old or orphaned UI', () => {
    for (const match of app.matchAll(/const (\w+) = lazy/g)) expect(app).toMatch(new RegExp(`<${match[1]}(?:\\s|/)`));
  });

  it('keeps breadcrumb rendering exclusive to the shared component and layout renderer', () => {
    const renderers = ['/src/components/shared/Breadcrumbs.tsx', '/src/components/shared/DashboardBreadcrumbs.tsx'];
    for (const [file, source] of Object.entries(sources)) {
      if (renderers.includes(file)) continue;
      expect(source, file).not.toMatch(/\bBreadcrumbTrail\b|className=["'](?:breadcrumb|breadcrumbs|breadcrumb-sep|bc-sep)["']/);
    }
  });

  it('reaches every page module through the router or an actual mounted hub', () => {
    const reached = new Set<string>();
    function visit(file: string) {
      if (reached.has(file)) return;
      reached.add(file);
      for (const match of sources[file].matchAll(/(?:from\s*['"]|import\s*\(\s*['"])(\.[^'"?]+)['"]/g)) {
        const target = resolve(file, match[1]);
        if (target) visit(target);
      }
    }
    visit('/src/App.tsx');
    const pages = Object.keys(sources).filter(file => file.startsWith('/src/pages/') && file.endsWith('.tsx'));
    expect(pages.length).toBeGreaterThan(80);
    expect(pages.filter(file => !reached.has(file))).toEqual([]);
  });

  it('keeps every sidebar and command palette destination on an explicit route', () => {
    for (const command of flattenNavCommands()) expect(matches(command.to), command.to).toBe(true);
  });

  it('gives every global breadcrumb ancestor a real route, excluding catch-all recovery', () => {
    for (const route of dashboardBreadcrumbRoutes) {
      const path = route.replace(/:\w+\??/g, 'example');
      for (const item of dashboardBreadcrumbs(path)) if (item.to) expect(matches(item.to), `${path} -> ${item.to}`).toBe(true);
    }
    for (const entry of Object.values(dashboardCollections)) expect(matches(entry.to), entry.to).toBe(true);
  });

  it('has no literal or interpolated page links into missing routes', () => {
    for (const [file, source] of Object.entries(sources)) {
      for (const match of source.matchAll(/(?:\bto\s*[=:]\s*(?:\{\s*)?|\bnavigate\(\s*)['"`](\/[^'"`\n]*)['"`]/g)) {
        const target = match[1].replace(/\$\{.*?\}/g, 'example');
        expect(matches(target), `${file} links to ${match[1]}`).toBe(true);
      }
    }
  });

  const hubPages: Record<string, string> = {
    '/vessels': 'VesselsHub', '/captains': 'CaptainsHub', '/missions': 'MissionsHub', '/dispatch': 'DispatchHub',
    '/configuration': 'Configuration', '/delivery': 'DeliveryHub', '/server': 'ServerHub', '/activity': 'Activity', '/fleet-actions': 'FleetActions',
  };
  it.each(dashboardHubs)('$path breadcrumb coverage matches every mounted hub tab', hub => {
    const source = sources[`/src/pages/${hubPages[hub.path]}.tsx`];
    const tabs = [...source.matchAll(/\{ key: '([^']+)', label: '([^']+)'/g)].map(match => ({ key: match[1], label: match[2] }));
    expect(tabs).toEqual(hub.tabs);
  });

  it('exposes the former orphan Settings usage controls on the active Settings page', () => {
    expect(sources['/src/pages/Server.tsx']).toContain('<UsageCaptureSettingsPanel canEdit={isAdmin} />');
    expect(sources['/src/pages/Settings.tsx']).toBeUndefined();
  });
});
