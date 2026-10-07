// Jest resolver: mirrors metro.config.js. Bare imports made from shared dashboard modules (react, @babel/runtime
// helpers injected by the transform, ...) resolve as if they were made from this project, so the tests never depend
// on the dashboard's own node_modules being installed (CI installs only this project) and never load a second copy.
const path = require('path');
const { sharedRoots } = require('./sharing.config');

const projectRoot = __dirname;
const sharedPrefix = sharedRoots.dashboardSrc + path.sep;

function isBare(request) {
  return !request.startsWith('.') && !path.isAbsolute(request);
}

function resolveRequest(request, options) {
  const fromShared = typeof options.basedir === 'string' && (options.basedir + path.sep).startsWith(sharedPrefix);
  if (fromShared && isBare(request)) {
    return options.defaultResolver(request, { ...options, basedir: projectRoot });
  }
  return options.defaultResolver(request, options);
}

module.exports = resolveRequest;
