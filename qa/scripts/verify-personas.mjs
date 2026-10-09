import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

const bytes = readFileSync(new URL('../fixtures/personas.json', import.meta.url));
const { personas } = JSON.parse(bytes);
assert.equal(personas.length, 100);
assert.equal(new Set(personas.map(p => p.id)).size, 100);
assert.equal(new Set(personas.map(p => p.email)).size, 100);
for (const p of personas) {
  assert.equal(p.synthetic, true);
  assert.ok(p.age >= 18 && p.age <= 65);
  assert.ok(p.email.endsWith('@woven.invalid'));
  assert.ok(p.interestedIn.length > 0);
  assert.ok(p.preferences.ageMin <= p.preferences.ageMax);
  assert.ok(p.sparkBalance >= 0 && p.sparkBalance <= 10);
  assert.ok(Math.abs(p.location.lat) <= 90 && Math.abs(p.location.lng) <= 180);
}
assert.equal(new Set(personas.map(p => p.location.city)).size, 4);
assert.equal(new Set(personas.map(p => p.gender)).size, 3);
assert.equal(new Set(personas.map(p => p.targetState)).size, 5);
assert.ok(personas.some(p => p.sparkBalance === 0));
assert.ok(personas.some(p => p.age === 18));
assert.ok(personas.some(p => p.age === 60));
console.log(JSON.stringify({ result: 'passed', count: personas.length, sha256: createHash('sha256').update(bytes).digest('hex'), scope: 'fixture integrity only; no application behavior validated' }, null, 2));
