import { chromium } from '/home/shan/critic/p10-numbering-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const OUT='/home/shan/evidence-staging/p10-numbering/r4/';
const base='http://localhost:21050';
const log=(...a)=>console.log(...a);
const browser=await chromium.launch({executablePath:'/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome'});
async function session(email, lang){
  const ctx=await browser.newContext({viewport:{width:1440,height:900}});
  const page=await ctx.newPage();
  const errors=[]; page.on('console',m=>{ if(m.type()==='error') errors.push(m.text()); }); page.on('pageerror',e=>errors.push(String(e)));
  await page.goto(base+'/');
  await page.waitForTimeout(1500);
  await page.keyboard.type(email); await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForTimeout(3000);
  return {ctx,page,errors};
}
const which=process.argv[2]||'en';
const email= which==='ar' ? 'admin.ar@alnoor.example' : which==='viewer' ? 'viewer@alnoor.example' : 'admin@alnoor.example';
const {ctx,page,errors}=await session(email);
log('url after sign-in', page.url(), 'dir', await page.evaluate(()=>document.documentElement.dir), 'lang', await page.evaluate(()=>document.documentElement.lang));
await page.keyboard.press('g'); await page.keyboard.press('n');
await page.waitForTimeout(2500);
log('url', page.url());
await page.screenshot({path:OUT+`walk/${which}-list.png`});
log('h1', await page.locator('h1').allInnerTexts());
// search for quotation, open first row by keyboard
await page.keyboard.press('/'); await page.waitForTimeout(300);
await page.keyboard.type(process.argv[3]||'quotation'); await page.waitForTimeout(1500);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
await page.waitForTimeout(2000);
log('url', page.url(), 'focused', await page.evaluate(()=>{const e=document.activeElement; return e && (e.getAttribute('name')||e.id||e.tagName)+' sel='+(e.selectionStart!==undefined? e.selectionStart+'-'+e.selectionEnd:'')}));
await page.screenshot({path:OUT+`walk/${which}-form.png`});
const text=await page.locator('main').innerText().catch(()=>'' );
log('MAIN TEXT\n'+text.slice(0,2500));

if (process.argv[4]==='edit') {
  // keyboard only: prefix is focused and selected
  await page.keyboard.type('SQ-{YY}{MM}-'); await page.waitForTimeout(800);
  const prev1=await page.locator('main').innerText(); log('PREVIEW after yearly with MM:', (prev1.match(/Next numbers[\s\S]{0,260}/)||[''])[0]);
  // go to Starts again select: Tab through
  for (let i=0;i<12;i++){ await page.keyboard.press('Tab'); const f=await page.evaluate(()=>document.activeElement.tagName+':'+(document.activeElement.name||document.activeElement.id||'')); if (f.startsWith('SELECT')) { log('reached', f, 'after tabs', i+1); break; } }
  await page.keyboard.press('ArrowDown'); await page.waitForTimeout(800);
  const prev2=await page.locator('main').innerText(); log('PREVIEW after choosing next option:', (prev2.match(/Next numbers[\s\S]{0,260}/)||[''])[0]);
  await page.screenshot({path:OUT+`walk/${which}-edit-monthly.png`});
  await page.keyboard.press('Control+s'); await page.waitForTimeout(2500);
  const after=await page.locator('main').innerText(); log('AFTER ENTER:', (after.match(/Next numbers[\s\S]{0,200}/)||[''])[0]);
  await page.screenshot({path:OUT+`walk/${which}-saved.png`});
  // history panel
  await page.getByRole('button',{name:/History|السجل|التاريخ/}).first().click().catch(e=>log('no history btn',e.message));
  await page.waitForTimeout(2000);
  await page.screenshot({path:OUT+`walk/${which}-history.png`});
  const h=await page.locator('body').innerText(); log('HISTORY:', (h.match(/(History|السجل)[\s\S]{0,900}$/)||[''])[0].slice(0,1200));
}
log('console errors', errors);
await browser.close();
