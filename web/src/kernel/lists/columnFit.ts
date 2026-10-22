/**
 * Which of a list's columns are shown while a record is open beside it. The list keeps every
 * column it can show whole, in the user's order, and the rest wait behind a marker at the row's
 * end that names them (critic p04 round 10: with a record open the Arabic users grid ran past its
 * frame, so its leftmost columns, the end of a right-to-left row, were cut without an ellipsis and
 * the Roles column could not be seen at all). Nothing is hidden from the view itself: the user's
 * columns, saved views, copies and exports are unchanged, and closing the record or giving the list
 * room brings the columns back.
 */

/** Width of the marker's column, in rem (lists.css .list-more). */
export const moreColumnRem = 3;

/**
 * How many of the columns (their minimum widths in pixels, in order) fit whole in `available`
 * pixels after the leading `fixed` pixels (the selection column); when some do not, the marker's
 * `marker` pixels are kept for it. At least one column is always shown. An unknown width (0: no
 * layout yet) shows every column.
 */
export function shownColumns(minimums: readonly number[], available: number, fixed: number, marker: number): number {
  if (!(available > 0)) return minimums.length;
  const all = fixed + minimums.reduce((sum, w) => sum + w, 0);
  if (all <= available + 0.5) return minimums.length;
  let used = fixed + marker;
  let count = 0;
  for (const width of minimums) {
    if (used + width > available + 0.5) break;
    used += width;
    count++;
  }
  return Math.max(1, Math.min(count, minimums.length));
}
