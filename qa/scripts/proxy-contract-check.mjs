import{readFileSync,writeFileSync}from'node:fs';
import{createHmac}from'node:crypto';
import{resolve}from'node:path';
const workspace=resolve(process.argv[2]||'.'),label=process.argv[3]||'unspecified';
const local=resolve(workspace,'qa/.local/full-qa-runtime');
const actor=JSON.parse(readFileSync(resolve(local,'seed-accounts.json'),'utf8')).accounts[98];
if(!actor.email.endsWith('@woven.invalid'))throw new Error('Synthetic actor required');
const key=readFileSync(resolve(local,'runtime.env'),'utf8').split(/\r?\n/).find(l=>l.startsWith('WOVEN_QA_JWT_KEY=')).slice('WOVEN_QA_JWT_KEY='.length);
const now=Math.floor(Date.now()/1000),h=Buffer.from('{"alg":"HS256","typ":"JWT"}').toString('base64url'),p=Buffer.from(JSON.stringify({uid:`${actor.userId}`,sub:`${actor.userId}`,iss:'WovenBackend',aud:'WovenFrontend',exp:now+3600,nbf:now-60})).toString('base64url');
const token=`${h}.${p}.${createHmac('sha256',key).update(`${h}.${p}`).digest('base64url')}`;
const results=[];
async function check(id,path,headers,method='GET',expected){
 try{const response=await fetch('http://127.0.0.1:5180'+path,{method,headers,signal:AbortSignal.timeout(20000)});const text=await response.text();const html=/text\/html/i.test(response.headers.get('content-type')||'');let json=false;try{JSON.parse(text);json=true;}catch{}
 const actual={status:response.status,html,json,frame:response.headers.get('x-frame-options'),nosniff:response.headers.get('x-content-type-options')};const passed=expected(actual);results.push({id,path,result:passed?'passed':'failed',...actual});console.log(`${passed?'PASS':'FAIL'} ${id}:${response.status}`);}catch(e){results.push({id,path,result:'blocked',error:e.message});}
}
for(const path of ['/me/data-summary','/me/accessibility','/commons?page=1','/tiles/mine','/sparks/balance','/moments','/chats','/matches'])await check('API-'+path,path,{Authorization:`Bearer ${token}`,Accept:'application/json, text/plain, */*'},'GET',r=>r.status===200&&r.json&&!r.html);
await check('API-COACHING','/coaching/current-summary',{Authorization:`Bearer ${token}`,Accept:'application/json'},'GET',r=>[200,204].includes(r.status)&&!r.html);
for(const path of ['/moments','/chats','/matches/00000000-0000-0000-0000-000000000001/profile','/onboarding/basics'])await check('DOCUMENT-'+path,path,{Accept:'text/html'},'GET',r=>r.status===200&&r.html&&r.frame==='SAMEORIGIN'&&r.nosniff==='nosniff');
await check('ANONYMOUS-API','/me/data-summary',{Accept:'application/json'},'GET',r=>r.status===401&&!r.html);
await check('SIGNALR-ANONYMOUS','/hubs/woven/negotiate?negotiateVersion=1',{Accept:'application/json'},'POST',r=>r.status===401&&!r.html);
await check('SIGNALR-AUTH','/hubs/woven/negotiate?negotiateVersion=1',{Authorization:`Bearer ${token}`,Accept:'application/json'},'POST',r=>r.status===200&&r.json&&!r.html);
writeFileSync(resolve(local,`proxy-results-${label}.json`),JSON.stringify({label,scope:'Actual local frontend proxy to model-created QA backend. Shared-route document/API contracts and headers, not full browser/provider/HTTPS validation.',results},null,2));
console.log(JSON.stringify({passed:results.filter(r=>r.result==='passed').length,failed:results.filter(r=>r.result==='failed').length,blocked:results.filter(r=>r.result==='blocked').length}));
