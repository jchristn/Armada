/*
 * Loaded with `node --require` before the unit tests: makes `require('vscode')` resolve to test/fakes/vscode so the
 * extension's modules load without a VS Code host.
 */
import * as path from 'path';

// eslint-disable-next-line @typescript-eslint/no-require-imports
const Module = require('module') as { _resolveFilename: (request: string, ...rest: unknown[]) => string };
const fakePath = path.join(__dirname, 'fakes', 'vscode.js');
const original = Module._resolveFilename;
Module._resolveFilename = function resolve(request: string, ...rest: unknown[]): string {
  if (request === 'vscode') return fakePath;
  return original.call(this, request, ...rest);
};
