const {chromium}=require('playwright');
const fs=require('node:fs');
const path=require('node:path');
const root=path.join(__dirname,'..');
const assert=require('node:assert/strict');
(async()=>{
 const dict=JSON.parse(fs.readFileSync(path.join(root,'src/dictionary.json'),'utf8'));
 const script=fs.readFileSync(path.join(root,'src/ui-translator.js'),'utf8').replace('__AG_CHINESE_DICTIONARY__',JSON.stringify(dict));
 const browser=await chromium.launch({headless:true,...(process.env.AGZH_BROWSER_CHANNEL ? {channel:process.env.AGZH_BROWSER_CHANNEL} : {})});
 try {
  const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('https://127.0.0.1:4455/**',r=>r.fulfill({contentType:'text/html',body:`<!doctype html><html><head><meta charset="utf-8"></head><body>
   <div data-testid="title-menu-bar"><button>Workspace</button></div>
   <div class="settings-modal-container"><h1>Settings</h1><button id="save" title="Save" aria-label="Save" data-value="Save">Save</button>
   <input id="input" value="Settings" placeholder="Settings"><textarea id="textarea">Settings</textarea>
   <div contenteditable="true" id="editor">Settings</div><pre id="code">Settings</pre><kbd id="key">General</kbd>
   <div data-testid="workspace-customizations-view" id="custom">Settings</div>
   <div data-testid="project-selector-item" id="project">Settings</div>
   <div data-testid="user-message-content" id="user">Settings</div>
   <div id="dynamic"></div><span id="updated">Open</span><span id="removed">General</span></div>
   <main id="chat">Settings</main><script>window.setting='Save';document.getElementById('save').onclick=()=>window.clicked=window.setting;</script>
  </body></html>`}));
  await page.goto('https://127.0.0.1:4455/');
  const original=await page.locator('body').innerHTML();
  await page.evaluate(script);await page.waitForFunction(()=>document.querySelector('h1').textContent==='设置');
  assert.equal(await page.locator('#save').textContent(),'保存');
  assert.equal(await page.locator('#save').getAttribute('title'),'保存');
  assert.equal(await page.locator('#save').getAttribute('data-value'),'Save');
  assert.equal(await page.locator('#input').inputValue(),'Settings');
  assert.equal(await page.locator('#input').getAttribute('placeholder'),'Settings');
  assert.equal(await page.locator('#textarea').inputValue(),'Settings');
  for(const id of ['editor','code','custom','project','user','chat'])assert.equal(await page.locator('#'+id).textContent(),'Settings');
  assert.equal(await page.locator('#key').textContent(),'General');
  await page.locator('#save').click();assert.equal(await page.evaluate(()=>window.clicked),'Save');
  const count=await page.evaluate(()=>globalThis.__agChineseUI.status().translated);
  await page.evaluate(script);assert.equal(await page.evaluate(()=>globalThis.__agChineseUI.status().translated),count);
  await page.evaluate(()=>{document.querySelector('#dynamic').innerHTML='<label>Models</label>';document.querySelector('#updated').firstChild.data='External update';document.querySelector('#removed').remove();});
  await page.waitForFunction(()=>document.querySelector('#dynamic').textContent==='模型');
  assert.equal(await page.locator('#updated').textContent(),'External update');
  const restore=await page.evaluate(()=>globalThis.__agChineseUI.restore());assert.ok(restore.restored>=4);
  assert.equal(await page.locator('h1').textContent(),'Settings');assert.equal(await page.locator('#save').getAttribute('title'),'Save');
  assert.equal(await page.locator('#dynamic').textContent(),'Models');assert.equal(await page.locator('#updated').textContent(),'External update');
  await page.evaluate(()=>document.querySelector('h1').firstChild.data='Models');await page.waitForTimeout(200);
  assert.equal(await page.locator('h1').textContent(),'Models');
  await page.evaluate(script);await page.waitForFunction(()=>document.querySelector('h1').textContent==='模型');
  await page.evaluate(()=>globalThis.__agChineseUI.restore());
  // Full restoration must return byte-identical DOM in an unchanged fresh page.
  await page.goto('https://127.0.0.1:4455/');await page.evaluate(script);await page.waitForFunction(()=>document.querySelector('h1').textContent==='设置');
  await page.evaluate(()=>document.dispatchEvent(new Event('ag-chinese-restore-v1')));assert.equal(await page.locator('body').innerHTML(),original);
  const session1=await page.context().newCDPSession(page);
  const frame=(await session1.send('Page.getFrameTree')).frameTree.frame.id;
  const world1=await session1.send('Page.createIsolatedWorld',{frameId:frame,worldName:'AntigravityChineseCompanion_v1'});
  await session1.send('Runtime.evaluate',{expression:script,contextId:world1.executionContextId});
  await page.waitForFunction(()=>document.querySelector('h1').textContent==='设置');
  await session1.detach();
  const session2=await page.context().newCDPSession(page);
  const world2=await session2.send('Page.createIsolatedWorld',{frameId:frame,worldName:'AntigravityChineseCompanion_v1'});
  await session2.send('Runtime.evaluate',{expression:"document.dispatchEvent(new Event('ag-chinese-restore-v1'))",contextId:world2.executionContextId});
  assert.equal(await page.locator('body').innerHTML(),original);
  await session2.detach();
  assert.deepEqual(errors,[]);
  console.log(JSON.stringify({passed:true,dictionary:Object.keys(dict).length,checks:['static and dynamic translation','unchanged setting values and click handlers','protected input/code/user text','idempotent reinjection','external update preserved','removed nodes pruned','stop and restart','byte-identical DOM restoration','restore after disconnected CDP session','no page errors']}));
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
