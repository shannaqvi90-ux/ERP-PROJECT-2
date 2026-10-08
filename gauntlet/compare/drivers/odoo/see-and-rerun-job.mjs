import { adminRpc, developerMode, signInAs, technicalMenu } from './_common.mjs';

export default {
  built: true,
  path: 'Apps menu > Settings > scroll > Activate the developer mode > Technical > (scroll) Scheduled Actions > search the job > open it > Run Manually. Odoo shows the next execution only; the last run is not shown anywhere in its interface.',
  async setup(ctx) {
    const rpc = await adminRpc(ctx);
    const [job] = await rpc.searchRead('ir.cron', [['name', '=', ctx.task.input.job]], ['id', 'lastcall', 'active']);
    if (!job) throw new Error(`scheduled action "${ctx.task.input.job}" missing on the rig`);
    ctx.state.job = job;
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op, ctx) {
    const { job } = ctx.task.input;
    const page = op.page;
    await developerMode(op);
    await technicalMenu(op, 'Scheduled Actions');
    // The list must have finished loading before typing: under load Odoo re-renders the search
    // box once the first page arrives and drops what was typed before (round 3: 'anager').
    await op.waitFor(() => !document.querySelector('.o_loading_indicator, .o_blockUI') && document.querySelectorAll('.o_data_row').length > 0 &&
      document.activeElement?.matches('.o_searchview_input'), { label: 'scheduled action list loaded, search focused' });
    await op.type(job, { label: 'job name' });
    await op.waitFor(j => document.querySelector('.o_searchview_input')?.value === j, { label: 'job name in the search box', arg: job, timeout: 10_000 });
    await op.press('Enter', { label: 'search' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length === 1, { label: 'one job' });
    await op.click(page.locator('.o_data_row').first(), { label: 'open the job' });
    const run = page.getByRole('button', { name: 'Run Manually' });
    await op.waitFor(run, { label: 'job form (last run on screen)' });
    // Odoo shows the next execution on the job, but its last run nowhere in the interface.
    await op.shot('job opened');
    await op.click(run, { label: 'Run Manually' });
    await op.waitFor(() => !document.querySelector('.o_loading_indicator, .o_blockUI') && !!document.querySelector('.o_form_view button[name="method_direct_trigger"]:not([disabled]), .o_form_view button:not([disabled])'), { label: 'run finished (no message is shown)' });
    return {};
  },
  async verify(ctx) {
    const rpc = await adminRpc(ctx);
    const [job] = await rpc.read('ir.cron', [ctx.state.job.id], ['lastcall']);
    const before = ctx.state.job.lastcall || '';
    return { verified: !!job.lastcall && job.lastcall > before, details: { last_run_before: before || null, last_run_now: job.lastcall, last_run_shown_in_interface: false } };
  },
};
