// Our product allows 30 sign-in requests a minute from one client (Erp:RateLimits:SignInPerMinute)
// and the harness signs in from one address: API sessions and every driver's browser sign-in. Like
// the end-to-end suite's helper (tests/e2e/specs/demo.ts) the harness keeps its own sign-ins under a
// budget, so a long run never meets the limit. The budget cannot see sign-ins made just before by
// another process (./erp verify runs the end-to-end suite first, in the same minute), so a sign-in
// the server still refuses with 429 waits out the window and is tried again. The limit itself is
// tested by the gate suite; none of this is inside a measured part of a task.
//
// This module runs in the harness process only (lib/runner.mjs for a driver's browser sign-in,
// lib/sandbox/bridge.mjs for an API session's), so one budget counts every sign-in of the run.
let budget = 24;
export let windowMs = 61_000;
const recent = [];

/** Tests only: a smaller budget and window. Returns the function that restores the defaults. */
export function configureSignInLimit(options) {
  const saved = { budget, windowMs };
  ({ budget = budget, windowMs = windowMs } = options);
  recent.length = 0;
  return () => { ({ budget, windowMs } = saved); recent.length = 0; };
}

/** Wait, if needed, until one more sign-in fits the budget, then count it. */
export async function paceSignIn() {
  for (;;) {
    const now = Date.now();
    while (recent.length > 0 && now - recent[0] >= windowMs) recent.shift();
    if (recent.length < budget) break;
    await new Promise(r => setTimeout(r, windowMs - (now - recent[0]) + 50));
  }
  recent.push(Date.now());
}

/** After a 429: wait out the server's window, which also empties the budget's. */
export async function waitOutSignInLimit() {
  recent.length = 0;
  await new Promise(r => setTimeout(r, windowMs));
}

/** Sign-in attempts per sign-in before a 429 is an error. */
export const signInAttempts = 3;
