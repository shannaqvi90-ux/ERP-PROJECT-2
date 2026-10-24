import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../../kernel/api";
import { newRecord, useRecordPanel } from "../../kernel/forms/recordPanel";
import { useI18n } from "../../kernel/i18n";
import { ListView, type BulkAction } from "../../kernel/lists/ListView";
import type { Row } from "../../kernel/lists/model";
import { useSession } from "../../kernel/session";
import { useShortcut } from "../../kernel/shortcuts";
import { ContactForm, type NewContact } from "./ContactForm";
import { contactsChanged, organisationName, type ContactRow } from "./types";
import "./contacts.css";

/** How many archive or restore requests a bulk change runs at once. */
export const bulkChanges = 6;

/**
 * Archives (or restores) the chosen contacts, a few at a time; contacts already in that state are
 * left alone. Returns how many changed and how many the API refused.
 */
export async function setContactsActive(rows: Row[], active: boolean): Promise<{ changed: number; refused: number }> {
  const pending = rows.filter((row) => Boolean(row.isActive) !== active);
  let changed = 0;
  let refused = 0;
  let next = 0;
  async function worker() {
    while (next < pending.length) {
      const row = pending[next++]!;
      try {
        await api("POST", `/api/contacts/contacts/${String(row.id)}/${active ? "restore" : "archive"}`);
        changed++;
      } catch {
        refused++;
      }
    }
  }
  await Promise.all(Array.from({ length: Math.min(bulkChanges, pending.length) }, worker));
  window.dispatchEvent(new Event(contactsChanged));
  return { changed, refused };
}

/** The list's search box (the list framework's own). */
const searchBox = () => document.querySelector<HTMLInputElement>(".list-search input");

/**
 * Contacts: the shared directory of organisations and people on the list framework (search as you
 * type over names in both scripts, e-mail, numbers, reference and tax number; filters, sort,
 * grouping, views; Enter opens the best match), with the open contact's form in the list's panel
 * (<screen>/<id>, a new one at <screen>/new). Keyboard: "/" finds, Alt+N starts a new contact,
 * arrow keys move, Enter opens, Ctrl+S saves, Alt+PageDown/PageUp move to the next or previous
 * contact, Alt+R prints its card, Escape closes. What the user's roles do not grant is not offered.
 */
export function ContactsPage() {
  const { t, language } = useI18n();
  const { can } = useSession();
  const panel = useRecordPanel(can("contacts.contacts.create"));
  const [start, setStart] = useState<NewContact | undefined>(undefined);
  const [message, setMessage] = useState<string | null>(null);
  const { refresh, onOpenIdChange, startNew } = panel;

  useEffect(() => {
    window.addEventListener(contactsChanged, refresh);
    return () => window.removeEventListener(contactsChanged, refresh);
  }, [refresh]);

  const newContact = useCallback(() => {
    setStart(undefined);
    startNew?.();
  }, [startNew]);
  const newPerson = useCallback(
    (organisationId: string, organisationLabel: string) => {
      setStart({ kind: "person", organisationId, organisationLabel });
      startNew?.();
    },
    [startNew],
  );
  const latest = useRef(newContact);
  latest.current = newContact;

  // Alt+N starts a new contact from anywhere on the screen, the search box included; the palette
  // lists it as the screen's command (and runs it for "New contact" from any screen).
  useShortcut({
    id: "contacts.screen.new",
    chord: "Alt+KeyN",
    labelKey: "contacts.contact.new",
    keywordsKey: "contacts.action.newKeywords",
    groupKey: "contacts.shortcut.group",
    enabled: Boolean(startNew),
    run: () => latest.current(),
  });

  // "/" puts the cursor in the search box when no field has it.
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      const typing = event.target instanceof HTMLElement && /^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName);
      if (event.key !== "/" || typing || event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented) return;
      const box = searchBox();
      if (!box) return;
      event.preventDefault();
      box.focus();
      box.select();
    };
    window.addEventListener("keydown", handle);
    return () => window.removeEventListener("keydown", handle);
  }, []);

  const bulk = (active: boolean) => async (rows: Row[]) => {
    const result = await setContactsActive(rows, active);
    setMessage(t(active ? "contacts.bulk.restored" : "contacts.bulk.archived", { count: result.changed }) + (result.refused ? ` ${t("contacts.bulk.refused", { count: result.refused })}` : ""));
  };
  const bulkActions: BulkAction[] = [
    { key: "archive", labelKey: "contacts.action.archiveChosen", permission: "contacts.contacts.archive", run: bulk(false) },
    { key: "restore", labelKey: "contacts.action.restoreChosen", permission: "contacts.contacts.archive", run: bulk(true) },
  ];

  return (
    <div className="ct-list">
      {message && (
        <p className="notice" role="status">
          {message}
        </p>
      )}
      <ListView
        listKey="contacts.contacts"
        titleKey="contacts.contacts.title"
        countKey="contacts.contacts.count"
        searchPlaceholderKey="contacts.contacts.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={onOpenIdChange}
        bulkActions={bulkActions}
        renderRecord={(id, close, nav) => (
          <ContactForm
            key={panel.formKey}
            id={id === newRecord ? null : id}
            start={id === newRecord ? start : undefined}
            onSaved={panel.saved}
            onClose={close}
            onOpen={(other) => onOpenIdChange(other)}
            onNewPerson={newPerson}
            nav={nav}
          />
        )}
        actions={
          startNew && (
            <button type="button" className="button primary" onClick={newContact} title={t("contacts.contact.newHint")} aria-keyshortcuts="Alt+N">
              {t("contacts.contact.new")}
            </button>
          )
        }
        renderCell={{
          kind: (row) => <span className={`ct-kind ct-kind-${String(row.kind)}`}>{t(`contacts.kind.${row.kind === "person" ? "person" : "organisation"}`)}</span>,
          organisationId: (row) => organisationName(row as unknown as ContactRow, language),
          email: (row) => <span dir="ltr">{String(row.email ?? "")}</span>,
          mobile: (row) => <span dir="ltr">{String(row.mobile ?? "")}</span>,
          phone: (row) => <span dir="ltr">{String(row.phone ?? "")}</span>,
          reference: (row) => <span dir="ltr">{String(row.reference ?? "")}</span>,
          taxRegistrationNumber: (row) => <span dir="ltr">{String(row.taxRegistrationNumber ?? "")}</span>,
          isActive: (row) => (row.isActive ? t("contacts.status.active") : <span className="muted">{t("contacts.status.archived")}</span>),
        }}
      />
    </div>
  );
}
