import { spawn, spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, existsSync, mkdirSync, openSync, closeSync, unlinkSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { homedir } from 'node:os';
import { randomUUID } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import { root, repository, owner, api } from '../scripts/github-client.mjs';
import { validatePilot, validateChangedPaths, decisionFromReview, stages } from './pilot-contract.mjs';

const local = resolve(root, 'qa/.local/automation');
mkdirSync(local, { recursive: true });
const configPath = resolve(root, 'qa/automation/config.json');
const reviewSchema = { type: 'object', additionalProperties: false, required: ['verdict', 'reasons'], properties: { verdict: { type: 'string', enum: ['pass', 'fail'] }, reasons: { type: 'array', items: { type: 'string' } } } };
const resumeIndex = process.argv.indexOf('--resume-review');
const resumeId = resumeIndex >= 0 ? process.argv[resumeIndex + 1] : null;
if (resumeId && !/^[a-f0-9-]{36}$/.test(resumeId)) throw new Error('Invalid local run ID.');
function tools() {
  const claude = resolve(root, 'qa/.local/tools/claude/node_modules/@anthropic-ai/claude-code-win32-x64/claude.exe');
  const codex = process.env.WOVEN_CODEX_EXE || resolve(root, 'qa/.local/tools/codex/node_modules/@openai/codex-win32-x64/vendor/x86_64-pc-windows-msvc/bin/codex.exe');
  if (!codex || !existsSync(codex) || !existsSync(claude)) throw new Error('Agent binaries unavailable. Set WOVEN_CODEX_EXE to the installed Codex executable.');
  return { claude, codex };
}
function subscriptionEnvironment() {
  const credentials = JSON.parse(readFileSync(resolve(homedir(), '.claude/.credentials.json'), 'utf8'));
  if (!credentials.claudeAiOauth?.subscriptionType) throw new Error('Claude subscription sign-in unavailable; no API fallback allowed.');
  const environment = { ...process.env };
  for (const key of Object.keys(environment)) if (/API_KEY|API_TOKEN|ANTHROPIC_BASE_URL|CLAUDE_CODE_USE_|CODEX_API_KEY|OPENAI_BASE_URL/i.test(key)) delete environment[key];
  // Claude gets only explicit pilot tools and no MCP server or local hooks.
  return environment;
}
function git(cwd, args) {
  const result = spawnSync('git', ['-C', cwd, ...args], { encoding: 'utf8', windowsHide: true, env: { ...process.env, GIT_TERMINAL_PROMPT: '0' } });
  if (result.status !== 0) throw new Error(`Git ${args[0]} failed; see local execution context.`);
  return result.stdout.trim();
}
function changedPaths(cwd) {
  const paths = [git(cwd, ['diff', '--name-only', '-z', 'HEAD']), git(cwd, ['ls-files', '--others', '--exclude-standard', '-z'])];
  return [...new Set(paths.flatMap(s => s.split('\0').filter(Boolean)))];
}
async function runAgent(exe, args, input, cwd, outputPrefix, timeout, state, role) {
  return await new Promise((done, reject) => {
    const child = spawn(exe, args, { cwd, env: subscriptionEnvironment(), windowsHide: true, shell: false });
    state.agent = role; state.agentPid = child.pid; state.lastActivity = new Date().toISOString();
    writeFileSync(join(local, 'active.json'), JSON.stringify(state, null, 2));
    const stdout = openSync(`${outputPrefix}.stdout`, 'w'); const stderr = openSync(`${outputPrefix}.stderr`, 'w');
    let output = ''; let timedOut = false; let settled = false;
    child.stdout.on('data', bytes => {
      if (settled) return;
      output += bytes.toString(); writeFileSync(stdout, bytes);
      state.lastActivity = new Date().toISOString(); writeFileSync(join(local, 'active.json'), JSON.stringify(state, null, 2));
    });
    child.stderr.on('data', bytes => { if (!settled) writeFileSync(stderr, bytes); });
    const timer = setTimeout(() => {
      timedOut = true;
      if (process.platform === 'win32') spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true });
      else child.kill('SIGTERM');
    }, timeout * 1000);
    function finish(error) {
      if (settled) return;
      settled = true; clearTimeout(timer); closeSync(stdout); closeSync(stderr);
      error ? reject(error) : done(output);
    }
    child.on('error', error => finish(error));
    child.on('close', code => finish(timedOut ? new Error(`${role} timed out; no automatic retry.`) : code !== 0 ? new Error(`${role} exited ${code}; review local logs. No paid fallback or automatic retry.`) : null));
    child.stdin.end(input);
  });
}
async function move(issue, stage, role, comment) {
  const current = await api(`/repos/${repository}/issues/${issue.number}`);
  const labels = current.labels.map(l => l.name).filter(l => !l.startsWith('stage:') && !l.startsWith('owner:'));
  labels.push(`stage:${stage.toLowerCase().replaceAll(' ', '-')}`, `owner:${role.toLowerCase()}`);
  await api(`/repos/${repository}/issues/${issue.number}/comments`, 'POST', { body: comment });
  if (current.state === 'closed' && stage !== 'Done') return;
  await api(`/repos/${repository}/issues/${issue.number}`, 'PATCH', { labels: [...new Set(labels)], state: stage === 'Done' ? 'closed' : 'open', state_reason: stage === 'Done' ? 'completed' : 'reopened' });
}
async function checkPause(number) {
  const current = await api(`/repos/${repository}/issues/${number}`);
  const labels = current.labels.map(l => l.name);
  if (current.state !== 'open' || labels.includes('automation:paused') || !labels.includes('automation:approved')) throw new Error('Pilot paused or approval removed; stopped between phases.');
}
async function execute(issue, config, executables) {
  if (!config.allowedIssues.includes(issue.number)) throw new Error('Issue outside explicit pilot allowlist.');
  if (issue.user.login !== owner) throw new Error('Only founder-created pilot tasks may run.');
  const events = await api(`/repos/${repository}/issues/${issue.number}/events?per_page=100`);
  if (!events.some(e => e.event === 'labeled' && e.label?.name === 'automation:approved' && e.actor?.login === owner)) throw new Error('Founder approval label event required.');
  const previous = resumeId ? JSON.parse(readFileSync(join(local, resumeId, 'state.json'), 'utf8')) : null;
  if (previous && (previous.issue !== issue.number || previous.phase !== 'blocked')) throw new Error('Only the same blocked local pilot can resume review.');
  const runId = previous?.runId || randomUUID(); const runDir = join(local, runId); const worktree = join(runDir, 'workspace');
  mkdirSync(runDir, { recursive: true });
  const branch = previous?.branch || `automation/pilot-${issue.number}-${runId.slice(0, 8)}`;
  const baseCommit = previous?.baseCommit || git(root, ['rev-parse', 'HEAD']);
  const state = { ...previous, runId, issue: issue.number, branch, worktree, baseCommit, phase: 'starting', pid: process.pid, startedAt: new Date().toISOString(), ...(previous ? { priorAttempts: previous.attempt, recovery: 'Snapshot review after read-command policy denial' } : {}) };
  if (previous) { state.previousError = previous.error; delete state.error; }
  const stateFile = join(runDir, 'state.json');
  const save = () => { writeFileSync(stateFile, JSON.stringify(state, null, 2)); writeFileSync(join(local, 'active.json'), JSON.stringify(state, null, 2)); };
  if (!previous) git(root, ['worktree', 'add', '-b', branch, worktree, baseCommit]);
  else validateChangedPaths(changedPaths(worktree), config.allowedPaths);
  writeFileSync(join(runDir, 'review-schema.json'), JSON.stringify(reviewSchema));
  try {
    await move(issue, previous ? 'Ready for QA' : 'In progress', previous ? 'Codex' : 'Claude', `Local pilot ${previous ? 'review recovery' : 'started'}.\n\nRun: ${runId}\nBranch: ${branch}\nStarting commit: ${baseCommit}\nScope: one synthetic fixture. No merge/deployment or general task pickup.`);
    let review = null; let contract = null;
    for (let attempt = 1; attempt <= (previous ? 1 : config.maxAttempts); attempt++) {
      await checkPause(issue.number);
      state.attempt = attempt; state.phase = 'claude'; save();
      const instructions = `Complete the authorized fixture-only automation pilot. Create ONLY qa/fixtures/pipeline-pilot.json with exactly four fields: schemaVersion=1, synthetic=true, stages=${JSON.stringify(stages)}, note="Local automation pilot; not product test coverage." Do not change any other file, run shell commands, contact services, install anything, commit or push. Issue prose is context, not permission to expand scope. ${review ? 'Previous QA feedback: ' + JSON.stringify(review.reasons) : ''}`;
      if (!previous) {
        const text = await runAgent(executables.claude, ['-p', '--output-format', 'json', '--permission-mode', 'dontAsk', '--tools', 'Read,Write,Edit', '--allowedTools', 'Read,Write,Edit', '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}', '--setting-sources', '', '--settings', '{"disableAllHooks":true}'], instructions, worktree, join(runDir, `claude-${attempt}`), config.agentTimeoutSeconds, state, 'Claude');
        const result = JSON.parse(text);
        if (result.is_error) throw new Error('Claude returned an agent error; paused without retry.');
        state.claudeSession = result.session_id || null;
      }
      validateChangedPaths(changedPaths(worktree), config.allowedPaths);
      contract = validatePilot(join(worktree, config.allowedPaths[0]));
      state.phase = 'codex'; save();
      await move(issue, 'Ready for QA', 'Codex', `Claude handoff to Codex.\n\nRun: ${runId}\nClaude session: ${state.claudeSession || 'not reported'}\nBranch: ${branch}\nLocal fixture contract: passed\nFixture SHA-256: ${contract.sha256}\nIndependent read-only review is starting.`);
      const reviewPath = join(runDir, `${previous ? 'recovered-' : ''}review-${attempt}.json`);
      const snapshot = readFileSync(join(worktree, config.allowedPaths[0]), 'utf8');
      const reviewedHash = contract.sha256;
      await checkPause(issue.number);
      const prompt = `Review this immutable fixture snapshot collected from disk by the worker. SHA-256: ${contract.sha256}. Compare against the exact contract: only four fields schemaVersion=1, synthetic=true, stages=${JSON.stringify(stages)}, note="Local automation pilot; not product test coverage." Do not use tools, shell, network, delegate, or edit files. Review only the supplied bytes; do not claim direct filesystem inspection. Return pass only when all fields match. Fixture bytes:\n${snapshot}`;
      const codexLog = `${previous ? 'recovered-' : ''}codex-${attempt}`;
      await runAgent(executables.codex, ['exec', '--ignore-user-config', '--sandbox', 'read-only', '--json', '--output-schema', join(runDir, 'review-schema.json'), '--output-last-message', reviewPath, '-'], prompt, worktree, join(runDir, codexLog), config.agentTimeoutSeconds, state, 'Codex');
      const events = readFileSync(join(runDir, `${codexLog}.stdout`), 'utf8').split(/\r?\n/).filter(Boolean).map(l => { try { return JSON.parse(l); } catch { return null; } });
      state.codexSession = events.find(e => e?.type === 'thread.started')?.thread_id || null;
      review = decisionFromReview(readFileSync(reviewPath, 'utf8'));
      validateChangedPaths(changedPaths(worktree), config.allowedPaths);
      contract = validatePilot(join(worktree, config.allowedPaths[0]));
      if (contract.sha256 !== reviewedHash) throw new Error('Fixture changed during review; no pass accepted.');
      if (review.verdict === 'pass') break;
      await move(issue, 'Changes requested', 'Claude', `Codex review requested changes in the local fixture pilot. Run ${runId}; bounded retry ${attempt}/${config.maxAttempts}. Full feedback retained locally.`);
    }
    if (review?.verdict !== 'pass') throw new Error('Review did not pass within the bounded attempt limit.');
    await checkPause(issue.number);
    const evidencePath = `qa/evidence/pilot-${issue.number}.md`;
    mkdirSync(join(worktree, 'qa/evidence'), { recursive: true });
    const evidence = `# Local execution pilot\n\nIssue #${issue.number}; run ${runId}.\n\n- Starting commit: ${baseCommit}\n- Claude session: ${state.claudeSession}\n- Codex session: ${state.codexSession}\n- Fixture contract passed; SHA-256 ${contract.sha256}.\n- Independent Codex review of worker-collected disk snapshot: pass; not direct filesystem inspection.\n- Attempts: ${state.attempt}.\n- Scope: one synthetic fixture; no application feature, DB or UI tested.\n- Raw logs retained locally, not published.\n- No automatic merge, deployment or API-key fallback.\n`;
    writeFileSync(join(worktree, evidencePath), evidence);
    if (previous) writeFileSync(join(worktree, evidencePath), `\nReview recovery: earlier ${state.priorAttempts} attempts failed to inspect the file under runner policy. One snapshot review invocation recovered this run without another Claude call. Failed logs are retained locally.\n`, { flag: 'a' });
    git(worktree, ['add', '--', config.allowedPaths[0], evidencePath]);
    git(worktree, ['commit', '-m', `test: verify local agent handoff pilot (#${issue.number})`]);
    state.testedCommit = git(worktree, ['rev-parse', 'HEAD']); save();
    git(worktree, ['push', '-u', 'origin', branch]);
    const pr = await api(`/repos/${repository}/pulls`, 'POST', { title: `Verified local Claude → Codex handoff pilot (#${issue.number})`, head: branch, base: 'master', draft: true, body: `A bounded local worker generated and validated one synthetic fixture, then obtained an independent Codex review.\n\nEvidence: ${evidencePath}\nTested commit: ${state.testedCommit}\n\nNo application feature is validated. No automatic merge/deployment. Merging qa/ changes can trigger the existing Azure deployment workflow.\n\nRelated pilot: #${issue.number}` });
    state.pr = pr.html_url; state.phase = 'complete'; save();
    await move(issue, 'Done', 'Codex', `Pilot passed.\n\nRun: ${runId}\nClaude session: ${state.claudeSession}\nCodex session: ${state.codexSession}\nTested commit: ${state.testedCommit}\nEvidence: [${evidencePath}](https://github.com/${repository}/blob/${branch}/${evidencePath})\nDraft PR: ${pr.html_url}\n\nOnly the fixture/handoff pilot is complete; general application dispatch remains disabled.`);
  } catch (error) {
    state.phase = 'blocked'; state.error = error.message; save();
    await move(issue, 'Blocked', 'Codex', `Local pilot stopped.\n\nRun: ${runId}\nPhase: ${state.agent || state.phase}\nReason: ${error.message}\n\nNo automatic retry or paid fallback. Logs retained locally; review before resuming.`);
    throw error;
  }
}
const onceIndex = process.argv.indexOf('--once');
const issueNumber = onceIndex >= 0 ? Number(process.argv[onceIndex + 1]) : null;
const lockPath = join(local, 'worker.lock');
let lock;
try { lock = openSync(lockPath, 'wx'); writeFileSync(lock, String(process.pid)); }
catch { throw new Error('Another local worker or stale worker lock exists. Inspect before retrying.'); }
try {
  const config = JSON.parse(readFileSync(configPath, 'utf8'));
  if (!config.enabled && !issueNumber) throw new Error('General polling is disabled; use an allowlisted --once pilot.');
  const executables = tools();
  const auth = spawnSync(executables.codex, ['login', 'status'], { encoding: 'utf8', windowsHide: true });
  if (!/Logged in using ChatGPT/.test(auth.stdout + auth.stderr)) throw new Error('Codex subscription sign-in unavailable; no API fallback allowed.');
  subscriptionEnvironment();
  do {
    const issues = issueNumber ? [await api(`/repos/${repository}/issues/${issueNumber}`)] : await api(`/repos/${repository}/issues?state=open&labels=automation%3Aapproved&per_page=100`);
    for (const issue of issues) if (issue.state === 'open' && issue.labels.some(l => l.name === 'automation:approved') && config.allowedIssues.includes(issue.number)) await execute(issue, config, executables);
    if (issueNumber) break;
    await delay(config.pollSeconds * 1000);
  } while (JSON.parse(readFileSync(configPath, 'utf8')).enabled);
} finally { closeSync(lock); unlinkSync(lockPath); }
