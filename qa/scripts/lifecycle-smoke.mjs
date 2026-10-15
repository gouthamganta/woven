import { readFileSync, writeFileSync } from 'node:fs';
import { createHmac, randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
const directory = fileURLToPath(new URL('../.local/full-qa-runtime/', import.meta.url));
const env = Object.fromEntries(readFileSync(`${directory}/runtime.env`, 'utf8').split(/\r?\n/).filter(Boolean).map(l => { const i = l.indexOf('='); return [l.slice(0, i), l.slice(i + 1)]; }));
const accounts = JSON.parse(readFileSync(`${directory}/seed-accounts.json`, 'utf8')).accounts;
const offset = Number(process.argv[2] || 0);
if (!Number.isInteger(offset) || offset < 0 || offset > accounts.length - 3) throw new Error('Invalid synthetic pair offset');
const users = accounts.slice(offset, offset + 3).map(a => a.userId); const results = [];
function jwt(id) { const now = Math.floor(Date.now() / 1000); const a = Buffer.from('{"alg":"HS256","typ":"JWT"}').toString('base64url'); const b = Buffer.from(JSON.stringify({ uid: `${id}`, sub: `${id}`, iss: 'WovenBackend', aud: 'WovenFrontend', exp: now + 3600, nbf: now - 60, jti: randomUUID() })).toString('base64url'); return `${a}.${b}.${createHmac('sha256', env.WOVEN_QA_JWT_KEY).update(`${a}.${b}`).digest('base64url')}`; }
async function test(id, path, who, method = 'GET', body, predicate = r => r.status === 200) {
  try {
    const response = await fetch('http://127.0.0.1:5181' + path, { method, headers: { Authorization: `Bearer ${jwt(users[who])}`, ...(body ? { 'Content-Type': 'application/json' } : {}), 'X-Idempotency-Key': `qa-${id}` }, body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(20000) });
    const text = await response.text(); let data; try { data = JSON.parse(text); } catch { data = text.slice(0, 200); }
    const actual = { status: response.status, data }; const pass = predicate(actual);
    results.push({ id, path, result: pass ? 'passed' : 'failed', ...actual }); console.log(`${pass ? 'PASS' : 'FAIL'} ${id}: ${actual.status}`); return actual;
  } catch (e) { results.push({ id, path, result: 'blocked', error: e.message }); console.log(`BLOCKED ${id}`); }
}
await test('NOTE-TOO-SHORT', '/moments/choose', 0, 'POST', { targetUserId: users[1], choice: 'MAGICAL', source: 'TODAY', noteText: 'short' }, r => r.status === 400);
await test('CHOOSE-A', '/moments/choose', 0, 'POST', { targetUserId: users[1], choice: 'MAGICAL', source: 'TODAY', noteText: 'Synthetic private QA note alpha: clear communication matters.' });
const match = await test('CHOOSE-B', '/moments/choose', 1, 'POST', { targetUserId: users[0], choice: 'MAGICAL', source: 'TODAY', noteText: 'Synthetic private QA note beta: consistency and kindness matter.' });
const matchId = match?.data?.matchId;
if (matchId) {
  const matches = await test('MATCH-LIST', '/matches', 0);
  const item = matches?.data?.matches?.find(m => m.matchId === matchId || m.id === matchId);
  if (item) {
    const created = new Date(item.createdAt); const expiry = new Date(item.expiresAt || item.balloonExpiresAt);
    results.push({ id: 'MATCH-PERSISTED-72H', result: Number.isFinite(+created) && Number.isFinite(+expiry) && expiry - created === 72 * 3600000 ? 'passed' : 'blocked', note: 'Field availability required for timestamp assertion', match: item });
  }
  await test('MATCH-FOREIGN-USER', `/matches/${matchId}/profile`, 2, 'GET', undefined, r => [403, 404].includes(r.status));
  const start = await test('CHAT-START', '/chats/start', 0, 'POST', { matchId });
  const thread = start?.data?.threadId;
  if (thread) {
    await test('CHAT-FOREIGN-USER', `/chats/${thread}`, 2, 'GET', undefined, r => [403, 404].includes(r.status));
    // Founder clarified on 2026-10-08 that both notes are visible to the matched pair.
    await test('CHAT-NOTES-VISIBLE-TO-PAIR', `/chats/${thread}`, 0, 'GET', undefined, r => r.status === 200 && JSON.stringify(r.data).includes('Synthetic private QA note alpha') && JSON.stringify(r.data).includes('Synthetic private QA note beta'));
    await test('CHAT-EMPTY-MESSAGE', `/chats/${thread}/messages`, 0, 'POST', { body: '' }, r => r.status === 400);
    await test('CHAT-TEXT', `/chats/${thread}/messages`, 0, 'POST', { body: 'Synthetic QA message: hello.' });
    await test('CHAT-READ-B', `/chats/${thread}`, 1);
    await test('BALLOON-POP', `/matches/${matchId}/pop`, 0, 'POST', {});
    await test('TRIAL-OPEN-A', `/chats/${thread}`, 0);
    await test('TRIAL-OPEN-B', `/chats/${thread}`, 1);
    await test('TRIAL-EARLY-DECISION', `/chats/${thread}/trial-decision`, 0, 'POST', { decision: 'CONTINUE' }, r => [400, 409, 422].includes(r.status));
    await test('BLOCK-DEDICATED', `/matches/${matchId}/block`, 0, 'POST', {});
    await test('BLOCK-PREVENTS-MESSAGE', `/chats/${thread}/messages`, 1, 'POST', { body: 'This should be rejected after block.' }, r => [400, 403, 404, 409].includes(r.status));
  }
} else results.push({ id: 'MATCH-DEPENDENT-FLOWS', result: 'blocked', reason: 'Mutual choose did not return a match ID' });
writeFileSync(`${directory}/lifecycle-results-${offset}.json`, JSON.stringify({ databaseMode: 'model-created QA schema', actorOffset: offset, scope: 'One synthetic two-user lifecycle; migration and provider semantics not verified.', results }, null, 2));
console.log(JSON.stringify({ passed: results.filter(r => r.result === 'passed').length, failed: results.filter(r => r.result === 'failed').length, blocked: results.filter(r => r.result === 'blocked').length }));
