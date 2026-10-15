import { readFileSync, writeFileSync } from 'node:fs';
import { createHmac, randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
const directory = fileURLToPath(new URL('../.local/full-qa-runtime/', import.meta.url));
const accounts = JSON.parse(readFileSync(`${directory}/seed-accounts.json`, 'utf8')).accounts;
const actor = accounts[50];
if (!actor.email.endsWith('@woven.invalid')) throw new Error('Synthetic actor required');
const secret = readFileSync(`${directory}/runtime.env`, 'utf8').split(/\r?\n/).find(l => l.startsWith('WOVEN_QA_JWT_KEY=')).slice('WOVEN_QA_JWT_KEY='.length);
const now = Math.floor(Date.now() / 1000);
const head = Buffer.from('{"alg":"HS256","typ":"JWT"}').toString('base64url');
const payload = Buffer.from(JSON.stringify({ uid: `${actor.userId}`, sub: `${actor.userId}`, iss: 'WovenBackend', aud: 'WovenFrontend', exp: now + 3600, nbf: now - 60, jti: randomUUID() })).toString('base64url');
const token = `${head}.${payload}.${createHmac('sha256', secret).update(`${head}.${payload}`).digest('base64url')}`;
const results = await Promise.all(accounts.slice(60, 68).map(async target => {
  const start = performance.now();
  try {
    const response = await fetch('http://127.0.0.1:5181/moments/choose', {
      method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ targetUserId: target.userId, choice: 'MAGICAL', source: 'TODAY', noteText: 'Synthetic concurrency QA note: consistency and clear communication.' }),
      signal: AbortSignal.timeout(45000)
    });
    const data = await response.json();
    return { target: target.userId, status: response.status, error: data.error, durationMs: Math.round(performance.now() - start) };
  } catch (error) { return { target: target.userId, status: null, error: error.message, durationMs: Math.round(performance.now() - start) }; }
}));
const successes = results.filter(r => r.status === 200).length;
const summary = { actor: actor.userId, scope: 'Eight simultaneous distinct positive choices from one synthetic user; reruns reuse state and cannot prove a fresh cap.', successes, unexpected: results.filter(r => ![200, 400].includes(r.status)).length, results };
writeFileSync(`${directory}/concurrency-results.json`, JSON.stringify(summary, null, 2));
console.log(JSON.stringify(summary, null, 2));
