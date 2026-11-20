import {start,out} from './lib.mjs';
const {browser,page} = await start({scheme:'light'});
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(3500);
for (const th of ['light','dark','hc']) {
  await page.evaluate(t=>{document.documentElement.dataset.theme=t},th); await page.waitForTimeout(300);
  const r = await page.evaluate(()=>{
    function rgb(s){const m=s.match(/[\d.]+/g).map(Number);return m}
    function lum([r,g,b]){const f=c=>{c/=255;return c<=0.03928?c/12.92:((c+0.055)/1.055)**2.4};return 0.2126*f(r)+0.7152*f(g)+0.0722*f(b)}
    function bgOf(e){while(e){const c=getComputedStyle(e).backgroundColor; const m=rgb(c); if(m.length<4||m[3]>0.9) if(!(m.length>=4&&m[3]===0)) return m; e=e.parentElement} return [255,255,255]}
    let worst=[];
    for (const e of document.querySelectorAll('main *, header *, footer *, nav *')) {
      if (!e.childNodes.length || ![...e.childNodes].some(n=>n.nodeType===3&&n.textContent.trim())) continue;
      const s=getComputedStyle(e); if (s.visibility==='hidden'||s.display==='none') continue;
      const fg=rgb(s.color), bg=bgOf(e); const a=lum(fg),b=lum(bg); const cr=(Math.max(a,b)+0.05)/(Math.min(a,b)+0.05);
      const size=parseFloat(s.fontSize), bold=+s.fontWeight>=700; const need=(size>=24||(size>=18.66&&bold))?3:4.5;
      if (cr<need) worst.push([cr.toFixed(2), e.textContent.trim().slice(0,30), e.className.toString().slice(0,30)]);
    }
    return worst.slice(0,8).concat([['count',worst.length]]);
  });
  console.log(th, JSON.stringify(r));
}
await browser.close();
