// The bar's volume rule on the live reference rig: at least 100,000 rows in each main list.
//
// volume.json records what up.sh counted when it last ran, but the rig does not hold still: Odoo's
// own vacuum deletes scheduled-job run rows (ir.cron.progress) older than a week, so a rig seeded
// once falls below the bar about a week later. Every Odoo run checks the live rig first, and a
// comparison against a rig short of the bar is refused instead of recorded.
import { OdooRpc } from './odoo-rpc.mjs';

export const MIN_ROWS_PER_MAIN_LIST = 100_000;

/** The main lists that have a counterpart in our platform, as model and domain. */
export const MAIN_LISTS = Object.freeze({
  contacts: ['res.partner', [['ref', '=like', 'C______']]],
  users: ['res.users', [['share', '=', false]]],
  currency_rates: ['res.currency.rate', []],
  audit_messages: ['mail.message', [['message_type', '=', 'tracking']]],
  attachments: ['ir.attachment', [['res_model', '=', 'res.partner']]],
  job_runs: ['ir.cron.progress', []],
  approvals: ['purchase.order', []],
});

export const TOP_UP_HINT = 'run tools/odoo-reference/up.sh to top the rig up (it is idempotent and does not restart the shared rig; Odoo vacuums job-run rows older than a week)';

/**
 * Checks each main list for at least `n` rows without counting everything: it asks for the n-th
 * row (Odoo refuses to count chatter messages above a limit). `rpc` is a signed-in OdooRpc or
 * anything with the same `call(model, method, args, kwargs)`.
 * Returns { ok, minimum, lists: { name: { model, at_least } }, short: [names] }.
 */
export async function checkRigVolume(rpc, n = MIN_ROWS_PER_MAIN_LIST) {
  const lists = {};
  const short = [];
  for (const [name, [model, domain]] of Object.entries(MAIN_LISTS)) {
    const found = await rpc.call(model, 'search', [domain], { offset: n - 1, limit: 1, order: 'id', context: { active_test: false } });
    const atLeast = Array.isArray(found) && found.length === 1;
    lists[name] = { model, at_least: atLeast };
    if (!atLeast) short.push(name);
  }
  return { ok: short.length === 0, minimum: n, lists, short };
}

/** Signs in to the configured rig as its administrator and checks it. */
export async function checkLiveRig(product) {
  const rpc = await new OdooRpc(product).login(product.users.admin);
  return checkRigVolume(rpc);
}

export function describeShort(result) {
  return `${result.short.map(k => `${k} (${result.lists[k].model})`).join(', ')} hold fewer than ${result.minimum.toLocaleString('en-US')} rows`;
}
