// Product-neutral task definition. Drivers for each product live in drivers/<product>/.
export default {
  id: 'api-update-user',
  title: 'Do a screen task through the API: find a user and switch their language',
  named: false,
  piece: 'p15',
  channel: 'api',
  actor: 'an integration developer with an administrator\'s API access, in an HTTP client already signed in',
  startAt: 'api',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.users/read', parts: ['result.lang'] }] },
    ours: { reads: [{ read: 'GET /api/identity/users/*', parts: ['language'] }] },
  },
  moments: [],
  start: 'An HTTP client holding a signed-in administrator session (base address and token set up); nothing sent yet.',
  goal: 'Through the product\'s documented API only, find the user {user.name} among 100,000 users and switch their interface language to Arabic.',
  done: 'The API answered every request successfully and the back end shows the user\'s language as Arabic.',
  data: ['user.name'],
  notes: 'Steps are HTTP requests. Keystrokes are each request as typed in the client: method, path and query, compact JSON body, ' +
    'and Enter to send; the sign-in (base address, token) is set up beforehand in both products and not counted. ' +
    'Machine seconds are the round trips. Set-up switches the user to English first; clean-up restores the dataset\'s language.',
};
