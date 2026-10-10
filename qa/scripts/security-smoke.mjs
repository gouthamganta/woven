import { readFileSync, writeFileSync } from 'node:fs';
import { createHmac, randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
const directory = fileURLToPath(new URL('../.local/full-qa-runtime/', import.meta.url));
const env = Object.fromEntries(readFileSync(`${directory}/runtime.env`, 'utf8').split(/\r?\n/).filter(Boolean).map(l => { const i = l.indexOf('='); return [l.slice(0, i), l.slice(i + 1)]; }));
const accounts = JSON.parse(readFileSync(`${directory}/seed-accounts.json`, 'utf8')).accounts;
if (!accounts.every(a => a.email.endsWith('@woven.invalid'))) throw new Error('Synthetic accounts required');
const followup = process.argv.includes('--signalr-only');
const results = followup ? JSON.parse(readFileSync(`${directory}/security-results.json`, 'utf8')).results.filter(r => !r.id.startsWith('SIGNALR-')) : [];
function jwt(id) {
  const now = Math.floor(Date.now() / 1000);
  const a = Buffer.from('{"alg":"HS256","typ":"JWT"}').toString('base64url');
  const b = Buffer.from(JSON.stringify({ uid: `${id}`, sub: `${id}`, iss: 'WovenBackend', aud: 'WovenFrontend', exp: now + 3600, nbf: now - 60, jti: randomUUID() })).toString('base64url');
  return `${a}.${b}.${createHmac('sha256', env.WOVEN_QA_JWT_KEY).update(`${a}.${b}`).digest('base64url')}`;
}
async function check(id, path, user, method = 'GET', body, predicate = r => r.status === 200, headers = {}) {
  try {
    const start = performance.now();
    const response = await fetch('http://127.0.0.1:5181' + path, { method, headers: { ...(user ? { Authorization: `Bearer ${jwt(user)}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}), ...headers }, body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(20000) });
    const raw = await response.text(); let data; try { data = JSON.parse(raw); } catch { data = raw.slice(0, 200); }
    const actual = { status: response.status, data, durationMs: Math.round(performance.now() - start), allowOrigin: response.headers.get('access-control-allow-origin') };
    const pass = predicate(actual);
    results.push({ id, path, result: pass ? 'passed' : 'failed', ...actual }); console.log(`${pass ? 'PASS' : 'FAIL'} ${id}: ${response.status}`); return actual;
  } catch (error) { results.push({ id, path, result: 'blocked', reason: error.message }); }
}
if (!followup) {
const life = JSON.parse(readFileSync(`${directory}/lifecycle-results-80.json`, 'utf8'));
const matchId = life.results.find(r => r.id === 'CHOOSE-B').data.matchId;
const threadId = life.results.find(r => r.id === 'CHAT-START').data.threadId;
await check('BLOCK-DEDICATED', `/matches/${matchId}/block`, accounts[80].userId, 'POST', {});
await check('BLOCK-REJECTS-SEND', `/chats/${threadId}/messages`, accounts[81].userId, 'POST', { body: 'Synthetic blocked message.' }, r => [400, 403, 404, 409].includes(r.status));
await check('BLOCK-LIST', '/me/blocks', accounts[80].userId, 'GET', undefined, r => r.status === 200 && JSON.stringify(r.data).includes(`${accounts[81].userId}`));
await check('PRODUCTION-DEV-LOGIN-ABSENT', `/dev/login/${accounts[80].userId}`, undefined, 'POST', undefined, r => r.status === 404);
await check('CORS-UNTRUSTED-ORIGIN', '/me/data-summary', accounts[80].userId, 'GET', undefined, r => !r.allowOrigin, { Origin: 'https://qa-untrusted.invalid' });
await check('EXPORT-OWN', '/me/data-export', accounts[98].userId);
await check('EXPORT-REPEAT-LIMIT', '/me/data-export', accounts[98].userId, 'GET', undefined, r => r.status === 429);
await check('DELETE-ANONYMOUS-DENIED', '/me/account', undefined, 'DELETE', undefined, r => r.status === 401);
// Only delete a designated synthetic account in the isolated model-created database.
await check('DELETE-SYNTHETIC-ACCOUNT', '/me/account', accounts[99].userId, 'DELETE');
await check('DELETED-TOKEN-REJECTED', '/me/data-summary', accounts[99].userId, 'GET', undefined, r => [401, 403, 404, 410].includes(r.status));
}
await check('SIGNALR-ANONYMOUS-DENIED', '/hubs/woven/negotiate?negotiateVersion=1', undefined, 'POST', undefined, r => r.status === 401);
await check('SIGNALR-AUTH-NEGOTIATE', '/hubs/woven/negotiate?negotiateVersion=1', accounts[80].userId, 'POST');
for (const r of results) if (r.data?.connectionToken) r.data = { ...r.data, connectionToken: '[redacted]', connectionId: '[redacted]' };
writeFileSync(`${directory}/security-results.json`, JSON.stringify({ databaseMode: 'model-created QA schema', results }, null, 2));
console.log(JSON.stringify({ passed: results.filter(r => r.result === 'passed').length, failed: results.filter(r => r.result === 'failed').length, blocked: results.filter(r => r.result === 'blocked').length }));
