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

/** Pixels of the splitter between the list and the form (lists.css: its 10px grid column). */
export const splitterWidth = 10;

/** The narrowest a screen's form gets by itself (lists.css: min(380px, 50%) of the list body). */
export const formFloor = 380;

/**
 * The share a form opens at when the user has not chosen one: the standard share, or less when the
 * list beside it would then be narrower than its columns need (`listNeed` pixels: every visible
 * column at its minimum, with the frame), never under the form's own floor (critic p04 round 10:
 * the Arabic users list at 1440px kept 614 of the 791 pixels its columns need, and its leftmost
 * columns were cut at the frame's edge). Unknown widths (no layout yet) keep the standard share.
 */
export function openingShare(bodyWidth: number, listNeed: number): number {
  if (!(bodyWidth > 0) || !(listNeed > 0)) return recordShare.standard;
  const leaves = Math.floor(((bodyWidth - listNeed - splitterWidth) / bodyWidth) * 100);
  const floor = Math.ceil((Math.min(formFloor, bodyWidth / 2) / bodyWidth) * 100);
  return clampShare(Math.min(recordShare.standard, Math.max(floor, leaves)));
}

/** The share that leaves the list `listNeed` pixels beside the form, never under the narrowest share. */
export function shareForList(bodyWidth: number, listNeed: number): number {
  if (!(bodyWidth > 0)) return recordShare.standard;
  return clampShare(Math.floor(((bodyWidth - listNeed - splitterWidth) / bodyWidth) * 100));
}
