import { Dialog } from "../../kernel/dialog";
import { useI18n } from "../../kernel/i18n";
import { Keys, useShortcutList } from "../../kernel/shortcuts";

/** Keys that work inside a component rather than globally; listed so the sheet is complete. */
const localKeys: { groupKey: string; chords: string[]; labelKey: string }[] = [
  { groupKey: "shell.shortcuts.group.palette", chords: ["ArrowUp", "ArrowDown"], labelKey: "shell.shortcuts.palette.move" },
  { groupKey: "shell.shortcuts.group.palette", chords: ["Enter"], labelKey: "shell.shortcuts.palette.open" },
  { groupKey: "shell.shortcuts.group.dialogs", chords: ["Escape"], labelKey: "shell.shortcuts.dialog.close" },
  { groupKey: "shell.shortcuts.group.navigation", chords: ["ArrowUp", "ArrowDown"], labelKey: "shell.shortcuts.nav.move" },
  { groupKey: "shell.shortcuts.group.navigation", chords: ["Home", "End"], labelKey: "shell.shortcuts.nav.ends" },
  { groupKey: "shell.shortcuts.group.navigation", chords: ["Tab"], labelKey: "shell.shortcuts.skip" },
];

const groupOrder = ["shell.shortcuts.group.general", "shell.shortcuts.group.navigation", "shell.shortcuts.group.palette", "shell.shortcuts.group.dialogs"];

/** Every shortcut that is active right now (modules' included), with what it does. */
export function ShortcutHelp({ onClose }: { onClose: () => void }) {
  const { t } = useI18n();
  const shortcuts = useShortcutList();
  const rows = [
    ...shortcuts.map((s) => ({ groupKey: s.groupKey, chords: [s.chord], labelKey: s.labelKey, id: s.id })),
    ...localKeys.map((k, i) => ({ ...k, id: `local-${i}` })),
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
                      {row.chords.map((chord) => (
                        <Keys key={chord} chord={chord} />
                      ))}
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
