import { readFileSync, writeFileSync } from 'node:fs';
import { createHmac, randomUUID } from 'node:crypto';
import { resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
const workspace = resolve(process.argv[2] || '.');
const local = resolve(workspace, 'qa/.local/full-qa-runtime');
const accounts = JSON.parse(readFileSync(resolve(local, 'seed-accounts.json'), 'utf8')).accounts;
const offset=Number(process.argv[3]||70);
if(!Number.isInteger(offset)||offset<0||offset>accounts.length-6)throw new Error('Invalid untouched fixture offset');
const actor = accounts[offset], target = accounts[offset+1], warmupTarget = accounts[offset+5];
if (![actor,target,warmupTarget].every(a => Number.isInteger(a.userId) && a.email.endsWith('@woven.invalid'))) throw new Error('Verified synthetic actors required');
const key = readFileSync(resolve(local, 'runtime.env'), 'utf8').split(/\r?\n/).find(l => l.startsWith('WOVEN_QA_JWT_KEY=')).slice('WOVEN_QA_JWT_KEY='.length);
const now = Math.floor(Date.now()/1000);
const header = Buffer.from('{"alg":"HS256","typ":"JWT"}').toString('base64url');
const payload = Buffer.from(JSON.stringify({ uid: `${actor.userId}`, sub: `${actor.userId}`, iss:'WovenBackend', aud:'WovenFrontend', exp:now+3600, nbf:now-60 })).toString('base64url');
const token = `${header}.${payload}.${createHmac('sha256',key).update(`${header}.${payload}`).digest('base64url')}`;
const raceId = `qa-duplicate-${randomUUID()}`;
function persisted(){
 const sql=`SELECT json_build_object('totalUsed',COALESCE((SELECT total_used FROM daily_interactions WHERE user_id=${actor.userId} AND date_utc=CURRENT_DATE),0),'responses',(SELECT COUNT(*) FROM moment_responses WHERE from_user_id=${actor.userId} AND date_utc=CURRENT_DATE),'notes',(SELECT COUNT(*) FROM chat_notes WHERE from_user_id=${actor.userId}),'targetResponses',(SELECT COUNT(*) FROM moment_responses WHERE from_user_id=${actor.userId} AND to_user_id=${target.userId} AND date_utc=CURRENT_DATE));`;
 const r=spawnSync('docker',['exec','-i','woven-qa-postgres-1','psql','-U','woven_qa','-d','woven_qa_model','-t','-A'],{encoding:'utf8',windowsHide:true,input:sql+'\n'});
 if(r.status!==0||!r.stdout.trim().startsWith('{'))throw new Error('Independent QA persistence query failed');
 return JSON.parse(r.stdout);
}
async function choose(userId, idempotency) {
 const response = await fetch('http://127.0.0.1:5181/moments/choose',{method:'POST',headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json','X-Idempotency-Key':idempotency},body:JSON.stringify({targetUserId:userId,choice:'MAGICAL',source:'TODAY',noteText:'Synthetic duplicate QA note: consistency and thoughtful communication.'}),signal:AbortSignal.timeout(30000)});
 const raw = await response.text(); let data; try {data=JSON.parse(raw);}catch{data={unparsed:true};}
 return {status:response.status,error:data.error,statusCode:data.status,correlation:response.headers.get('X-Correlation-ID')};
}
const initial=persisted();
if(initial.totalUsed!==0||initial.responses!==0||initial.notes!==0)throw new Error('Fixture already used; choose an untouched offset rather than reusing evidence');
const warmup = await choose(warmupTarget.userId, raceId+'-warmup');
if(warmup.status!==200)throw new Error('Warmup did not create fresh fixture state; use untouched actor indexes before rerun');
const requests = await Promise.all([1,2,3,4].map(()=>choose(target.userId,raceId)));
const after=persisted();
const passed=requests.every(r=>[200,409].includes(r.status))&&after.totalUsed===2&&after.responses===2&&after.notes===2&&after.targetResponses===1;
const result = {applicationCandidate:process.argv[4]||'unspecified',schemaMode:'model-created local QA',actorOffset:offset,actor:actor.userId,target:target.userId,initial,warmup,requests,after,result:passed?'passed':'failed',scope:'Four identical simultaneous TODAY choices after one warmup; independent PostgreSQL invariants. Reject/defined replay allowed, exactly one new persisted action and debit.'};
writeFileSync(resolve(local,`duplicate-choice-results-${offset}.json`),JSON.stringify(result,null,2));
console.log(JSON.stringify(result,null,2));
