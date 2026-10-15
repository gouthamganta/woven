import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

// A source candidate is optional. Only the three already-reviewed auth files
// are overlaid, and their original bytes are restored even when tests fail.
const root = fileURLToPath(new URL('../..', import.meta.url));
const frontend = resolve(root, 'frontend/woven-frontend');
const candidate = process.argv[2] && resolve(process.argv[2]);
const output = resolve(process.argv[3] || resolve(root, 'qa/.local/frontend-qa'));
const files = ['src/app/core/auth/auth.guard.ts', 'src/app/core/auth/auth.interceptor.ts', 'src/app/app.routes.ts'];
const originals = new Map();
mkdirSync(output, { recursive: true });
let result;
try {
  if (candidate) {
    const status = spawnSync('git', ['status', '--porcelain', '--', ...files.map(f => 'frontend/woven-frontend/' + f)], { cwd: root, encoding: 'utf8', windowsHide: true });
    if (status.status !== 0 || status.stdout.trim()) throw new Error('Refusing to overlay modified auth source files.');
    for (const file of files) {
      const target = resolve(frontend, file);
      originals.set(target, readFileSync(target));
      writeFileSync(target, readFileSync(resolve(candidate, 'frontend/woven-frontend', file)));
    }
  }
  result = spawnSync(process.execPath, ['node_modules/@angular/cli/bin/ng.js', 'test', '--configuration=qa', '--watch=false', '--reporters=json', '--output-file=' + resolve(output, 'tests.json')], {
    cwd: frontend, windowsHide: true, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024,
    env: { ...process.env, NG_CLI_ANALYTICS: 'false', NG_BUILD_MAX_WORKERS: '2' },
    timeout: 240000,
  });
  writeFileSync(resolve(output, 'run.log'), (result.stdout || '') + (result.stderr || ''));
  process.stdout.write((result.stdout || '') + (result.stderr || ''));
  if (result.error) throw result.error;
} finally {
  for (const [file, bytes] of originals) writeFileSync(file, bytes);
}
process.exitCode = result.status ?? 1;
