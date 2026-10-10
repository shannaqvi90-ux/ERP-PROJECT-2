export default {
  id: 'see-and-rerun-job',
  title: 'See a background job and run it again',
  named: false,
  piece: 'p12',
  actor: 'admin',
  startAt: 'home',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/ir.cron/read', parts: ['result.lastcall'] }] },
  },
  moments: ['job opened'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Find the background job "{job}", see when it last ran, and run it again now.',
  done: 'The product reports the job ran again; the back end shows a new last-run time after the task started.',
  input: { job: 'Mail: Email Queue Manager' },
  notes: 'Odoo shows its scheduled actions only in developer mode, which the path switches on, and shows no last-run time anywhere in its interface (only the next execution) and no message after a manual run: its baseline is its nearest path and misses that part of the goal. The rig holds 100,000 recorded job runs.',
};
