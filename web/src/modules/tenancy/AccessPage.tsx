import { useState } from "react";
import { api } from "../../kernel/api";
import { RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { listSearch, useRecordPanel } from "./records";
import type { AccessCompanySummary, CompanyAccess, UserAccess } from "./types";
import { useLocalName, useScreenKeys } from "./ui";

/**
 * Who may work in which company and branch: the workspace's users in the shared list (search by
 * name or e-mail, filters, sort, views), each with the companies they may work in, and the open
 * user's companies and branches in the list's panel (?open=id). Only the administrator's own
 * companies appear; access to other companies is neither shown nor changed.
 */
export function AccessPage() {
  const { formatNumber, language } = useI18n();
  const { can } = useSession();
  const panel = useRecordPanel(false);
  useScreenKeys({ search: listSearch });

  return (
    <div className="tn-list">
      <ListView
        listKey="tenancy.access"
        titleKey="tenancy.access.title"
        countKey="tenancy.access.count"
        searchPlaceholderKey="tenancy.access.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        renderRecord={(id, close, nav) => <AccessForm key={id} userId={id} onSaved={() => panel.saved(id)} onClose={close} nav={nav} />}
        renderCell={{
          email: (u) => <span dir="ltr">{String(u.email ?? "")}</span>,
          companies: (u) => {
            const companies = Array.isArray(u.companies) ? (u.companies as AccessCompanySummary[]) : [];
            return companies.length === 0 ? (
              <span className="muted">—</span>
            ) : (
              <span dir="ltr">
                {companies.map((c) => (c.allBranches ? c.code : `${c.code} (${formatNumber(c.branchCount)})`)).join(language === "ar" ? "، " : ", ")}
              </span>
            );
          },
        }}
      />
    </div>
  );
}

function AccessForm({ userId, onSaved, onClose, nav }: { userId: string; onSaved: () => void; onClose: () => void; nav?: RecordNavigation }) {
  const { t } = useI18n();
  const { can } = useSession();
  const name = useLocalName();
  // Company access is a grant: the server says whether this caller may change this user at all.
  const [editable, setEditable] = useState(false);
  const form = useRecordForm<UserAccess, { companies: CompanyAccess[] }>({
    load: async (signal) => {
      const access = await api<UserAccess>("GET", `/api/tenancy/access/${userId}`, undefined, { signal });
      setEditable(can("tenancy.access.update") && !access.isCaller && access.canEdit !== false);
      return access;
    },
    initial: (a) => ({ companies: a?.companies ?? [] }),
    canEdit: editable,
    // The version that was read: the server refuses the save (409) if the access changed since.
    save: (draft, read) => api<UserAccess>("PUT", `/api/tenancy/access/${userId}`, { companies: draft.companies, version: read?.version }),
    onSaved: () => onSaved(),
  }, userId);
  const access = form.record;
  const draft = form.draft.companies;
  const change = (next: (companies: CompanyAccess[]) => CompanyAccess[]) => form.update((d) => ({ companies: next(d.companies) }));
  const entry = (companyId: string) => draft.find((d) => d.companyId === companyId);
  const canGiveAll = (companyId: string) => access?.options.find((o) => o.id === companyId)?.canGiveAllBranches !== false;
  const toggleCompany = (companyId: string, on: boolean) => {
    const all = canGiveAll(companyId);
    change((d) => (on ? [...d, { companyId, allBranches: all, branchIds: [] }] : d.filter((x) => x.companyId !== companyId)));
  };
  const setAll = (companyId: string, all: boolean) =>
    change((d) => d.map((x) => (x.companyId === companyId ? { ...x, allBranches: all, branchIds: all ? [] : x.branchIds } : x)));
  const toggleBranch = (companyId: string, branchId: string, on: boolean) =>
    change((d) => d.map((x) => (x.companyId === companyId ? { ...x, branchIds: on ? [...x.branchIds, branchId] : x.branchIds.filter((b) => b !== branchId) } : x)));

  const reason = access?.isCaller ? t("tenancy.access.ownAccess") : access && access.canEdit === false && access.readOnlyReason ? t(access.readOnlyReason) : undefined;
  return (
    <RecordForm form={form} label={t("tenancy.access.form")} title={access ? access.displayName : ""} subtitle={access && <span dir="ltr">{access.email}</span>}
      onClose={onClose} nav={nav} readOnlyReason={reason}>
      {access?.isCaller && <div className="notice">{t("tenancy.access.ownAccess")}</div>}
      {access && !access.isCaller && access.canEdit === false && access.readOnlyReason && (
        <div className="notice" data-testid="access-read-only">
          {t(access.readOnlyReason)}
        </div>
      )}
      {form.conflict && (
        <div className="alert" role="alert" data-testid="access-changed-elsewhere">
          {t("tenancy.access.changedElsewhere")}
        </div>
      )}
      {access && access.options.length === 0 && <p className="muted">{t("tenancy.access.noCompanies")}</p>}
      <fieldset disabled={!editable} className="access-list form-section">
        <legend>{t("tenancy.access.companies")}</legend>
        {access?.options.map((company) => {
          const current = entry(company.id);
          return (
            <div key={company.id} className="access-company" data-company={company.code}>
              <label className="check">
                <input type="checkbox" checked={Boolean(current)} onChange={(e) => toggleCompany(company.id, e.target.checked)} />
                <span>
                  <span dir="ltr">{company.code}</span> · {name(company.legalNameEn, company.legalNameAr)}
                  {!company.isActive && <span className="muted"> ({t("tenancy.common.inactive")})</span>}
                </span>
              </label>
              {current && (
                <div className="access-branches">
                  <label className="check" title={company.canGiveAllBranches === false ? t("tenancy.access.onlyOwnBranches") : undefined}>
                    <input
                      type="checkbox"
                      checked={current.allBranches}
                      disabled={company.canGiveAllBranches === false && !current.allBranches}
                      onChange={(e) => setAll(company.id, e.target.checked)}
                    />
                    <span>{t("tenancy.access.allBranches")}</span>
                  </label>
                  {company.canGiveAllBranches === false && <p className="muted">{t("tenancy.access.onlyOwnBranches")}</p>}
                  {!current.allBranches &&
                    company.branches.map((b) => (
                      <label key={b.id} className="check">
                        <input type="checkbox" checked={current.branchIds.includes(b.id)} onChange={(e) => toggleBranch(company.id, b.id, e.target.checked)} />
                        <span>
                          <span dir="ltr">{b.code}</span> · {name(b.nameEn, b.nameAr)}
                        </span>
                      </label>
                    ))}
                </div>
              )}
            </div>
          );
        })}
      </fieldset>
    </RecordForm>
  );
}
