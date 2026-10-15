import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

const [unitPath, httpPath, outputPath, unitIdentityPath, httpIdentityPath] = process.argv.slice(2);
if (!unitPath || !httpPath || !outputPath || !unitIdentityPath || !httpIdentityPath) throw new Error('Usage: merge-backend-coverage.mjs <unit Coverage JSON> <HTTP Coverage JSON> <summary JSON> <unit identity.json> <HTTP identity.json>');
const unitIdentity = JSON.parse(readFileSync(unitIdentityPath));
const httpIdentity = JSON.parse(readFileSync(httpIdentityPath));
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
if (!unitIdentity.sourceBinarySha256 || unitIdentity.sourceBinarySha256 !== httpIdentity.sourceBinarySha256 || !httpIdentity.copyRestored) throw new Error('Unit/API source binary identity mismatch.');
if (hash(unitPath) !== unitIdentity.rawJsonSha256) throw new Error('Unit coverage report identity mismatch.');
const files = new Map();
const sources = [unitPath, httpPath].map(path => {
  const bytes = readFileSync(path);
  return { path, bytes, report: JSON.parse(bytes) };
});
for (const { report } of sources) {
  if (!report['WovenBackend.dll']) throw new Error('Expected WovenBackend.dll coverage.');
  for (const [absolute, classes] of Object.entries(report['WovenBackend.dll'])) {
    const normalized = absolute.replaceAll('\\', '/');
    const marker = '/backend/WovenBackend/';
    const index = normalized.indexOf(marker);
    if (index < 0) throw new Error('Unrecognized source root; refusing an ambiguous merge.');
    const file = normalized.slice(index + marker.length);
    const existing = files.get(file) || { lines: new Map(), branches: new Map() };
    for (const [className, methods] of Object.entries(classes)) {
      for (const [method, counters] of Object.entries(methods)) {
        for (const [line, hits] of Object.entries(counters.Lines)) {
          if (!Number.isFinite(hits) || hits < 0) throw new Error('Invalid line counter.');
          existing.lines.set(line, Boolean(existing.lines.get(line)) || hits > 0);
        }
        for (const branch of counters.Branches) {
          const id = [className, method, branch.Line, branch.Offset, branch.EndOffset, branch.Path, branch.Ordinal].join('|');
          if (!Number.isFinite(branch.Hits) || branch.Hits < 0) throw new Error('Invalid branch counter.');
          existing.branches.set(id, Boolean(existing.branches.get(id)) || branch.Hits > 0);
        }
      }
    }
    files.set(file, existing);
  }
}
const rows = [...files].map(([file, counters]) => ({
  file, lines: counters.lines.size, coveredLines: [...counters.lines.values()].filter(Boolean).length,
  branches: counters.branches.size, coveredBranches: [...counters.branches.values()].filter(Boolean).length,
}));
const migrations = rows.filter(row => row.file.startsWith('Migrations/'));
const app = rows.filter(row => !row.file.startsWith('Migrations/'));
const counts = app.reduce((result, row) => {
  for (const key of ['lines', 'coveredLines', 'branches', 'coveredBranches']) result[key] += row[key];
  return result;
}, { lines: 0, coveredLines: 0, branches: 0, coveredBranches: 0 });
const result = {
  scope: 'Union of matching source/method/IL-branch counters, not addition of percentages. Requires the same Debug application binary for both runs; caller records its SHA-256.',
  ...counts,
  sourceBinarySha256: unitIdentity.sourceBinarySha256,
  sameApplicationBinaryVerified: true,
  linePercent: Number((counts.coveredLines / counts.lines * 100).toFixed(2)),
  branchPercent: Number((counts.coveredBranches / counts.branches * 100).toFixed(2)),
  generatedMigrationLines: migrations.reduce((n, row) => n + row.lines, 0),
  files: app,
  sourceReportSha256: sources.map(source => createHash('sha256').update(source.bytes).digest('hex')),
};
writeFileSync(outputPath, JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ ...result, files: undefined }, null, 2));
