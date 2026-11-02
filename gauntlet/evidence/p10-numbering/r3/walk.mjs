import { chromium } from '/home/shan/critic/p10-numbering-r3/gauntlet/compare/node_modules/playwright-core/index.mjs';
const B='http://localhost:21050', OUT='/home/shan/evidence-staging/p10-numbering/r3/';
const exe=process.env.HOME+'/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const browser=await chromium.launch({executablePath:exe});
const notes=[]; const log=(...a)=>{const s=a.join(' ');notes.push(s);console.log(s);};
async function signIn(page,email){
  await page.goto(B+'/?domain=alnoor.example');
  await page.waitForTimeout(1500);
  const inputs=page.locator('input');
  await page.keyboard.type(email.split('@')[0]); await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForTimeout(3000);
}
for (const [lang,email] of [['en','admin@alnoor.example'],['ar','admin.ar@alnoor.example']]){
  const ctx=await browser.newContext({viewport:{width:1440,height:900}});
  const page=await ctx.newPage(); const errs=[]; page.on('console',m=>{if(m.type()==='error')errs.push(m.text())}); page.on('pageerror',e=>errs.push(String(e)));
  await signIn(page,email);
  log(lang,'after sign-in url',page.url(),'dir',await page.evaluate(()=>document.documentElement.dir),'lang',await page.evaluate(()=>document.documentElement.lang));
  // keyboard: G then N
  await page.locator('body').click({position:{x:5,y:5}}).catch(()=>{});
  await page.keyboard.press('Escape');
  await page.keyboard.press('g'); await page.keyboard.press('n');
  await page.waitForTimeout(2500);
  log(lang,'after g n url',page.url());
  if(!page.url().includes('/numbering')){ await page.goto(B+'/numbering/sequences'); await page.waitForTimeout(2500); log(lang,'navigated by url'); }
  await page.screenshot({path:OUT+`0${lang==='en'?1:5}-${lang}-list.jpg`,type:'jpeg',quality:60});
  // search the tax invoice row
  const rows=page.locator('[role=row], tr');
  log(lang,'rows',await rows.count());
  const target = lang==='en' ? 'Tax invoice' : 'فاتورة ضريبية';
  const row=page.locator('[role=row], tr').filter({hasText: target}).filter({hasText:'ALN-DXB'}).first();
  await row.click(); await page.waitForTimeout(2500);
  const tax=page.getByTestId('tax-note'); log(lang,'tax note visible',await tax.isVisible().catch(()=>false), (await tax.textContent().catch(()=>''))?.slice(0,200));
  const checks=page.locator('input[type=checkbox]'); const n=await checks.count();
  for(let i=0;i<n;i++){ const c=checks.nth(i); const lab=await c.evaluate(el=>(el.closest('label')||el.parentElement).textContent.trim().slice(0,60)); log(lang,'checkbox',i,lab,'checked',await c.isChecked(),'disabled',await c.isDisabled()); }
  const texts=page.locator('input[inputmode=numeric]'); log(lang,'numeric inputs',await texts.count(), 'disabled', await texts.first().isDisabled().catch(()=>null));
  const hint=await page.locator('.hint, [class*=hint]').allTextContents().catch(()=>[]); log(lang,'hints',JSON.stringify(hint).slice(0,400));
  await page.screenshot({path:OUT+`0${lang==='en'?2:6}-${lang}-tax-invoice.jpg`,type:'jpeg',quality:60});
  // focused element after opening
  log(lang,'focused',await page.evaluate(()=>{const a=document.activeElement;return a.tagName+' '+(a.getAttribute('name')||a.id||'')+' value='+(a.value||'')}));
  // try a pattern change that repeats? change padding to 6 and save with Enter (allowed: forward change)
  await page.keyboard.press('Escape'); await page.waitForTimeout(800);
  // credit note: no number given -> next number editable
  const target2 = lang==='en' ? 'Tax credit note' : 'إشعار دائن ضريبي';
  const row2=page.locator('[role=row], tr').filter({hasText: target2}).filter({hasText:'ALN-DXB'}).first();
  if(await row2.count()){ await row2.click(); await page.waitForTimeout(2000);
    log(lang,'credit note next input disabled', await page.locator('input[inputmode=numeric]').first().isDisabled().catch(()=>null));
    await page.screenshot({path:OUT+`0${lang==='en'?3:7}-${lang}-tax-credit-note.jpg`,type:'jpeg',quality:60});
    await page.keyboard.press('Escape'); await page.waitForTimeout(500);
  } else log(lang,'no credit note row found with', target2);
  log(lang,'console errors',errs.length, errs.slice(0,3).join(' | ').slice(0,400));
  await ctx.close();
}
await browser.close();
import fs from 'fs'; fs.writeFileSync(OUT+'walk-notes.txt',notes.join('\n')+'\n');
