import { Dialog } from "../../kernel/dialog";
import { useI18n } from "../../kernel/i18n";
import { formChords } from "../../kernel/forms/RecordForm";
import { Keys, useShortcutList } from "../../kernel/shortcuts";
import { goToLead, type GoToTarget } from "./goTo";

const formGroup = "forms.shortcut.group";

/**
 * The keys of every record form (kernel/forms/RecordForm), listed whether a record is open or not:
 * forms are where people spend their time, and the sheet is where they learn the keys (critic p04
 * round 12: the sheet had sections for lists, the palette and dialogs, none for forms). While a
 * form is open it registers the same keys itself; `activeId` names that registration, and the
 * static row gives way to it, so no key is listed twice.
 */
const formKeys: { chords: string[]; labelKey: string; activeId?: string }[] = [
  { chords: ["Enter"], labelKey: "shell.shortcuts.form.enter" },
  { chords: [formChords.save], labelKey: "forms.shortcut.save", activeId: "forms.save" },
  { chords: [formChords.saveEnter], labelKey: "forms.shortcut.save", activeId: "forms.saveEnter" },
  { chords: [formChords.discard], labelKey: "forms.shortcut.discard", activeId: "forms.discard" },
  { chords: [formChords.next], labelKey: "forms.shortcut.next", activeId: "forms.next" },
  { chords: [formChords.previous], labelKey: "forms.shortcut.previous", activeId: "forms.previous" },
  { chords: [formChords.print], labelKey: "forms.shortcut.print", activeId: "forms.print" },
  { chords: ["Escape"], labelKey: "shell.shortcuts.form.close" },
  // The user form's role filter (identity's RolePicker registers it as identity.roles.find).
  { chords: ["Alt+KeyO"], labelKey: "shell.shortcuts.form.findRole", activeId: "identity.roles.find" },
];

/** Keys that work inside a component rather than globally; listed so the sheet is complete. */
const localKeys: { groupKey: string; chords: string[]; labelKey: string }[] = [
  // The browser's own print key prints the screen as a document (kernel/print.tsx); the palette's
  // "Print this screen" does the same.
  { groupKey: "shell.shortcuts.group.general", chords: ["Mod+KeyP"], labelKey: "shell.action.print" },
  { groupKey: "shell.shortcuts.group.palette", chords: ["ArrowUp", "ArrowDown"], labelKey: "shell.shortcuts.palette.move" },
  { groupKey: "shell.shortcuts.group.palette", chords: ["Enter"], labelKey: "shell.shortcuts.palette.open" },
  { groupKey: "shell.shortcuts.group.dialogs", chords: ["Escape"], labelKey: "shell.shortcuts.dialog.close" },
  { groupKey: "shell.shortcuts.group.navigation", chords: ["ArrowUp", "ArrowDown"], labelKey: "shell.shortcuts.nav.move" },
  { groupKey: "shell.shortcuts.group.navigation", chords: ["Home", "End"], labelKey: "shell.shortcuts.nav.ends" },
  { groupKey: "shell.shortcuts.group.navigation", chords: ["Tab"], labelKey: "shell.shortcuts.skip" },
  // The list grid's own keys (kernel/lists/ListView), the same on every list screen; the grid's
  // footer gives the short form.
  { groupKey: "shell.shortcuts.group.lists", chords: ["ArrowUp", "ArrowDown"], labelKey: "shell.shortcuts.list.move" },
  { groupKey: "shell.shortcuts.group.lists", chords: ["PageUp", "PageDown"], labelKey: "shell.shortcuts.list.page" },
  { groupKey: "shell.shortcuts.group.lists", chords: ["Home", "End"], labelKey: "shell.shortcuts.list.ends" },
  { groupKey: "shell.shortcuts.group.lists", chords: ["Enter"], labelKey: "shell.shortcuts.list.open" },
  { groupKey: "shell.shortcuts.group.lists", chords: ["Space"], labelKey: "shell.shortcuts.list.select" },
  { groupKey: "shell.shortcuts.group.lists", chords: ["Mod+KeyA"], labelKey: "shell.shortcuts.list.selectAll" },
  { groupKey: "shell.shortcuts.group.lists", chords: ["Slash"], labelKey: "shell.shortcuts.list.search" },
  { groupKey: "shell.shortcuts.group.lists", chords: ["ArrowDown"], labelKey: "shell.shortcuts.list.fromSearch" },
  { groupKey: "shell.shortcuts.group.lists", chords: ["Escape"], labelKey: "shell.shortcuts.list.back" },
];

const groupOrder = [
  "shell.shortcuts.group.general",
  "shell.shortcuts.group.navigation",
  "shell.shortcuts.group.goTo",
  "shell.shortcuts.group.palette",
  "shell.shortcuts.group.lists",
  formGroup,
  "shell.shortcuts.group.dialogs",
];

/** Every shortcut that is active right now (modules' included), with what it does. */
export function ShortcutHelp({ onClose, goTo = [] }: { onClose: () => void; goTo?: GoToTarget[] }) {
  const { t } = useI18n();
  const shortcuts = useShortcutList();
  type Row = { groupKey: string; chords: string[]; labelKey: string; id: string; sequence?: boolean };
  const rows: Row[] = [
    ...shortcuts.map((s) => ({ groupKey: s.groupKey, chords: [s.chord], labelKey: s.labelKey, id: s.id })),
    // G, then a letter (goTo.tsx): only the screens this user's navigation lists.
    ...goTo.map((g) => ({ groupKey: "shell.shortcuts.group.goTo", chords: [goToLead, g.code], labelKey: g.labelKey, id: `goto-${g.code}`, sequence: true })),
    ...localKeys.map((k, i) => ({ ...k, id: `local-${i}` })),
    ...formKeys
      .filter((k) => !k.activeId || !shortcuts.some((s) => s.id === k.activeId))
      .map((k, i) => ({ groupKey: formGroup, chords: k.chords, labelKey: k.labelKey, id: `form-${i}` })),
  ];
  const groups = [...new Set([...groupOrder, ...rows.map((r) => r.groupKey)])].filter((g) => rows.some((r) => r.groupKey === g));

  return (
    <Dialog title={t("shell.shortcuts.title")} onClose={onClose} className="shortcut-help">
      <p className="muted">{t("shell.shortcuts.intro")}</p>
      <div className="shortcut-groups">
        {groups.map((group) => (
          <table key={group} className="shortcut-table">
            <caption>{t(group)}</caption>
            <thead className="visually-hidden">
              <tr>
                <th scope="col">{t("shell.shortcuts.column.keys")}</th>
                <th scope="col">{t("shell.shortcuts.column.action")}</th>
              </tr>
            </thead>
            <tbody>
              {rows
                .filter((r) => r.groupKey === group)
                .map((row) => (
                  <tr key={row.id} data-shortcut={row.id}>
                    <td>
                      {row.sequence
                        ? row.chords.map((chord, i) => (
                            <span key={chord}>
                              {i > 0 && <span className="keys-then"> {t("shell.shortcuts.then")} </span>}
                              <Keys chord={chord} />
                            </span>
                          ))
                        : row.chords.map((chord) => <Keys key={chord} chord={chord} />)}
                    </td>
                    <td>{t(row.labelKey)}</td>
                  </tr>
                ))}
            </tbody>
          </table>
        ))}
      </div>
      <div className="dialog-actions">
        <button type="button" className="button" onClick={onClose}>
          {t("shell.prefs.close")}
        </button>
      </div>
    </Dialog>
  );
}
