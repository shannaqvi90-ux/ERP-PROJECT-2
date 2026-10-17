import {open,signIn} from './lib.mjs';
const {b,p}=await open('en'); await signIn(p,'admin@alnoor.example'); if(await p.getByRole('button',{name:'English',exact:true}).count()){await p.getByRole('button',{name:'English',exact:true}).click(); await p.waitForTimeout(1200);}
await p.keyboard.press('g'); await p.keyboard.press('r'); await p.waitForTimeout(1500);
await p.keyboard.press('Alt+n'); await p.waitForTimeout(800);
const cb=p.getByRole('checkbox',{name:"Reset other users' passwords"});
console.log(await cb.evaluate(e=>{const r=e.getBoundingClientRect(); const cs=getComputedStyle(e); return {w:r.width,h:r.height,appearance:cs.appearance,opacity:cs.opacity,parent:e.parentElement.outerHTML.slice(0,300)}}));
const other=p.getByRole('checkbox',{name:'View users'});
console.log(await other.evaluate(e=>{const r=e.getBoundingClientRect(); return {w:r.width,h:r.height}}));
await cb.focus(); await p.keyboard.press('Space'); console.log('toggles by keyboard:', await cb.isChecked());
const box=await cb.evaluate(e=>e.closest('td').getBoundingClientRect().toJSON());
await p.screenshot({path:'/home/shan/evidence-staging/p03-identity/r10/walks/other-cell.png',clip:{x:box.x-150,y:box.y-10,width:box.width+170,height:box.height+20}});
await b.close();
