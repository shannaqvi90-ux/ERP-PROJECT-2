// Driver for our product. A stub until the feature exists: the critic of p03 (users, roles and permissions)
// replaces it with the shortest expert path through our screens, using the same operator
// (lib/operator.mjs) as the Odoo driver so the counts are comparable.
export default {
  built: false,
  reason: 'not built yet: waits for (1) a create-user screen on the integration branch (p03), (2) a contacts permission a role can grant, so "may view and create contacts and nothing else" can be set and verified (p16), and (3) a way to remove or retire a user, so the task can run again with the same sign-in',
  async run() {
    throw new Error('not built yet');
  },
};
