import assert from 'node:assert/strict';
import test from 'node:test';

import {
  buildCaptainUpdatePayload,
  createCaptainEditForm,
  getCaptainModelDisplayText,
  getCaptainSaveErrorMessage,
  RUNTIME_DEFAULT_MODEL_TEXT,
} from '../src/pages/captainDetailUtils.ts';
import { formatDuration, MISSION_DETAIL_GRID_COLUMNS } from '../src/pages/missionDetailUtils.ts';

test('createCaptainEditForm preserves model and defaults runtime', () => {
  const form = createCaptainEditForm({
    name: 'Captain Model',
    runtime: null,
    model: 'gpt-5.4-mini',
    systemInstructions: null,
    allowedPersonas: null,
    preferredPersona: null,
  });

  assert.deepEqual(form, {
    name: 'Captain Model',
    runtime: 'ClaudeCode',
    model: 'gpt-5.4-mini',
    systemInstructions: '',
    allowedPersonas: '',
    preferredPersona: '',
  });
});

test('buildCaptainUpdatePayload normalizes blank model to null and drops empty optional fields', () => {
  const payload = buildCaptainUpdatePayload({
    name: 'Captain Model',
    runtime: 'Cursor',
    model: '   ',
    systemInstructions: '',
    allowedPersonas: '',
    preferredPersona: '',
  });

  assert.deepEqual(payload, {
    name: 'Captain Model',
    runtime: 'Cursor',
    model: null,
  });
});

test('captain save errors surface nested validation messages for the modal', () => {
  const message = getCaptainSaveErrorMessage({
    response: {
      data: {
        message: 'Model "bad-model" is not valid for Cursor.',
      },
    },
  });

  assert.equal(message, 'Model "bad-model" is not valid for Cursor.');
});

test('captain model display falls back to the runtime default label', () => {
  assert.equal(getCaptainModelDisplayText(null), RUNTIME_DEFAULT_MODEL_TEXT);
  assert.equal(getCaptainModelDisplayText('gpt-5.4-mini'), 'gpt-5.4-mini');
});

test('mission runtime formatting covers short, minute, hour, and null values', () => {
  assert.equal(formatDuration(null), '--');
  assert.equal(formatDuration(12300), '12.3s');
  assert.equal(formatDuration(135000), '2m 15s');
  assert.equal(formatDuration(3661000), '1h 1m');
});

test('mission detail uses the four-column grid constant', () => {
  assert.equal(MISSION_DETAIL_GRID_COLUMNS, '1fr 1fr 1fr 1fr');
});
