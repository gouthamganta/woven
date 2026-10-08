import{readFileSync,writeFileSync}from'node:fs';
import{resolve}from'node:path';
import{pathToFileURL}from'node:url';
const workspace=resolve(process.argv[2]||'.');
const{chromium}=await import(pathToFileURL(resolve(workspace,'qa/.local/browser-tools/node_modules/playwright/index.mjs')));
const access=JSON.parse(readFileSync(resolve(workspace,'qa/.local/full-qa-runtime/browser-access.json'),'utf8'));
const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});const results=[];
for(const signedIn of[false,true]){
 const context=await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'});
 await context.route('**/*',route=>{const url=new URL(route.request().url());return ['127.0.0.1','localhost'].includes(url.hostname)||['data:','blob:'].includes(url.protocol)?route.continue():route.abort();});
 if(signedIn)await context.addInitScript(token=>{localStorage.setItem('accessToken',token);localStorage.setItem('woven:pushAsked','1');},access.token);
 const page=await context.newPage();const errors=[],api=[],sockets=[];page.on('pageerror',e=>errors.push(e.message));page.on('websocket',socket=>{const record={path:new URL(socket.url()).pathname,handshake:false,errors:[]};sockets.push(record);socket.on('framereceived',event=>{if(event.payload==='{}\u001e')record.handshake=true;});socket.on('socketerror',error=>record.errors.push(String(error)));});page.on('response',response=>{if(['fetch','xhr'].includes(response.request().resourceType())){const url=new URL(response.url());api.push({path:url.pathname,status:response.status()});}});
 await page.goto('http://127.0.0.1:5180/moments',{waitUntil:'domcontentloaded'});
 await page.waitForTimeout(signedIn?15000:2500);
 const observation={signedIn,path:new URL(page.url()).pathname,text:(await page.locator('body').innerText()).slice(0,700),pageErrors:errors,api,sockets,scope:'Actual local frontend/backend, signed sandbox JWT; external requests blocked. No Google SSO or semantic matching validated.'};
 observation.result=signedIn?(observation.path==='/moments'&&api.some(r=>r.path==='/moments'&&r.status===200)?'passed':'failed'):(observation.path==='/login'?'passed':'failed');
 results.push(observation);await page.screenshot({path:resolve(workspace,`qa/.local/full-qa-runtime/real-browser-${signedIn?'signed':'anonymous'}.png`),fullPage:true});await context.close();
}
await browser.close();writeFileSync(resolve(workspace,'qa/.local/full-qa-runtime/real-browser-core.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results.map(r=>({signedIn:r.signedIn,path:r.path,result:r.result,errors:r.pageErrors,api:r.api})),null,2));
