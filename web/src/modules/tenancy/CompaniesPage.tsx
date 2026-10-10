import { useCallback, useEffect, useState } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { CompanyForm } from "./CompanyForm";
import { listSearch, newRecord, useRecordPanel, type ListPage } from "./records";
import { companiesChanged, type CompanyRow } from "./types";
import { useScreenKeys } from "./ui";

/**
 * Companies of the workspace (those the user may work in): the shared list (search as you type,
 * filters, sort, grouping, views) with the open company's form in its panel (?open=id, or
 * ?open=new). Keyboard: "/" search, arrows move, Enter opens, Alt+N new, Ctrl+S save, Escape
 * closes the form.
 *
 * Company codes are unique across the workspace, so only someone who works in every company of it
 * may create a company (the server answers 403 tenancy.companyNeedsEveryCompany otherwise; critic
 * p02 round 7). The list rows say whether the user does (everyCompany): the screen reads one row
 * before it draws, so New, Alt+N and a new company from the address (<screen>/new, typed or
 * bookmarked, which the list opens once its definition arrives) are offered only to them.
 */
export function CompaniesPage() {
  const everyCompany = useEveryCompany();
  if (everyCompany === undefined) return <div className="tn-list" aria-busy="true" />;
  return <CompaniesScreen everyCompany={everyCompany} />;
}

/** Whether the user works in every company of the workspace (undefined until known). No row means
 * they work in none, and every workspace has a company. */
function useEveryCompany(): boolean | undefined {
  const [everyCompany, setEveryCompany] = useState<boolean | undefined>(undefined);
  useEffect(() => {
    const controller = new AbortController();
    const load = () =>
      api<ListPage<CompanyRow>>("GET", "/api/tenancy/companies?take=1", undefined, { signal: controller.signal })
        .then((page) => setEveryCompany(page.items.length > 0 && page.items[0]!.everyCompany !== false))
        .catch(() => {
          if (!controller.signal.aborted) setEveryCompany(false);
        });
    void load();
    window.addEventListener(companiesChanged, load);
    return () => {
      controller.abort();
      window.removeEventListener(companiesChanged, load);
    };
  }, []);
  return everyCompany;
}

function CompaniesScreen({ everyCompany }: { everyCompany: boolean }) {
  const { t } = useI18n();
  const { can } = useSession();
  const creatable = can("tenancy.companies.create") && everyCompany;
  const panel = useRecordPanel(creatable);
  useScreenKeys({ onNew: panel.startNew, search: listSearch });
  const { onOpenIdChange } = panel;
  const openIdChange = useCallback((id: string | null) => onOpenIdChange(id === newRecord && !creatable ? null : id), [onOpenIdChange, creatable]);

  return (
    <div className="tn-list">
      <ListView
        listKey="tenancy.companies"
        titleKey="tenancy.companies.title"
        countKey="tenancy.companies.count"
        searchPlaceholderKey="tenancy.companies.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={openIdChange}
        renderRecord={(id, close, nav) => (
          <CompanyForm key={panel.formKey} id={id === newRecord ? null : id} onSaved={panel.saved} onClose={close} nav={nav} />
        )}
        actions={
          panel.startNew && (
            <button type="button" className="button primary" onClick={panel.startNew} title={t("tenancy.common.newHint")} aria-keyshortcuts="Alt+N">
              {t("tenancy.common.new")}
            </button>
          )
        }
        renderCell={{
          code: (c) => <span dir="ltr">{String(c.code ?? "")}</span>,
          legalNameEn: (c) => <span dir="ltr">{String(c.legalNameEn ?? "")}</span>,
          legalNameAr: (c) => <span dir="rtl">{String(c.legalNameAr ?? "")}</span>,
          baseCurrency: (c) => <span dir="ltr">{String(c.baseCurrency ?? "")}</span>,
          isActive: (c) => (c.isActive ? t("tenancy.common.active") : <span className="muted">{t("tenancy.common.inactive")}</span>),
        }}
      />
    </div>
  );
}
