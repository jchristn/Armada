import { expect, it } from 'vitest';
import { discoveredContextText } from './Endpoints';

const t = (key: string, params?: Record<string, string | number>) => key.replace(/\{\{(\w+)\}\}/g, (_, name: string) => String(params?.[name] ?? name));

it('shows the discovered context with where it was read, or that none was discovered', () => {
  expect(discoveredContextText({ discoveredContextLength: 40960, discoveredContextSource: 'ollama /api/ps' }, t)).toBe(`${(40960).toLocaleString()} tokens (ollama /api/ps)`);
  expect(discoveredContextText({ discoveredContextLength: 8192, discoveredContextSource: null }, t)).toBe(`${(8192).toLocaleString()} tokens`);
  expect(discoveredContextText({ discoveredContextLength: null, discoveredContextSource: null }, t)).toBe('Not discovered');
});
