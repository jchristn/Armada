// Metro configuration. The app shares modules with the web dashboard instead of copying them (MOBILE_APP_PLAN.md,
// "Code sharing"): `@dashboard/*` resolves into src/Armada.Dashboard/src and `@armada-i18n/*` into the Admiral's
// i18n catalog folder. Both folders are watched. Bare imports from shared files (react, ...) resolve as if they were
// made from this project, so the dashboard's own node_modules (when installed) never produce a second React.
const path = require('path');
const { getDefaultConfig } = require('expo/metro-config');
const { sharedRoots } = require('./sharing.config');

const projectRoot = __dirname;
const config = getDefaultConfig(projectRoot);

config.watchFolders = [...(config.watchFolders || []), sharedRoots.dashboardSrc, sharedRoots.i18nDir];

// Aliases are resolved here rather than from tsconfig paths (app.config.ts turns that off): tsconfig also maps
// react for the type checker, which must not reach the bundler.
const aliases = {
  '@/': path.join(projectRoot, 'src') + path.sep,
  '@dashboard/': sharedRoots.dashboardSrc + path.sep,
  '@armada-i18n/': sharedRoots.i18nDir + path.sep,
};
const projectOrigin = path.join(projectRoot, 'package.json');

const upstreamResolve = config.resolver.resolveRequest;
const resolve = (context, moduleName, platform) =>
  upstreamResolve ? upstreamResolve(context, moduleName, platform) : context.resolveRequest(context, moduleName, platform);

config.resolver.resolveRequest = (context, moduleName, platform) => {
  for (const [prefix, target] of Object.entries(aliases)) {
    if (moduleName.startsWith(prefix)) {
      return context.resolveRequest(context, target + moduleName.slice(prefix.length), platform);
    }
  }
  const fromShared = context.originModulePath.startsWith(sharedRoots.dashboardSrc + path.sep);
  if (fromShared && !moduleName.startsWith('.') && !path.isAbsolute(moduleName)) {
    return resolve({ ...context, originModulePath: projectOrigin }, moduleName, platform);
  }
  return resolve(context, moduleName, platform);
};

module.exports = config;
