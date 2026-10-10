import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
const root = fileURLToPath(new URL('../../', import.meta.url));
function walk(directory, extension) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    if (['node_modules', 'obj', 'bin', '.git'].includes(entry.name)) return [];
    const path = resolve(directory, entry.name);
    return entry.isDirectory() ? walk(path, extension) : path.endsWith(extension) ? [path] : [];
  });
}
const sources = [...walk(resolve(root, 'backend/WovenBackend'), '.cs'), ...walk(resolve(root, 'frontend/woven-frontend/src/app'), '.ts')];
const registrations = [];
const backendServices = []; const frontendServices = [];
for (const file of sources) {
  const path = relative(root, file).replaceAll('\\', '/');
  const lines = readFileSync(file, 'utf8').split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const match = lines[i].match(/\b(MapGet|MapPost|MapPut|MapDelete|MapPatch|MapMethods|MapHub|MapHealthChecks)\s*(?:<[^>]+>)?\s*\(\s*"([^"]*)"/);
    if (match && !lines[i].trimStart().startsWith('//')) registrations.push({ id: `BE-${registrations.filter(r => r.layer === 'backend').length + 1}`, layer: 'backend', source: path, line: i + 1, kind: match[1], routeFragment: match[2], cases: [], status: 'unmapped' });
    const route = path.endsWith('app.routes.ts') && lines[i].match(/\bpath:\s*'([^']*)'/);
    if (route) registrations.push({ id: `FE-${registrations.filter(r => r.layer === 'frontend').length + 1}`, layer: 'frontend', source: path, line: i + 1, kind: 'route', routeFragment: route[1], cases: [], status: 'unmapped' });
  }
  if (path.includes('/Services/') || /Worker\.cs$/.test(path)) backendServices.push(path);
  if (/\.service\.ts$|\.guard\.ts$|\.interceptor\.ts$/.test(path)) frontendServices.push(path);
}
const result = {
  snapshot: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(),
  method: 'Static literal-registration discovery, not a compiler or runtime route list. Group prefixes, inherited policies, dynamic/multiline declarations and conditional registrations need manual/runtime reconciliation.',
  coverageMeaning: 'Inventory only: no discovered surface is marked tested without mapped cases and evidence. Service coverage is not inferred from endpoint checks.',
  requiredCaseDimensions: ['happy path', 'anonymous and forbidden ownership', 'invalid/missing/oversized input', 'empty/not-found/duplicate', 'boundary and state transitions', 'retry/idempotency/concurrency', 'dependency failure and recovery', 'data effects and privacy', 'UI loading/error/accessibility where applicable'],
  counts: { backendRegistrationSites: registrations.filter(r => r.layer === 'backend').length, frontendRouteSites: registrations.filter(r => r.layer === 'frontend').length, backendServiceFiles: backendServices.length, frontendServiceFiles: frontendServices.length },
  registrations, backendServices, frontendServices
};
writeFileSync(resolve(root, 'qa/TEST_SURFACES.json'), JSON.stringify(result, null, 2));
console.log(JSON.stringify(result.counts));
