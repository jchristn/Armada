// Minimal typing for the one Node API used by tests that read backend source files. The dashboard does not
// depend on @types/node; if it is present these declarations merge with it.
declare module 'node:fs' {
  export function readFileSync(path: string | URL, encoding: 'utf8'): string;
}
