import { chromium } from 'playwright-core';
export const BASE='http://localhost:20350';
export async function open(lang){
  const b=await chromium.launch({executablePath:process.env.HOME+'/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome'});
  const ctx=await b.newContext({viewport:{width:1600,height:900}, locale: lang==='ar'?'ar-AE':'en-GB'});
  const p=await ctx.newPage(); return {b,ctx,p};
}
export async function signIn(p,email,pw='Demo-Pass-2026'){
  await p.goto(BASE+'/'); await p.getByRole('textbox',{name:/E-mail|البريد/}).fill(email);
  await p.locator('#signin-password').fill(pw); await p.keyboard.press('Enter'); await p.waitForTimeout(2000);
}
export const snap=async(p,n=4000)=>console.log((await p.locator('main').ariaSnapshot()).slice(0,n));
export const shot=(p,f)=>p.screenshot({path:'/home/shan/evidence-staging/p03-identity/r10/'+f,type:'jpeg',quality:70});
export async function page(p){ return (await p.locator('body').ariaSnapshot()).split('\n').filter(l=>!/row "Select|gridcell|checkbox "Select|columnheader|Options for the column|listitem|\/url/.test(l)).join('\n'); }
