import { chromium } from '/home/shan/critic/p10-numbering-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const map = JSON.parse(process.argv[2]);
const b = await chromium.launch({executablePath:'/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome'});
const p = await b.newPage({viewport:{width:1440,height:900}});
for (const [src,dst] of Object.entries(map)) { await p.goto('file://'+src); await p.screenshot({path:dst,type:'jpeg',quality:72,fullPage:false}); }
await b.close();
