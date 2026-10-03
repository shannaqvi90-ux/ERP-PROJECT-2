import { ListView } from "../../kernel/lists/ListView";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";

/** The roles list on the shared list framework (p03 adds the permission matrix and editing). */
export function RolesPage() {
  const { t, language, formatNumber } = useI18n();
  const { can } = useSession();
  return (
    <ListView
      listKey="identity.roles"
      titleKey="identity.roles.title"
      countKey="identity.roles.count"
      searchPlaceholderKey="identity.roles.search"
      can={can}
      renderCell={{
        nameEn: (r) => String((language === "ar" ? r.nameAr : r.nameEn) ?? ""),
        isSystem: (r) => (r.isSystem ? t("identity.roles.system") : t("identity.roles.custom")),
        permissions: (r) => formatNumber(Array.isArray(r.permissions) ? r.permissions.length : 0),
      }}
    />
  );
}
