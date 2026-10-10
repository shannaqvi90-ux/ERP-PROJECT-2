export default {
  id: 'create-company-branch',
  title: 'Create a company with a branch',
  named: false,
  piece: 'p02',
  actor: 'admin',
  startAt: 'home',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.company/search_read', parts: ['result.name', 'result.parent_id'] }], writes: ['POST /web/dataset/call_kw/res.company/web_save'] },
    ours: { reads: [{ read: 'GET /api/tenancy/companies', parts: ['items.legalNameEn'] }], writes: ['POST /api/tenancy/companies'] },
  },
  enters: ['company', 'branch'],
  moments: ['branch filled in'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Create the company "{company}" with one branch, "{branch}".',
  done: 'Both are saved; the back end holds the company and the branch under it.',
  input: { company: 'Falcon Logistics LLC', branch: 'Falcon Logistics LLC - Jebel Ali Branch' },
  notes: 'Set-up removes any earlier copy; clean-up deletes both.',
};
