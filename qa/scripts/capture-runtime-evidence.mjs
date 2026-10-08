import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../../', import.meta.url));
const local = `${root}/qa/.local/full-qa-runtime`;
const input = name => JSON.parse(readFileSync(`${local}/${name}`, 'utf8').replace(/^\uFEFF/, ''));
const api = input('api-results.json').records;
const lifecycle = input('lifecycle-results-30.json').results;
const security = input('security-results.json').results;
const digest = name => createHash('sha256').update(readFileSync(`${local}/${name}`)).digest('hex');
const safe = records => records.map(r => ({ id: r.id, path: r.path, result: r.result, status: r.status, durationMs: r.durationMs }));
const count = rows => Object.fromEntries(['passed', 'failed', 'blocked'].map(s => [s, rows.filter(r => r.result === s).length]));
const concurrency = input('concurrency-results.json');
const audit = input('npm-audit.json');
const report = {
  recordedAt: new Date().toISOString(),
  baseline: '3df9759d6799da8823e16d8f6db8a2a9727c403a', candidate: '5461c85', candidatePullRequest: 'https://github.com/gouthamganta/woven/pull/144',
  environment: { host: 'Windows local Docker', backendEnvironment: 'Production', database: 'woven_qa_model', schemaMode: 'EF model-created, NOT migration verified', initialSyntheticUsers: 100, remainingAfterDeletionProbe: 99, externalProviderRequests: 'blocked', batchWorkers: 'disabled' },
  counts: { api: count(api), lifecycle: count(lifecycle), security: count(security) },
  api: safe(api), lifecycle: safe(lifecycle), security: safe(security),
  concurrency: { ...concurrency, persistedVerification: { totalUsed: 5, responseCount: 5, noteCount: 5, method: 'Independent read-only psql query after eight simultaneous requests' } },
  browser: input('browser-guest.json').map(r => ({ scenario: r.scenario, status: r.status, horizontalOverflow: r.horizontalOverflow, loginCardVisible: r.loginCardVisible, consentKeyboardControl: r.consentKeyboardControl, visibleControlCount: r.controls.length, scope: r.scope })),
  frontendDependencyAudit: audit.metadata.vulnerabilities,
  artifactHashes: Object.fromEntries(['api-results.json', 'lifecycle-results-30.json', 'security-results.json', 'concurrency-results.json', 'browser-guest.json', 'npm-audit.json', 'backend/WovenBackend.dll', 'test-results/full-qa-final.trx'].map(name => [name, digest(name)])),
  limitations: ['Counts are targeted observations, not full product coverage.', 'JWTs are locally signed synthetic tokens; Google SSO is not tested.', 'Model-created schema bypasses broken fresh migrations.', 'Private raw evidence remains in ignored local storage; hashes alone do not establish independent certification.', 'No production deployment or external delivery validated.', 'Concurrent choice probe does not validate failure rollback, ambiguous commit or capacity.']
};
mkdirSync(`${root}/qa/evidence`, { recursive: true });
writeFileSync(`${root}/qa/evidence/2026-10-08-full-qa-results.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report.counts));
