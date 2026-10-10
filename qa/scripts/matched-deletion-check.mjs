import { readFileSync, writeFileSync } from 'node:fs';
import { createHmac } from 'node:crypto';
import { resolve } from 'node:path';
const workspace=resolve(process.argv[2]||'.'); const local=resolve(workspace,'qa/.local/full-qa-runtime');
const seed=JSON.parse(readFileSync(resolve(local,'seed-accounts.json'),'utf8'));
const actor=seed.accounts[30];
if(seed.database!=='woven_qa_model'||actor.userId!==32||!actor.email.endsWith('@woven.invalid'))throw new Error('Refusing outside designated synthetic matched actor/model-created sandbox');
const key=readFileSync(resolve(local,'runtime.env'),'utf8').split(/\r?\n/).find(l=>l.startsWith('WOVEN_QA_JWT_KEY=')).slice('WOVEN_QA_JWT_KEY='.length);
const now=Math.floor(Date.now()/1000);const header=Buffer.from('{"alg":"HS256","typ":"JWT"}').toString('base64url');
const payload=Buffer.from(JSON.stringify({uid:`${actor.userId}`,sub:`${actor.userId}`,iss:'WovenBackend',aud:'WovenFrontend',exp:now+3600,nbf:now-60})).toString('base64url');
const token=`${header}.${payload}.${createHmac('sha256',key).update(`${header}.${payload}`).digest('base64url')}`;
async function call(path,method='GET'){
 const response=await fetch('http://127.0.0.1:5181'+path,{method,headers:{Authorization:`Bearer ${token}`},signal:AbortSignal.timeout(30000)});
 const text=await response.text();let data;try{data=JSON.parse(text);}catch{data=text.slice(0,100);}
 return {status:response.status,data,correlation:response.headers.get('X-Correlation-ID')};
}
const before=await call('/me/data-summary');if(before.status!==200)throw new Error('Actor is not available for matched-deletion fixture');
const deletion=await call('/me/account','DELETE');const after=await call('/me/data-summary');
const result={candidate:'5461c85',schemaMode:'model-created QA',actor:actor.userId,before,deletion,after,scope:'Destructive to designated synthetic actor only. Matched/blocked user; do not rerun as fresh evidence.'};
writeFileSync(resolve(local,'matched-deletion-results.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
