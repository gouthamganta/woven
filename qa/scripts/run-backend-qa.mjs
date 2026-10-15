import { spawnSync } from 'node:child_process';
import { mkdirSync, readdirSync, writeFileSync, readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../..', import.meta.url));
const candidate = process.argv[2] && resolve(process.argv[2]);
const results = resolve(process.argv[3] || resolve(root, 'qa/.local/backend-qa'));
mkdirSync(results, { recursive: true });
const args = ['test', 'backend/WovenBackend.Tests/WovenBackend.Tests.csproj',
  ...(candidate ? ['-p:WovenBackendProject=' + candidate] : []),
  '--collect:XPlat Code Coverage', '--settings', 'qa/backend.coverage.runsettings',
  '--logger', 'trx;LogFileName=tests.trx', '--results-directory', results];
const tests = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 240000, maxBuffer: 16 * 1024 * 1024 });
writeFileSync(resolve(results, 'run.log'), (tests.stdout || '') + (tests.stderr || ''));
process.stdout.write((tests.stdout || '') + (tests.stderr || ''));
if (tests.error) throw tests.error;
const reports = readdirSync(results, { withFileTypes: true }).filter(entry => entry.isDirectory())
  .flatMap(entry => readdirSync(resolve(results, entry.name)).filter(name => name === 'coverage.cobertura.xml').map(name => resolve(results, entry.name, name)));
if (reports.length !== 1) throw new Error('Expected one current coverage report; use a fresh result directory per run.');
const digest = path => createHash('sha256').update(readFileSync(path)).digest('hex');
writeFileSync(resolve(results, 'identity.json'), JSON.stringify({
  sourceBinarySha256: digest(resolve(root, 'backend/WovenBackend.Tests/bin/Debug/net10.0/WovenBackend.dll')),
  rawJsonSha256: digest(resolve(dirname(reports[0]), 'coverage.json')),
  testExitCode: tests.status,
}, null, 2));
const gate = spawnSync(process.execPath, ['qa/scripts/check-coverage.mjs', 'backend', reports[0], resolve(results, 'gate.json')], { cwd: root, encoding: 'utf8', windowsHide: true });
process.stdout.write((gate.stdout || '') + (gate.stderr || ''));
if (gate.error) throw gate.error;
process.exitCode = tests.status === 0 && gate.status === 0 ? 0 : 1;
