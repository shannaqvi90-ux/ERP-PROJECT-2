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

/** Lists Odoo vacuums by age, and how long it keeps their rows (days). */
export const VACUUMED = Object.freeze({ job_runs: { days: 7, field: 'create_date' } });

/** How many days ahead run.mjs warns that a vacuumed list will fall short. */
export const WARN_DAYS = 2;

export const TOP_UP_HINT = [
  'top the rig up, from the repository root:',
  '    tools/odoo-reference/up.sh                      (the shared rig on port 8069: idempotent, adds only what is missing, restarts nothing;',
  '                                                     the lead or the owner runs it there, needs-human #13: never stop, recreate or reset the shared rig)',
  '    ODOO_REF_PROJECT=<yours>-odoo ODOO_REF_PORT=<port> tools/odoo-reference/up.sh   (a private rig, for a builder or critic; then COMPARE_ODOO_URL=http://localhost:<port>)',
  '    tools/odoo-reference/up.sh --status             (what is running and the recorded volume; changes nothing)',
  'Odoo deletes job-run rows (ir.cron.progress) a week after they were made, so a rig falls short a week after its last top-up; see tools/odoo-reference/README.md ("Keeping the rig at the bar").',
].join('\n');

/**
 * Checks each main list for at least `n` rows without counting everything: it asks for the n-th
 * row (Odoo refuses to count chatter messages above a limit). `rpc` is a signed-in OdooRpc or
 * anything with the same `call(model, method, args, kwargs)`.
 * Returns { ok, minimum, lists: { name: { model, at_least } }, short: [names] }.
 */
export async function checkRigVolume(rpc, n = MIN_ROWS_PER_MAIN_LIST, { now = Date.now() } = {}) {
  const lists = {};
  const short = [];
  const soon = [];
  for (const [name, [model, domain]] of Object.entries(MAIN_LISTS)) {
    const found = await rpc.call(model, 'search', [domain], { offset: n - 1, limit: 1, order: 'id', context: { active_test: false } });
    const atLeast = Array.isArray(found) && found.length === 1;
    lists[name] = { model, at_least: atLeast };
    if (!atLeast) { short.push(name); continue; }
    // Round 9: a list Odoo vacuums stays at the bar until its n-th newest row is vacuumed.
    const vac = VACUUMED[name];
    if (vac) {
      const [row] = await rpc.call(model, 'search_read', [domain], { fields: [vac.field], offset: n - 1, limit: 1, order: `${vac.field} desc, id desc`, context: { active_test: false } }) || [];
      const made = row?.[vac.field] ? Date.parse(`${String(row[vac.field]).replace(' ', 'T')}Z`) : NaN;
      if (Number.isFinite(made)) {
        const until = made + vac.days * 86_400_000;
        lists[name].at_bar_until = new Date(until).toISOString();
        if (until - now < WARN_DAYS * 86_400_000) soon.push(name);
      }
    }
  }
  return { ok: short.length === 0, minimum: n, lists, short, soon };
}

/** Signs in to the configured rig as its administrator and checks it. */
export async function checkLiveRig(product) {
  const rpc = await new OdooRpc(product).login(product.users.admin);
  return checkRigVolume(rpc);
}

/** The lists that fall short within WARN_DAYS, with the time each does (a warning; the run goes on). */
export function describeSoon(result) {
  return (result.soon || []).map(k => `${k} (${result.lists[k].model}) falls below ${result.minimum.toLocaleString('en-US')} rows about ${result.lists[k].at_bar_until} (Odoo vacuums them after ${VACUUMED[k]?.days ?? '?'} days)`).join('; ');
}

export function describeShort(result) {
  return `${result.short.map(k => `${k} (${result.lists[k].model})`).join(', ')} hold fewer than ${result.minimum.toLocaleString('en-US')} rows`;
}
