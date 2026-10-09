import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { stages, validatePilot, validateChangedPaths, decisionFromReview } from './pilot-contract.mjs';

const directory = mkdtempSync(join(tmpdir(), 'woven-pilot-'));
const fixture = join(directory, 'fixture.json');
const good = { schemaVersion: 1, synthetic: true, stages, note: 'Local automation pilot; not product test coverage.' };
test('valid fixture has a content hash', () => {
  writeFileSync(fixture, JSON.stringify(good));
  assert.match(validatePilot(fixture).sha256, /^[a-f0-9]{64}$/);
});
test('rejects missing, extra, reordered or non-synthetic data', () => {
  for (const bad of [{ ...good, synthetic: false }, { ...good, token: 'test' }, { ...good, stages: [...stages].reverse() }, { ...good, schemaVersion: 2 }]) {
    writeFileSync(fixture, JSON.stringify(bad));
    assert.throws(() => validatePilot(fixture));
  }
});
test('refuses application edits or paths escaping scope', () => {
  const allowed = ['qa/fixtures/pipeline-pilot.json'];
  validateChangedPaths(allowed, allowed);
  for (const path of ['backend/WovenBackend/Program.cs', '../secret', 'qa/fixtures/pipeline-pilot.json.bak']) assert.throws(() => validateChangedPaths([path], allowed));
});
test('review cannot pass with malformed output', () => {
  assert.equal(decisionFromReview('{"verdict":"fail","reasons":["bad"]}').verdict, 'fail');
  for (const text of ['passed', '{}', '{"verdict":"pass","reasons":"none"}']) assert.throws(() => decisionFromReview(text));
});
