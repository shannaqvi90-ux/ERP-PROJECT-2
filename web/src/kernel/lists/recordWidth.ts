/**
 * How wide a screen's record form is beside its list (renderRecord: a role's permission matrix, a
 * user's "What they can do"). The share is the form's percentage of the list body; the list keeps
 * the rest, never less than `listFloor` pixels (enforced in lists.css), so the dense list stays on
 * screen however wide the form gets.
 *
 * - A form opens at the standard share and grows by itself, up to the widest share, when its
 *   content is wider than the panel (a wide permission table at any window size).
 * - The user can set the width: drag the splitter, or focus it and use the arrow keys, Home and
 *   End; Enter on the splitter or Alt+W anywhere switches between the standard and the widest
 *   share. The user's choice is kept per list in this browser (a convenience: without storage the
 *   panel simply opens at the standard width and fits its content).
 */
export const recordShare = { min: 30, standard: 48, wide: 75, step: 4 } as const;

/** Pixels the list keeps beside the widest form (lists.css caps the form's column with it). */
export const listFloor = 288;

/** Widen or narrow the open record (the standard and the widest share). */
export const widenChord = "Alt+KeyW";

const storageKey = (listKey: string) => `erp.lists.recordShare.${listKey}`;

export function clampShare(share: number): number {
  if (!Number.isFinite(share)) return recordShare.standard;
  return Math.min(recordShare.wide, Math.max(recordShare.min, Math.round(share)));
}

/** The share the user chose for this list, or null (none, or storage unavailable). */
export function readShare(listKey: string): number | null {
  try {
    const text = window.localStorage.getItem(storageKey(listKey));
    if (text === null) return null;
    const value = Number(text);
    return Number.isFinite(value) ? clampShare(value) : null;
  } catch {
    return null;
  }
}

export function writeShare(listKey: string, share: number | null): void {
  try {
    if (share === null) window.localStorage.removeItem(storageKey(listKey));
    else window.localStorage.setItem(storageKey(listKey), String(clampShare(share)));
  } catch {
    // Storage blocked (private window, previews): the choice lasts until the page closes.
  }
}

/** The share a pointer at clientX gives the form: the form sits at the inline end of the body
 * (the right in English, the left in Arabic). */
export function shareFromPointer(clientX: number, body: { left: number; right: number; width: number }, rtl: boolean): number {
  if (body.width <= 0) return recordShare.standard;
  const formWidth = rtl ? clientX - body.left : body.right - clientX;
  return clampShare((formWidth / body.width) * 100);
}

/** The share at which a form now `panelWidth` wide, whose content needs `overflow` more pixels,
 * shows all of it; never less than the current share. */
export function shareToFit(current: number, panelWidth: number, overflow: number, bodyWidth: number): number {
  if (overflow <= 1 || bodyWidth <= 0) return current;
  return clampShare(Math.max(current, Math.ceil(((panelWidth + overflow + 2) / bodyWidth) * 100)));
}

/** The share after a key on the focused splitter, or null for a key it does not handle. The arrow
 * pointing away from the form widens it (left in English, right in Arabic). */
export function shareAfterKey(key: string, share: number, rtl: boolean): number | null {
  const widen = rtl ? "ArrowRight" : "ArrowLeft";
  const narrow = rtl ? "ArrowLeft" : "ArrowRight";
  switch (key) {
    case widen:
      return clampShare(share + recordShare.step);
    case narrow:
      return clampShare(share - recordShare.step);
    case "Home":
      return recordShare.min;
    case "End":
      return recordShare.wide;
    case "Enter":
    case " ":
      return toggledShare(share);
    default:
      return null;
  }
}

/** Alt+W, Enter on the splitter, a double click on it: the widest share, or back to the standard. */
export function toggledShare(share: number): number {
  return share >= recordShare.wide ? recordShare.standard : recordShare.wide;
}
