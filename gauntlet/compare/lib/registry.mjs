// Tasks and their per-product drivers, discovered from the file system so a critic adds a
// task by adding files, never by editing a central list.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { HARNESS_DIR } from './config.mjs';

export const PRODUCT_IDS = Object.freeze(['odoo', 'ours']);
export const TASKS_DIR = path.join(HARNESS_DIR, 'tasks');
export const DRIVERS_DIR = path.join(HARNESS_DIR, 'drivers');

const isModule = f => f.endsWith('.mjs') && !f.startsWith('_');

export async function loadTasks() {
  const tasks = [];
  for (const f of fs.readdirSync(TASKS_DIR).filter(isModule).sort()) {
    const t = (await import(pathToFileURL(path.join(TASKS_DIR, f)).href)).default;
    if (!t || t.id !== f.replace(/\.mjs$/, '')) throw new Error(`tasks/${f}: default export must have id "${f.replace(/\.mjs$/, '')}"`);
    tasks.push(t);
  }
  return tasks;
}

export async function loadTask(id) {
  const t = (await loadTasks()).find(x => x.id === id);
  if (!t) throw new Error(`unknown task "${id}"; known: ${(await loadTasks()).map(x => x.id).join(', ')}`);
  return t;
}

export function driverPath(product, taskId) {
  return path.join(DRIVERS_DIR, product, `${taskId}.mjs`);
}

export async function loadDriver(product, taskId) {
  if (!PRODUCT_IDS.includes(product)) throw new Error(`unknown product "${product}"`);
  const p = driverPath(product, taskId);
  if (!fs.existsSync(p)) throw new Error(`no ${product} driver for task ${taskId} (${path.relative(HARNESS_DIR, p)})`);
  const d = (await import(pathToFileURL(p).href)).default;
  const variants = d?.variants ? Object.entries(d.variants) : [];
  if (!d || (typeof d.run !== 'function' && !(variants.length && variants.every(([, v]) => typeof v?.run === 'function')))) {
    throw new Error(`${path.relative(HARNESS_DIR, p)}: default export needs run(op, ctx), or variants that each have run(op, ctx)`);
  }
  return d;
}

/** Fill "{contact.name}" placeholders from the dataset needles. */
export function describe(text, needles) {
  return String(text).replace(/\{([a-z_.]+)\}/gi, (m, key) => {
    const v = key.split('.').reduce((o, k) => (o == null ? o : o[k]), needles);
    return v == null ? m : String(v);
  });
}
