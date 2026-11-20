import * as assert from 'assert';
import { describe, it } from 'node:test';
import { applyEdits, createValues, editFields, fieldKind, fieldLabel, recordId, recordStatus, recordTitle, ResourceSpec, rowsOf } from '../../src/surfaces/resources/resourceSpec';
import { resourcePage } from '../../src/surfaces/resources/resourceViews';

const spec: ResourceSpec = {
  key: 'persona', label: 'Persona', plural: 'Personas', icon: 'account', view: 'configuration', idKey: 'name',
  list: async () => [], update: async () => undefined, remove: async () => undefined,
  readOnly: ['isBuiltIn'],
  actions: [{ label: 'Reset', when: (r) => r.isBuiltIn === true, run: async () => undefined }],
  route: (r) => `/personas/${String(r.name)}`,
};

const persona = {
  id: 'prs_1', tenantId: 'ten_1', name: 'Worker', description: 'Writes code', promptTemplateName: 'persona.worker', isBuiltIn: true, active: true,
  captainPriorities: [{ captainId: 'cpt_1', order: 1 }], createdUtc: '2026-11-20T10:00:00Z', lastUpdateUtc: '2026-11-20T11:00:00Z', retries: 3,
  instructions: 'Line one\nLine two',
};

describe('resource records', () => {
  it('reads rows, ids, titles and statuses from any record shape', () => {
    assert.deepStrictEqual(rowsOf({ objects: [{ id: 'a' }] }), [{ id: 'a' }]);
    assert.deepStrictEqual(rowsOf([{ id: 'b' }]), [{ id: 'b' }]);
    assert.deepStrictEqual(rowsOf(null), []);
    assert.strictEqual(recordId(spec, persona), 'Worker');
    assert.strictEqual(recordTitle({ title: 'Release 1.4\nnotes' }), 'Release 1.4');
    assert.strictEqual(recordTitle({}), '(untitled)');
    assert.strictEqual(recordStatus({ enabled: false }), 'Disabled');
    assert.strictEqual(fieldLabel('lastUpdateUtc'), 'Last Update (UTC)');
    assert.strictEqual(fieldLabel('baseUrl'), 'Base URL');
  });

  it('builds the edit form from the record, skipping identity, ownership and timestamps', () => {
    const fields = editFields(spec, persona);
    assert.deepStrictEqual(fields.map((f) => [f.key, f.fieldKind]), [
      ['description', 'text'], ['promptTemplateName', 'string'], ['active', 'boolean'], ['captainPriorities', 'json'], ['retries', 'number'], ['instructions', 'text'],
    ]);
    assert.strictEqual(fieldKind('notes', null), 'text');
    const edited = applyEdits(persona, fields, { description: 'Writes tests', active: 'false', retries: '5', captainPriorities: '[]', instructions: 'x', promptTemplateName: 'persona.worker' });
    assert.deepStrictEqual([edited.description, edited.active, edited.retries, edited.captainPriorities, edited.name, edited.id], ['Writes tests', false, 5, [], 'Worker', 'prs_1']);
    assert.throws(() => applyEdits(persona, fields, { retries: 'many' }), /Retries must be a number/);
    assert.throws(() => applyEdits(persona, fields, { captainPriorities: '{' }), /not valid JSON/);
  });

  it('converts create form values, including line lists and JSON', () => {
    const values = createValues([
      { key: 'title', label: 'Title', type: 'text', required: true },
      { key: 'criteria', label: 'Criteria', type: 'textarea', kind: 'lines' },
      { key: 'stages', label: 'Stages', type: 'textarea', kind: 'json' },
      { key: 'empty', label: 'Empty', type: 'text' },
    ], { title: 'T', criteria: 'a\n\n b ', stages: '[{"order":1}]', empty: '' });
    assert.deepStrictEqual(values, { title: 'T', criteria: ['a', 'b'], stages: [{ order: 1 }] });
  });

  it('renders a page with the record fields, long text, structured values and the fitting actions', () => {
    const page = resourcePage(spec, persona);
    assert.strictEqual(page.title, 'Worker');
    assert.strictEqual(page.subtitle, 'Worker');
    assert.deepStrictEqual(page.actions.map((a) => a.command), ['armada.resource.edit', 'armada.resource.action', 'armada.resource.delete', 'armada.openInBrowser']);
    const fields = page.sections[0];
    assert.ok(fields.kind === 'fields' && fields.rows.some((r) => r.label === 'Prompt Template Name' && r.value === 'persona.worker'));
    assert.ok(page.sections.some((s) => s.kind === 'pre' && s.title === 'Instructions' && s.text === 'Line one\nLine two'));
    assert.ok(page.sections.some((s) => s.kind === 'pre' && s.title === 'Captain Priorities'));
    assert.deepStrictEqual(resourcePage(spec, { ...persona, isBuiltIn: false }).actions.map((a) => a.label), ['Edit', 'Delete', 'Open in Browser']);
  });
});

describe('display language', () => {
  it('translates labels through the Admiral catalog and stays English without one', async () => {
    const { t, useCatalog, activeLocale } = await import('../../src/i18n');
    const { humanize } = await import('../../src/surfaces/ops/presentation');
    assert.strictEqual(t('Missions'), 'Missions');
    assert.strictEqual(activeLocale(), 'en');
    const catalog = {
      defaultLocale: 'en',
      supportedLocales: [{ code: 'en', label: 'English', nativeLabel: 'English', dir: 'ltr' }, { code: 'de', label: 'German', nativeLabel: 'Deutsch', dir: 'ltr' }],
      locales: { de: { terms: { Missions: 'Missionen' }, phrases: { 'In Progress': 'In Bearbeitung' } } },
    };
    useCatalog(catalog as never, 'de');
    try {
      assert.strictEqual(activeLocale(), 'de');
      assert.strictEqual(t('Missions'), 'Missionen');
      assert.strictEqual(humanize('InProgress'), 'In Bearbeitung');
      assert.strictEqual(t('Something new'), 'Something new');
    } finally {
      useCatalog(null, 'en');
    }
  });
});
