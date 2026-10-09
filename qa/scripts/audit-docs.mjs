import { readFileSync, readdirSync, existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { resolve, relative, dirname } from 'node:path';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../..', import.meta.url));
const docs = resolve(root, 'docs');
const active = ['features', 'systems', 'architecture', 'api', 'development', 'business', 'security'];
const inventory = [];
const missing = [];
function walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = resolve(dir, entry.name);
    if (entry.isDirectory()) walk(path);
    else if (entry.name.endsWith('.md')) {
      const bytes = readFileSync(path);
      const text = bytes.toString('utf8');
      const rel = relative(root, path).replaceAll('\\', '/');
      inventory.push({ path: rel, lines: text.split(/\r?\n/).length, sha256: createHash('sha256').update(bytes).digest('hex'), review: 'not reviewed' });
      let fenced = false;
      for (const [i, line] of text.split(/\r?\n/).entries()) {
        if (/^\s*(```|~~~)/.test(line)) { fenced = !fenced; continue; }
        if (fenced) continue;
        for (const match of line.matchAll(/\]\(([^)]+)\)/g)) {
          let target = match[1].replace(/^<|>$/g, '').split('#')[0];
          if (!target || /^(?:[a-z]+:|\/\/)/i.test(target)) continue;
          target = target.split(/\s+["']/)[0];
          try { target = decodeURIComponent(target); } catch {}
          if (!existsSync(resolve(dirname(path), target))) missing.push({ document: rel, line: i + 1, target });
        }
      }
    }
  }
}
for (const area of active) if (existsSync(resolve(docs, area))) walk(resolve(docs, area));
// Index checked separately because it sits outside the active documentation areas.
const index = resolve(docs, 'INDEX.md');
if (existsSync(index)) {
  for (const [i, line] of readFileSync(index, 'utf8').split(/\r?\n/).entries()) {
    for (const m of line.matchAll(/\]\(([^)]+)\)/g)) {
      const target = m[1].split('#')[0];
      if (target && !/^[a-z]+:/i.test(target) && !existsSync(resolve(docs, target))) missing.push({ document: 'docs/INDEX.md', line: i + 1, target });
    }
  }
}
const counts = Object.fromEntries(active.map(area => [area, inventory.filter(f => f.path.startsWith(`docs/${area}/`)).length]));
const result = {
  commit: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(),
  scope: 'Structural scan only. Reading file bytes and checking link targets does not establish content review, line/anchor validity, source accuracy, or runtime behavior.',
  counts, total: inventory.length, missingLinks: missing, inventory,
};
const output = resolve(root, 'qa/evidence/docs-structure.json');
mkdirSync(dirname(output), { recursive: true });
writeFileSync(output, `${JSON.stringify(result, null, 2)}\n`);
console.log(JSON.stringify({ commit: result.commit, counts, total: result.total, missingLinkOccurrences: missing.length, missingIndexLinks: missing.filter(m => m.document === 'docs/INDEX.md'), report: 'qa/evidence/docs-structure.json' }, null, 2));
