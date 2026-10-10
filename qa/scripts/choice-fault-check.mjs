import{readFileSync,writeFileSync}from'node:fs';
import{createHmac,randomUUID}from'node:crypto';
import{spawnSync}from'node:child_process';
import{resolve}from'node:path';
const workspace=resolve(process.argv[2]||'.'),offset=Number(process.argv[3]||76),label=process.argv[4]||'unspecified';
const local=resolve(workspace,'qa/.local/full-qa-runtime'),seed=JSON.parse(readFileSync(resolve(local,'seed-accounts.json'),'utf8'));
if(seed.database!=='woven_qa_model'||!Number.isInteger(offset)||offset<0||offset>=seed.accounts.length-1)throw new Error('Isolated model-created fixture required');
const actor=seed.accounts[offset],target=seed.accounts[offset+1];if(![actor,target].every(a=>Number.isInteger(a.userId)&&a.email.endsWith('@woven.invalid')))throw new Error('Synthetic actors required');
function sql(statement){const r=spawnSync('docker',['exec','-i','woven-qa-postgres-1','psql','-U','woven_qa','-d','woven_qa_model','-v','ON_ERROR_STOP=1','-t','-A'],{encoding:'utf8',windowsHide:true,input:statement+'\n'});if(r.status!==0)throw new Error('QA fault SQL failed: '+r.stderr.slice(0,180));return r.stdout.trim();}
function state(){return JSON.parse(sql(`SELECT json_build_object('budget',COALESCE((SELECT total_used FROM daily_interactions WHERE user_id=${actor.userId} AND date_utc=CURRENT_DATE),0),'responses',(SELECT COUNT(*) FROM moment_responses WHERE from_user_id=${actor.userId}),'notes',(SELECT COUNT(*) FROM chat_notes WHERE from_user_id=${actor.userId}));`));}
const before=state();if(before.budget||before.responses||before.notes)throw new Error('Use an untouched actor, not prior race/deletion state');
const name='qa_note_fault_'+randomUUID().replaceAll('-','');
const key=readFileSync(resolve(local,'runtime.env'),'utf8').split(/\r?\n/).find(l=>l.startsWith('WOVEN_QA_JWT_KEY=')).slice('WOVEN_QA_JWT_KEY='.length);
const now=Math.floor(Date.now()/1000),h=Buffer.from('{"alg":"HS256","typ":"JWT"}').toString('base64url'),p=Buffer.from(JSON.stringify({uid:`${actor.userId}`,sub:`${actor.userId}`,iss:'WovenBackend',aud:'WovenFrontend',exp:now+3600,nbf:now-60})).toString('base64url'),token=`${h}.${p}.${createHmac('sha256',key).update(`${h}.${p}`).digest('base64url')}`;
let installed=false,responseStatus;
try{
 sql(`CREATE FUNCTION ${name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.from_user_id=${actor.userId} AND NEW.to_user_id=${target.userId} THEN RAISE EXCEPTION 'Synthetic QA forced note-write failure' USING ERRCODE='P0001'; END IF; RETURN NEW; END $$; CREATE TRIGGER ${name} BEFORE INSERT ON chat_notes FOR EACH ROW EXECUTE FUNCTION ${name}();`);installed=true;
 const response=await fetch('http://127.0.0.1:5181/moments/choose',{method:'POST',headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json'},body:JSON.stringify({targetUserId:target.userId,choice:'MAGICAL',source:'TODAY',noteText:'Synthetic fault-injection QA note: consistency and thoughtful communication.'}),signal:AbortSignal.timeout(30000)});responseStatus=response.status;await response.text();
}finally{sql(`DROP TRIGGER IF EXISTS ${name} ON chat_notes; DROP FUNCTION IF EXISTS ${name}();`);}
const after=state(),result={label,actorOffset:offset,scope:'Controlled note-write failure in verified local synthetic-only model database; trigger removed in finally. Not a production-safe script.',before,responseStatus,after,result:responseStatus>=400&&after.budget===before.budget&&after.responses===before.responses&&after.notes===before.notes?'passed':'failed',triggerRemoved:true};
writeFileSync(resolve(local,`choice-fault-results-${offset}.json`),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
