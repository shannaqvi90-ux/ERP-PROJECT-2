import type { RouteDef } from "../../kernel/router";
import { TenantPage } from "./TenantPage";

export const routes: RouteDef[] = [
  { path: "/tenancy/tenant", titleKey: "tenancy.menu.tenant", permission: "tenancy.tenant.read", component: TenantPage },
];
