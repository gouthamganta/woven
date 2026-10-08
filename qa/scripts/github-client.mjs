import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const root = fileURLToPath(new URL('../..', import.meta.url));
export const repository = 'gouthamganta/woven';
export const owner = 'gouthamganta';
export function findGh() {
  const path = resolve(root, 'qa/.local/tools/gh/bin/gh.exe');
  if (existsSync(path)) return path;
  const base = resolve(root, 'qa/.local/tools/gh');
  if (existsSync(base)) {
    for (const dir of readdirSync(base)) {
      const candidate = resolve(base, dir, 'bin/gh.exe');
      if (existsSync(candidate)) return candidate;
    }
  }
  return 'gh';
}
function credential() {
  if (process.env.GH_TOKEN || process.env.GITHUB_TOKEN) return process.env.GH_TOKEN || process.env.GITHUB_TOKEN;
  const gh = spawnSync(findGh(), ['auth', 'token', '--hostname', 'github.com'], { encoding: 'utf8', windowsHide: true });
  if (gh.status === 0 && gh.stdout.trim()) return gh.stdout.trim();
  const git = spawnSync('git', ['credential', 'fill'], {
    input: 'protocol=https\nhost=github.com\n\n', encoding: 'utf8', windowsHide: true,
    env: { ...process.env, GIT_TERMINAL_PROMPT: '0', GCM_INTERACTIVE: 'Never' },
  });
  if (git.status !== 0) throw new Error('GitHub login unavailable. Sign in locally; never paste a token into chat.');
  const line = git.stdout.split(/\r?\n/).find(l => l.startsWith('password='));
  if (!line) throw new Error('GitHub credential unavailable.');
  return line.slice('password='.length);
}
const token = credential(); // Kept in process memory only; never logged or written.
export async function api(path, method = 'GET', body) {
  const url = `https://api.github.com${path}`;
  const response = await fetch(url, {
    method, signal: AbortSignal.timeout(30000),
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'Content-Type': 'application/json', 'User-Agent': 'woven-control-room', 'X-GitHub-Api-Version': '2022-11-28' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  const data = text ? JSON.parse(text) : null;
  if (!response.ok) throw new Error(`GitHub ${response.status}: ${data?.message || 'Request failed'}`);
  return data;
}
export async function graphql(query, variables = {}) {
  const result = await api('/graphql', 'POST', { query, variables });
  if (result.errors?.length) throw new Error(result.errors.map(e => e.message).join('\n'));
  return result.data;
}
