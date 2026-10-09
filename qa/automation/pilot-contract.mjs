import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

export const stages = ['Intake', 'Needs decision', 'Ready', 'In progress', 'Ready for QA', 'Changes requested', 'Blocked', 'Done'];
export function validatePilot(path) {
  const bytes = readFileSync(path);
  const value = JSON.parse(bytes);
  assert.deepEqual(Object.keys(value).sort(), ['note', 'schemaVersion', 'stages', 'synthetic']);
  assert.equal(value.schemaVersion, 1);
  assert.equal(value.synthetic, true);
  assert.deepEqual(value.stages, stages);
  assert.equal(value.note, 'Local automation pilot; not product test coverage.');
  return { result: 'passed', sha256: createHash('sha256').update(bytes).digest('hex'), scope: 'pipeline fixture integrity only' };
}
export function validateChangedPaths(paths, allowed) {
  const forbidden = paths.filter(p => !allowed.includes(p.replaceAll('\\', '/')));
  assert.equal(forbidden.length, 0, `Changes outside pilot scope: ${forbidden.join(', ')}`);
}
export function decisionFromReview(text) {
  const review = JSON.parse(text);
  assert.ok(['pass', 'fail'].includes(review.verdict));
  assert.ok(Array.isArray(review.reasons) && review.reasons.every(r => typeof r === 'string'));
  return review;
}
