import { useState } from "react";
import { useI18n } from "../../kernel/i18n";
import { useCommand } from "../../kernel/shortcuts";
import { ImportDialog } from "./ImportDialog";

/** The Import button in the toolbar of a list of records that can be imported; Alt+I does the same from anywhere on the screen. */
export function ImportButton({ table, titleKey, reload }: { table: string; titleKey: string; reload: () => void }) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  useCommand({ id: "importexport.import", chord: "Alt+KeyI", labelKey: "importexport.import.open", keywordsKey: "importexport.import.keywords", groupKey: "importexport.shortcut.group", run: () => setOpen(true) });
  return (
    <>
      <button type="button" className="button" onClick={() => setOpen(true)} title={t("importexport.import.hint")} aria-keyshortcuts="Alt+I" data-testid="import-open">
        {t("importexport.import.open")}
      </button>
      {open && <ImportDialog table={table} titleKey={titleKey} onClose={() => setOpen(false)} onDone={reload} />}
    </>
  );
}
