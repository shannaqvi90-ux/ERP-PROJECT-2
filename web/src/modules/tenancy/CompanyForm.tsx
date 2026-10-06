import { useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "../../kernel/api";
import { BooleanField, SelectField, TextAreaField, TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { useRecordForm, type RecordFormState } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { loadAll } from "./records";
import { companiesChanged, type BranchRow, type Company } from "./types";
import { emirates, problemOf, useMonths, useLocalName, type Emirate, type FieldErrors } from "./ui";

type Draft = {
  code: string;
  legalNameEn: string;
  legalNameAr: string;
  tradeLicenceNumber: string;
  tradeLicenceAuthority: string;
  taxRegistrationNumber: string;
  baseCurrency: string;
  fiscalYearStartMonth: string;
  fiscalYearStartDay: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  emirate: Emirate | "";
  poBox: string;
  country: string;
  addressAr: string;
  phone: string;
  email: string;
  website: string;
  isActive: boolean;
};

const blank: Draft = Object.freeze({
  code: "",
  legalNameEn: "",
  legalNameAr: "",
  tradeLicenceNumber: "",
  tradeLicenceAuthority: "",
  taxRegistrationNumber: "",
  baseCurrency: "AED",
  fiscalYearStartMonth: "1",
  fiscalYearStartDay: "1",
  addressLine1: "",
  addressLine2: "",
  city: "",
  emirate: "",
  poBox: "",
  country: "AE",
  addressAr: "",
  phone: "",
  email: "",
  website: "",
  isActive: true,
});

function draftOf(c: Company | null): Draft {
  if (!c) return { ...blank };
  return {
    code: c.code,
    legalNameEn: c.legalNameEn,
    legalNameAr: c.legalNameAr,
    tradeLicenceNumber: c.tradeLicenceNumber ?? "",
    tradeLicenceAuthority: c.tradeLicenceAuthority ?? "",
    taxRegistrationNumber: c.taxRegistrationNumber ?? "",
    baseCurrency: c.baseCurrency,
    fiscalYearStartMonth: String(c.fiscalYearStartMonth),
    fiscalYearStartDay: String(c.fiscalYearStartDay),
    addressLine1: c.addressLine1 ?? "",
    addressLine2: c.addressLine2 ?? "",
    city: c.city ?? "",
    emirate: c.emirate ?? "",
    poBox: c.poBox ?? "",
    country: c.country,
    addressAr: c.addressAr ?? "",
    phone: c.phone ?? "",
    email: c.email ?? "",
    website: c.website ?? "",
    isActive: c.isActive,
  };
}

const optional = (value: string) => (value.trim() === "" ? null : value.trim());

/** Currencies offered first; any ISO 4217 code is accepted. */
const currencies = Object.freeze(["AED", "SAR", "OMR", "QAR", "BHD", "KWD", "USD", "EUR", "GBP", "INR", "PKR", "CNY"]);

/** Create or edit one company (the shared record form: keys, unsaved changes, server errors,
 * previous and next, print in English or Arabic); once saved, its branches can be added below. */
export function CompanyForm({ id, onSaved, onClose, nav }: { id: string | null; onSaved: (id: string) => void; onClose: () => void; nav?: RecordNavigation }) {
  const { t } = useI18n();
  const { can } = useSession();
  const months = useMonths();
  const name = useLocalName();
  const [justCreated, setJustCreated] = useState(false);
  const form = useRecordForm<Company, Draft>({
    load: id === null ? undefined : (signal) => api<Company>("GET", `/api/tenancy/companies/${id}`, undefined, { signal }),
    initial: draftOf,
    canEdit: id === null ? can("tenancy.companies.create") : can("tenancy.companies.update"),
    save: (draft, company) => {
      const body = {
        code: draft.code.trim(),
        legalNameEn: draft.legalNameEn,
        legalNameAr: draft.legalNameAr,
        tradeLicenceNumber: optional(draft.tradeLicenceNumber),
        tradeLicenceAuthority: optional(draft.tradeLicenceAuthority),
        taxRegistrationNumber: optional(draft.taxRegistrationNumber),
        baseCurrency: draft.baseCurrency.trim().toUpperCase(),
        fiscalYearStartMonth: Number.parseInt(draft.fiscalYearStartMonth, 10) || null,
        fiscalYearStartDay: Number.parseInt(draft.fiscalYearStartDay, 10) || null,
        addressLine1: optional(draft.addressLine1),
        addressLine2: optional(draft.addressLine2),
        city: optional(draft.city),
        emirate: draft.emirate === "" ? null : draft.emirate,
        poBox: optional(draft.poBox),
        country: draft.country.trim().toUpperCase(),
        addressAr: optional(draft.addressAr),
        phone: optional(draft.phone),
        email: optional(draft.email),
        website: optional(draft.website),
        isActive: draft.isActive,
        version: company?.version ?? null,
      };
      return company === null
        ? api<Company>("POST", "/api/tenancy/companies", body)
        : api<Company>("PUT", `/api/tenancy/companies/${company.id}`, body);
    },
    onSaved: (saved, created) => {
      // A new company's next step is its first branch: the branch line takes the focus.
      setJustCreated(created);
      onSaved(saved.id);
      window.dispatchEvent(new Event(companiesChanged));
    },
  }, id);
  const company = form.record;
  const bind = form.bind;

  return (
    <RecordForm
      form={form}
      label={t("tenancy.company.form")}
      title={company ? `${company.code} · ${name(company.legalNameEn, company.legalNameAr)}` : t("tenancy.company.new")}
      onClose={onClose}
      nav={nav}
      document={company ? { report: "tenancy.companyProfile", parameter: "company", id: company.id } : undefined}
      after={
        company && (
          <>
            <CompanyLogo company={company} editable={can("tenancy.companies.update")} onChange={form.adopt} />
            {can("tenancy.branches.read") && <CompanyBranches companyId={company.id} defaultEmirate={company.emirate ?? ""} autoFocus={justCreated} />}
          </>
        )
      }
    >
      <FormSection title={t("tenancy.company.general")}>
        <TextField field={bind("legalNameEn")} label={t("tenancy.company.legalNameEn")} dir="ltr" maxLength={200} autoFocus={id === null} />
        <TextField field={bind("legalNameAr")} label={t("tenancy.company.legalNameAr")} dir="rtl" maxLength={200}
          hint={form.draft.legalNameAr.trim() === "" ? t("tenancy.company.legalNameArMissing") : undefined} />
        <TextField field={bind("code")} label={t("tenancy.company.code")} dir="ltr" maxLength={20} upper hint={t("tenancy.company.codeHint")} />
        <BooleanField field={bind("isActive")} label={t("tenancy.common.active")} />
      </FormSection>
      <FormSection title={t("tenancy.company.registration")}>
        <TextField field={bind("tradeLicenceNumber")} label={t("tenancy.company.tradeLicenceNumber")} dir="ltr" maxLength={50} />
        <TextField field={bind("tradeLicenceAuthority")} label={t("tenancy.company.tradeLicenceAuthority")} maxLength={100} />
        <TextField field={bind("taxRegistrationNumber")} label={t("tenancy.company.taxRegistrationNumber")} dir="ltr" inputMode="numeric" maxLength={20} />
      </FormSection>
      <FormSection title={t("tenancy.company.accounting")}>
        <TextField field={bind("baseCurrency")} label={t("tenancy.company.baseCurrency")} dir="ltr" maxLength={3} upper list="tenancy-currencies" hint={t("tenancy.company.baseCurrencyHint")} />
        <datalist id="tenancy-currencies">
          {currencies.map((c) => (
            <option key={c} value={c} />
          ))}
        </datalist>
        <SelectField field={bind("fiscalYearStartMonth")} label={t("tenancy.company.fiscalYearStartMonth")} options={months} />
        <TextField field={bind("fiscalYearStartDay")} label={t("tenancy.company.fiscalYearStartDay")} dir="ltr" inputMode="numeric" maxLength={2} />
      </FormSection>
      <AddressFields form={form} />
      <FormSection title={t("tenancy.company.contact")}>
        <TextField field={bind("phone")} label={t("tenancy.address.phone")} dir="ltr" type="tel" maxLength={30} />
        <TextField field={bind("email")} label={t("tenancy.address.email")} dir="ltr" type="email" maxLength={254} />
        <TextField field={bind("website")} label={t("tenancy.company.website")} dir="ltr" type="url" maxLength={200} />
      </FormSection>
    </RecordForm>
  );
}

type AddressDraft = { addressLine1: string; addressLine2: string; city: string; emirate: Emirate | ""; poBox: string; country: string; addressAr: string };

/** Address fields shared by companies and branches. */
export function AddressFields<R, D extends AddressDraft>({ form }: { form: RecordFormState<R, D> }) {
  const { t } = useI18n();
  const bind = form.bind as unknown as RecordFormState<R, AddressDraft>["bind"];
  return (
    <FormSection title={t("tenancy.address.title")}>
      <TextField field={bind("addressLine1")} label={t("tenancy.address.line1")} maxLength={200} />
      <TextField field={bind("addressLine2")} label={t("tenancy.address.line2")} maxLength={200} />
      <TextField field={bind("city")} label={t("tenancy.address.city")} maxLength={100} />
      <SelectField
        field={bind("emirate")}
        label={t("tenancy.address.emirate")}
        empty={t("tenancy.address.noEmirate")}
        options={emirates.map((e) => ({ value: e, label: t(`tenancy.emirate.${e}`) }))}
      />
      <TextField field={bind("poBox")} label={t("tenancy.address.poBox")} dir="ltr" maxLength={20} />
      <TextField field={bind("country")} label={t("tenancy.address.country")} dir="ltr" maxLength={2} upper hint={t("tenancy.address.countryHint")} />
      <TextAreaField field={bind("addressAr")} label={t("tenancy.address.arabic")} dir="rtl" maxLength={400} />
    </FormSection>
  );
}

function CompanyLogo({ company, editable, onChange }: { company: Company; editable: boolean; onChange: (c: Company) => void }) {
  const { t } = useI18n();
  const [message, setMessage] = useState<string | null>(null);
  const upload = async (file: File) => {
    setMessage(null);
    const data = await new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result));
      reader.onerror = () => reject(reader.error);
      reader.readAsDataURL(file);
    });
    try {
      onChange(await api<Company>("PUT", `/api/tenancy/companies/${company.id}/logo`, { contentType: file.type, data }));
    } catch (error) {
      const problem = problemOf(error);
      setMessage(Object.values(problem.fields).flat().map((f) => f.message).join(" ") || problem.message);
    }
  };
  const remove = async () => {
    await api<void>("DELETE", `/api/tenancy/companies/${company.id}/logo`);
    onChange({ ...company, hasLogo: false, logoHash: null });
  };
  return (
    <section className="record-section" aria-label={t("tenancy.company.logo")}>
      <h3>{t("tenancy.company.logo")}</h3>
      <div className="logo-row">
        {company.hasLogo ? (
          <img className="logo" src={`/api/tenancy/companies/${company.id}/logo?v=${company.logoHash}`} alt={t("tenancy.company.logoAlt", { code: company.code })} />
        ) : (
          <span className="muted">{t("tenancy.company.noLogo")}</span>
        )}
        {editable && (
          <>
            <label className="button">
              {t("tenancy.company.uploadLogo")}
              <input
                type="file"
                accept="image/png,image/jpeg,image/webp"
                className="visually-hidden"
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (file) void upload(file);
                  e.target.value = "";
                }}
              />
            </label>
            {company.hasLogo && (
              <button type="button" className="button" onClick={() => void remove()}>
                {t("tenancy.company.removeLogo")}
              </button>
            )}
          </>
        )}
      </div>
      <p className="muted">{t("tenancy.company.logoHint")}</p>
      {message && (
        <div className="alert" role="alert">
          {message}
        </div>
      )}
    </section>
  );
}

type QuickBranch = { code: string; nameEn: string; nameAr: string; city: string; emirate: Emirate | "" };
const emptyBranch: QuickBranch = Object.freeze({ code: "", nameEn: "", nameAr: "", city: "", emirate: "" });

/** The company's branches, with a one-line form to add another (Enter saves). A new branch
 * starts in the company's emirate; right after the company is created the line has the focus. */
function CompanyBranches({ companyId, defaultEmirate, autoFocus }: { companyId: string; defaultEmirate: Emirate | ""; autoFocus: boolean }) {
  const { t } = useI18n();
  const { can } = useSession();
  const [branches, setBranches] = useState<BranchRow[]>([]);
  const fresh = (): QuickBranch => ({ ...emptyBranch, emirate: defaultEmirate });
  const [draft, setDraft] = useState<QuickBranch>(fresh);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [message, setMessage] = useState<string | null>(null);
  const nameRef = useRef<HTMLInputElement>(null);

  // Only the latest read fills the table: a read started earlier (when the form opened) that
  // answers after the read following a new branch would otherwise put the stale table back.
  const latestRead = useRef(0);
  const load = () => {
    const read = ++latestRead.current;
    return loadAll<BranchRow>("/api/tenancy/branches", `companyId eq '${companyId}'`)
      .then((rows) => {
        if (read === latestRead.current) setBranches(rows);
      })
      .catch((e) => {
        if (read === latestRead.current) setMessage(problemOf(e).message);
      });
  };

  useEffect(() => {
    void load();
    return () => {
      // A read still running when the form closes or moves to another company is ignored.
      latestRead.current++;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [companyId]);

  useEffect(() => {
    if (autoFocus) nameRef.current?.focus();
  }, [autoFocus]);

  const add = async (event: FormEvent) => {
    event.preventDefault();
    setMessage(null);
    try {
      await api("POST", "/api/tenancy/branches", {
        companyId,
        code: draft.code.trim(),
        nameEn: draft.nameEn,
        nameAr: draft.nameAr,
        city: optional(draft.city),
        emirate: draft.emirate === "" ? null : draft.emirate,
        country: "AE",
        isActive: true,
      });
      setDraft(fresh());
      setErrors({});
      window.dispatchEvent(new Event(companiesChanged));
      await load();
      nameRef.current?.focus();
    } catch (error) {
      const problem = problemOf(error);
      setErrors(problem.fields);
      setMessage(problem.message);
    }
  };

  const set = (key: keyof QuickBranch) => (e: { target: { value: string } }) =>
    setDraft((d) => ({ ...d, [key]: key === "code" ? e.target.value.toUpperCase() : e.target.value }));
  const invalid = (field: string) => (errors[field]?.length ? true : undefined);

  return (
    <section className="record-section" aria-label={t("tenancy.branches.title")}>
      <h3>{t("tenancy.company.branches", { count: branches.length })}</h3>
      <table className="grid compact">
        <thead>
          <tr>
            <th scope="col">{t("tenancy.branch.code")}</th>
            <th scope="col">{t("tenancy.branch.nameEn")}</th>
            <th scope="col">{t("tenancy.branch.nameAr")}</th>
            <th scope="col">{t("tenancy.address.city")}</th>
            <th scope="col">{t("tenancy.address.emirate")}</th>
          </tr>
        </thead>
        <tbody>
          {branches.map((b) => (
            <tr key={b.id} className={b.isActive ? undefined : "inactive"}>
              <td dir="ltr">{b.code}</td>
              <td dir="ltr">{b.nameEn}</td>
              <td dir="rtl">{b.nameAr}</td>
              <td>{b.city}</td>
              <td>{b.emirate ? t(`tenancy.emirate.${b.emirate}`) : ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {branches.length === 0 && <p className="muted">{t("tenancy.company.noBranches")}</p>}
      {can("tenancy.branches.create") && (
        <form className="quick-add" onSubmit={add} aria-label={t("tenancy.branch.add")}>
          <input ref={nameRef} name="branchNameEn" value={draft.nameEn} onChange={set("nameEn")} placeholder={t("tenancy.branch.nameEn")} aria-label={t("tenancy.branch.nameEn")} aria-invalid={invalid("nameEn")} dir="ltr" maxLength={200} />
          <input name="branchNameAr" value={draft.nameAr} onChange={set("nameAr")} placeholder={t("tenancy.branch.nameAr")} aria-label={t("tenancy.branch.nameAr")} aria-invalid={invalid("nameAr")} dir="rtl" maxLength={200} />
          <input name="branchCode" value={draft.code} onChange={set("code")} placeholder={t("tenancy.branch.codeOptional")} aria-label={t("tenancy.branch.codeOptional")} aria-invalid={invalid("code")} dir="ltr" maxLength={20} />
          <input name="branchCity" value={draft.city} onChange={set("city")} placeholder={t("tenancy.address.city")} aria-label={t("tenancy.address.city")} maxLength={100} />
          <select name="branchEmirate" value={draft.emirate} onChange={set("emirate")} aria-label={t("tenancy.address.emirate")}>
            <option value="">{t("tenancy.address.emirateUnset")}</option>
            {emirates.map((e) => (
              <option key={e} value={e}>
                {t(`tenancy.emirate.${e}`)}
              </option>
            ))}
          </select>
          <button type="submit" className="button">
            {t("tenancy.branch.add")}
          </button>
        </form>
      )}
      {message && (
        <div className="alert" role="alert">
          {message}
          {Object.entries(errors).map(([field, list]) => (
            <div key={field}>{list.map((e) => e.message).join(" ")}</div>
          ))}
        </div>
      )}
    </section>
  );
}
