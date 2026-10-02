// Chromium for the harness. Uses the browser preinstalled for Playwright on this machine
// (PLAYWRIGHT_BROWSERS_PATH, /opt/pw-browsers by default) or COMPARE_CHROMIUM; never downloads one.
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright-core';
import { LOCALE, TIMEZONE, VIEWPORT } from './config.mjs';

export function findChromium() {
  if (process.env.COMPARE_CHROMIUM) return process.env.COMPARE_CHROMIUM;
  const roots = [process.env.PLAYWRIGHT_BROWSERS_PATH, '/opt/pw-browsers', path.join(process.env.HOME || '', '.cache', 'ms-playwright')].filter(Boolean);
  for (const root of roots) {
    if (!fs.existsSync(root)) continue;
    const dirs = fs.readdirSync(root).filter(d => /^chromium-\d+$/.test(d)).sort((a, b) => Number(b.split('-')[1]) - Number(a.split('-')[1]));
    for (const d of dirs) {
      for (const rel of ['chrome-linux/chrome', 'chrome-linux64/chrome', 'chrome-mac/Chromium.app/Contents/MacOS/Chromium']) {
        const p = path.join(root, d, rel);
        if (fs.existsSync(p)) return p;
      }
    }
  }
  return undefined; // let playwright-core resolve its own
}

export async function launch({ headed = false } = {}) {
  return chromium.launch({
    executablePath: findChromium(),
    headless: !headed,
    // No calls home from the browser: they add noise to machine seconds and fail behind the proxy.
    args: ['--disable-background-networking', '--disable-component-update', '--no-first-run', '--disable-sync',
      '--disable-features=Translate,OptimizationHints,MediaRouter', '--no-default-browser-check'],
  });
}

export async function newContext(browser, extra = {}) {
  return browser.newContext({ viewport: VIEWPORT, locale: LOCALE, timezoneId: TIMEZONE, deviceScaleFactor: 1, ...extra });
}
