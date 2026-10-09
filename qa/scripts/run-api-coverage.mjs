import { spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, mkdirSync, existsSync, cpSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes, createHash } from 'node:crypto';

const qaCheckout = fileURLToPath(new URL('../..', import.meta.url));
const workspace = resolve(process.argv[2] || qaCheckout);
const source = resolve(process.argv[3] || resolve(workspace, 'qa/.local/claude-startup-fix/backend/WovenBackend/bin/Debug/net10.0'));
const runId = new Date().toISOString().replace(/\D/g, '').slice(0, 14) + '_' + randomBytes(3).toString('hex');
const database = 'woven_qa_cov80_' + runId;
const directory = resolve(workspace, 'qa/.local/api-coverage-' + runId);
const results = resolve(directory, 'results');
mkdirSync(results, { recursive: true });
function execute(args, { input, allowFailure = false, timeout = 60000 } = {}) {
  const result = spawnSync(args[0], args.slice(1), {
    cwd: qaCheckout, input, timeout, encoding: 'utf8', windowsHide: true, maxBuffer: 32 * 1024 * 1024,
  });
  if (result.error || (result.status !== 0 && !allowFailure)) {
    writeFileSync(resolve(results, 'command-error.log'), (result.stdout || '') + (result.stderr || '') + String(result.error || ''));
    throw new Error('Local coverage command failed; diagnostics retained in private results.');
  }
  return result;
}
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const inspected = JSON.parse(execute(['docker', 'inspect', 'woven-qa-postgres-1']).stdout)[0];
if (inspected.Config.Labels['com.docker.compose.project'] !== 'woven-qa' || !inspected.NetworkSettings.Networks['woven-qa_qa']) throw new Error('QA database container identity mismatch.');
if (execute(['docker', 'network', 'inspect', 'woven-qa_qa', '--format', '{{.Internal}}']).stdout.trim() !== 'true') throw new Error('QA network must deny external egress.');
for (const path of [resolve(source, 'WovenBackend.dll'), resolve(source, 'WovenBackend.pdb'), resolve(workspace, 'qa/.local/coverlet-cli/coverlet')]) {
  if (!existsSync(path)) throw new Error('Missing local compiled app/PDB or Coverlet6.0.4 tool.');
}
function sql(db, input) {
  if (!['postgres', 'woven_qa_model', database].includes(db)) throw new Error('Database outside dedicated QA scope.');
  return execute(['docker', 'exec', '-i', 'woven-qa-postgres-1', 'psql', '-U', 'woven_qa', '-d', db, '-v', 'ON_ERROR_STOP=1', '-At'], { input }).stdout.trim();
}
if (sql('postgres', `SELECT count(*) FROM pg_database WHERE datname='${database}'`) !== '0') throw new Error('Refusing to overwrite existing database.');
if (sql('woven_qa_model', 'SELECT count(*) FROM "Users"') !== '100') throw new Error('Unexpected synthetic source population.');
const accounts = JSON.parse(readFileSync(resolve(workspace, 'qa/.local/full-qa-runtime/seed-accounts.json'))).accounts;
const actor = accounts[0]?.userId;
if (!Number.isInteger(actor)) throw new Error('Synthetic fixture actor missing.');
sql('postgres', `CREATE DATABASE ${database}`);
const dump = execute(['docker', 'exec', 'woven-qa-postgres-1', 'pg_dump', '-U', 'woven_qa', '-d', 'woven_qa_model', '--no-owner', '--no-acl']).stdout;
sql(database, dump);
if (sql(database, 'SELECT count(*) FROM "Users"') !== '100') throw new Error('Clone population mismatch.');
// Establish the new-wallet scenario only in the new clone; source DB is untouched.
sql(database, `DELETE FROM spark_wallets WHERE user_id=${actor}`);
cpSync(source, resolve(directory, 'backend'), { recursive: true });
const sourceHash = hash(resolve(source, 'WovenBackend.dll'));
const environment = resolve(directory, 'coverage.env');
const posix = path => path.replaceAll('\\', '/');
writeFileSync(environment, [
  `WOVEN_QA_COVERAGE_DATABASE=${database}`,
  `WOVEN_QA_COVERAGE_BACKEND=${posix(resolve(directory, 'backend'))}`,
  `WOVEN_QA_COVERAGE_RESULTS=${posix(results)}`,
  `WOVEN_QA_COVERLET=${posix(resolve(workspace, 'qa/.local/coverlet-cli'))}`,
].join('\n') + '\n');
const compose = ['docker', 'compose', '-p', 'woven-qa', '-f', 'qa/compose.yaml', '-f', 'qa/compose.runtime.yaml', '-f', 'qa/compose.coverage.yaml',
  '--env-file', resolve(workspace, 'qa/.local/full-qa-runtime/runtime.env'),
  '--env-file', resolve(workspace, 'qa/.local/full-qa-runtime/model.env'), '--env-file', environment];
let smoke;
let started = false;
try {
  // The existing app, PostgreSQL, Redis and storage containers are preserved.
  started = true;
  execute([...compose, 'up', '-d', '--no-deps', '--force-recreate', 'coverage-redis', 'coverage-backend', 'coverage-gateway']);
  let ready = false;
  const readinessDeadline = Date.now() + 90000;
  for (let attempt = 0; Date.now() < readinessDeadline; attempt++) {
    try {
      const response = await fetch('http://127.0.0.1:5182/health/ready', { signal: AbortSignal.timeout(1500) });
      if (response.status === 200) { ready = true; break; }
    } catch { }
    if (attempt > 0 && attempt % 20 === 0) console.log('Waiting for local instrumentation/startup; no external provider calls.');
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  if (!ready) throw new Error('Instrumented API not ready.');
  smoke = spawnSync(process.execPath, ['qa/scripts/api-smoke.mjs', workspace], {
    cwd: qaCheckout, encoding: 'utf8', windowsHide: true, timeout: 180000,
    env: { ...process.env, WOVEN_QA_API_BASE: 'http://127.0.0.1:5182', WOVEN_QA_RESULTS_DIRECTORY: results },
  });
  writeFileSync(resolve(results, 'smoke.log'), (smoke.stdout || '') + (smoke.stderr || ''));
  process.stdout.write(smoke.stdout || '');
  if (smoke.error) throw smoke.error;
} finally {
  if (started) {
    writeFileSync(resolve(results, 'stop'), 'Graceful exit so Coverlet flushes counters.\n');
    try { execute(['docker', 'wait', 'woven-qa-coverage-backend-1'], { timeout: 45000 }); }
    catch (error) {
      execute(['docker', 'stop', 'woven-qa-coverage-backend-1'], { allowFailure: true });
      throw error;
    }
    finally { execute(['docker', 'stop', 'woven-qa-coverage-gateway-1', 'woven-qa-coverage-redis-1'], { allowFailure: true }); }
  }
}
if (hash(resolve(directory, 'backend/WovenBackend.dll')) !== sourceHash) throw new Error('Instrumented app copy was not restored.');
const metadata = { runId, database, sourceDatabase: 'woven_qa_model', users: 100, sourceBinarySha256: sourceHash, copyRestored: true, internalNetwork: true, isolatedRedis: true, smokeExitCode: smoke.status, freshMigrationProof: false };
writeFileSync(resolve(results, 'identity.json'), JSON.stringify(metadata, null, 2));
const gate = execute([process.execPath, 'qa/scripts/check-coverage.mjs', 'backend', resolve(results, 'backend-http.cobertura.xml'), resolve(results, 'gate.json')], { allowFailure: true });
process.stdout.write(gate.stdout || '');
console.log('Private coverage evidence: ' + results);
process.exitCode = smoke.status === 0 && gate.status === 0 ? 0 : 1;
