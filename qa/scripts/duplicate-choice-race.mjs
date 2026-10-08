import { readFileSync, writeFileSync } from 'node:fs';
import { createHmac, randomUUID } from 'node:crypto';
import { resolve } from 'node:path';
const workspace = resolve(process.argv[2] || '.');
const local = resolve(workspace, 'qa/.local/full-qa-runtime');
const accounts = JSON.parse(readFileSync(resolve(local, 'seed-accounts.json'), 'utf8')).accounts;
const actor = accounts[90], target = accounts[91], warmupTarget = accounts[95];
if (![actor,target,warmupTarget].every(a => Number.isInteger(a.userId) && a.email.endsWith('@woven.invalid'))) throw new Error('Verified synthetic actors required');
const key = readFileSync(resolve(local, 'runtime.env'), 'utf8').split(/\r?\n/).find(l => l.startsWith('WOVEN_QA_JWT_KEY=')).slice('WOVEN_QA_JWT_KEY='.length);
const now = Math.floor(Date.now()/1000);
const header = Buffer.from('{"alg":"HS256","typ":"JWT"}').toString('base64url');
const payload = Buffer.from(JSON.stringify({ uid: `${actor.userId}`, sub: `${actor.userId}`, iss:'WovenBackend', aud:'WovenFrontend', exp:now+3600, nbf:now-60 })).toString('base64url');
const token = `${header}.${payload}.${createHmac('sha256',key).update(`${header}.${payload}`).digest('base64url')}`;
const raceId = `qa-duplicate-${randomUUID()}`;
async function choose(userId, idempotency) {
 const response = await fetch('http://127.0.0.1:5181/moments/choose',{method:'POST',headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json','X-Idempotency-Key':idempotency},body:JSON.stringify({targetUserId:userId,choice:'MAGICAL',source:'TODAY',noteText:'Synthetic duplicate QA note: consistency and thoughtful communication.'}),signal:AbortSignal.timeout(30000)});
 const raw = await response.text(); let data; try {data=JSON.parse(raw);}catch{data={unparsed:true};}
 return {status:response.status,error:data.error,statusCode:data.status,correlation:response.headers.get('X-Correlation-ID')};
}
const warmup = await choose(warmupTarget.userId, raceId+'-warmup');
if(warmup.status!==200)throw new Error('Warmup did not create fresh fixture state; use untouched actor indexes before rerun');
const requests = await Promise.all([1,2,3,4].map(()=>choose(target.userId,raceId)));
const result = {applicationCandidate:'5461c85',schemaMode:'model-created local QA',actor:actor.userId,target:target.userId,warmup,requests,scope:'Four identical simultaneous TODAY choices after one distinct warmup. Requires DB delta verification; reruns reuse synthetic state.'};
writeFileSync(resolve(local,'duplicate-choice-results.json'),JSON.stringify(result,null,2));
console.log(JSON.stringify(result,null,2));
