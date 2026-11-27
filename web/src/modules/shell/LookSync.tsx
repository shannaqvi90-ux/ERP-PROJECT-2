import { useEffect } from "react";
import { api } from "../../kernel/api";
import { useSession } from "../../kernel/session";
import { lookChosenEvent, type LookChange } from "../../kernel/theme";

/** The permission the user's own preferences, the look included, need (the server's identity.profile.update). */
const lookPermission = "identity.profile.update";

/**
 * Saves the look a signed-in person chooses (theme, density) to their user, so it follows them to
 * every device and never reaches the next person on this one. The screen has changed already; a
 * refused or lost save leaves it so for this visit (the choice is sent with keepalive, so a reload
 * right after it still reaches the server).
 */
export function LookSync() {
  const { state } = useSession();
  const allowed = state.status === "signedIn" && state.session.permissions.includes(lookPermission);
  useEffect(() => {
    if (!allowed) return;
    const saved = (event: Event) => {
      const change = (event as CustomEvent<LookChange>).detail;
      if (!change || (!change.theme && !change.density)) return;
      api("PUT", "/api/identity/me/look", change, { keepalive: true }).catch(() => undefined);
    };
    window.addEventListener(lookChosenEvent, saved);
    return () => window.removeEventListener(lookChosenEvent, saved);
  }, [allowed]);
  return null;
}
