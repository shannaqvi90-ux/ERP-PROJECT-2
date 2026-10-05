import { useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { loadAll } from "./records";
import { companiesChanged, type BranchRow, type Company } from "./types";
import {
  CheckField,
  emirates,
  problemOf,
  SelectField,
  TextField,
  useMonths,
  useLocalName,
  useScreenKeys,
  type Emirate,
  type FieldErrors,
} from "./ui";

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
  version: number | null;
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
  version: null,
});

function draftOf(c: Company): Draft {
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
    version: c.version,
  };
}

const optional = (value: string) => (value.trim() === "" ? null : value.trim());

/** Currencies offered first; any ISO 4217 code is accepted. */
const currencies = Object.freeze(["AED", "SAR", "OMR", "QAR", "BHD", "KWD", "USD", "EUR", "GBP", "INR", "PKR", "CNY"]);

/** Create or edit one company; once saved, its branches can be added right below. */
export function CompanyForm({ id, onSaved, onClose }: { id: string | null; onSaved: (id: string) => void; onClose: () => void }) {
  const { t } = useI18n();
  const { can } = useSession();
  const months = useMonths();
  const [company, setCompany] = useState<Company | null>(null);
  const [draft, setDraft] = useState<Draft>(blank);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [message, setMessage] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [justCreated, setJustCreated] = useState(false);
  const formRef = useRef<HTMLFormElement>(null);
  const name = useLocalName();
  // The company record is shared by every branch: changing it needs every branch of it.
  const editable = id === null ? can("tenancy.companies.create") : can("tenancy.companies.update") && company?.everyBranch !== false;

  useEffect(() => {
    if (id === null) return;
    api<Company>("GET", `/api/tenancy/companies/${id}`)
      .then((c) => {
        setCompany(c);
        setDraft(draftOf(c));
      })
      .catch((e) => setMessage(problemOf(e).message));
  }, [id]);

  const set = <K extends keyof Draft>(key: K) => (value: Draft[K]) => {
    setDraft((d) => ({ ...d, [key]: value }));
    setSaved(false);
  };

  const save = async (event?: FormEvent) => {
    event?.preventDefault();
    if (!editable || busy) return;
    setBusy(true);
    setMessage(null);
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
      version: draft.version,
    };
    try {
      const result = id === null
        ? await api<Company>("POST", "/api/tenancy/companies", body)
        : await api<Company>("PUT", `/api/tenancy/companies/${id}`, body);
      setCompany(result);
      setDraft(draftOf(result));
      setErrors({});
      setSaved(true);
      // A new company's next step is its first branch: the branch line takes the focus.
      setJustCreated(id === null);
      onSaved(result.id);
      window.dispatchEvent(new Event(companiesChanged));
    } catch (error) {
      const problem = problemOf(error);
      setErrors(problem.fields);
      setMessage(problem.message);
      const first = Object.keys(problem.fields)[0];
      formRef.current?.querySelector<HTMLElement>(`[data-field="${first}"] input, [data-field="${first}"] select, [data-field="${first}"] textarea`)?.focus();
    } finally {
      setBusy(false);
    }
  };

  useScreenKeys({ onSave: () => void save(), onClose });

  const common = { errors };
  return (
    <div className="record">
      <form ref={formRef} className="record-form" onSubmit={save} noValidate aria-label={t("tenancy.company.form")}>
        <div className="record-header">
          <h2>{id === null && !company ? t("tenancy.company.new") : `${company?.code ?? ""} · ${company ? name(company.legalNameEn, company.legalNameAr) : ""}`}</h2>
          <div className="record-actions">
            {editable && (
              <button type="submit" className="button primary" disabled={busy} title={t("tenancy.common.saveHint")} aria-keyshortcuts="Control+S Control+Enter">
                {busy ? t("tenancy.common.saving") : t("tenancy.common.save")}
              </button>
            )}
            <button type="button" className="button" onClick={onClose} title={t("tenancy.common.closeHint")} aria-keyshortcuts="Escape">
              {t("tenancy.common.close")}
            </button>
          </div>
        </div>
        {message && (
          <div className="alert" role="alert">
            {message}
          </div>
        )}
        {saved && (
          <div className="notice" role="status">
            {t("tenancy.common.saved")}
          </div>
        )}
        {company?.everyBranch === false && can("tenancy.companies.update") && (
          <div className="notice" data-testid="company-some-branches">
            {t("tenancy.company.someBranchesOnly")}
          </div>
        )}
        <fieldset disabled={!editable}>
          <legend>{t("tenancy.company.general")}</legend>
          <div className="form-grid">
            <TextField name="legalNameEn" label={t("tenancy.company.legalNameEn")} value={draft.legalNameEn} onChange={set("legalNameEn")} {...common} dir="ltr" maxLength={200} autoFocus={id === null} />
            <TextField name="legalNameAr" label={t("tenancy.company.legalNameAr")} value={draft.legalNameAr} onChange={set("legalNameAr")} {...common} dir="rtl" maxLength={200} hint={draft.legalNameAr.trim() === "" ? t("tenancy.company.legalNameArMissing") : undefined} />
            <TextField name="code" label={t("tenancy.company.code")} value={draft.code} onChange={set("code")} {...common} dir="ltr" maxLength={20} upper hint={t("tenancy.company.codeHint")} />
            <CheckField name="isActive" label={t("tenancy.common.active")} checked={draft.isActive} onChange={set("isActive")} />
          </div>
        </fieldset>
        <fieldset disabled={!editable}>
          <legend>{t("tenancy.company.registration")}</legend>
          <div className="form-grid">
            <TextField name="tradeLicenceNumber" label={t("tenancy.company.tradeLicenceNumber")} value={draft.tradeLicenceNumber} onChange={set("tradeLicenceNumber")} {...common} dir="ltr" maxLength={50} />
            <TextField name="tradeLicenceAuthority" label={t("tenancy.company.tradeLicenceAuthority")} value={draft.tradeLicenceAuthority} onChange={set("tradeLicenceAuthority")} {...common} maxLength={100} />
            <TextField name="taxRegistrationNumber" label={t("tenancy.company.taxRegistrationNumber")} value={draft.taxRegistrationNumber} onChange={set("taxRegistrationNumber")} {...common} dir="ltr" inputMode="numeric" maxLength={20} />
          </div>
        </fieldset>
        <fieldset disabled={!editable}>
          <legend>{t("tenancy.company.accounting")}</legend>
          <div className="form-grid">
            <TextField name="baseCurrency" label={t("tenancy.company.baseCurrency")} value={draft.baseCurrency} onChange={set("baseCurrency")} {...common} dir="ltr" maxLength={3} upper list="tenancy-currencies" hint={t("tenancy.company.baseCurrencyHint")} />
            <datalist id="tenancy-currencies">
              {currencies.map((c) => (
                <option key={c} value={c} />
              ))}
            </datalist>
            <SelectField name="fiscalYearStartMonth" label={t("tenancy.company.fiscalYearStartMonth")} value={draft.fiscalYearStartMonth} options={months} onChange={(v) => set("fiscalYearStartMonth")(v)} {...common} />
            <TextField name="fiscalYearStartDay" label={t("tenancy.company.fiscalYearStartDay")} value={draft.fiscalYearStartDay} onChange={set("fiscalYearStartDay")} {...common} dir="ltr" inputMode="numeric" maxLength={2} />
          </div>
        </fieldset>
        <AddressFields draft={draft} set={set} errors={errors} disabled={!editable} />
        <fieldset disabled={!editable}>
          <legend>{t("tenancy.company.contact")}</legend>
          <div className="form-grid">
            <TextField name="phone" label={t("tenancy.address.phone")} value={draft.phone} onChange={set("phone")} {...common} dir="ltr" type="tel" maxLength={30} />
            <TextField name="email" label={t("tenancy.address.email")} value={draft.email} onChange={set("email")} {...common} dir="ltr" type="email" maxLength={254} />
            <TextField name="website" label={t("tenancy.company.website")} value={draft.website} onChange={set("website")} {...common} dir="ltr" type="url" maxLength={200} />
          </div>
        </fieldset>
      </form>
      {company && <CompanyLogo company={company} editable={can("tenancy.companies.update") && company.everyBranch !== false} onChange={setCompany} />}
      {company && can("tenancy.branches.read") && (
        <CompanyBranches companyId={company.id} companyName={company.legalNameEn} defaultEmirate={company.emirate ?? ""} autoFocus={justCreated} />
      )}
    </div>
  );
}

/** Address fields shared by companies and branches. */
export function AddressFields<D extends { addressLine1: string; addressLine2: string; city: string; emirate: Emirate | ""; poBox: string; country: string; addressAr: string }>({
  draft,
  set,
  errors,
  disabled,
}: {
  draft: D;
  set: <K extends keyof D>(key: K) => (value: D[K]) => void;
  errors: FieldErrors;
  disabled?: boolean;
}) {
  const { t } = useI18n();
  const common = { errors };
  return (
    <fieldset disabled={disabled}>
      <legend>{t("tenancy.address.title")}</legend>
      <div className="form-grid">
        <TextField name="addressLine1" label={t("tenancy.address.line1")} value={draft.addressLine1} onChange={set("addressLine1") as (v: string) => void} {...common} maxLength={200} />
        <TextField name="addressLine2" label={t("tenancy.address.line2")} value={draft.addressLine2} onChange={set("addressLine2") as (v: string) => void} {...common} maxLength={200} />
        <TextField name="city" label={t("tenancy.address.city")} value={draft.city} onChange={set("city") as (v: string) => void} {...common} maxLength={100} />
        <SelectField
          name="emirate"
          label={t("tenancy.address.emirate")}
          value={draft.emirate}
          empty={t("tenancy.address.noEmirate")}
          options={emirates.map((e) => ({ value: e, label: t(`tenancy.emirate.${e}`) }))}
          onChange={set("emirate") as (v: Emirate | "") => void}
          {...common}
        />
        <TextField name="poBox" label={t("tenancy.address.poBox")} value={draft.poBox} onChange={set("poBox") as (v: string) => void} {...common} dir="ltr" maxLength={20} />
        <TextField name="country" label={t("tenancy.address.country")} value={draft.country} onChange={set("country") as (v: string) => void} {...common} dir="ltr" maxLength={2} upper hint={t("tenancy.address.countryHint")} />
        <TextField name="addressAr" label={t("tenancy.address.arabic")} value={draft.addressAr} onChange={set("addressAr") as (v: string) => void} {...common} dir="rtl" maxLength={400} multiline wide />
      </div>
    </fieldset>
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

/** The start of a new branch's English name: a UAE branch trades under its company's name
 * followed by its own ("Falcon Logistics LLC - Jebel Ali Branch"), so the line starts with the
 * company's name and the user types only the branch's part. */
export const branchNamePrefix = (companyName: string) => (companyName.trim() === "" ? "" : `${companyName.trim()} - `);

/** A branch name the user left at the prefix alone is the company's name itself. */
export const branchNameOf = (typed: string) => typed.replace(/\s+-\s*$/, "");

/** The company's branches, with a one-line form to add another (Enter saves). A new branch
 * starts in the company's emirate, its English name with the company's; right after the company
 * is created the line has the focus, the caret after the company's name. */
function CompanyBranches({ companyId, companyName, defaultEmirate, autoFocus }: { companyId: string; companyName: string; defaultEmirate: Emirate | ""; autoFocus: boolean }) {
  const { t } = useI18n();
  const { can } = useSession();
  const [branches, setBranches] = useState<BranchRow[]>([]);
  const fresh = (): QuickBranch => ({ ...emptyBranch, nameEn: branchNamePrefix(companyName), emirate: defaultEmirate });
  const [draft, setDraft] = useState<QuickBranch>(fresh);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [message, setMessage] = useState<string | null>(null);
  const nameRef = useRef<HTMLInputElement>(null);

  const load = () =>
    loadAll<BranchRow>("/api/tenancy/branches", `companyId eq '${companyId}'`)
      .then(setBranches)
      .catch((e) => setMessage(problemOf(e).message));

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [companyId]);

  /** Focus the name with the caret at its end (after the company's name). */
  const focusName = () => {
    const input = nameRef.current;
    if (!input) return;
    input.focus();
    input.setSelectionRange(input.value.length, input.value.length);
  };

  useEffect(() => {
    if (autoFocus) focusName();
  }, [autoFocus]);

  const add = async (event: FormEvent) => {
    event.preventDefault();
    setMessage(null);
    try {
      await api("POST", "/api/tenancy/branches", {
        companyId,
        code: draft.code.trim(),
        nameEn: branchNameOf(draft.nameEn),
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
      focusName();
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
