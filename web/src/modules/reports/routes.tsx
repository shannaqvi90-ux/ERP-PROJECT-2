import type { RouteDef } from "../../kernel/router";
import { ReportsPage } from "./ReportsPage";

export const routes: RouteDef[] = [{ path: "/reports/catalog", titleKey: "reports.menu.catalog", permission: "reports.catalog.read", component: ReportsPage }];
