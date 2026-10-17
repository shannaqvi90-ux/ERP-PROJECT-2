/**
 * A Tax Registration Number as the company form keeps it while it is typed (docs/compliance/
 * vat-trn-format.md, owner decision 2026-10-17): the Arabic-Indic digits ٠ to ٩ (U+0660 to U+0669)
 * of an Arabic keyboard become 0 to 9 at once, so the field shows the TRN in 0 to 9 on English and
 * Arabic screens alike. Nothing else is changed here: other digit sets (the Persian ۰ to ۹) are left
 * as typed and the server refuses them; spaces and dashes are removed by the server when it saves.
 */
export function trnDigits(typed: string): string {
  return typed.replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660));
}
