import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../../', import.meta.url));
const read = path => JSON.parse(readFileSync(`${root}/${path}`, 'utf8').replace(/^\uFEFF/, ''));
const evidence = read('qa/evidence/2026-10-08-regressions-round2.json');
const catalog = read('qa/TEST_CASE_FAMILIES.json');
const mappings = [];
function add(layer, name, result, variant) {
  const id = `${layer === 'backend' ? 'BE' : 'FE'}-${createHash('sha256').update(name).digest('hex').slice(0, 12)}`;
  let family;
  if (/EncryptionContract/.test(name)) family = 'QA-COV-10-5';
  else if (/CookieContract/.test(name)) family = 'QA-COV-01-3';
  else if (/NewMatch_Expires/.test(name)) family = 'QA-COV-05-3';
  else if (/PairIdentity/.test(name)) family = 'QA-COV-05-2';
  else if (/Jwt/.test(name)) family = 'QA-COV-01-1';
  else if (/CircuitBreaker/.test(name)) family = 'QA-COV-12-5';
  else if (/request boundaries|credentials|request body/i.test(name)) family = 'QA-COV-01-5';
  else if (/session|navigation/i.test(name)) family = 'QA-COV-01-4';
  else family = 'QA-COV-15-4';
  const entry = mappings.find(m => m.id === id);
  if (entry) entry.results[variant] = result;
  else mappings.push({ id, layer, name, family, results: { [variant]: result }, evidence: 'qa/evidence/2026-10-08-regressions-round2.json' });
}
for (const row of evidence.baselineBackend.cases) add('backend', row.name, row.result, 'baseline-3df9759');
for (const row of evidence.candidateBackend.cases) add('backend', row.name, row.result, 'candidate-5461c85');
for (const row of evidence.frontend.cases) add('frontend', row.name, row.result, 'baseline-3df9759');
for (const suite of catalog.suites) for (const family of suite.families) {
  const rows = mappings.filter(m => m.family === family.id);
  if (rows.length) { family.status = 'partially-tested'; family.cases = rows.map(m => m.id); }
}
writeFileSync(`${root}/qa/TEST_CASE_FAMILIES.json`, JSON.stringify(catalog, null, 2));
writeFileSync(`${root}/qa/REGRESSION_CASES.json`, JSON.stringify({ scope: 'Named executed regression cases mapped to family obligations; no family declared complete.', cases: mappings }, null, 2));
console.log(`${mappings.length} distinct cases mapped; application variants kept separate.`);
