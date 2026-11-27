import { useEffect } from "react";
import { api } from "../../kernel/api";
import { lookChosenEvent, type LookChange } from "../../kernel/theme";

/**
 * Saves the look a signed-in person chooses (theme, density) to their user, so it follows them to
 * every device and never reaches the next person on this one. It is the identity module's own (the
 * endpoint is the module's, judged by the endpoint gates), shown by the status bar for the users who
 * may change their own preferences (identity.profile.update): the extension below gives the permission. The screen has changed already; a
 * refused or lost save leaves it so for this visit (the choice is sent with keepalive, so a reload
 * right after it still reaches the server).
 */
export function LookSync() {
  useEffect(() => {
    const saved = (event: Event) => {
      const change = (event as CustomEvent<LookChange>).detail;
      if (!change || (!change.theme && !change.density)) return;
      api("PUT", "/api/identity/me/look", change, { keepalive: true }).catch(() => undefined);
    };
    window.addEventListener(lookChosenEvent, saved);
    return () => window.removeEventListener(lookChosenEvent, saved);
  }, []);
  return null;
}
