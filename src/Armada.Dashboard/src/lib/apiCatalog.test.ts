import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import catalog from './apiCatalog.generated.json';
import { buildCatalogOperations, type ApiCatalog } from './apiExplorer';

describe('built-in API catalog', () => {
  it('lists every REST route of the frozen API surface (run scripts/dashboard/generate-api-catalog.py when it drifts)', () => {
    const surface = JSON.parse(readFileSync('../../docs/api-surface-1.0.json', 'utf8')) as { rest: Array<{ method: string; route: string }> };
    const expected = surface.rest.map((r) => `${r.method.toLowerCase()} ${r.route}`).sort();
    expect((catalog as ApiCatalog).routes.map((r) => `${r.method} ${r.path}`).sort()).toEqual(expected);
  });

  it('builds operations with path parameters and a JSON body for methods that take one', () => {
    const operations = buildCatalogOperations({
      contractVersion: '1.0',
      routes: [
        { method: 'put', path: '/api/v1/vessels/{id}', summary: 'Update a vessel', tag: 'Vessels', requestType: 'Vessel', auth: 'Authenticated' },
        { method: 'get', path: '/api/v1/vessels/{id}/docks/{dockId}', summary: '', tag: '', requestType: '', auth: '' },
      ],
    });
    expect(operations[0]).toMatchObject({ id: 'put:/api/v1/vessels/{id}', tag: 'Vessels', requestBodyContentType: 'application/json' });
    expect(operations[0].parameters.map((p) => [p.name, p.in, p.required])).toEqual([['id', 'path', true]]);
    expect(operations[1]).toMatchObject({ summary: 'GET /api/v1/vessels/{id}/docks/{dockId}', tag: 'General', requestBody: null });
    expect(operations[1].parameters.map((p) => p.name)).toEqual(['id', 'dockId']);
  });
});
