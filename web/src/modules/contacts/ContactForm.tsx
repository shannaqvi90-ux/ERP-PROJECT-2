import { useEffect, useRef, useState } from "react";
import { api, ApiError } from "../../kernel/api";
import { CountryField, LookupField, MoneyField, PhoneField, SelectField, TextAreaField, TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { recordPath } from "../../kernel/router";
import { useSession } from "../../kernel/session";
import {
  contactName,
  contactsChanged,
  contactsPath,
  duplicateQuery,
  emirates,
  organisationName,
  parseTags,
  trnDigits,
  type Contact,
  type ContactDefaults,
  type ContactDuplicate,
  type ContactKind,
  type ContactRow,
  type Emirate,
  type ListPage,
} from "./types";

export type Draft = {
  kind: ContactKind;
  nameEn: string;
  nameAr: string;
  organisationId: string;
  jobTitle: string;
  code: string;
  email: string;
  mobile: string;
  phone: string;
  website: string;
  addressLine1: string;
  addressLine2: string;
  area: string;
  city: string;
  emirate: Emirate | "";
  poBox: string;
  country: string;
  addressAr: string;
  tags: string;
  tradeLicenceNumber: string;
  taxRegistrationNumber: string;
  defaultCurrency: string;
  creditLimitAmount: string;
  creditLimitCurrency: string;
  creditLimitExchangeRate: string;
  notes: string;
};

/** What a new contact starts as: an organisation in the UAE trading in the working company's currency,
 * or a person of the organisation it was started from. */
export type NewContact = { kind?: ContactKind; organisationId?: string; organisationLabel?: string };

export function draftOf(c: Contact | null, defaults: ContactDefaults | null, start: NewContact = {}): Draft {
  const currency = defaults?.defaultCurrency ?? "AED";
  if (!c) {
    return {
      kind: start.kind ?? "organisation",
      nameEn: "",
      nameAr: "",
      organisationId: start.organisationId ?? "",
      jobTitle: "",
      code: "",
      email: "",
      mobile: "",
      phone: "",
      website: "",
      addressLine1: "",
      addressLine2: "",
      area: "",
      city: "",
      emirate: "",
      poBox: "",
      country: defaults?.country ?? "AE",
      addressAr: "",
      tags: "",
      tradeLicenceNumber: "",
      taxRegistrationNumber: "",
      defaultCurrency: currency,
      creditLimitAmount: "",
      creditLimitCurrency: currency,
      creditLimitExchangeRate: "",
      notes: "",
    };
  }
  return {
    kind: c.kind,
    nameEn: c.nameEn,
    nameAr: c.nameAr,
    organisationId: c.organisationId ?? "",
    jobTitle: c.jobTitle ?? "",
    code: c.code ?? "",
    email: c.email ?? "",
    mobile: c.mobile ?? "",
    phone: c.phone ?? "",
    website: c.website ?? "",
    addressLine1: c.addressLine1 ?? "",
    addressLine2: c.addressLine2 ?? "",
    area: c.area ?? "",
    city: c.city ?? "",
    emirate: c.emirate ?? "",
    poBox: c.poBox ?? "",
    country: c.country,
    addressAr: c.addressAr ?? "",
    tags: c.tags.join(", "),
    tradeLicenceNumber: c.tradeLicenceNumber ?? "",
    taxRegistrationNumber: c.taxRegistrationNumber ?? "",
    defaultCurrency: c.defaultCurrency,
    creditLimitAmount: c.creditLimit?.amount ?? "",
    creditLimitCurrency: c.creditLimit?.currency ?? c.defaultCurrency,
    creditLimitExchangeRate: c.creditLimit && c.creditLimit.currency !== c.creditLimit.baseCurrency ? c.creditLimit.exchangeRate : "",
    notes: c.notes ?? "",
  };
}

const optional = (value: string) => (value.trim() === "" ? null : value.trim());

/** What the API is sent for a draft (src/Modules/Contacts SaveContactRequest). */
export function requestOf(draft: Draft, version: number | null) {
  const person = draft.kind === "person";
  const amount = optional(draft.creditLimitAmount);
  return {
    kind: draft.kind,
    code: optional(draft.code),
    nameEn: draft.nameEn.trim(),
    nameAr: draft.nameAr.trim(),
    organisationId: person && draft.organisationId ? draft.organisationId : null,
    jobTitle: person ? optional(draft.jobTitle) : null,
    email: optional(draft.email),
    phone: optional(draft.phone.replace(/^\+971\s*$/, "")),
    mobile: optional(draft.mobile.replace(/^\+971\s*$/, "")),
    website: optional(draft.website),
    addressLine1: optional(draft.addressLine1),
    addressLine2: optional(draft.addressLine2),
    area: optional(draft.area),
    city: optional(draft.city),
    emirate: draft.country.trim().toUpperCase() === "AE" && draft.emirate ? draft.emirate : null,
    poBox: optional(draft.poBox),
    country: draft.country.trim().toUpperCase(),
    addressAr: optional(draft.addressAr),
    tags: parseTags(draft.tags),
    tradeLicenceNumber: optional(draft.tradeLicenceNumber),
    taxRegistrationNumber: optional(draft.taxRegistrationNumber),
    defaultCurrency: draft.defaultCurrency.trim().toUpperCase(),
    creditLimitAmount: amount,
    creditLimitCurrency: amount === null ? null : optional(draft.creditLimitCurrency)?.toUpperCase() ?? null,
    creditLimitExchangeRate: amount === null ? null : optional(draft.creditLimitExchangeRate),
    notes: optional(draft.notes),
    version,
  };
}

/** Currencies offered first; any ISO 4217 code is accepted. */
const currencies = Object.freeze(["AED", "USD", "EUR", "GBP", "SAR", "OMR", "QAR", "BHD", "KWD", "INR", "PKR", "CNY"]);

/** What a new contact starts with for the signed-in user (asked each time a form opens: nothing of
 * one identity is kept in the page for the next). */
export function contactDefaults(): Promise<ContactDefaults> {
  return api<ContactDefaults>("GET", "/api/contacts/contacts/defaults");
}

/**
 * Create or edit one contact on the shared record form (keys, unsaved changes, server errors,
 * previous and next, print as a contact card in English or Arabic). As the names, e-mail, numbers
 * or tax registration number are typed, contacts that may be the same are named above the form
 * (a warning, never a refusal). An organisation lists its people below, with "New person" for
 * those who may create contacts; archive and restore sit in the toolbar for those who may.
 */
export function ContactForm(props: ContactFormProps) {
  // A new contact starts from the defaults (the working company's currency), so its form waits for
  // them: an untouched new form then has nothing unsaved. A saved contact needs them only for the
  // credit limit's base currency.
  const [defaults, setDefaults] = useState<ContactDefaults | null | undefined>(undefined);
  useEffect(() => {
    let live = true;
    contactDefaults().then(
      (d) => live && setDefaults(d),
      () => live && setDefaults(null),
    );
    return () => {
      live = false;
    };
  }, []);
  if (props.id === null && defaults === undefined) return <div className="ct-form-wait" aria-busy="true" />;
  return <ContactFormWith {...props} defaults={defaults ?? null} />;
}

type ContactFormProps = {
  id: string | null;
  start?: NewContact;
  onSaved: (id: string) => void;
  onClose: () => void;
  onOpen: (id: string) => void;
  onNewPerson?: (organisationId: string, label: string) => void;
  nav?: RecordNavigation;
};

function ContactFormWith({ id, start, onSaved, onClose, onOpen, onNewPerson, nav, defaults }: ContactFormProps & { defaults: ContactDefaults | null }) {
  const { t, language } = useI18n();
  const { can } = useSession();
  const form = useRecordForm<Contact, Draft>({
    load: id === null ? undefined : (signal) => api<Contact>("GET", `/api/contacts/contacts/${id}`, undefined, { signal }),
    initial: (c) => draftOf(c, defaults, start),
    canEdit: id === null ? can("contacts.contacts.create") : can("contacts.contacts.update"),
    save: (draft, contact) =>
      contact === null
        ? api<Contact>("POST", "/api/contacts/contacts", requestOf(draft, null))
        : api<Contact>("PUT", `/api/contacts/contacts/${contact.id}`, requestOf(draft, contact.version)),
    onSaved: (saved) => {
      onSaved(saved.id);
      window.dispatchEvent(new Event(contactsChanged));
    },
  }, id);
  const contact = form.record;
  const bind = form.bind;
  const draft = form.draft;
  const person = draft.kind === "person";
  const inUae = draft.country.trim().toUpperCase() === "AE";
  const arabic = language === "ar";
  const trn = bind("taxRegistrationNumber");
  const kind = bind("kind");
  const kindField = { ...kind, onComplete: undefined, onChange: (value: ContactKind | "") => kind.onChange(value === "person" ? "person" : "organisation") };
  const currency = bind("defaultCurrency");

  const nameEn = <TextField key="nameEn" field={bind("nameEn")} label={t("contacts.contact.nameEn")} dir="ltr" maxLength={200} autoFocus={id === null && !arabic} />;
  const nameAr = <TextField key="nameAr" field={bind("nameAr")} label={t("contacts.contact.nameAr")} dir="rtl" maxLength={200} autoFocus={id === null && arabic} />;

  return (
    <RecordForm
      form={form}
      label={t("contacts.contact.form")}
      title={contact ? contactName(contact, language) || t("contacts.contact.unnamed") : t(person ? "contacts.contact.newPerson" : "contacts.contact.new")}
      subtitle={contact ? <ContactSubtitle contact={contact} /> : undefined}
      onClose={onClose}
      nav={nav}
      document={contact ? { report: "contacts.contactCard", parameter: "contact", id: contact.id } : undefined}
      readOnlyReason={contact && !can("contacts.contacts.update") ? t("contacts.contact.readOnly") : undefined}
      actions={contact && can("contacts.contacts.archive") ? <ArchiveButton contact={contact} onChanged={form.adopt} /> : undefined}
      after={
        contact && contact.kind === "organisation" ? (
          <OrganisationPeople organisation={contact} onOpen={onOpen} onNewPerson={can("contacts.contacts.create") && contact.isActive ? onNewPerson : undefined} />
        ) : undefined
      }
    >
      <DuplicateWarning draft={draft} except={contact?.id ?? null} onOpen={onOpen} />
      {contact && !contact.isActive && (
        <p className="ct-archived" role="status">
          {t("contacts.contact.archivedNotice")}
        </p>
      )}
      <FormSection title={t("contacts.section.general")}>
        {/* On an Arabic screen the Arabic name comes first and a new contact starts there. */}
        {arabic ? [nameAr, nameEn] : [nameEn, nameAr]}
        <SelectField
          field={kindField}
          label={t("contacts.contact.kind")}
          options={[
            { value: "organisation", label: t("contacts.kind.organisation") },
            { value: "person", label: t("contacts.kind.person") },
          ]}
        />
        {person && (
          <LookupField
            field={bind("organisationId")}
            label={t("contacts.contact.organisation")}
            endpoint="/api/contacts/contacts"
            filter="kind eq 'organisation' and isActive eq true"
            labelOf={(row) => contactName(row as unknown as ContactRow, language)}
            initialLabel={
              contact?.organisationId && contact.organisationId === draft.organisationId
                ? organisationName(contact, language)
                : start?.organisationId === draft.organisationId
                  ? start?.organisationLabel
                  : undefined
            }
          />
        )}
        {person && <TextField field={bind("jobTitle")} label={t("contacts.contact.jobTitle")} maxLength={100} />}
        <TextField field={bind("code")} label={t("contacts.contact.code")} dir="ltr" maxLength={30} hint={t("contacts.contact.codeHint")} />
      </FormSection>
      <FormSection title={t("contacts.section.reach")}>
        <TextField field={bind("email")} label={t("contacts.contact.email")} dir="ltr" type="email" maxLength={254} />
        <PhoneField field={bind("mobile")} label={t("contacts.contact.mobile")} />
        <PhoneField field={bind("phone")} label={t("contacts.contact.phone")} />
        <TextField field={bind("website")} label={t("contacts.contact.website")} dir="ltr" type="url" maxLength={200} />
      </FormSection>
      <FormSection title={t("contacts.section.address")}>
        <TextField field={bind("addressLine1")} label={t("contacts.contact.addressLine1")} maxLength={200} />
        <TextField field={bind("addressLine2")} label={t("contacts.contact.addressLine2")} maxLength={200} />
        <TextField field={bind("area")} label={t("contacts.contact.area")} maxLength={100} />
        <TextField field={bind("city")} label={t("contacts.contact.city")} maxLength={100} />
        {inUae && (
          <SelectField
            field={bind("emirate")}
            label={t("contacts.contact.emirate")}
            empty={t("contacts.contact.noEmirate")}
            options={emirates.map((e) => ({ value: e, label: t(`contacts.emirate.${e}`) }))}
          />
        )}
        <TextField field={bind("poBox")} label={t("contacts.contact.poBox")} dir="ltr" maxLength={20} />
        <CountryField field={bind("country")} label={t("contacts.contact.country")} />
        <TextAreaField field={bind("addressAr")} label={t("contacts.contact.addressAr")} dir="rtl" maxLength={400} />
      </FormSection>
      <FormSection title={t("contacts.section.registration")}>
        <TextField field={bind("tradeLicenceNumber")} label={t("contacts.contact.tradeLicenceNumber")} dir="ltr" maxLength={50} />
        {/* Arabic-Indic digits typed on an Arabic keyboard show as 0 to 9 at once (owner 2026-10-17). */}
        <TextField field={{ ...trn, onChange: (value: string) => trn.onChange(trnDigits(value)) }} label={t("contacts.contact.taxRegistrationNumber")} dir="ltr" inputMode="numeric" maxLength={40}
          hint={t("contacts.contact.taxRegistrationNumberHint")} />
      </FormSection>
      <FormSection title={t("contacts.section.commercial")}>
        <TextField field={{ ...currency, onChange: (value: string) => {
          currency.onChange(value);
          // The credit limit follows the default currency until it holds an amount of its own.
          if (draft.creditLimitAmount.trim() === "") form.set("creditLimitCurrency")(value.toUpperCase());
        } }} label={t("contacts.contact.defaultCurrency")} dir="ltr" maxLength={3} upper list="contacts-currencies" />
        <datalist id="contacts-currencies">
          {currencies.map((c) => (
            <option key={c} value={c} />
          ))}
        </datalist>
        <MoneyField
          amount={bind("creditLimitAmount")}
          currency={bind("creditLimitCurrency")}
          rate={bind("creditLimitExchangeRate")}
          baseCurrency={defaults?.baseCurrency ?? contact?.creditLimit?.baseCurrency ?? "AED"}
          currencies={currencies}
          label={t("contacts.contact.creditLimit")}
          hint={t("contacts.contact.creditLimitHint")}
        />
        <TextField field={bind("tags")} label={t("contacts.contact.tags")} maxLength={400} hint={t("contacts.contact.tagsHint")} />
      </FormSection>
      <FormSection title={t("contacts.section.notes")} columns={false}>
        <TextAreaField field={bind("notes")} label={t("contacts.contact.notes")} maxLength={2000} rows={3} />
      </FormSection>
    </RecordForm>
  );
}

function ContactSubtitle({ contact }: { contact: Contact }) {
  const { t, language } = useI18n();
  const parts = [
    t(`contacts.kind.${contact.kind}`),
    contact.code,
    contact.kind === "person" && contact.organisationId ? organisationName(contact, language) : null,
    contact.isActive ? null : t("contacts.status.archived"),
  ].filter((p): p is string => Boolean(p));
  return <span className="muted">{parts.join(" · ")}</span>;
}

/** Archive (or restore) the open contact: a contact is never deleted. */
function ArchiveButton({ contact, onChanged }: { contact: Contact; onChanged: (c: Contact) => void }) {
  const { t } = useI18n();
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const run = async () => {
    setBusy(true);
    setMessage(null);
    try {
      const changed = await api<Contact>("POST", `/api/contacts/contacts/${contact.id}/${contact.isActive ? "archive" : "restore"}`);
      onChanged(changed);
      window.dispatchEvent(new Event(contactsChanged));
    } catch (error) {
      setMessage(error instanceof ApiError ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <button type="button" className="button" disabled={busy} onClick={() => void run()} data-action={contact.isActive ? "archive" : "restore"}>
        {t(contact.isActive ? "contacts.action.archive" : "contacts.action.restore")}
      </button>
      {message && (
        <span className="field-error" role="alert">
          {message}
        </span>
      )}
    </>
  );
}

/** How long after the last key the duplicate check asks. */
export const duplicateDelay = 400;

/**
 * Contacts that may be the same as the one in the form: asked a moment after the names, e-mail,
 * numbers or tax registration number change; each is named with what it shares and opens on a click.
 */
function DuplicateWarning({ draft, except, onOpen }: { draft: Draft; except: string | null; onOpen: (id: string) => void }) {
  const { t, language } = useI18n();
  const [found, setFound] = useState<ContactDuplicate[]>([]);
  const query = duplicateQuery(draft, except);
  const latest = useRef(0);
  useEffect(() => {
    const ask = ++latest.current;
    if (query === null) {
      setFound([]);
      return;
    }
    const timer = window.setTimeout(() => {
      api<ContactDuplicate[]>("GET", `/api/contacts/contacts/duplicates?${query}`)
        .then((rows) => ask === latest.current && setFound(rows))
        .catch(() => ask === latest.current && setFound([]));
    }, duplicateDelay);
    return () => window.clearTimeout(timer);
  }, [query]);
  if (found.length === 0) return null;
  return (
    <div className="ct-duplicates" role="status" aria-live="polite" data-duplicates={found.length}>
      <strong>{t("contacts.duplicates.title", { count: found.length })}</strong>
      <ul>
        {found.slice(0, 5).map((d) => (
          <li key={d.id}>
            <a
              href={recordPath(contactsPath, d.id)}
              onClick={(event) => {
                event.preventDefault();
                onOpen(d.id);
              }}
            >
              {contactName(d, language)}
            </a>{" "}
            <span className="muted">
              {[d.code, d.isActive ? null : t("contacts.status.archived"), d.matches.map((m) => t(`contacts.duplicates.match.${m}`)).join(", ")]
                .filter(Boolean)
                .join(" · ")}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** The people of an organisation, each opening on a click, and "New person" for this organisation. */
function OrganisationPeople({ organisation, onOpen, onNewPerson }: { organisation: Contact; onOpen: (id: string) => void; onNewPerson?: (organisationId: string, label: string) => void }) {
  const { t, language } = useI18n();
  const [people, setPeople] = useState<ContactRow[]>([]);
  const [total, setTotal] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    const load = () => {
      const query = new URLSearchParams({ filter: `organisationId eq '${organisation.id}'`, take: "50" });
      api<ListPage<ContactRow>>("GET", `/api/contacts/contacts?${query}`, undefined, { signal: controller.signal })
        .then((page) => {
          setPeople(page.items);
          setTotal(page.total);
        })
        .catch(() => undefined);
    };
    load();
    window.addEventListener(contactsChanged, load);
    return () => {
      controller.abort();
      window.removeEventListener(contactsChanged, load);
    };
  }, [organisation.id]);
  return (
    <section className="record-section" aria-label={t("contacts.people.title")}>
      <h3>{t("contacts.people.count", { count: total })}</h3>
      {people.length > 0 ? (
        <table className="grid compact">
          <thead>
            <tr>
              <th scope="col">{t("contacts.contact.name")}</th>
              <th scope="col">{t("contacts.contact.jobTitle")}</th>
              <th scope="col">{t("contacts.contact.mobile")}</th>
              <th scope="col">{t("contacts.contact.email")}</th>
            </tr>
          </thead>
          <tbody>
            {people.map((p) => (
              <tr key={p.id} className={p.isActive ? undefined : "inactive"}>
                <td>
                  <a
                    href={recordPath(contactsPath, p.id)}
                    onClick={(event) => {
                      event.preventDefault();
                      onOpen(p.id);
                    }}
                  >
                    {contactName(p, language)}
                  </a>
                </td>
                <td>{p.jobTitle}</td>
                <td dir="ltr">{p.mobile}</td>
                <td dir="ltr">{p.email}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <p className="muted">{t("contacts.people.none")}</p>
      )}
      {onNewPerson && (
        <button type="button" className="button" onClick={() => onNewPerson(organisation.id, contactName(organisation, language))}>
          {t("contacts.people.new")}
        </button>
      )}
    </section>
  );
}
