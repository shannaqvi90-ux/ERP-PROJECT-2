import type { RouteDef } from "../../kernel/router";
import { AccessPage } from "./AccessPage";
import { BranchesPage } from "./BranchesPage";
import { CompaniesPage } from "./CompaniesPage";
import { TenantPage } from "./TenantPage";

export const routes: RouteDef[] = [
  { path: "/tenancy/companies", titleKey: "tenancy.menu.companies", permission: "tenancy.companies.read", component: CompaniesPage },
  { path: "/tenancy/branches", titleKey: "tenancy.menu.branches", permission: "tenancy.branches.read", component: BranchesPage },
  { path: "/tenancy/access", titleKey: "tenancy.menu.access", permission: "tenancy.access.read", component: AccessPage },
  { path: "/tenancy/tenant", titleKey: "tenancy.menu.tenant", permission: "tenancy.tenant.read", component: TenantPage },
];
