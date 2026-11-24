/**
 * State set from the answer of a request is drawn in the task that delivered the answer.
 *
 * A state update made outside an event (in a promise's continuation, after a fetch) is a default-lane
 * update: React draws it in a task of its own, posted through a message channel, later and in a task
 * that nothing the person did leads to (the person pressed Import; the screen showing the dialog's
 * field list, 30 ms later, was drawn by a task a harness command of someone watching the page could
 * come between). A synchronous commit draws the screen, runs the layout effects and flushes the
 * passive effects in the continuation itself, so the page changes only inside the task of the answer
 * to a request a step of the person sent. Nothing else about the update changes.
 */
import { flushSync } from "react-dom";

export function commitNow(update: () => void): void {
  try {
    flushSync(update);
  } catch (error) {
    // flushSync refuses to run while React is already rendering (an update made from a render or an
    // effect): the update is made the ordinary way then, still drawn, only not at once.
    if (error instanceof Error && /flushSync/i.test(error.message)) update();
    else throw error;
  }
}
