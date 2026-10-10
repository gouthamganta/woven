import { spawn, spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { homedir } from 'node:os';
import { root } from './github-client.mjs';

const [directory, promptFile, resultFile] = process.argv.slice(2);
const workspace = resolve(root, directory || '');
if (!workspace.startsWith(resolve(root, 'qa/.local') + '\\')) throw new Error('Use a dedicated local QA worktree.');
const credentials = JSON.parse(readFileSync(resolve(homedir(), '.claude/.credentials.json'), 'utf8'));
if (!credentials.claudeAiOauth?.subscriptionType) throw new Error('Subscription sign-in required; API fallback not allowed.');
const executable = resolve(root, 'qa/.local/tools/claude/node_modules/@anthropic-ai/claude-code-win32-x64/claude.exe');
if (!existsSync(executable)) throw new Error('Side-by-side Claude executable missing.');
const environment = { ...process.env };
for (const key of Object.keys(environment)) if (/API_KEY|API_TOKEN|ANTHROPIC_BASE_URL|CLAUDE_CODE_USE_/i.test(key)) delete environment[key];
const child = spawn(executable, ['-p', '--output-format', 'json', '--permission-mode', 'dontAsk', '--tools', 'Read,Write,Edit,Glob,Grep', '--allowedTools', 'Read,Write,Edit,Glob,Grep', '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}', '--setting-sources', '', '--settings', '{"disableAllHooks":true}'], { cwd: workspace, env: environment, windowsHide: true });
let stdout = ''; let stderr = '';
const timer = setTimeout(() => spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true }), 300000);
child.stdout.on('data', b => stdout += b);
child.stderr.on('data', b => stderr += b);
child.on('error', error => { clearTimeout(timer); console.error(error.message); process.exitCode = 1; });
child.on('close', code => {
  clearTimeout(timer); writeFileSync(resolve(root, resultFile), stdout); writeFileSync(resolve(root, resultFile + '.stderr'), stderr);
  if (code !== 0) { console.error(`Claude stopped with exit ${code}; inspect local logs. No retry or paid fallback.`); process.exitCode = 1; return; }
  const result = JSON.parse(stdout);
  console.log(JSON.stringify({ session: result.session_id, isError: result.is_error, summary: result.result }, null, 2));
  if (result.is_error) process.exitCode = 1;
});
child.stdin.end(readFileSync(resolve(root, promptFile), 'utf8'));
