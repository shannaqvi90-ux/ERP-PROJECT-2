import { OursApi, oursAs } from '../../lib/ours-api.mjs';

// Written by the p12 builder (round 1). The shortest expert path through our screens: G then J opens
// Background jobs (the search box has the focus), the job's name is typed, Enter opens the best
// match (newest first among equal names, so the job that ran last), its panel shows when it
// finished, Alt+R runs it again, and the new run opens in the panel and goes from Waiting to Done.
// Odoo's input names a scheduled action of its own ("Mail: Email Queue Manager"), a different thing in
// a different product; ours runs the job kind it has the same way: the self-check, found by its
// name, with the run-again key.

const NAME = 'Self-check of the job queue';
const filter = encodeURIComponent("jobType eq 'jobs.selfCheck' and jobStatus eq 'succeeded'");

async function newestRun(api) {
  const page = await api.get(`/api/jobs/jobs?take=1&sort=-finishedAt&filter=${filter}`);
  const job = page.items?.[0];
  if (!job) throw new Error('our product holds no finished self-check job');
  return job;
}

export default {
  built: true,
  path: 'G then J (Background jobs, the search box focused) > type the job name > Enter (opens the newest run; its panel shows when it finished) > Alt+R (run again; the new run opens and goes to Done).',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const job = await newestRun(api);
    ctx.state.job = { id: job.id, title: job.titleEn, finishedBefore: job.finishedAt };
    ctx.state.startedAt = new Date().toISOString();
  },
  async signIn(ctx) {
    const page = ctx.page;
    const { login, password } = ctx.product.users.admin;
    await page.goto(ctx.product.baseUrl + '/');
    await page.locator('input[name="email"]:focus').waitFor();
    await page.keyboard.type(login);
    await page.keyboard.press('Tab');
    await page.keyboard.type(password);
    await page.keyboard.press('Enter');
    await page.locator('nav[aria-label="Main navigation"]').waitFor();
    await page.locator('main h1').waitFor();
  },
  async run(op, ctx) {
    await op.press('g', { label: 'go to' });
    await op.press('j', { label: 'Background jobs' });
    await op.waitFor(() => document.activeElement && document.activeElement.matches('input.search, input[type="search"]') && document.querySelectorAll('table[role=grid] tbody tr').length > 0,
      { label: 'job list ready, search focused' });
    await op.type(NAME, { label: 'the job name' });
    await op.waitFor(name => [...document.querySelectorAll('table[role=grid] tbody tr')].length > 0 && document.querySelector('input.search, input[type="search"]')?.value === name, { label: 'name in the search box', arg: NAME });
    await op.press('Enter', { label: 'open the best match' });
    await op.waitFor(() => !!document.querySelector('.list-record [data-testid="job-finished"]') && document.querySelector('.list-record [data-testid="job-finished"]').textContent.trim() !== 'Not yet', { label: 'job panel (last run on screen)' });
    await op.shot('job opened');
    await op.press('Alt+r', { label: 'run again' });
    await op.waitFor(() => /^Done$/.test(document.querySelector('.list-record .jb-status')?.textContent ?? '') && !!document.querySelector('.list-record [data-testid="job-finished"]') && document.querySelector('.list-record [data-testid="job-finished"]').textContent.trim() !== 'Not yet' && new URLSearchParams(location.search).get('open') !== null,
      { label: 'the new run is done', timeout: 60_000 });
    return {};
  },
  async verify(ctx) {
    const api = new OursApi(ctx.product).withBrowserSession(await ctx.context.cookies());
    const originalId = ctx.state.job.id;
    const page = await api.get(`/api/jobs/jobs?take=5&sort=-createdAt&filter=${encodeURIComponent(`startedBy eq 'rerun' and jobStatus eq 'succeeded'`)}`);
    const again = page.items?.find(j => j.finishedAt && j.finishedAt > ctx.state.startedAt);
    const detail = again ? await api.get(`/api/jobs/jobs/${again.id}`) : null;
    return {
      verified: !!again && detail?.rerunOf === originalId,
      details: { last_run_before: ctx.state.job.finishedBefore, last_run_now: again?.finishedAt ?? null, rerun_of_the_job_opened: detail?.rerunOf === originalId, last_run_shown_in_interface: true },
    };
  },
};
