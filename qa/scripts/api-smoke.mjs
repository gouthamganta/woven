import { readFileSync, writeFileSync } from 'node:fs';
import { createHmac, randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { resolve, relative, isAbsolute } from 'node:path';
const workspace = process.argv[2] ? resolve(process.argv[2]) : fileURLToPath(new URL('../..', import.meta.url));
const directory = resolve(workspace, 'qa/.local/full-qa-runtime');
const output = process.env.WOVEN_QA_RESULTS_DIRECTORY ? resolve(process.env.WOVEN_QA_RESULTS_DIRECTORY) : directory;
const privatePath = relative(resolve(workspace, 'qa/.local'), output);
if (privatePath.startsWith('..') || isAbsolute(privatePath)) throw new Error('Raw results must stay in the private QA directory.');
const config = Object.fromEntries(readFileSync(`${directory}/runtime.env`, 'utf8').split(/\r?\n/).filter(Boolean).map(l => { const i = l.indexOf('='); return [l.slice(0, i), l.slice(i + 1)]; }));
const base = process.env.WOVEN_QA_API_BASE || 'http://127.0.0.1:5181';
const local = new URL(base);
if (local.protocol !== 'http:' || !['localhost', '127.0.0.1'].includes(local.hostname) || !['5181', '5182'].includes(local.port)) throw new Error('Only verified loopback QA API ports are allowed.');
const records = [];
async function call(path, { method = 'GET', token, body, headers = {} } = {}) {
  const started = performance.now();
  const response = await fetch(base + path, { method, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}), ...headers }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(15000) });
  const text = await response.text(); let data; try { data = JSON.parse(text); } catch { data = text.slice(0, 200); }
  return { status: response.status, data, correlation: response.headers.get('x-correlation-id'), retryAfter: response.headers.get('retry-after'), durationMs: Math.round(performance.now() - started) };
}
async function check(id, path, options, expected) {
  try {
    const actual = await call(path, options);
    const passed = typeof expected === 'function' ? expected(actual) : expected.includes(actual.status);
    records.push({ id, path, result: passed ? 'passed' : 'failed', expected: typeof expected === 'function' ? 'scenario predicate' : expected, ...actual });
    console.log(`${passed ? 'PASS' : 'FAIL'} ${id}: ${actual.status}`); return actual;
  } catch (error) { records.push({ id, path, result: 'blocked', error: error.message }); console.log(`BLOCKED ${id}: ${error.message}`); }
}
function token(userId, extra = {}, secret = config.WOVEN_QA_JWT_KEY) {
  const now = Math.floor(Date.now() / 1000);
  const head = Buffer.from(JSON.stringify({ alg: 'HS256', typ: 'JWT' })).toString('base64url');
  const payload = Buffer.from(JSON.stringify({ uid: String(userId), sub: String(userId), email: 'qa@woven.invalid', iss: 'WovenBackend', aud: 'WovenFrontend', iat: now, nbf: now - 60, exp: now + 3600, jti: randomUUID(), ...extra })).toString('base64url');
  return `${head}.${payload}.${createHmac('sha256', secret).update(`${head}.${payload}`).digest('base64url')}`;
}
const ready = await check('OPS-READINESS', '/health/ready', {}, [200]);
if (!ready || ready.status !== 200) { writeFileSync(`${output}/api-results.json`, JSON.stringify(records, null, 2)); process.exit(2); }
const accounts = JSON.parse(readFileSync(`${directory}/seed-accounts.json`, 'utf8')).accounts;
const a = accounts[0].userId, b = accounts[1].userId, c = accounts[2].userId;
const ta = token(a), tb = token(b), tc = token(c);
for (const path of ['/moments', '/chats', '/matches', '/commons', '/tiles/mine', '/me/data-summary', '/me/accessibility', '/sparks/balance', '/coaching/current-summary', '/admin/moderation/queue']) await check(`AUTH-ANONYMOUS-${path}`, path, {}, [401]);
await check('AUTH-MALFORMED', '/sparks/balance', { token: 'invalid' }, [401]);
await check('AUTH-WRONG-SIGNATURE', '/sparks/balance', { token: token(a, {}, 'wrong-test-key-that-is-long-enough-for-sha256') }, [401]);
await check('AUTH-EXPIRED', '/sparks/balance', { token: token(a, { exp: Math.floor(Date.now() / 1000) - 300 }) }, [401]);
await check('AUTH-WRONG-AUDIENCE', '/sparks/balance', { token: token(a, { aud: 'wrong' }) }, [401]);
await check('AUTH-ADMIN-ESCALATION', '/admin/moderation/queue', { token: ta }, [403]);
await check('SPARK-INITIAL-GRANT', '/sparks/balance', { token: ta }, r => r.status === 200 && r.data.balance === 5);
await check('SPARK-NO-DOUBLE-GRANT', '/sparks/balance', { token: ta }, r => r.status === 200 && r.data.balance === 5);
await check('DATA-OWN-SUMMARY', '/me/data-summary', { token: ta }, r => r.status === 200 && r.data.userId === a);
await check('PROFILE-ACCESSIBILITY', '/me/accessibility', { token: ta }, [200]);
await check('MOMENTS-SELF-TARGET', '/moments/respond', { method: 'POST', token: ta, body: { targetUserId: a, choice: 'MAGICAL', source: 'TODAY' } }, [400]);
await check('MOMENTS-UNKNOWN-TARGET', '/moments/respond', { method: 'POST', token: ta, body: { targetUserId: 2147480000, choice: 'MAGICAL', source: 'TODAY' } }, [400]);
await check('MOMENTS-BAD-CHOICE', '/moments/respond', { method: 'POST', token: ta, body: { targetUserId: b, choice: 'INVALID', source: 'TODAY' } }, [400]);
await check('MEDIA-OTHER-OWNER', '/media/confirm', { method: 'POST', token: ta, body: { blobPath: `${b}/fixture.jpg`, container: 'profile-photo' } }, [403]);
await check('PUSH-DELETE-ANONYMOUS', '/me/push-subscription', { method: 'DELETE', body: { endpoint: 'https://qa.invalid/push' } }, [401]);
await check('PUSH-DELETE-OWN-NONEXISTENT', '/me/push-subscription', { method: 'DELETE', token: ta, body: { endpoint: 'https://qa.invalid/push' } }, [200]);
await check('CHATS-UNKNOWN-THREAD', `/chats/${randomUUID()}`, { token: ta }, [404]);
await check('CORRELATION-GENERATED', '/health/live', {}, r => r.status === 200 && /^[a-f0-9]{16}$/.test(r.correlation || ''));
await check('CORRELATION-BOUNDED-PROPOSED-SECURITY', '/health/live', { headers: { 'X-Correlation-ID': 'q'.repeat(1000) } }, r => r.status === 400 || (r.correlation || '').length <= 64);
await check('USER-A-MATCHES', '/matches', { token: ta }, [200]);
await check('USER-A-CHATS', '/chats', { token: ta }, [200]);
await check('USER-A-COMMONS', '/commons?page=1', { token: ta }, [200]);
await check('USER-A-DECK', '/moments', { token: ta }, [200]);
await check('USER-C-PROFILE', '/onboarding/state', { token: tc }, [200]);
await check('USER-B-Sparks', '/sparks/balance', { token: tb }, [200]);
writeFileSync(`${output}/api-results.json`, JSON.stringify({ scope: 'Local candidate API smoke; synthetic data. Not full lifecycle/security coverage.', records }, null, 2));
console.log(JSON.stringify({ passed: records.filter(r => r.result === 'passed').length, failed: records.filter(r => r.result === 'failed').length, blocked: records.filter(r => r.result === 'blocked').length }));
if (records.some(record => record.result !== 'passed')) process.exitCode = 1;
