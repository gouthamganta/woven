import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { createHash } from 'node:crypto';

const root = fileURLToPath(new URL('../..', import.meta.url));
const policy = JSON.parse(readFileSync(resolve(root, 'qa/coverage-policy.json')));
const [layer, inputPath, outputPath] = process.argv.slice(2);
if (!['backend', 'backend-combined', 'frontend'].includes(layer) || !inputPath) throw new Error('Usage: node qa/scripts/check-coverage.mjs backend|backend-combined|frontend <report> [output.json]');
const bytes = readFileSync(resolve(inputPath));
let lines, branches;
let generatedMigrations;
const reasons = [];
if (layer === 'backend') {
  const xml = bytes.toString('utf8');
  const header = xml.match(/<coverage\s[^>]+>/)?.[0];
  if (!header) throw new Error('Cobertura coverage root missing.');
  const attributes = Object.fromEntries([...header.matchAll(/([\w-]+)="([^"]*)"/g)].map(m => [m[1], m[2]]));
  const counts = ['lines-covered', 'lines-valid', 'branches-covered', 'branches-valid'].map(k => Number(attributes[k]));
  if (counts.some(n => !Number.isFinite(n)) || counts[1] <= 0 || counts[3] <= 0) throw new Error('Invalid or empty coverage denominator.');
  // Linux collection of Windows PDBs can retain backslash source names, so
  // Coverlet's file glob may leave generated migrations in its raw denominator.
  // Partition those files explicitly; never remove other application code.
  const migrationLines = new Map();
  for (const match of xml.matchAll(/<class\s([^>]+)>([\s\S]*?)<\/class>/g)) {
    const file = match[1].match(/filename="([^"]+)"/)?.[1];
    if (!file || !/(^|[\\/])Migrations[\\/]/.test(file)) continue;
    const blocks = [...match[2].matchAll(/<lines>([\s\S]*?)<\/lines>/g)];
    if (!blocks.length) continue;
    for (const line of blocks.at(-1)[1].matchAll(/<line\s([^>]+)>/g)) {
      const a = Object.fromEntries([...line[1].matchAll(/([\w-]+)="([^"]*)"/g)].map(m => [m[1], m[2]]));
      const key = file + ':' + a.number;
      if (a.branch === 'true') throw new Error('Generated migrations contain branch logic; separate manual classification required.');
      migrationLines.set(key, Boolean(migrationLines.get(key)) || Number(a.hits) > 0);
    }
  }
  generatedMigrations = { lines: migrationLines.size, coveredLines: [...migrationLines.values()].filter(Boolean).length };
  const applicationLines = counts[1] - generatedMigrations.lines;
  if (applicationLines <= 0) throw new Error('No application coverage denominator.');
  lines = (counts[0] - generatedMigrations.coveredLines) / applicationLines * 100;
  branches = counts[2] / counts[3] * 100;
} else if (layer === 'backend-combined') {
  const report = JSON.parse(bytes);
  if (!report.sameApplicationBinaryVerified || !report.sourceBinarySha256 || report.sourceReportSha256?.length !== 2 || report.lines <= 0 || report.branches <= 0) throw new Error('Incomplete combined report identity/denominator.');
  lines = report.linePercent;
  branches = report.branchPercent;
  generatedMigrations = { lines: report.generatedMigrationLines, separatelyReported: true };
} else {
  const report = JSON.parse(bytes);
  if (!report.completeTypeScriptDenominator || report.missing?.length || !report.typescriptFiles) reasons.push('Frontend TypeScript inventory incomplete.');
  lines = report.coverage?.lines?.pct;
  branches = report.coverage?.branches?.pct;
}
for (const [name, measured] of [['lines', lines], ['branches', branches]]) {
  if (!Number.isFinite(measured)) throw new Error(`Missing ${name} measurement.`);
  if (measured < policy.minimum[name]) reasons.push(`${name}: ${measured.toFixed(2)}% below ${policy.minimum[name]}%.`);
}
const result = { layer, target: policy.minimum, lines: Number(lines.toFixed(2)), branches: Number(branches.toFixed(2)), generatedMigrations, passed: reasons.length === 0, reasons, inputSha256: createHash('sha256').update(bytes).digest('hex') };
if (outputPath) writeFileSync(resolve(outputPath), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify(result, null, 2));
if (!result.passed) process.exitCode = 1;
