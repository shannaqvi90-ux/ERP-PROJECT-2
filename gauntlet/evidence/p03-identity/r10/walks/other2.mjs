import { chromium } from 'playwright-core';
const b=await chromium.launch({executablePath:process.env.HOME+'/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome'});
for (const w of [1280,1600,1920,2560]) {
  const p=await b.newPage({viewport:{width:w,height:1000}});
  await p.goto('http://localhost:20350/'); await p.getByRole('textbox',{name:/E-mail|البريد/}).fill('admin@alnoor.example'); await p.locator('#signin-password').fill('Demo-Pass-2026'); await p.keyboard.press('Enter'); await p.waitForTimeout(2000);
  if(await p.getByRole('button',{name:'English',exact:true}).count()){await p.getByRole('button',{name:'English',exact:true}).click(); await p.waitForTimeout(1000);}
  await p.goto('http://localhost:20350/identity/roles'); await p.waitForTimeout(1500);
  await p.getByRole('row',{name:/^Select this row Administrator/}).click(); await p.waitForTimeout(1200);
  const r=[];
  for (const n of ["Reset other users' passwords","Share list views with everyone","View users"]) r.push(n.slice(0,12)+'='+JSON.stringify(await p.getByRole('checkbox',{name:n}).first().evaluate(e=>{const x=e.getBoundingClientRect();return [Math.round(x.width*100)/100,x.height, e.checked]})));
  console.log(w, r.join('  '));
  if (w===1920) { const box=await p.getByRole('checkbox',{name:"Reset other users' passwords"}).first().evaluate(e=>e.closest('table').getBoundingClientRect().toJSON()); await p.screenshot({path:'matrix-1920.png',clip:{x:box.x,y:box.y,width:box.width,height:Math.min(box.height,260)}}); }
  await p.close();
}
await b.close();
