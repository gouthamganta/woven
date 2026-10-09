import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const root = fileURLToPath(new URL('../..', import.meta.url));
const frontend = resolve(root, 'frontend/woven-frontend');
const report = resolve(frontend, 'coverage/woven-frontend/coverage-final.json');
const summaryPath = resolve(frontend, 'coverage/woven-frontend/coverage-summary.json');
const output = process.argv[2] && resolve(process.argv[2]);
function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const path = resolve(dir, entry.name);
    return entry.isDirectory() ? walk(path) : [path];
  });
}
const source = walk(resolve(frontend, 'src/app'));
const typescript = source.filter(path => path.endsWith('.ts') && !path.endsWith('.spec.ts'));
const templates = source.filter(path => path.endsWith('.html'));
const rawBytes = readFileSync(report);
const raw = JSON.parse(rawBytes);
const reported = new Set(Object.keys(raw).map(path => resolve(path).toLowerCase()));
const missing = typescript.filter(path => !reported.has(path.toLowerCase()));
const summary = JSON.parse(readFileSync(summaryPath));
const result = {
  scope: 'All src/app non-spec TypeScript files; HTML/template behavior is a separate unmeasured obligation.',
  typescriptFiles: typescript.length,
  reportedTypescriptFiles: typescript.length - missing.length,
  missing: missing.map(path => relative(root, path).replaceAll('\\', '/')),
  templates: templates.length,
  completeTypeScriptDenominator: missing.length === 0,
  coverage: summary.total,
  rawCoverageSha256: createHash('sha256').update(rawBytes).digest('hex'),
  files: Object.entries(summary).filter(([path]) => path !== 'total').map(([path, value]) => ({
    file: relative(root, path).replaceAll('\\', '/'), ...value,
  })),
};
if (output) writeFileSync(output, JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ ...result, files: undefined }, null, 2));
if (missing.length) process.exitCode = 1;
