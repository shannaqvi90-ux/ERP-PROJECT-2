import {open,signIn,page,shot} from './lib.mjs';
const {b,p}=await open('ar'); await signIn(p,'admin@alnoor.example');
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForTimeout(1500);
await p.keyboard.type('accountant@alnoor'); await p.waitForTimeout(1200); await p.keyboard.press('ArrowDown'); await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
for (const tab of [0,1]) {
  await p.getByRole('tab').nth(tab).click(); await p.waitForTimeout(800);
  await p.keyboard.press('Control+k'); await p.waitForTimeout(400); await p.keyboard.type('إعادة'); await p.waitForTimeout(900);
  console.log('tab',tab,'palette:',(await p.getByRole('option').allTextContents()).filter(t=>!/^(العربية|الإنجليزية|ALN|English)/.test(t)).join(' | '));
  await p.keyboard.press('Escape'); await p.waitForTimeout(300);
}
await b.close();
