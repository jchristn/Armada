// What Vite provides the dashboard sources at build time; esbuild.mjs supplies the same for the page tabs.
declare module '*.css';

interface ImportMetaEnv {
  readonly [key: string]: string | undefined;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
